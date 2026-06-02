using SuperSocketLite.LoadTest.ServerProbe;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.LoadTest.Server;

public sealed class LoadTestServer : AppServer<LoadTestSession, LoadTestRequestInfo>, IDisposable
{
    private ServerMetricsCollector? _metrics;
    private IDisposable? _metricsLoop;

    public LoadTestServer()
        : base(new DefaultReceiveFilterFactory<ReceiveFilter, LoadTestRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    public bool Configure(LoadTestServerOptions options)
    {
        var config = new ServerConfig
        {
            Port = options.Port,
            Ip = "Any",
            MaxConnectionNumber = options.MaxConnections,
            Mode = SocketMode.Tcp,
            Name = "LoadTestServer",
            MaxRequestLength = 1024 * 1024,
            NoDelay = true
        };

        _metrics = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = options.RunId,
            OutputDirectory = options.Output,
            SampleInterval = TimeSpan.FromMilliseconds(options.SampleIntervalMs),
            ServerName = "LoadTestServer",
            RequestEventSampling = options.RequestEventSampling
        });

        return Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());
    }

    public bool StartWithMetrics()
    {
        if (_metrics is null)
            throw new InvalidOperationException("Server is not configured.");

        if (!Start())
            return false;

        _metricsLoop = _metrics.Start();
        return true;
    }

    private void OnConnected(LoadTestSession session)
    {
        _metrics?.OnConnected(session.SessionID, session.RemoteEndPoint?.ToString());
    }

    private void OnClosed(LoadTestSession session, CloseReason reason)
    {
        _metrics?.OnClosed(session.SessionID, reason.ToString(), session.RemoteEndPoint?.ToString());
    }

    private void OnRequestReceived(LoadTestSession session, LoadTestRequestInfo request)
    {
        if (_metrics is null)
            return;

        using var recorder = _metrics.BeginRequest(session.SessionID, request.PacketId, request.TotalSize);
        PacketHandlers.Handle(session, request, _metrics);
    }

    public new void Stop()
    {
        base.Stop();
        _metricsLoop?.Dispose();
        _metricsLoop = null;
        _metrics?.Flush();
    }

    public new void Dispose()
    {
        Stop();
        _metrics?.Dispose();
        _metrics = null;
    }
}
