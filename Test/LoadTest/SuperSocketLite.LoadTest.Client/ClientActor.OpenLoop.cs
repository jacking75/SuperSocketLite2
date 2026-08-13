using System.Collections.Concurrent;
using System.Diagnostics;
using SuperSocketLite.LoadTest.Client.Connections;
using SuperSocketLite.LoadTest.Client.Scenarios;
using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Client;

/// <summary>
/// 열린 루프(open loop) 송신 경로입니다.
/// </summary>
/// <remarks>
/// 닫힌 루프는 응답을 받은 뒤에 다음 지연을 시작하므로 사이클 시간이 지연 + RTT가 됩니다.
/// 서버가 느려지면 부하량도 함께 줄어들어, 정작 서버가 힘들 때 부하가 약해지고 지연 시간이 실제보다 좋게 측정됩니다.
///
/// 여기서는 송신 시각을 실행 시작 기준의 절대 일정으로 고정하고 송신과 수신을 독립 루프로 돌립니다.
/// 한 번 늦게 나가도 다음 송신은 원래 예정 시각을 따르므로 오차가 누적되지 않습니다.
/// 요청과 응답은 본문 앞 8바이트에 실은 상관 ID로 짝지으므로 응답 순서에 의존하지 않습니다.
/// </remarks>
public sealed partial class ClientActor
{
    private int _openLoopSequence;

    private async Task RunOpenLoopTcpAsync(ILoadTestConnection connection, CancellationToken cancellationToken)
    {
        var pending = new ConcurrentDictionary<long, PendingRequest>();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var sendTask = SendLoopAsync(connection, pending, session);
        var receiveTask = ReceiveLoopAsync(connection, pending, session);

        try
        {
            await Task.WhenAll(sendTask, receiveTask).ConfigureAwait(false);
        }
        finally
        {
            DiscardPending(pending);
        }
    }

    private async Task SendLoopAsync(
        ILoadTestConnection connection,
        ConcurrentDictionary<long, PendingRequest> pending,
        CancellationTokenSource session)
    {
        var token = session.Token;

        try
        {
            var maxInFlight = _options.ResolveMaxInFlight();
            var intervalTicks = _options.SendRatePerClient > 0
                ? Stopwatch.Frequency / _options.SendRatePerClient
                : Stopwatch.Frequency;

            // 접속 직후를 기준으로 일정을 세운다. 여기에 위상 오프셋을 더하면 송신 레이트가 낮을 때
            // 첫 송신이 실행 시간 밖으로 밀려날 수 있으므로 넣지 않는다.
            // 클라이언트 사이의 송신 시각 분산은 --ramp-up이 접속 시각을 흩어 주는 것으로 얻는다.
            var baseTicks = Stopwatch.GetTimestamp();
            long emitted = 0;
            var nextBurstAt = _options.BurstEvery;

            while (!token.IsCancellationRequested)
            {
                if (_options.Scenario == "reconnect-storm" &&
                    ElapsedTime() >= ReconnectStormScenario.DisconnectAt(_clientId, _options))
                {
                    SetState(ClientState.Reconnecting);
                    break;
                }

                var targetTicks = baseTicks + (long)(emitted * intervalTicks);
                var now = Stopwatch.GetTimestamp();

                if (targetTicks > now)
                {
                    await Task.Delay(TicksToTimeSpan(targetTicks - now), token).ConfigureAwait(false);
                    _metrics.OnScheduleDelay(0);
                }
                else
                {
                    // 예정 시각을 이미 넘겼다. 클라이언트가 요청한 부하를 내지 못하고 있다는 뜻이다.
                    _metrics.OnScheduleDelay(TicksToMicroseconds(now - targetTicks));
                }

                emitted++;
                ExpirePending(pending);

                var batchSize = _options.CoalescedPacket ? 2 : 1;
                if (_options.Scenario == "burst" && ElapsedTime() >= nextBurstAt)
                {
                    // 기본 레이트는 그대로 두고 주기마다 한 뭉치를 얹는다.
                    // 열린 루프이므로 이 뭉치가 응답 대기에 막히지 않고 실제로 몰려 나간다.
                    batchSize = _options.BurstSize;
                    nextBurstAt += _options.BurstEvery;
                }

                var available = maxInFlight - pending.Count;
                if (available <= 0)
                {
                    _metrics.OnSendSkipped();
                    continue;
                }

                // 한도가 모자라면 보낼 수 있는 만큼만 보내고 부족분을 남긴다.
                // 통째로 건너뛰면 폭주 시나리오가 아무 일도 하지 않은 것처럼 보인다.
                if (available < batchSize)
                {
                    _metrics.OnSendSkipped();
                    batchSize = available;
                }

                await SendBatchAsync(connection, pending, batchSize, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            session.Cancel();
        }
    }

    private async Task ReceiveLoopAsync(
        ILoadTestConnection connection,
        ConcurrentDictionary<long, PendingRequest> pending,
        CancellationTokenSource session)
    {
        var token = session.Token;

        try
        {
            var state = new TcpReceiveState(1024 * 1024);

            while (!token.IsCancellationRequested)
            {
                await DelayBeforeReceiveAsync(timeout: false, token).ConfigureAwait(false);

                var response = await ReceiveBinaryResponseAsync(connection, state, token).ConfigureAwait(false);
                var completedTicks = Stopwatch.GetTimestamp();

                if (!BinaryPacket.TryReadCorrelationId(response.Packet.Body, out var correlationId))
                {
                    _metrics.OnProtocolError();
                    continue;
                }

                // 이미 만료 처리된 요청의 늦은 응답이다. 타임아웃으로 한 번 센 것을 성공으로 되돌리지 않는다.
                if (!pending.TryRemove(correlationId, out var request))
                    continue;

                _metrics.OnRequestCompleted();

                var endedMs = ElapsedMs();
                var rttUs = TicksToMicroseconds(completedTicks - request.StartedTicks);
                RecordReceive(response.BytesRead, rttUs);
                WriteOperation(
                    endedMs,
                    request.OperationId,
                    request.OperationType,
                    request.PacketId,
                    request.PayloadBytes,
                    request.StartedMs,
                    endedMs,
                    rttUs,
                    true,
                    string.Empty,
                    string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // 실행이 끝나 소켓이 정리되는 중이다. 장애가 아니다.
        }
        finally
        {
            session.Cancel();
        }
    }

    private async Task SendBatchAsync(
        ILoadTestConnection connection,
        ConcurrentDictionary<long, PendingRequest> pending,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var requests = new List<PendingRequest>(batchSize);
        var encodedPackets = new List<byte[]>(batchSize);

        for (var i = 0; i < batchSize; i++)
        {
            var sequence = ++_openLoopSequence;
            var (operationType, packet) = CreateNextTcpPacket(sequence);
            var operationId = _writers.NextOperationId();
            var body = BinaryPacket.WithCorrelationId(packet.Body, operationId);
            var encoded = BinaryPacket.Encode(packet.PacketId, packet.Value1, body);

            encodedPackets.Add(encoded);
            requests.Add(new PendingRequest(
                operationId,
                operationType,
                packet.PacketId,
                body.Length,
                ElapsedMs(),
                0));
        }

        var payload = new byte[encodedPackets.Sum(packet => packet.Length)];
        var offset = 0;
        foreach (var encodedPacket in encodedPackets)
        {
            encodedPacket.CopyTo(payload.AsSpan(offset));
            offset += encodedPacket.Length;
        }

        // 응답이 송신 완료보다 먼저 도착할 수 있으므로 보내기 전에 등록한다.
        var startedTicks = Stopwatch.GetTimestamp();
        foreach (var request in requests)
        {
            var tracked = request with { StartedTicks = startedTicks };
            pending[tracked.OperationId] = tracked;
            _metrics.OnRequestStarted();
        }

        try
        {
            if (_options.PartialPacket && !_options.CoalescedPacket && payload.Length > BinaryPacket.HeaderSize)
            {
                await connection.SendAsync(payload.AsMemory(0, BinaryPacket.HeaderSize), cancellationToken).ConfigureAwait(false);
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                await connection.SendAsync(payload.AsMemory(BinaryPacket.HeaderSize), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await connection.SendAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            foreach (var encodedPacket in encodedPackets)
                _metrics.OnSendSuccess(encodedPacket.Length);
        }
        catch (Exception ex)
        {
            foreach (var request in requests)
            {
                if (pending.TryRemove(request.OperationId, out _))
                    _metrics.OnRequestCompleted();
            }

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            var endedMs = ElapsedMs();
            _metrics.OnSendFail();
            _metrics.OnSocketError();
            foreach (var request in requests)
            {
                WriteOperation(
                    endedMs,
                    request.OperationId,
                    request.OperationType,
                    request.PacketId,
                    request.PayloadBytes,
                    request.StartedMs,
                    endedMs,
                    0,
                    false,
                    ex.GetType().Name,
                    ex.Message);
            }

            throw;
        }
    }

    /// <summary>
    /// 응답이 오지 않은 채 수신 타임아웃을 넘긴 요청을 정리합니다.
    /// 요청마다 타이머를 두는 대신 송신 주기마다 훑습니다. 동시 요청 수가 작아 훑는 비용이 낮습니다.
    /// </summary>
    private void ExpirePending(ConcurrentDictionary<long, PendingRequest> pending)
    {
        if (pending.IsEmpty)
            return;

        var timeoutTicks = (long)(_options.ReceiveTimeout.TotalSeconds * Stopwatch.Frequency);
        var now = Stopwatch.GetTimestamp();

        foreach (var entry in pending)
        {
            if (now - entry.Value.StartedTicks < timeoutTicks)
                continue;

            if (!pending.TryRemove(entry.Key, out var expired))
                continue;

            _metrics.OnRequestCompleted();
            _metrics.OnTimeout();

            var endedMs = ElapsedMs();
            WriteOperation(
                endedMs,
                expired.OperationId,
                expired.OperationType,
                expired.PacketId,
                expired.PayloadBytes,
                expired.StartedMs,
                endedMs,
                0,
                false,
                "timeout",
                string.Empty);
        }
    }

    /// <summary>
    /// 실행이 끝나는 시점에 아직 응답을 기다리던 요청을 버립니다.
    /// 타임아웃이 아니라 실행 종료로 끊긴 것이므로 오류로 세지 않습니다.
    /// </summary>
    private void DiscardPending(ConcurrentDictionary<long, PendingRequest> pending)
    {
        foreach (var entry in pending)
        {
            if (pending.TryRemove(entry.Key, out _))
                _metrics.OnRequestCompleted();
        }
    }

    private static long TicksToMicroseconds(long ticks)
    {
        return (long)(ticks * 1_000_000.0 / Stopwatch.Frequency);
    }

    private static TimeSpan TicksToTimeSpan(long ticks)
    {
        return TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
    }

    private sealed record PendingRequest(
        long OperationId,
        string OperationType,
        int PacketId,
        int PayloadBytes,
        long StartedMs,
        long StartedTicks);
}
