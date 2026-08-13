using System.Diagnostics;
using SuperSocketLite.LoadTest.Shared.Metrics;

namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class ServerMetricsCollector : IDisposable
{
    private readonly ServerMetricsOptions _options;
    private readonly ProcessMetricReader _processMetricReader = new();
    private readonly ServerMetricCsvWriter _writer;
    private readonly LatencyHistogram _handlerLatency = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly object _rateLock = new();
    private long _totalConnected;
    private long _totalClosed;
    private long _totalRequests;
    private long _totalBytesIn;
    private long _totalBytesOut;
    private long _sendFailTotal;
    private long _exceptionTotal;
    private long _protocolErrorTotal;
    private long _droppedMetricRows;
    private long _lastRateElapsedMs;
    private long _lastRateRequests;
    private long _lastRateBytesIn;
    private long _lastRateBytesOut;
    private long _phasePreviousActive = -1;
    private long _phasePeakActive;

    private ServerMetricsCollector(ServerMetricsOptions options)
    {
        _options = options;
        _writer = new ServerMetricCsvWriter(options.OutputDirectory, options.WriterChannelCapacity, options.AutoStartWriter);
    }

    public static ServerMetricsCollector Create(ServerMetricsOptions options)
    {
        if (options.SampleInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "SampleInterval must be greater than zero.");
        if (options.RequestEventSampling is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "RequestEventSampling must be between 0.0 and 1.0.");

        return new ServerMetricsCollector(options);
    }

    /// <summary>
    /// 현재 열려 있는 세션 수입니다.
    /// 별도 카운터를 증감하지 않고 두 누적 카운터의 차이로 구합니다.
    /// 접속과 종료 이벤트는 서로 다른 스레드에서 오므로 순서가 뒤바뀔 수 있는데,
    /// 그때 증감식 카운터는 값이 영구히 어긋납니다.
    /// 실제로 재접속이 잦은 시나리오에서 접속 50건·종료 50건인데 활성 2건으로 남는 일이 있었습니다.
    /// </summary>
    public long ActiveSessions => Volatile.Read(ref _totalConnected) - Volatile.Read(ref _totalClosed);

    public void OnConnected(object? session, string? remoteEndpoint = null)
    {
        Interlocked.Increment(ref _totalConnected);
        TryWriteEvent("connect", SessionId(session), remoteEndpoint ?? string.Empty, 0, 0, 0, string.Empty, string.Empty, string.Empty);
    }

    public void OnClosed(object? session, string closeReason, string? remoteEndpoint = null)
    {
        Interlocked.Increment(ref _totalClosed);
        TryWriteEvent("close", SessionId(session), remoteEndpoint ?? string.Empty, 0, 0, 0, closeReason, string.Empty, string.Empty);
    }

    public void OnException(object? session, Exception exception)
    {
        Interlocked.Increment(ref _exceptionTotal);
        TryWriteEvent("error", SessionId(session), string.Empty, 0, 0, 0, string.Empty, exception.GetType().Name, exception.Message);
    }

    public void OnProtocolError(object? session, string message)
    {
        Interlocked.Increment(ref _protocolErrorTotal);
        TryWriteEvent("protocol_error", SessionId(session), string.Empty, 0, 0, 0, string.Empty, "ProtocolError", message);
    }

    public void OnSendFailed(object? session, long bytesOut, string message)
    {
        Interlocked.Increment(ref _sendFailTotal);
        TryWriteEvent("send_fail", SessionId(session), string.Empty, 0, 0, bytesOut, string.Empty, "SendFail", message);
    }

    public void OnBytesOut(long bytesOut)
    {
        if (bytesOut > 0)
            Interlocked.Add(ref _totalBytesOut, bytesOut);
    }

    public RequestMetricRecorder BeginRequest(object? session, int packetId, long bytesIn)
    {
        return new RequestMetricRecorder(this, SessionId(session), packetId, bytesIn);
    }

    internal void RecordRequest(string sessionId, int packetId, long bytesIn, long elapsedUs)
    {
        var requestOrdinal = Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalBytesIn, bytesIn);
        _handlerLatency.Record(elapsedUs);

        if (ShouldWriteRequestEvent(requestOrdinal))
            TryWriteEvent("request", sessionId, string.Empty, packetId, bytesIn, 0, string.Empty, string.Empty, string.Empty);
    }

    private bool ShouldWriteRequestEvent(long requestOrdinal)
    {
        if (_options.RequestEventSampling <= 0)
            return false;
        if (_options.RequestEventSampling >= 1.0)
            return true;

        var interval = Math.Max(1, (long)Math.Round(1.0 / _options.RequestEventSampling, MidpointRounding.AwayFromZero));
        return requestOrdinal % interval == 0;
    }

    public ServerMetricsSnapshot CaptureSnapshot(bool resetLatency)
    {
        var now = DateTimeOffset.UtcNow;
        var process = _processMetricReader.Read();
        var latency = _handlerLatency.Snapshot(resetLatency);
        var elapsedMs = _elapsed.ElapsedMilliseconds;
        var activeSessions = ActiveSessions;
        var phase = DeterminePhase(activeSessions);
        var totalRequests = Volatile.Read(ref _totalRequests);
        var totalBytesIn = Volatile.Read(ref _totalBytesIn);
        var totalBytesOut = Volatile.Read(ref _totalBytesOut);

        var requestsPerSec = 0.0;
        var bytesInPerSec = 0.0;
        var bytesOutPerSec = 0.0;

        lock (_rateLock)
        {
            var deltaMs = elapsedMs - _lastRateElapsedMs;
            if (deltaMs > 0)
            {
                requestsPerSec = (totalRequests - _lastRateRequests) * 1000.0 / deltaMs;
                bytesInPerSec = (totalBytesIn - _lastRateBytesIn) * 1000.0 / deltaMs;
                bytesOutPerSec = (totalBytesOut - _lastRateBytesOut) * 1000.0 / deltaMs;
            }

            _lastRateElapsedMs = elapsedMs;
            _lastRateRequests = totalRequests;
            _lastRateBytesIn = totalBytesIn;
            _lastRateBytesOut = totalBytesOut;
        }

        return new ServerMetricsSnapshot(
            now,
            elapsedMs,
            _options.RunId,
            _options.ServerName,
            process.ProcessId,
            activeSessions,
            Volatile.Read(ref _totalConnected),
            Volatile.Read(ref _totalClosed),
            totalRequests,
            requestsPerSec,
            totalBytesIn,
            bytesInPerSec,
            totalBytesOut,
            bytesOutPerSec,
            Volatile.Read(ref _sendFailTotal),
            Volatile.Read(ref _exceptionTotal),
            Volatile.Read(ref _protocolErrorTotal),
            process.GcGen0Total,
            process.GcGen1Total,
            process.GcGen2Total,
            process.GcGen0Delta,
            process.GcGen1Delta,
            process.GcGen2Delta,
            process.GcHeapBytes,
            process.WorkingSetBytes,
            process.PrivateMemoryBytes,
            process.ThreadCount,
            process.ThreadPoolWorkerAvailable,
            process.ThreadPoolWorkerMax,
            process.ThreadPoolIoAvailable,
            process.ThreadPoolIoMax,
            process.CpuPercent,
            latency.P50Us,
            latency.P95Us,
            latency.P99Us,
            latency.MaxUs,
            Volatile.Read(ref _droppedMetricRows),
            phase);
    }

    /// <summary>
    /// 활성 세션 수의 변화로 구간을 판정합니다.
    /// 서버는 클라이언트의 실행 계획을 모르므로 관측 가능한 세션 수만으로 추정합니다.
    /// 재접속처럼 작은 흔들림이 정상 구간을 깨지 않도록 최고치의 2%를 문턱으로 둡니다.
    /// </summary>
    private string DeterminePhase(long activeSessions)
    {
        var previous = _phasePreviousActive;
        _phasePreviousActive = activeSessions;

        if (activeSessions > _phasePeakActive)
            _phasePeakActive = activeSessions;

        if (activeSessions == 0)
            return "idle";

        if (previous < 0)
            return "rampup";

        var threshold = Math.Max(1, _phasePeakActive / 50);
        var delta = activeSessions - previous;

        if (delta >= threshold)
            return "rampup";
        if (delta <= -threshold)
            return "rampdown";

        return "steady";
    }

    public IDisposable Start()
    {
        return new ServerMetricsHostedLoop(this, _options.SampleInterval);
    }

    internal void WriteSample()
    {
        if (!_writer.TryWriteSample(CaptureSnapshot(resetLatency: true)))
            Interlocked.Increment(ref _droppedMetricRows);
    }

    /// <summary>
    /// 버퍼에 남은 행을 파일로 내보냅니다.
    /// 샘플을 새로 만들지는 않습니다. 종료 경로에서 여러 번 불려도 표본 주기가 깨지지 않아야 하기 때문입니다.
    /// 마지막 샘플은 <see cref="ServerMetricsHostedLoop"/>가 종료할 때 한 번만 기록합니다.
    /// </summary>
    public void Flush()
    {
        _writer.Flush();
    }

    private void TryWriteEvent(
        string eventType,
        string sessionId,
        string remoteEndpoint,
        int packetId,
        long bytesIn,
        long bytesOut,
        string closeReason,
        string errorType,
        string message)
    {
        var metricEvent = new ServerMetricEvent(
            DateTimeOffset.UtcNow,
            _elapsed.ElapsedMilliseconds,
            _options.RunId,
            eventType,
            sessionId,
            remoteEndpoint,
            packetId,
            bytesIn,
            bytesOut,
            closeReason,
            errorType,
            message);

        if (!_writer.TryWriteEvent(metricEvent))
            Interlocked.Increment(ref _droppedMetricRows);
    }

    private static string SessionId(object? session)
    {
        return session?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}
