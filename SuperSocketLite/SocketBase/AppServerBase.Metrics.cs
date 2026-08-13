using System.Diagnostics.Metrics;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;
/// <summary>계측: System.Diagnostics.Metrics 계기와 누적 카운터.</summary>

public abstract partial class AppServerBase<TAppSession, TRequestInfo>
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
    // Metrics
    private static readonly Meter s_Meter = new("SuperSocketLite");
    private static readonly Counter<long> s_TotalRequestsCounter = s_Meter.CreateCounter<long>("total-requests", "requests", "Total number of requests received");
    private static readonly Counter<long> s_TotalBytesReceivedCounter = s_Meter.CreateCounter<long>("total-bytes-received", "bytes", "Total bytes received");
    private static readonly Counter<long> s_TotalBytesSentCounter = s_Meter.CreateCounter<long>("total-bytes-sent", "bytes", "Total bytes sent");
    private static readonly Counter<long> s_SessionsRejectedCounter = s_Meter.CreateCounter<long>("sessions-rejected", "connections", "Connections refused because the connection limit was reached");
    private static readonly Counter<long> s_SendQueueFullCounter = s_Meter.CreateCounter<long>("send-queue-full", "sends", "Sends dropped because the session's sending queue was full");
    private static readonly Counter<long> s_SendErrorsCounter = s_Meter.CreateCounter<long>("send-errors", "sends", "Sends that failed with a socket error");
    private static readonly Histogram<double> s_RequestDurationHistogram = s_Meter.CreateHistogram<double>("request-duration", "ms", "Time spent in the request handler");
    private static UpDownCounter<int>? s_ActiveConnectionsCounter;

    // Registered once per server instance so that "session-count" reports the live session count.
    private ObservableGauge<int>? _sessionCountGauge;
    private ObservableGauge<int>? _sendQueueDepthTotalGauge;
    private ObservableGauge<int>? _sendQueueDepthMaxGauge;
    private ObservableGauge<int>? _receivePoolAvailableGauge;
    private ObservableGauge<int>? _receivePoolTotalGauge;
    private ObservableGauge<int>? _sendPoolAvailableGauge;
    private ObservableGauge<int>? _sendPoolTotalGauge;

    private long _totalBytesReceived = 0;
    private long _totalBytesSent = 0;

    private KeyValuePair<string, object?> ServerTag => new("server", Name);

    /// <summary>
    /// Registers the instruments that need a live server instance.
    /// The gauges are observable, so nothing is computed unless a collector is listening.
    /// </summary>
    private void RegisterMetrics()
    {
        s_ActiveConnectionsCounter ??= s_Meter.CreateUpDownCounter<int>(
            "active-connections", "connections", "Number of active connections");

        _sessionCountGauge ??= s_Meter.CreateObservableGauge(
            "session-count", () => new Measurement<int>(SessionCount, ServerTag), "sessions", "Number of sessions currently registered");

        _sendQueueDepthTotalGauge ??= s_Meter.CreateObservableGauge(
            "send-queue-depth-total", () => ObserveSendQueueDepth(reportMax: false), "items", "Send requests waiting across all session sending queues");

        _sendQueueDepthMaxGauge ??= s_Meter.CreateObservableGauge(
            "send-queue-depth-max", () => ObserveSendQueueDepth(reportMax: true), "items", "Send requests waiting in the busiest session's sending queue");

        _receivePoolAvailableGauge ??= s_Meter.CreateObservableGauge(
            "receive-saea-pool-available", () => ObservePoolUsage(static usage => usage.ReceiveAvailable), "items", "Receive SocketAsyncEventArgs left in the pool");

        _receivePoolTotalGauge ??= s_Meter.CreateObservableGauge(
            "receive-saea-pool-total", () => ObservePoolUsage(static usage => usage.ReceiveTotal), "items", "Receive SocketAsyncEventArgs created so far");

        _sendPoolAvailableGauge ??= s_Meter.CreateObservableGauge(
            "send-saea-pool-available", () => ObservePoolUsage(static usage => usage.SendAvailable), "items", "Send SocketAsyncEventArgs left in the pool");

        _sendPoolTotalGauge ??= s_Meter.CreateObservableGauge(
            "send-saea-pool-total", () => ObservePoolUsage(static usage => usage.SendTotal), "items", "Send SocketAsyncEventArgs created so far");
    }

    /// <summary>
    /// The pending send load across sessions.
    /// Servers that cannot enumerate their sessions return null, and the gauge then reports nothing
    /// rather than a misleading zero.
    /// </summary>
    private protected virtual (int Total, int Max)? CollectSendQueueDepth() => null;

    private IEnumerable<Measurement<int>> ObserveSendQueueDepth(bool reportMax)
    {
        if (!IsObservable || CollectSendQueueDepth() is not { } depth)
            yield break;

        yield return new Measurement<int>(reportMax ? depth.Max : depth.Total, ServerTag);
    }

    private IEnumerable<Measurement<int>> ObservePoolUsage(Func<SocketEngine.SocketAsyncEventArgsPoolUsage, int> selector)
    {
        if (!IsObservable || (_socketServer as SocketEngine.SocketServerBase)?.GetPoolUsage() is not { } usage)
            yield break;

        yield return new Measurement<int>(selector(usage), ServerTag);
    }

    /// <summary>
    /// Whether this instance should still report runtime state.
    /// Instruments live as long as the Meter, so a stopped server keeps being polled; without this
    /// guard its idle queues and pools would be folded into the numbers of the server that replaced it.
    /// </summary>
    private bool IsObservable => State == ServerState.Running;

    /// <summary>Records bytes received for metrics.</summary>
    /// <param name="count">The number of bytes received.</param>
    public void RecordBytesReceived(int count)
    {
        Interlocked.Add(ref _totalBytesReceived, count);
        s_TotalBytesReceivedCounter.Add(count, ServerTag);
    }

    /// <summary>Records bytes sent for metrics.</summary>
    /// <param name="count">The number of bytes sent.</param>
    public void RecordBytesSent(int count)
    {
        Interlocked.Add(ref _totalBytesSent, count);
        s_TotalBytesSentCounter.Add(count, ServerTag);
    }

    /// <summary>Records a connection that was refused because the connection limit was reached.</summary>
    public void RecordSessionRejected()
    {
        Interlocked.Increment(ref _totalSessionsRejected);
        s_SessionsRejectedCounter.Add(1, ServerTag);
    }

    /// <summary>Records a send that was dropped because the session's sending queue was full.</summary>
    public void RecordSendQueueFull()
    {
        Interlocked.Increment(ref _totalSendQueueFull);
        s_SendQueueFullCounter.Add(1, ServerTag);
    }

    /// <summary>Records a failed send.</summary>
    public void RecordSendError()
    {
        Interlocked.Increment(ref _totalSendErrors);
        s_SendErrorsCounter.Add(1, ServerTag);
    }

    private long _totalSessionsRejected = 0;
    private long _totalSendQueueFull = 0;
    private long _totalSendErrors = 0;

    /// <summary>Gets the total bytes received.</summary>
    public long TotalBytesReceived => _totalBytesReceived;

    /// <summary>Gets the total bytes sent.</summary>
    public long TotalBytesSent => _totalBytesSent;

    /// <summary>Gets the number of connections refused because the connection limit was reached.</summary>
    public long TotalSessionsRejected => _totalSessionsRejected;

    /// <summary>Gets the number of sends dropped because the sending queue was full.</summary>
    public long TotalSendQueueFull => _totalSendQueueFull;

    /// <summary>Gets the number of sends that failed with a socket error.</summary>
    public long TotalSendErrors => _totalSendErrors;

}
