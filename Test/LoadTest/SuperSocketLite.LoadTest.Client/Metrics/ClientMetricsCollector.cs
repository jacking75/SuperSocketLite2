using SuperSocketLite.LoadTest.Shared.Metrics;

namespace SuperSocketLite.LoadTest.Client.Metrics;

public sealed class ClientMetricsCollector
{
    private readonly LatencyHistogram _rtt = new();

    /// <summary>
    /// 예정 시각보다 늦게 나간 송신의 지연 분포입니다.
    /// 서버가 느린 것과 클라이언트가 부하를 내지 못하는 것을 구분하는 근거입니다.
    /// </summary>
    private readonly LatencyHistogram _scheduleDelay = new();
    private long _sendSkippedInFlight;
    private long _inFlight;
    private long _maxInFlightObserved;
    private long _localResourceExhaustion;
    private long _activeClients;
    private long _connectingClients;
    private long _connectedClients;
    private long _closedClients;
    private long _reconnectingClients;
    private long _totalConnectSuccess;
    private long _totalConnectFail;
    private long _totalDisconnect;
    private long _totalSendSuccess;
    private long _totalSendFail;
    private long _totalReceive;
    private long _totalTimeout;
    private long _bytesSent;
    private long _bytesReceived;
    private long _socketErrorTotal;
    private long _protocolErrorTotal;
    private long _runtimeErrorTotal;
    private long _outageTotal;
    private long _reconnectTotal;
    private long _maxOutageMs;
    private readonly object _rateLock = new();
    private long _lastRateElapsedMs;
    private long _lastRateSendSuccess;
    private long _lastRateReceive;
    private long _lastRateBytesSent;
    private long _lastRateBytesReceived;

    public void SetState(ClientState oldState, ClientState newState)
    {
        ApplyState(oldState, -1);
        ApplyState(newState, 1);
    }

    public void OnConnectSuccess() => Interlocked.Increment(ref _totalConnectSuccess);
    public void OnConnectFail() => Interlocked.Increment(ref _totalConnectFail);
    public void OnDisconnect() => Interlocked.Increment(ref _totalDisconnect);
    public void OnSendSuccess(long bytes) { Interlocked.Increment(ref _totalSendSuccess); Interlocked.Add(ref _bytesSent, bytes); }
    public void OnSendFail() => Interlocked.Increment(ref _totalSendFail);
    public void OnReceive(long bytes, long rttUs) { Interlocked.Increment(ref _totalReceive); Interlocked.Add(ref _bytesReceived, bytes); _rtt.Record(rttUs); }
    public void OnTimeout() => Interlocked.Increment(ref _totalTimeout);

    /// <summary>예정보다 늦게 나간 송신을 기록합니다. 제때 나갔으면 0을 기록합니다.</summary>
    public void OnScheduleDelay(long delayUs) => _scheduleDelay.Record(Math.Max(0, delayUs));

    /// <summary>동시 요청 한도에 걸려 보내지 못한 송신을 기록합니다.</summary>
    public void OnSendSkipped() => Interlocked.Increment(ref _sendSkippedInFlight);

    /// <summary>
    /// 서버가 아니라 부하 발생기 쪽 자원이 바닥나 실패한 연결을 기록합니다.
    /// 임시 포트 고갈 같은 상황을 서버 문제로 오해하지 않기 위한 것입니다.
    /// </summary>
    public void OnLocalResourceExhaustion() => Interlocked.Increment(ref _localResourceExhaustion);

    public long LocalResourceExhaustion => Volatile.Read(ref _localResourceExhaustion);

    public void OnRequestStarted()
    {
        var current = Interlocked.Increment(ref _inFlight);
        var observed = Volatile.Read(ref _maxInFlightObserved);
        while (current > observed)
        {
            var previous = Interlocked.CompareExchange(ref _maxInFlightObserved, current, observed);
            if (previous == observed)
                break;

            observed = previous;
        }
    }

    public void OnRequestCompleted() => Interlocked.Decrement(ref _inFlight);

    /// <summary>실행 시작부터 누적된 송신 지연 분포입니다.</summary>
    public HistogramSnapshot TotalScheduleDelay => _scheduleDelay.SnapshotTotal();

    public long SendSkippedInFlight => Volatile.Read(ref _sendSkippedInFlight);
    public long MaxInFlightObserved => Volatile.Read(ref _maxInFlightObserved);
    /// <summary>
    /// 연결이 예기치 않게 끊긴 것을 기록합니다.
    /// 실행 종료로 인한 정상 종료는 여기 들어오지 않습니다.
    /// </summary>
    public void OnOutageStarted() => Interlocked.Increment(ref _outageTotal);

    /// <summary>끊긴 뒤 다시 붙는 데 성공한 것을 기록합니다.</summary>
    public void OnReconnected() => Interlocked.Increment(ref _reconnectTotal);

    /// <summary>
    /// 끊긴 시각부터 응답을 다시 받기까지 걸린 시간을 기록합니다.
    /// 접속이 아니라 응답을 기준으로 삼습니다. 서버가 리슨을 다시 열었어도
    /// 요청을 처리하기 전이라면 아직 회복이 아니기 때문입니다.
    /// </summary>
    public void OnRecovered(long outageMs)
    {
        var observed = Volatile.Read(ref _maxOutageMs);
        while (outageMs > observed)
        {
            var previous = Interlocked.CompareExchange(ref _maxOutageMs, outageMs, observed);
            if (previous == observed)
                break;

            observed = previous;
        }
    }

    public long OutageTotal => Volatile.Read(ref _outageTotal);
    public long ReconnectTotal => Volatile.Read(ref _reconnectTotal);
    public long MaxOutageMs => Volatile.Read(ref _maxOutageMs);

    public void OnSocketError() => Interlocked.Increment(ref _socketErrorTotal);
    public void OnProtocolError() => Interlocked.Increment(ref _protocolErrorTotal);
    public void OnRuntimeError() => Interlocked.Increment(ref _runtimeErrorTotal);

    /// <summary>실행 시작부터 누적된 RTT 분포입니다. 창 스냅샷과 달리 초기화되지 않습니다.</summary>
    public HistogramSnapshot TotalLatency => _rtt.SnapshotTotal();

    /// <summary>현재 접속을 유지 중인 클라이언트 수입니다.</summary>
    public long ActiveClients => Volatile.Read(ref _activeClients);

    public ClientMetricsSnapshot Snapshot(string runId, long elapsedMs, bool resetLatency, string phase = "unknown")
    {
        var latency = _rtt.Snapshot(resetLatency);
        var totalSendSuccess = Volatile.Read(ref _totalSendSuccess);
        var totalReceive = Volatile.Read(ref _totalReceive);
        var bytesSent = Volatile.Read(ref _bytesSent);
        var bytesReceived = Volatile.Read(ref _bytesReceived);
        var sendPerSec = 0.0;
        var receivePerSec = 0.0;
        var bytesSentPerSec = 0.0;
        var bytesReceivedPerSec = 0.0;

        lock (_rateLock)
        {
            var deltaMs = elapsedMs - _lastRateElapsedMs;
            if (deltaMs > 0)
            {
                sendPerSec = (totalSendSuccess - _lastRateSendSuccess) * 1000.0 / deltaMs;
                receivePerSec = (totalReceive - _lastRateReceive) * 1000.0 / deltaMs;
                bytesSentPerSec = (bytesSent - _lastRateBytesSent) * 1000.0 / deltaMs;
                bytesReceivedPerSec = (bytesReceived - _lastRateBytesReceived) * 1000.0 / deltaMs;
            }

            _lastRateElapsedMs = elapsedMs;
            _lastRateSendSuccess = totalSendSuccess;
            _lastRateReceive = totalReceive;
            _lastRateBytesSent = bytesSent;
            _lastRateBytesReceived = bytesReceived;
        }

        return new ClientMetricsSnapshot(
            DateTimeOffset.UtcNow,
            elapsedMs,
            runId,
            Volatile.Read(ref _activeClients),
            Volatile.Read(ref _connectingClients),
            Volatile.Read(ref _connectedClients),
            Volatile.Read(ref _closedClients),
            Volatile.Read(ref _reconnectingClients),
            Volatile.Read(ref _totalConnectSuccess),
            Volatile.Read(ref _totalConnectFail),
            Volatile.Read(ref _totalDisconnect),
            totalSendSuccess,
            Volatile.Read(ref _totalSendFail),
            totalReceive,
            Volatile.Read(ref _totalTimeout),
            sendPerSec,
            receivePerSec,
            bytesSentPerSec,
            bytesReceivedPerSec,
            latency.P50Us,
            latency.P95Us,
            latency.P99Us,
            latency.MaxUs,
            Volatile.Read(ref _socketErrorTotal),
            Volatile.Read(ref _protocolErrorTotal),
            Volatile.Read(ref _runtimeErrorTotal),
            phase);
    }

    private void ApplyState(ClientState state, int delta)
    {
        switch (state)
        {
            case ClientState.Connecting:
                Interlocked.Add(ref _connectingClients, delta);
                break;
            case ClientState.Connected:
            case ClientState.Login:
            case ClientState.Active:
            case ClientState.Idle:
                Interlocked.Add(ref _connectedClients, delta);
                Interlocked.Add(ref _activeClients, delta);
                break;
            case ClientState.Closed:
                Interlocked.Add(ref _closedClients, delta);
                break;
            case ClientState.Reconnecting:
                Interlocked.Add(ref _reconnectingClients, delta);
                break;
        }
    }
}

public sealed record ClientMetricsSnapshot(
    DateTimeOffset TimestampUtc,
    long ElapsedMs,
    string RunId,
    long ActiveClients,
    long ConnectingClients,
    long ConnectedClients,
    long ClosedClients,
    long ReconnectingClients,
    long TotalConnectSuccess,
    long TotalConnectFail,
    long TotalDisconnect,
    long TotalSendSuccess,
    long TotalSendFail,
    long TotalReceive,
    long TotalTimeout,
    double SendPerSec,
    double ReceivePerSec,
    double BytesSentPerSec,
    double BytesReceivedPerSec,
    long RttP50Us,
    long RttP95Us,
    long RttP99Us,
    long RttMaxUs,
    long SocketErrorTotal,
    long ProtocolErrorTotal,
    long RuntimeErrorTotal,
    string Phase);
