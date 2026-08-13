using System.Net;
using System.Text;
using SuperSocketLite.Common;
using SuperSocketLite.SocketEngine;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;
/// <summary>설정: Setup 계열과 리스너 구성.</summary>

public abstract partial class AppServerBase<TAppSession, TRequestInfo>
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
            
    /// <summary>
    /// Called from <see cref="Setup(IRootConfig, IServerConfig, IReceiveFilterFactory{TRequestInfo}, ILogFactory)"/>
    /// once the config, logger, receive filter factory and listeners are in place. Override to add
    /// server specific initialization; return false to fail the setup.
    /// </summary>
    protected virtual bool OnSetup(IRootConfig rootConfig, IServerConfig config)
    {
        return true;
    }

    private void SetupBasic(IRootConfig rootConfig, IServerConfig config)
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

        //Read text encoding from the configuration
        if (!string.IsNullOrEmpty(config.TextEncoding))
            TextEncoding = Encoding.GetEncoding(config.TextEncoding);
        else
            TextEncoding = new ASCIIEncoding();
    }

    private bool SetupFinal()
    {
        //Check receiveFilterFactory
        if (ReceiveFilterFactory == null)
        {
            if (Logger.IsErrorEnabled)
                Logger.Error("receiveFilterFactory is required!");

            return false;
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

    private void TrySetInitializedState()
    {
        if (Interlocked.CompareExchange(ref _stateCode, ServerStateConst.Initializing, ServerStateConst.NotInitialized)
                != ServerStateConst.NotInitialized)
        {
            throw new Exception("The server has been initialized already, you cannot initialize it again!");
        }
    }


    /// <summary>Setups with the specified config.</summary>
    /// <param name="config">The server config.</param>
    public bool Setup(IServerConfig config, IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null, ILogFactory? logFactory = null)
    {
        return Setup(new RootConfig(), config, receiveFilterFactory, logFactory);
    }

    /// <summary>Setups the specified root config, this method used for programming setup</summary>
    /// <param name="config">The server config.</param>
    public bool Setup(IRootConfig rootConfig, IServerConfig config, IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null, ILogFactory? logFactory = null)
    {
        TrySetInitializedState();

        SetupBasic(rootConfig, config);

        SetupLogFactory(logFactory);

        Logger = CreateLogger(this.Name);

        if (receiveFilterFactory != null)
            ReceiveFilterFactory = receiveFilterFactory;

        if (!SetupListeners(config))
            return false;

        if (!OnSetup(rootConfig, config))
            return false;

        if (!SetupFinal())
            return false;

        _stateCode = ServerStateConst.NotStarted;
        return true;
    }

    private void SetupLogFactory(ILogFactory? logFactory)
    {
        if (logFactory != null)
        {
            LogFactory = logFactory;
            return;
        }

        //ConsoleLogFactory is default log factory
        LogFactory ??= new ConsoleLogFactory();
    }


    /// <summary>Creates the logger for the AppServer.</summary>
    protected virtual ILog CreateLogger(string loggerName)
    {
        return LogFactory.GetLog(loggerName);
    }

    /// <summary>Setups the socket server.instance</summary>
    private bool SetupSocketServer()
    {
        try
        {
            _socketServer = SocketServerFactory.CreateSocketServer<TRequestInfo>(this, _listeners!, Config);
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

    /// <summary>Setups the listeners base on server configuration</summary>
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

}
