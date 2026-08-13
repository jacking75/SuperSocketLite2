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
    private bool _configured;
    private bool _stopped;

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

        // --metrics off 은 수집기를 아예 만들지 않는다.
        // 계측이 결과에 주는 영향을 재려면 계측 코드가 실제로 돌지 않아야 한다.
        _metrics = options.Metrics == ServerMetricsMode.Off
            ? null
            : ServerMetricsCollector.Create(new ServerMetricsOptions
            {
                RunId = options.RunId,
                OutputDirectory = options.Output,
                SampleInterval = TimeSpan.FromMilliseconds(options.SampleIntervalMs),
                ServerName = "LoadTestServer",
                RequestEventSampling = options.RequestEventSampling,
                CollectRuntimeGauges = options.Metrics == ServerMetricsMode.Full
            });

        _configured = true;
        return Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());
    }

    /// <summary>
    /// 이 서버의 계측기입니다. <see cref="Configure"/> 이후에 유효하며, <c>--metrics off</c>이면 null입니다.
    /// 부가 리스너(text-line·UDP)가 같은 수집기를 공유하도록 노출합니다.
    /// </summary>
    public ServerMetricsCollector? Metrics => _metrics;

    public bool StartWithMetrics()
    {
        if (!_configured)
            throw new InvalidOperationException("Server is not configured.");

        if (!Start())
            return false;

        _metricsLoop = _metrics?.Start();
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
        // 계측을 껐어도 에코는 그대로 해야 한다. 클라이언트 쪽 수치를 두 실행에서 비교하기 때문이다.
        if (_metrics is null)
        {
            PacketHandlers.Handle(session, request, null);
            return;
        }

        using var recorder = _metrics.BeginRequest(session.SessionID, request.PacketId, request.TotalSize);
        PacketHandlers.Handle(session, request, _metrics);
    }

    /// <summary>
    /// 서버와 계측을 멈춥니다.
    /// <c>Program</c>이 명시적으로 부른 뒤 <c>using</c>이 <see cref="Dispose"/>에서 다시 부르므로 멱등이어야 합니다.
    /// </summary>
    public new void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        base.Stop();

        // 세션이 닫히는 데는 시간이 걸린다. 마지막 샘플이 정리 전에 찍히면
        // 누수가 없는데도 활성 세션이 남은 것처럼 기록된다.
        WaitForSessionsToDrain(TimeSpan.FromSeconds(5));

        _metricsLoop?.Dispose();
        _metricsLoop = null;
        _metrics?.Flush();
    }

    /// <summary>
    /// 활성 세션이 0이 되거나 제한 시간이 지날 때까지 기다립니다.
    /// 실제로 누수가 있으면 제한 시간이 지난 뒤 그대로 기록되므로 누수 검출은 그대로 동작합니다.
    /// </summary>
    private void WaitForSessionsToDrain(TimeSpan timeout)
    {
        if (_metrics is null)
            return;

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (_metrics.ActiveSessions > 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(20);
    }

    public new void Dispose()
    {
        Stop();
        _metrics?.Dispose();
        _metrics = null;
    }
}
