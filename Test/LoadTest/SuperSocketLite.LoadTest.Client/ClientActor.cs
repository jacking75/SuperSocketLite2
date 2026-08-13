using System.Diagnostics;
using SuperSocketLite.LoadTest.Client.Connections;
using SuperSocketLite.LoadTest.Client.Metrics;
using SuperSocketLite.LoadTest.Client.Scenarios;
using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Client;

public sealed partial class ClientActor
{
    private readonly int _clientId;
    private readonly LoadTestOptions _options;
    private readonly ClientMetricsCollector _metrics;
    private readonly ClientCsvWriters _writers;
    private readonly long _runStartMs;
    private readonly Guid _udpSessionId;
    private readonly GameLikeScenario _gameScenario = new();
    private readonly Random _random;
    private ClientState _state = ClientState.Created;

    public ClientActor(int clientId, LoadTestOptions options, ClientMetricsCollector metrics, ClientCsvWriters writers, long runStartMs)
    {
        _clientId = clientId;
        _options = options;
        _metrics = metrics;
        _writers = writers;
        _runStartMs = runStartMs;
        _udpSessionId = DeterministicGuid(clientId, options.RunId);
        _random = new Random(HashCode.Combine(clientId, options.RunId));
    }

    public async Task RunAsync(TimeSpan connectDelay, CancellationToken cancellationToken)
    {
        if (connectDelay > TimeSpan.Zero)
            await Task.Delay(connectDelay, cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var connection = CreateConnection();
            SetState(ClientState.Connecting);
            var connected = false;

            try
            {
                await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
                connected = true;
                _metrics.OnConnectSuccess();
                SetState(ClientState.Active);

                if (_options.Transport == "udp")
                    await RunUdpLoopAsync(connection, cancellationToken).ConfigureAwait(false);
                else if (_options.Protocol == "text-line")
                    await RunTextLineLoopAsync(connection, cancellationToken).ConfigureAwait(false);
                else if (_options.UsesOpenLoop())
                    await RunOpenLoopTcpAsync(connection, cancellationToken).ConfigureAwait(false);
                else
                    await RunTcpLoopAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!connected && _state == ClientState.Connecting)
                {
                    _metrics.OnConnectFail();
                    _metrics.OnSocketError();
                }

                break;
            }
            catch (Exception ex) when (!connected)
            {
                _metrics.OnConnectFail();
                _metrics.OnSocketError();

                // 서버가 거부한 것인지 이 머신이 더 이상 소켓을 못 내는 것인지 구분해 둔다.
                if (LoadGeneratorHost.IsLocalResourceExhaustion(ex))
                    _metrics.OnLocalResourceExhaustion();

                SetState(ClientState.Reconnecting);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _metrics.OnRuntimeError();
                _metrics.OnSocketError();
                SetState(ClientState.Reconnecting);
                if (_options.ReconnectPercent > 0)
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (connected)
                {
                    // 실행이 끝나는 시점에만 비정상 종료를 흉내 낸다.
                    // 재접속 중에도 RST를 보내면 재접속 시나리오의 의도가 흐려진다.
                    if (cancellationToken.IsCancellationRequested && _options.ShouldAbort(_clientId))
                        connection.Abort();

                    _metrics.OnDisconnect();
                }

                SetState(ClientState.Closed);
            }

            if (_options.ReconnectPercent <= 0)
                break;
        }
    }

    private async Task RunTcpLoopAsync(ILoadTestConnection connection, CancellationToken cancellationToken)
    {
        var sequence = 0;
        var receiveState = new TcpReceiveState(1024 * 1024);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_options.Scenario == "reconnect-storm" && ElapsedTime() >= ReconnectStormScenario.DisconnectAt(_clientId, _options))
            {
                SetState(ClientState.Reconnecting);
                break;
            }

            var requests = new List<TcpOperation>(capacity: _options.CoalescedPacket ? 2 : 1);
            requests.Add(CreateTcpOperation(++sequence));
            if (_options.CoalescedPacket)
                requests.Add(CreateTcpOperation(++sequence));

            var encodedPackets = requests
                .Select(request => BinaryPacket.Encode(request.Packet.PacketId, request.Packet.Value1, request.Packet.Body))
                .ToArray();
            var payload = new byte[encodedPackets.Sum(packet => packet.Length)];
            var offset = 0;
            foreach (var encodedPacket in encodedPackets)
            {
                encodedPacket.CopyTo(payload.AsSpan(offset));
                offset += encodedPacket.Length;
            }

            var startedMs = ElapsedMs();
            var started = Stopwatch.GetTimestamp();

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

                foreach (var request in requests)
                {
                    await DelayBeforeReceiveAsync(timeout: false, cancellationToken).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_options.ReceiveTimeout);
                    var response = await ReceiveBinaryResponseAsync(connection, receiveState, timeout.Token).ConfigureAwait(false);
                    var endedMs = ElapsedMs();
                    var rttUs = (long)((Stopwatch.GetTimestamp() - started) * 1_000_000.0 / Stopwatch.Frequency);
                    _metrics.OnReceive(response.BytesRead, rttUs);
                    WriteOperation(endedMs, request.OperationId, request.OperationType, request.Packet.PacketId, request.Packet.Body.Length, startedMs, endedMs, rttUs, true, string.Empty, string.Empty);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _metrics.OnTimeout();
                var endedMs = ElapsedMs();
                foreach (var request in requests)
                    WriteOperation(endedMs, request.OperationId, request.OperationType, request.Packet.PacketId, request.Packet.Body.Length, startedMs, endedMs, 0, false, "timeout", string.Empty);
            }
            catch (Exception ex)
            {
                // 실행 시간이 끝나 소켓이 정리되는 중이라면 장애가 아니다. 오류율에 넣지 않는다.
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                var endedMs = ElapsedMs();
                _metrics.OnSendFail();
                _metrics.OnSocketError();
                foreach (var request in requests)
                    WriteOperation(endedMs, request.OperationId, request.OperationType, request.Packet.PacketId, request.Packet.Body.Length, startedMs, endedMs, 0, false, ex.GetType().Name, ex.Message);
                throw;
            }

            await Task.Delay(LoadScenario.NextThinkTime(_options, _random), cancellationToken).ConfigureAwait(false);
        }
    }

    private TcpOperation CreateTcpOperation(int sequence)
    {
        var (operationType, packet) = CreateNextTcpPacket(sequence);
        return new TcpOperation(operationType, packet, _writers.NextOperationId());
    }

    private static async Task<TcpResponse> ReceiveBinaryResponseAsync(ILoadTestConnection connection, TcpReceiveState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (BinaryPacket.TryDecode(state.Buffer.AsSpan(0, state.Length), out var packet, out var consumed))
            {
                var remaining = state.Length - consumed;
                if (remaining > 0)
                    state.Buffer.AsSpan(consumed, remaining).CopyTo(state.Buffer);
                state.Length = remaining;
                return new TcpResponse(packet!, consumed);
            }

            if (state.Length == state.Buffer.Length)
                throw new InvalidOperationException("TCP response buffer is full before a complete packet was decoded.");

            var read = await connection.ReceiveAsync(state.Buffer.AsMemory(state.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new IOException("TCP connection closed before a complete response was received.");

            state.Length += read;
        }
    }

    private async Task RunUdpLoopAsync(ILoadTestConnection connection, CancellationToken cancellationToken)
    {
        var sequence = 0;
        var receiveBuffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested)
        {
            sequence++;
            var operationId = _writers.NextOperationId();
            var payloadText = $"{_clientId:D8}:{sequence:D8}";
            var payload = UdpEchoScenario.Encode("ECHO", _udpSessionId, payloadText);
            var startedMs = ElapsedMs();
            var started = Stopwatch.GetTimestamp();

            if (_options.UdpLossPercent > 0 && _random.NextDouble() * 100.0 < _options.UdpLossPercent)
            {
                _metrics.OnSendFail();
                WriteOperation(startedMs, operationId, "udp-loss", 0, payload.Length, startedMs, startedMs, 0, false, "simulated_loss", string.Empty);
                await Task.Delay(LoadScenario.NextThinkTime(_options, _random), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await connection.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                _metrics.OnSendSuccess(payload.Length);

                await DelayBeforeReceiveAsync(timeout: false, cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ReceiveTimeout);
                var read = await connection.ReceiveAsync(receiveBuffer, timeout.Token).ConfigureAwait(false);
                var endedMs = ElapsedMs();
                var rttUs = (long)((Stopwatch.GetTimestamp() - started) * 1_000_000.0 / Stopwatch.Frequency);
                _metrics.OnReceive(read, rttUs);
                WriteOperation(endedMs, operationId, "udp-echo", 0, payload.Length, startedMs, endedMs, rttUs, true, string.Empty, string.Empty);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _metrics.OnTimeout();
                var endedMs = ElapsedMs();
                WriteOperation(endedMs, operationId, "udp-echo", 0, payload.Length, startedMs, endedMs, 0, false, "timeout", string.Empty);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                _metrics.OnSendFail();
                _metrics.OnSocketError();
                var endedMs = ElapsedMs();
                WriteOperation(endedMs, operationId, "udp-echo", 0, payload.Length, startedMs, endedMs, 0, false, ex.GetType().Name, ex.Message);
                throw;
            }

            await Task.Delay(LoadScenario.NextThinkTime(_options, _random), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunTextLineLoopAsync(ILoadTestConnection connection, CancellationToken cancellationToken)
    {
        var sequence = 0;
        var receiveState = new TextLineReceiveState(8192);

        while (!cancellationToken.IsCancellationRequested)
        {
            sequence++;
            var operationId = _writers.NextOperationId();
            var line = $"PING {ElapsedMs()} {sequence}\r\n";
            var payload = System.Text.Encoding.UTF8.GetBytes(line);
            var startedMs = ElapsedMs();
            var started = Stopwatch.GetTimestamp();

            try
            {
                await connection.SendAsync(payload, cancellationToken).ConfigureAwait(false);
                _metrics.OnSendSuccess(payload.Length);

                await DelayBeforeReceiveAsync(timeout: false, cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ReceiveTimeout);
                var responseBytes = await ReceiveTextLineAsync(connection, receiveState, timeout.Token).ConfigureAwait(false);
                var endedMs = ElapsedMs();
                var rttUs = (long)((Stopwatch.GetTimestamp() - started) * 1_000_000.0 / Stopwatch.Frequency);
                _metrics.OnReceive(responseBytes, rttUs);
                WriteOperation(endedMs, operationId, "text-ping", 0, payload.Length, startedMs, endedMs, rttUs, true, string.Empty, string.Empty);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _metrics.OnTimeout();
                var endedMs = ElapsedMs();
                WriteOperation(endedMs, operationId, "text-ping", 0, payload.Length, startedMs, endedMs, 0, false, "timeout", string.Empty);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                _metrics.OnSendFail();
                _metrics.OnSocketError();
                var endedMs = ElapsedMs();
                WriteOperation(endedMs, operationId, "text-ping", 0, payload.Length, startedMs, endedMs, 0, false, ex.GetType().Name, ex.Message);
                throw;
            }

            await Task.Delay(LoadScenario.NextThinkTime(_options, _random), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReceiveTextLineAsync(ILoadTestConnection connection, TextLineReceiveState state, CancellationToken cancellationToken)
    {
        while (true)
        {
            for (var i = 0; i < state.Length; i++)
            {
                if (state.Buffer[i] != (byte)'\n')
                    continue;

                var consumed = i + 1;
                var remaining = state.Length - consumed;
                if (remaining > 0)
                    state.Buffer.AsSpan(consumed, remaining).CopyTo(state.Buffer);
                state.Length = remaining;
                return consumed;
            }

            if (state.Length == state.Buffer.Length)
                throw new InvalidOperationException("Text-line response buffer is full before a newline was received.");

            var read = await connection.ReceiveAsync(state.Buffer.AsMemory(state.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new IOException("TCP connection closed before a text-line response was received.");

            state.Length += read;
        }
    }

    private (string OperationType, BinaryPacket Packet) CreateNextTcpPacket(int sequence)
    {
        if (_options.Scenario == "game-like" || _options.Protocol == "game-binary")
        {
            var operation = _gameScenario.NextOperation(_clientId, sequence, _options);
            return (operation.OperationType, operation.Packet);
        }

        var echo = new EchoBinaryScenario();
        return ("echo", echo.CreateRequest(_clientId, sequence, _options));
    }

    private ILoadTestConnection CreateConnection()
    {
        return _options.Transport switch
        {
            "udp" => new UdpConnection(_options.Host, _options.Port),
            "text" => new TextLineConnection(_options.Host, _options.Port),
            _ => new TcpBinaryConnection(_options.Host, _options.Port)
        };
    }

    private void SetState(ClientState newState)
    {
        var old = _state;
        _state = newState;
        _metrics.SetState(old, newState);
    }

    private async Task DelayBeforeReceiveAsync(bool timeout, CancellationToken cancellationToken)
    {
        if (_options.SlowReceiverDelay > TimeSpan.Zero)
            await Task.Delay(_options.SlowReceiverDelay, cancellationToken).ConfigureAwait(false);
    }

    private void WriteOperation(long elapsedMs, long operationId, string operationType, int packetId, int payloadBytes, long sendStartMs, long responseEndMs, long rttUs, bool success, string errorType, string socketError)
    {
        if (_options.OperationSampling <= 0)
            return;

        if (_options.OperationSampling < 1.0 && _random.NextDouble() > _options.OperationSampling)
            return;

        _writers.WriteOperation(_options.RunId, elapsedMs, _clientId, operationId, operationType, packetId, payloadBytes, sendStartMs, responseEndMs, rttUs, success, errorType, socketError);
    }

    private long ElapsedMs()
    {
        return Math.Max(0, Environment.TickCount64 - _runStartMs);
    }

    private TimeSpan ElapsedTime()
    {
        return TimeSpan.FromMilliseconds(ElapsedMs());
    }

    private static Guid DeterministicGuid(int clientId, string runId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{runId}:{clientId}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record TcpOperation(string OperationType, BinaryPacket Packet, long OperationId);

    private sealed record TcpResponse(BinaryPacket Packet, int BytesRead);

    private sealed class TcpReceiveState
    {
        public TcpReceiveState(int capacity)
        {
            Buffer = new byte[capacity];
        }

        public byte[] Buffer { get; }
        public int Length { get; set; }
    }

    private sealed class TextLineReceiveState
    {
        public TextLineReceiveState(int capacity)
        {
            Buffer = new byte[capacity];
        }

        public byte[] Buffer { get; }
        public int Length { get; set; }
    }
}
