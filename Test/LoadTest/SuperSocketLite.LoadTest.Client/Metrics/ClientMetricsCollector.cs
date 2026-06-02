using SuperSocketLite.LoadTest.Shared.Metrics;

namespace SuperSocketLite.LoadTest.Client.Metrics;

public sealed class ClientMetricsCollector
{
    private readonly LatencyHistogram _rtt = new();
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
    public void OnSocketError() => Interlocked.Increment(ref _socketErrorTotal);
    public void OnProtocolError() => Interlocked.Increment(ref _protocolErrorTotal);
    public void OnRuntimeError() => Interlocked.Increment(ref _runtimeErrorTotal);

    public ClientMetricsSnapshot Snapshot(string runId, long elapsedMs, bool resetLatency)
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
            Volatile.Read(ref _runtimeErrorTotal));
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
    long RuntimeErrorTotal);
