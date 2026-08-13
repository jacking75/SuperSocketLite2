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

    private long _totalBytesReceived = 0;
    private long _totalBytesSent = 0;

    private KeyValuePair<string, object?> ServerTag => new("server", Name);

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
