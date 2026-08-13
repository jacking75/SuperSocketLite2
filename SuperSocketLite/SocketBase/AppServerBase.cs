using System.Net;
using System.Text;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>AppServer base class</summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract partial class AppServerBase<TAppSession, TRequestInfo> : IAppServer<TAppSession, TRequestInfo>, IRequestHandler<TRequestInfo>, IActiveConnector, IDisposable
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
    /// <summary>Gets the server's config.</summary>
    public IServerConfig Config { get; private set; } = null!;

    //Server instance name
    private string _name = null!;

    /// <summary>the current state's code</summary>
    private int _stateCode = ServerStateConst.NotInitialized;

    /// <summary>Gets the current state of the work item.</summary>
    public ServerState State => (ServerState)_stateCode;

    /// <summary>Gets or sets the receive filter factory.</summary>
    public virtual IReceiveFilterFactory<TRequestInfo> ReceiveFilterFactory { get; protected set; } = null!;

    /// <summary>Gets the Receive filter factory.</summary>
    object IAppServer.ReceiveFilterFactory => this.ReceiveFilterFactory;

    /// <summary>Gets the root config.</summary>
    protected IRootConfig RootConfig { get; private set; } = null!;

    /// <summary>Gets the logger assosiated with this object.</summary>
    public ILog Logger { get; private set; } = null!;
             
    // 0 = not configured, 1 = configured.
    // Interlocked.CompareExchange prevents two concurrently-initialized AppServer instances
    // from both configuring the thread pool (check-then-set race on plain bool).
    private static int s_ThreadPoolConfigured = 0;

    // Holds the registration created by Start(CancellationToken) so it can be
    // disposed in Stop() ??prevents the callback from firing after the server stops.
    private CancellationTokenRegistration _stopRegistration;

    private long _totalHandledRequests = 0;

    /// <summary>Gets the total handled requests number.</summary>
    protected long TotalHandledRequests => _totalHandledRequests;

    private ListenerInfo[]? _listeners;

    /// <summary>Gets or sets the listeners inforamtion.</summary>
    public ListenerInfo[]? Listeners => _listeners;

    /// <summary>Gets the started time of this server instance, in UTC.</summary>
    public DateTime StartedTime { get; private set; }


    /// <summary>Gets or sets the log factory.</summary>
    public ILogFactory LogFactory { get; private set; } = null!;


    /// <summary>Gets the default text encoding.</summary>
    public Encoding TextEncoding { get; private set; } = null!;

    public AppServerBase()
    {

    }

    public AppServerBase(IReceiveFilterFactory<TRequestInfo> receiveFilterFactory)
    {
        this.ReceiveFilterFactory = receiveFilterFactory;
    }

    /// <summary>Gets the name of the server instance.</summary>
    public string Name => _name;

    private ISocketServer _socketServer = null!;

    /// <summary>
    /// Starts this server instance and stops it automatically when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// The existing parameterless <see cref="Start()"/> still works unchanged.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token that triggers <see cref="Stop"/> when cancelled.
    /// Pass <see cref="CancellationToken.None"/> (or omit) for the original behaviour.
    /// </param>
    /// <returns>true if the server started successfully; otherwise false.</returns>
    public bool Start(CancellationToken cancellationToken)
    {
        if (!Start())
            return false;

        if (cancellationToken.CanBeCanceled)
        {
            // Use a static lambda + state object to avoid allocating a closure.
            _stopRegistration = cancellationToken.Register(
                static s => ((AppServerBase<TAppSession, TRequestInfo>)s!).Stop(),
                this);
        }

        return true;
    }

    /// <summary>Starts this server instance.</summary>
    /// <returns>return true if start successfull, else false</returns>
    public virtual bool Start()
    {
        var origStateCode = Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Starting, ServerStateConst.NotStarted);

        if (origStateCode != ServerStateConst.NotStarted)
        {
            if (origStateCode < ServerStateConst.NotStarted)
                throw new Exception("You cannot start a server instance which has not been setup yet.");

            if (Logger.IsErrorEnabled)
                Logger.Error($"This server instance is in the state {(ServerState)origStateCode}, you cannot start it now.");

            return false;
        }

        // Initialize active connections counter for metrics
        RegisterMetrics();

        if (!_socketServer.Start())
        {
            _stateCode = ServerStateConst.NotStarted;
            return false;
        }

        StartedTime = DateTime.UtcNow;
        _stateCode = ServerStateConst.Running;
                    
        try
        {
            OnStarted();
        }
        catch (Exception e)
        {
            if (Logger.IsErrorEnabled)
            {
                Logger.Error("One exception was thrown in the method 'OnStarted()'.", e);
            }
        }
        finally
        {
            if (Logger.IsInfoEnabled)
                Logger.Info(string.Format("The server instance {0} has been started!", Name));
        }

        return true;
    }

    /// <summary>Called when [started].</summary>
    protected virtual void OnStarted()
    {

    }

    /// <summary>Called when [stopped].</summary>
    protected virtual void OnStopped()
    {

    }

    /// <summary>
    /// Stops this server instance gracefully: new connections are refused immediately, then the
    /// existing sessions are given up to <paramref name="drainTimeout"/> to flush what they have
    /// already queued for sending, and finally the regular <see cref="Stop"/> runs.
    /// </summary>
    /// <param name="drainTimeout">How long to wait for the sending queues to empty.</param>
    /// <remarks>
    /// Receiving stays active while draining, so a client's last request can still be answered.
    /// Sessions that are still sending when the timeout expires are closed anyway.
    /// </remarks>
    public virtual async Task StopAsync(TimeSpan drainTimeout)
    {
        //Take ownership of the shutdown exactly like Stop() does, so a concurrent Stop()/StopAsync()
        //cannot run the teardown twice.
        if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Stopping, ServerStateConst.Running)
                != ServerStateConst.Running)
        {
            return;
        }

        try
        {
            (_socketServer as SuperSocketLite.SocketEngine.SocketServerBase)?.StopListeners();

            await DrainSendingSessionsAsync(drainTimeout).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (Logger.IsErrorEnabled)
                Logger.Error("Failed to drain the sessions during the graceful shutdown.", e);
        }
        finally
        {
            //Hand back to Stop(): it expects to make the Running -> Stopping transition itself.
            _stateCode = ServerStateConst.Running;
            Stop();
        }
    }

    private async Task DrainSendingSessionsAsync(TimeSpan drainTimeout)
    {
        if (drainTimeout <= TimeSpan.Zero)
            return;

        var deadline = Environment.TickCount64 + (long)drainTimeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            var sessions = GetAllSessions();

            if (sessions == null)
                return;

            var pending = 0;

            foreach (var session in sessions)
            {
                if (!session.SocketSession.IsSendIdle)
                    pending++;
            }

            if (pending == 0)
                return;

            await Task.Delay(50).ConfigureAwait(false);
        }

        if (Logger.IsInfoEnabled)
            Logger.Info(string.Format("The drain timeout of {0} elapsed before every session finished sending; the remaining sessions will be closed.", drainTimeout));
    }

    /// <summary>Stops this server instance.</summary>
    public virtual void Stop()
    {
        if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Stopping, ServerStateConst.Running)
                != ServerStateConst.Running)
        {
            return;
        }

        // Dispose the cancellation registration so the callback cannot fire after Stop()
        // has already run.  Dispose() is safe to call from within the callback itself
        // (.NET guarantees no deadlock in that case).
        var reg = _stopRegistration;
        _stopRegistration = default;
        reg.Dispose();

        _socketServer.Stop();

        _stateCode = ServerStateConst.NotStarted;

        OnStopped();
                    
        if (Logger.IsInfoEnabled)
            Logger.Info(string.Format("The server instance {0} has been stopped!", Name));
    }

    /// <summary>Connect the remote endpoint actively.</summary>
    /// <exception cref="System.Exception">This server cannot support active connect.</exception>
    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint, EndPoint? localEndPoint)
    {
        var activeConnector = _socketServer as IActiveConnector;

        if (activeConnector == null)
            throw new Exception("This server cannot support active connect.");

        return activeConnector.ActiveConnect(targetEndPoint, localEndPoint);
    }



    /// <summary>Releases unmanaged and - optionally - managed resources</summary>
    public void Dispose()
    {
        if (_stateCode == ServerStateConst.Running)
            Stop();
    }
}
