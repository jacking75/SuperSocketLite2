using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// AppServer base class
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class AppServerBase<TAppSession, TRequestInfo> : IAppServer<TAppSession, TRequestInfo>, IRawDataProcessor<TAppSession>, IRequestHandler<TRequestInfo>, IActiveConnector, IDisposable
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
    /// <summary>
    /// Null appSession instance
    /// </summary>
    protected readonly TAppSession NullAppSession = default(TAppSession)!;

    /// <summary>
    /// Gets the server's config.
    /// </summary>
    public IServerConfig Config { get; private set; } = null!;

    //Server instance name
    private string _name = null!;

    /// <summary>
    /// the current state's code
    /// </summary>
    private int _stateCode = ServerStateConst.NotInitialized;

    /// <summary>
    /// Gets the current state of the work item.
    /// </summary>
    /// <value>
    /// The state.
    /// </value>
    public ServerState State
    {
        get
        {
            return (ServerState)_stateCode;
        }
    }

    /// <summary>
    /// Gets or sets the receive filter factory.
    /// </summary>
    /// <value>
    /// The receive filter factory.
    /// </value>
    public virtual IReceiveFilterFactory<TRequestInfo> ReceiveFilterFactory { get; protected set; } = null!;

    /// <summary>
    /// Gets the Receive filter factory.
    /// </summary>
    object IAppServer.ReceiveFilterFactory
    {
        get { return this.ReceiveFilterFactory; }
    }
       

    private ISocketServerFactory _socketServerFactory = null!;

    /// <summary>
    /// Gets the root config.
    /// </summary>
    protected IRootConfig RootConfig { get; private set; } = null!;

    /// <summary>
    /// Gets the logger assosiated with this object.
    /// </summary>
    public ILog Logger { get; private set; } = null!;
             
    // 0 = not configured, 1 = configured.
    // Interlocked.CompareExchange prevents two concurrently-initialized AppServer instances
    // from both configuring the thread pool (check-then-set race on plain bool).
    private static int s_ThreadPoolConfigured = 0;

    // Holds the registration created by Start(CancellationToken) so it can be
    // disposed in Stop() ??prevents the callback from firing after the server stops.
    private CancellationTokenRegistration _stopRegistration;

    private List<IConnectionFilter>? _connectionFilters;

    private long _totalHandledRequests = 0;

    /// <summary>
    /// Gets the total handled requests number.
    /// </summary>
    protected long TotalHandledRequests
    {
        get { return _totalHandledRequests; }
    }

    private ListenerInfo[]? _listeners;

    /// <summary>
    /// Gets or sets the listeners inforamtion.
    /// </summary>
    /// <value>
    /// The listeners.
    /// </value>
    public ListenerInfo[]? Listeners
    {
        get { return _listeners; }
    }

    /// <summary>
    /// Gets the started time of this server instance, in UTC.
    /// </summary>
    /// <value>
    /// The started time.
    /// </value>
    public DateTime StartedTime { get; private set; }

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

    /// <summary>
    /// Records bytes received for metrics.
    /// </summary>
    /// <param name="count">The number of bytes received.</param>
    public void RecordBytesReceived(int count)
    {
        Interlocked.Add(ref _totalBytesReceived, count);
        s_TotalBytesReceivedCounter.Add(count, ServerTag);
    }

    /// <summary>
    /// Records bytes sent for metrics.
    /// </summary>
    /// <param name="count">The number of bytes sent.</param>
    public void RecordBytesSent(int count)
    {
        Interlocked.Add(ref _totalBytesSent, count);
        s_TotalBytesSentCounter.Add(count, ServerTag);
    }

    /// <summary>
    /// Records a connection that was refused because the connection limit was reached.
    /// </summary>
    public void RecordSessionRejected()
    {
        Interlocked.Increment(ref _totalSessionsRejected);
        s_SessionsRejectedCounter.Add(1, ServerTag);
    }

    /// <summary>
    /// Records a send that was dropped because the session's sending queue was full.
    /// </summary>
    public void RecordSendQueueFull()
    {
        Interlocked.Increment(ref _totalSendQueueFull);
        s_SendQueueFullCounter.Add(1, ServerTag);
    }

    /// <summary>
    /// Records a failed send.
    /// </summary>
    public void RecordSendError()
    {
        Interlocked.Increment(ref _totalSendErrors);
        s_SendErrorsCounter.Add(1, ServerTag);
    }

    private long _totalSessionsRejected = 0;
    private long _totalSendQueueFull = 0;
    private long _totalSendErrors = 0;

    /// <summary>
    /// Gets the total bytes received.
    /// </summary>
    public long TotalBytesReceived => _totalBytesReceived;

    /// <summary>
    /// Gets the total bytes sent.
    /// </summary>
    public long TotalBytesSent => _totalBytesSent;

    /// <summary>
    /// Gets the number of connections refused because the connection limit was reached.
    /// </summary>
    public long TotalSessionsRejected => _totalSessionsRejected;

    /// <summary>
    /// Gets the number of sends dropped because the sending queue was full.
    /// </summary>
    public long TotalSendQueueFull => _totalSendQueueFull;

    /// <summary>
    /// Gets the number of sends that failed with a socket error.
    /// </summary>
    public long TotalSendErrors => _totalSendErrors;


    /// <summary>
    /// Gets or sets the log factory.
    /// </summary>
    /// <value>
    /// The log factory.
    /// </value>
    public ILogFactory LogFactory { get; private set; } = null!;


    /// <summary>
    /// Gets the default text encoding.
    /// </summary>
    /// <value>
    /// The text encoding.
    /// </value>
    public Encoding TextEncoding { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServerBase&lt;TAppSession, TRequestInfo&gt;"/> class.
    /// </summary>
    public AppServerBase()
    {

    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServerBase&lt;TAppSession, TRequestInfo&gt;"/> class.
    /// </summary>
    /// <param name="receiveFilterFactory">The Receive filter factory.</param>
    public AppServerBase(IReceiveFilterFactory<TRequestInfo> receiveFilterFactory)
    {
        this.ReceiveFilterFactory = receiveFilterFactory;
    }

            
    /// <summary>
    /// Setups the specified root config.
    /// </summary>
    /// <param name="rootConfig">The root config.</param>
    /// <param name="config">The config.</param>
    /// <returns></returns>
    protected virtual bool Setup(IRootConfig rootConfig, IServerConfig config)
    {
        return true;
    }

    private void SetupBasic(IRootConfig rootConfig, IServerConfig config, ISocketServerFactory? socketServerFactory)
    {
        if (rootConfig == null)
            throw new ArgumentNullException("rootConfig");

        RootConfig = rootConfig;

        if (config == null)
            throw new ArgumentNullException("config");

        if (!string.IsNullOrEmpty(config.Name))
            _name = config.Name;
        else
            _name = string.Format("{0}-{1}", this.GetType().Name, Math.Abs(this.GetHashCode()));

        Config = config;

        // Only the first thread that wins the CAS configures the thread pool.
        if (Interlocked.CompareExchange(ref s_ThreadPoolConfigured, 1, 0) == 0)
        {
            if (!ThreadPoolEx.ResetThreadPool(rootConfig.MaxWorkingThreads >= 0 ? rootConfig.MaxWorkingThreads : new Nullable<int>(),
                    rootConfig.MaxCompletionPortThreads >= 0 ? rootConfig.MaxCompletionPortThreads : new Nullable<int>(),
                    rootConfig.MinWorkingThreads >= 0 ? rootConfig.MinWorkingThreads : new Nullable<int>(),
                    rootConfig.MinCompletionPortThreads >= 0 ? rootConfig.MinCompletionPortThreads : new Nullable<int>()))
            {
                Interlocked.Exchange(ref s_ThreadPoolConfigured, 0); // allow retry
                throw new Exception("Failed to configure thread pool!");
            }
        }

        if (socketServerFactory == null)
        {
            var socketServerFactoryType = Type.GetType("SuperSocketLite.SocketEngine.SocketServerFactory, SuperSocketLite", true)!;
            socketServerFactory = (ISocketServerFactory)Activator.CreateInstance(socketServerFactoryType)!;
        }

        _socketServerFactory = socketServerFactory;

        //Read text encoding from the configuration
        if (!string.IsNullOrEmpty(config.TextEncoding))
            TextEncoding = Encoding.GetEncoding(config.TextEncoding);
        else
            TextEncoding = new ASCIIEncoding();
    }

    private bool SetupMedium(IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory, IEnumerable<IConnectionFilter>? connectionFilters)
    {
        if (receiveFilterFactory != null)
            ReceiveFilterFactory = receiveFilterFactory;

        if (connectionFilters != null && connectionFilters.Any())
        {
            if (_connectionFilters == null)
                _connectionFilters = [];

            _connectionFilters.AddRange(connectionFilters);
        }
         
        return true;
    }

    private bool SetupAdvanced(IServerConfig config)
    {
        if (!SetupListeners(config))
            return false;
                                
        return true;
    }


    internal abstract IReceiveFilterFactory<TRequestInfo>? CreateDefaultReceiveFilterFactory();

    private bool SetupFinal()
    {
        //Check receiveFilterFactory
        if (ReceiveFilterFactory == null)
        {
            ReceiveFilterFactory = CreateDefaultReceiveFilterFactory()!;

            if (ReceiveFilterFactory == null)
            {
                if (Logger.IsErrorEnabled)
                    Logger.Error("receiveFilterFactory is required!");

                return false;
            }
        }

        var plainConfig = Config as ServerConfig;

        if (plainConfig == null)
        {
            //Using plain config model instead of .NET configuration element to improve performance
            plainConfig = new ServerConfig(Config);

            if (string.IsNullOrEmpty(plainConfig.Name))
                plainConfig.Name = Name;

            Config = plainConfig;
        }
        
        return SetupSocketServer();
    }

    /// <summary>
    /// Setups with the specified port.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <returns>return setup result</returns>
    public bool Setup(int port)
    {
        return Setup("Any", port);
    }

    private void TrySetInitializedState()
    {
        if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Initializing, ServerStateConst.NotInitialized)
                != ServerStateConst.NotInitialized)
        {
            throw new Exception("The server has been initialized already, you cannot initialize it again!");
        }
    }


    /// <summary>
    /// Setups with the specified config.
    /// </summary>
    /// <param name="config">The server config.</param>
    /// <param name="socketServerFactory">The socket server factory.</param>
    /// <param name="receiveFilterFactory">The receive filter factory.</param>
    /// <param name="logFactory">The log factory.</param>
    /// <param name="connectionFilters">The connection filters.</param>
    /// <returns></returns>
    public bool Setup(IServerConfig config, ISocketServerFactory? socketServerFactory = null, IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null, ILogFactory? logFactory = null, IEnumerable<IConnectionFilter>? connectionFilters = null)
    {
        return Setup(new RootConfig(), config, socketServerFactory, receiveFilterFactory, logFactory, connectionFilters);
    }

    /// <summary>
    /// Setups the specified root config, this method used for programming setup
    /// </summary>
    /// <param name="rootConfig">The root config.</param>
    /// <param name="config">The server config.</param>
    /// <param name="socketServerFactory">The socket server factory.</param>
    /// <param name="receiveFilterFactory">The Receive filter factory.</param>
    /// <param name="logFactory">The log factory.</param>
    /// <param name="connectionFilters">The connection filters.</param>
    /// <returns></returns>
    public bool Setup(IRootConfig rootConfig, IServerConfig config, ISocketServerFactory? socketServerFactory = null, IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null, ILogFactory? logFactory = null, IEnumerable<IConnectionFilter>? connectionFilters = null)
    {
        TrySetInitializedState();

        SetupBasic(rootConfig, config, socketServerFactory);

        SetupLogFactory(logFactory);

        Logger = CreateLogger(this.Name);

        if (!SetupMedium(receiveFilterFactory, connectionFilters))
            return false;

        if (!SetupAdvanced(config))
            return false;

        if (!Setup(rootConfig, config))
            return false;

        if (!SetupFinal())
            return false;

        _stateCode = ServerStateConst.NotStarted;
        return true;
    }

    /// <summary>
    /// Setups with the specified ip and port.
    /// </summary>
    /// <param name="ip">The ip.</param>
    /// <param name="port">The port.</param>
    /// <param name="socketServerFactory">The socket server factory.</param>
    /// <param name="receiveFilterFactory">The Receive filter factory.</param>
    /// <param name="logFactory">The log factory.</param>
    /// <param name="connectionFilters">The connection filters.</param>
    /// <returns>return setup result</returns>
    public bool Setup(string ip, int port, ISocketServerFactory? socketServerFactory = null, IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null, ILogFactory? logFactory = null, IEnumerable<IConnectionFilter>? connectionFilters = null)
    {
        return Setup(new ServerConfig
                        {
                            Ip = ip,
                            Port = port
                        },
                      socketServerFactory,
                      receiveFilterFactory,
                      logFactory,
                      connectionFilters);
    }
           
    private bool SetupLogFactory(ILogFactory? logFactory)
    {
        if (logFactory != null)
        {
            LogFactory = logFactory;
            return true;
        }

        //ConsoleLogFactory is default log factory
        if (LogFactory == null)
        {
            LogFactory = new ConsoleLogFactory();
        }

        return true;
    }


    /// <summary>
    /// Creates the logger for the AppServer.
    /// </summary>
    /// <param name="loggerName">Name of the logger.</param>
    /// <returns></returns>
    protected virtual ILog CreateLogger(string loggerName)
    {
        return LogFactory.GetLog(loggerName);
    }

    /// <summary>
    /// Setups the socket server.instance
    /// </summary>
    /// <returns></returns>
    private bool SetupSocketServer()
    {
        try
        {
            _socketServer = _socketServerFactory.CreateSocketServer<TRequestInfo>(this, _listeners!, Config);
            return _socketServer != null;
        }
        catch (Exception e)
        {
            if (Logger.IsErrorEnabled)
                Logger.Error(e.ToString());

            return false;
        }
    }

    private IPAddress ParseIPAddress(string? ip)
    {
        if (string.IsNullOrEmpty(ip) || "Any".Equals(ip, StringComparison.OrdinalIgnoreCase))
            return IPAddress.Any;
        else if ("IPv6Any".Equals(ip, StringComparison.OrdinalIgnoreCase))
            return IPAddress.IPv6Any;
        else
           return IPAddress.Parse(ip);
    }

    /// <summary>
    /// Setups the listeners base on server configuration
    /// </summary>
    /// <param name="config">The config.</param>
    /// <returns></returns>
    private bool SetupListeners(IServerConfig config)
    {
        var listeners = new List<ListenerInfo>();

        try
        {
            if (config.Port > 0)
            {
                listeners.Add(new ListenerInfo
                {
                    EndPoint = new IPEndPoint(ParseIPAddress(config.Ip), config.Port),
                    BackLog = config.ListenBacklog
                });
            }
            else
            {
                //Port is not configured, but ip is configured
                if (!string.IsNullOrEmpty(config.Ip))
                {
                    if (Logger.IsErrorEnabled)
                        Logger.Error("Port is required in config!");

                    return false;
                }
            }

            //There are listener defined
            if (config.Listeners != null && config.Listeners.Any())
            {
                //But ip and port were configured in server node
                //We don't allow this case
                if (listeners.Any())
                {
                    if (Logger.IsErrorEnabled)
                        Logger.Error("If you configured Ip and Port in server node, you cannot defined listener in listeners node any more!");

                    return false;
                }

                foreach (var l in config.Listeners)
                {
                    listeners.Add(new ListenerInfo
                    {
                        EndPoint = new IPEndPoint(ParseIPAddress(l.Ip), l.Port),
                        BackLog = l.Backlog
                    });
                }
            }

            if (!listeners.Any())
            {
                if (Logger.IsErrorEnabled)
                    Logger.Error("No listener defined!");

                return false;
            }

            _listeners = listeners.ToArray();

            return true;
        }
        catch (Exception e)
        {
            if (Logger.IsErrorEnabled)
                Logger.Error(e.ToString());

            return false;
        }
    }

    /// <summary>
    /// Gets the name of the server instance.
    /// </summary>
    public string Name
    {
        get { return _name; }
    }

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

    /// <summary>
    /// Starts this server instance.
    /// </summary>
    /// <returns>
    /// return true if start successfull, else false
    /// </returns>
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
        s_ActiveConnectionsCounter ??= s_Meter.CreateUpDownCounter<int>("active-connections", "connections", "Number of active connections");
        _sessionCountGauge ??= s_Meter.CreateObservableGauge("session-count", () => new Measurement<int>(SessionCount, ServerTag), "sessions", "Number of sessions currently registered");

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

    /// <summary>
    /// Called when [started].
    /// </summary>
    protected virtual void OnStarted()
    {

    }

    /// <summary>
    /// Called when [stopped].
    /// </summary>
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

    /// <summary>
    /// Stops this server instance.
    /// </summary>
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


    private Func<TAppSession, byte[], int, int, bool>? _rawDataReceivedHandler;

    /// <summary>
    /// Gets or sets the raw binary data received event handler.
    /// TAppSession: session
    /// byte[]: receive buffer
    /// int: receive buffer offset
    /// int: receive lenght
    /// bool: whether process the received data further
    /// </summary>
    event Func<TAppSession, byte[], int, int, bool> IRawDataProcessor<TAppSession>.RawDataReceived
    {
        add { _rawDataReceivedHandler += value; }
        remove { _rawDataReceivedHandler -= value; }
    }

    /// <summary>
    /// Called when [raw data received].
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    internal bool OnRawDataReceived(IAppSession session, byte[] buffer, int offset, int length)
    {
        var handler = _rawDataReceivedHandler;
        if (handler == null)
            return true;

        return handler((TAppSession)session, buffer, offset, length);
    }

    internal bool HasRawDataReceivedHandler
    {
        get { return _rawDataReceivedHandler != null; }
    }

    private RequestHandler<TAppSession, TRequestInfo>? _requestHandler;

    /// <summary>
    /// Occurs when a full request item received.
    /// </summary>
    public virtual event RequestHandler<TAppSession, TRequestInfo> NewRequestReceived
    {
        add { _requestHandler += value; }
        remove { _requestHandler -= value; }
    }


    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="requestInfo">The request info.</param>
    protected virtual void ExecuteCommand(TAppSession session, TRequestInfo requestInfo)
    {
        session.CurrentCommand = requestInfo.Key;

        var handler = _requestHandler;
        if (handler == null)
            return;

        //Stopwatch timestamps are allocation free, unlike DateTime based timing.
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            handler(session, requestInfo);
        }
        catch (Exception e)
        {
            session.InternalHandleExcetion(e);
        }

        s_RequestDurationHistogram.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, ServerTag);

        session.PrevCommand = requestInfo.Key;
        session.MarkActive();

        if (Config.LogCommand && Logger.IsInfoEnabled)
        {
            Logger.Log(LogEventLevel.Info, session.SessionLogContext,
                string.Format("Command - {0}", requestInfo.Key));
        }

        Interlocked.Increment(ref _totalHandledRequests);

        // Track total requests for metrics
        s_TotalRequestsCounter.Add(1, ServerTag);
    }


    /// <summary>
    /// Executes the command for the session.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="requestInfo">The request info.</param>
    internal void ExecuteCommand(IAppSession session, TRequestInfo requestInfo)
    {
        this.ExecuteCommand((TAppSession)session, requestInfo);
    }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="requestInfo">The request info.</param>
    void IRequestHandler<TRequestInfo>.ExecuteCommand(IAppSession session, TRequestInfo requestInfo)
    {
        this.ExecuteCommand((TAppSession)session, requestInfo);
    }

    /// <summary>
    /// Gets or sets the server's connection filter
    /// </summary>
    /// <value>
    /// The server's connection filters
    /// </value>
    public IEnumerable<IConnectionFilter>? ConnectionFilters
    {
        get { return _connectionFilters; }
    }

    /// <summary>
    /// Executes the connection filters.
    /// </summary>
    /// <param name="remoteAddress">The remote address.</param>
    /// <returns></returns>
    private bool ExecuteConnectionFilters(IPEndPoint? remoteAddress)
    {
        if (_connectionFilters == null)
            return true;

        for (var i = 0; i < _connectionFilters.Count; i++)
        {
            var currentFilter = _connectionFilters[i];
            if (!currentFilter.AllowConnect(remoteAddress))
            {
                if (Logger.IsInfoEnabled)
                    Logger.Info($"A connection from {remoteAddress} has been refused by filter {currentFilter.Name}!");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates the app session.
    /// </summary>
    /// <param name="socketSession">The socket session.</param>
    /// <returns></returns>
    IAppSession IAppServer.CreateAppSession(ISocketSession socketSession)
    {
        if (!ExecuteConnectionFilters(socketSession.RemoteEndPoint))
            return NullAppSession;

        var appSession = CreateAppSession(socketSession);
        
        appSession.Initialize(this, socketSession);

        return appSession;
    }

    /// <summary>
    /// create a new TAppSession instance, you can override it to create the session instance in your own way
    /// </summary>
    /// <param name="socketSession">the socket session.</param>
    /// <returns>the new created session instance</returns>
    protected virtual TAppSession CreateAppSession(ISocketSession socketSession)
    {
        return new TAppSession();
    }

    /// <summary>
    /// Registers the new created app session into the appserver's session container.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns></returns>
    bool IAppServer.RegisterSession(IAppSession session)
    {
        var appSession = (session as TAppSession)!;

        if (!RegisterSession(appSession.SessionID, appSession))
            return false;

        appSession.SocketSession.Closed += OnSocketSessionClosed;

        // Track active connections
        s_ActiveConnectionsCounter?.Add(1, new KeyValuePair<string, object?>("server", Name));

        //if (Config.LogBasicSessionActivity && Logger.IsInfoEnabled)
            //Logger.Info(session, "A new session connected!");

        OnNewSessionConnected(appSession);
        return true;
    }

    /// <summary>
    /// Registers the session into session container.
    /// </summary>
    /// <param name="sessionID">The session ID.</param>
    /// <param name="appSession">The app session.</param>
    /// <returns></returns>
    protected virtual bool RegisterSession(string sessionID, TAppSession appSession)
    {
        return true;
    }


    private SessionHandler<TAppSession>? _newSessionConnected;

    /// <summary>
    /// The action which will be executed after a new session connect
    /// </summary>
    public event SessionHandler<TAppSession> NewSessionConnected
    {
        add { _newSessionConnected += value; }
        remove { _newSessionConnected -= value; }
    }

    /// <summary>
    /// Called when [new session connected].
    /// </summary>
    /// <param name="session">The session.</param>
    /// <remarks>
    /// By default the handler runs on the thread pool, which means a fast client's first request
    /// can be delivered before it. Set <c>ServerConfig.SyncSessionConnectedEvent</c> to raise it
    /// synchronously: registration happens before the socket session starts receiving, so the
    /// ordering becomes structurally guaranteed - at the cost of blocking the accept path.
    /// </remarks>
    protected virtual void OnNewSessionConnected(TAppSession session)
    {
        var handler = _newSessionConnected;
        if (handler == null)
        {
            return;
        }

        if ((Config as ServerConfig)?.SyncSessionConnectedEvent == true)
        {
            try
            {
                handler(session);
            }
            catch (Exception e)
            {
                if (Logger.IsErrorEnabled)
                    Logger.Error("The NewSessionConnected handler threw", e);
            }

            return;
        }

        Task.Run(() => handler(session));
    }

    /// <summary>
    /// Called when [socket session closed].
    /// </summary>
    /// <param name="session">The socket session.</param>
    /// <param name="reason">The reason.</param>
    private void OnSocketSessionClosed(ISocketSession session, CloseReason reason)
    {
        s_ActiveConnectionsCounter?.Add(-1, new KeyValuePair<string, object?>("server", Name));

        var appSession = (session.AppSession as TAppSession)!;
        appSession.Connected = false;
        OnSessionClosed(appSession, reason);
    }

    private SessionHandler<TAppSession, CloseReason>? _sessionClosed;
    /// <summary>
    /// Gets/sets the session closed event handler.
    /// </summary>
    public event SessionHandler<TAppSession, CloseReason> SessionClosed
    {
        add { _sessionClosed += value; }
        remove { _sessionClosed -= value; }
    }

    /// <summary>
    /// Called when [session closed].
    /// </summary>
    /// <param name="session">The appSession.</param>
    /// <param name="reason">The reason.</param>
    protected virtual void OnSessionClosed(TAppSession session, CloseReason reason)
    {
        var handler = _sessionClosed;

        if (handler != null)
        {
            Task.Run(() => handler(session, reason)); 
        }

        session.OnSessionClosed(reason);
    }

    /// <summary>
    /// Gets the app session by ID.
    /// </summary>
    /// <param name="sessionID">The session ID.</param>
    /// <returns></returns>
    public abstract TAppSession? GetSessionByID(string sessionID);

    /// <summary>
    /// Gets the app session by ID.
    /// </summary>
    /// <param name="sessionID"></param>
    /// <returns></returns>
    IAppSession? IAppServer.GetSessionByID(string sessionID)
    {
        return this.GetSessionByID(sessionID);
    }

    /// <summary>
    /// Gets the matched sessions from sessions snapshot.
    /// </summary>
    /// <param name="critera">The prediction critera.</param>
    public virtual IEnumerable<TAppSession>? GetSessions(Func<TAppSession, bool> critera)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets all sessions in sessions snapshot.
    /// </summary>
    public virtual IEnumerable<TAppSession>? GetAllSessions()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the total session count.
    /// </summary>
    public abstract int SessionCount { get; }

    /// <summary>
    /// Connect the remote endpoint actively.
    /// </summary>
    /// <param name="targetEndPoint">The target end point.</param>
    /// <param name="localEndPoint">The local end point.</param>
    /// <returns></returns>
    /// <exception cref="System.Exception">This server cannot support active connect.</exception>
    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint, EndPoint? localEndPoint)
    {
        var activeConnector = _socketServer as IActiveConnector;

        if (activeConnector == null)
            throw new Exception("This server cannot support active connect.");

        return activeConnector.ActiveConnect(targetEndPoint, localEndPoint);
    }

    /// <summary>
    /// Connect the remote endpoint actively.
    /// </summary>
    /// <param name="targetEndPoint">The target end point.</param>
    /// <returns></returns>
    /// <exception cref="System.Exception">This server cannot support active connect.</exception>
    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint)
    {
        return ((IActiveConnector)this).ActiveConnect(targetEndPoint, null);
    }

    

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources
    /// </summary>
    public void Dispose()
    {
        if (_stateCode == ServerStateConst.Running)
            Stop();
    }
}
