using System.Collections.Concurrent;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// AppServer class
/// </summary>
public class AppServer : AppServer<AppSession>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer"/> class.
    /// </summary>
    public AppServer()
        : base()
    {

    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer"/> class.
    /// </summary>
    /// <param name="receiveFilterFactory">The Receive filter factory.</param>
    public AppServer(IReceiveFilterFactory<StringRequestInfo> receiveFilterFactory)
        : base(receiveFilterFactory)
    {

    }
}

/// <summary>
/// AppServer class
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
public class AppServer<TAppSession> : AppServer<TAppSession, StringRequestInfo>
    where TAppSession : AppSession<TAppSession, StringRequestInfo>, IAppSession, new()
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer&lt;TAppSession&gt;"/> class.
    /// </summary>
    public AppServer()
        : base()
    {

    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer&lt;TAppSession&gt;"/> class.
    /// </summary>
    /// <param name="receiveFilterFactory">The Receive filter factory.</param>
    public AppServer(IReceiveFilterFactory<StringRequestInfo> receiveFilterFactory)
        : base(receiveFilterFactory)
    {

    }

    internal override IReceiveFilterFactory<StringRequestInfo> CreateDefaultReceiveFilterFactory()
    {
        return new CommandLineReceiveFilterFactory(TextEncoding);
    }
}


/// <summary>
/// AppServer basic class
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class AppServer<TAppSession, TRequestInfo> : AppServerBase<TAppSession, TRequestInfo>
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer&lt;TAppSession, TRequestInfo&gt;"/> class.
    /// </summary>
    public AppServer()
        : base()
    {
        
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServer&lt;TAppSession, TRequestInfo&gt;"/> class.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    protected AppServer(IReceiveFilterFactory<TRequestInfo> protocol)
        : base(protocol)
    {

    }

    internal override IReceiveFilterFactory<TRequestInfo>? CreateDefaultReceiveFilterFactory()
    {
        return null;
    }

    /// <summary>
    /// Starts this AppServer instance.
    /// </summary>
    /// <returns></returns>
    public override bool Start()
    {
        if (!base.Start())
            return false;

        if (!Config.DisableSessionSnapshot)
            StartSessionSnapshotTimer();

        if (Config.ClearIdleSession)
            StartClearSessionTimer();

        if(Config.CollectSendIntervalMillSec > 0)
        {
            StartCollectSendSessionTimer();
        }

        return true;
    }

    private ConcurrentDictionary<string, TAppSession> _sessionDict = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers the session into the session container.
    /// </summary>
    /// <param name="sessionID">The session ID.</param>
    /// <param name="appSession">The app session.</param>
    /// <returns></returns>
    protected override bool RegisterSession(string sessionID, TAppSession appSession)
    {
        if (_sessionDict.TryAdd(sessionID, appSession))
            return true;

        if (Logger.IsErrorEnabled)
        {
            Logger.Log(LogEventLevel.Error,
                new LogSessionContext(appSession.SessionID, appSession.RemoteEndPoint),
                "The session is refused because its ID already exists!");
        }
        
        return false;
    }

    /// <summary>
    /// Gets the app session by ID.
    /// </summary>
    /// <param name="sessionID">The session ID.</param>
    /// <returns></returns>
    public override TAppSession? GetSessionByID(string sessionID)
    {
        if (string.IsNullOrEmpty(sessionID))
            return NullAppSession;

        TAppSession? targetSession;
        _sessionDict.TryGetValue(sessionID, out targetSession);
        return targetSession;
    }

    /// <summary>
    /// Called when [socket session closed].
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="reason">The reason.</param>
    protected override void OnSessionClosed(TAppSession session, CloseReason reason)
    {
        string sessionID = session.SessionID;

        if (!string.IsNullOrEmpty(sessionID))
        {
            TAppSession? removedSession;
            if (!_sessionDict.TryRemove(sessionID, out removedSession))
            {
                if (Logger.IsErrorEnabled)
                {
                    Logger.Log(LogEventLevel.Error,
                        new LogSessionContext(session.SessionID, session.RemoteEndPoint),
                        "Failed to remove this session, because it has not been in the session container!");
                }
            }
        }

        base.OnSessionClosed(session, reason);
    }

    /// <summary>
    /// Gets the total session count.
    /// </summary>
    public override int SessionCount
    {
        get
        {
            return _sessionDict.Count;
        }
    }


    private Timer? _collectSendSessionTimer = null;

    private void StartCollectSendSessionTimer()
    {
        int interval = Config.CollectSendIntervalMillSec;
        _collectSendSessionTimer = new Timer(CollectSendSession, new object(), interval, interval);

        if (Logger.IsInfoEnabled)
        {
            Logger.Info($"StartCollectSendSessionTimer. CollectSendIntervalMillSec:{interval}");
        }
    }

    /// <summary>
    /// 세션들의 데이터를 모아서 보내기
    /// </summary>
    /// <param name="state">The state.</param>
    private void CollectSendSession(object? state)
    {
        if (Monitor.TryEnter(state!))
        {
            try
            {
                var sessionSource = SessionSource;

                if (sessionSource == null)
                {
                    return;
                }

                Parallel.ForEach(sessionSource, s =>
                {
                    var session = s.Value;
                    var sendData = session.GetCollectSendData();
                    var sendDataLength = sendData.Count;

                    if (sendData.Count > 0)
                    {
                        //SendCopied takes its own pooled copy, so the collect buffer can be
                        //committed right after without the extra snapshot array.
                        session.SendCopied(new ReadOnlySpan<byte>(sendData.Array!, sendData.Offset, sendData.Count));
                    }

                    session.CommitCollectSend(sendDataLength);
                });
            }
            catch (Exception e)
            {
                if (Logger.IsErrorEnabled)
                    Logger.Error("Collect Send Session error!", e);
            }
            finally
            {
                Monitor.Exit(state!);
            }
        }
    }

     


    private Timer? _clearIdleSessionTimer = null;

    private void StartClearSessionTimer()
    {
        int interval = Config.ClearIdleSessionInterval * 1000;//in milliseconds
        _clearIdleSessionTimer = new Timer(ClearIdleSession, new object(), interval, interval);
    }

    /// <summary>
    /// Clears the idle session.
    /// </summary>
    /// <param name="state">The state.</param>
    private void ClearIdleSession(object? state)
    {
        if (Monitor.TryEnter(state!))
        {
            try
            {
                var sessionSource = SessionSource;

                if (sessionSource == null)
                    return;

                // Idle detection runs off the monotonic tick stamp, so it is immune to wall-clock
                // adjustments (DST, NTP) and costs nothing compared to DateTime.Now.
                var nowTicks = Environment.TickCount64;
                var idleTimeOutMillSec = (long)Config.IdleSessionTimeOut * 1000;

                var timeOutSessions = sessionSource.Where(s => nowTicks - s.Value.LastActiveTimeTicks >= idleTimeOutMillSec).Select(s => s.Value);

                Parallel.ForEach(timeOutSessions, s =>
                    {
                        if (Logger.IsInfoEnabled)
                        {
                            var idleSeconds = (nowTicks - s.LastActiveTimeTicks) / 1000.0;
                            Logger.Log(LogEventLevel.Info,
                                new LogSessionContext(s.SessionID, s.RemoteEndPoint),
                                string.Format("The session will be closed after being idle for {0} seconds, start time: {1}, last active time: {2}", idleSeconds, s.StartTime, s.LastActiveTime));
                        }

                        s.Close(CloseReason.TimeOut);
                    });
            }
            catch (Exception e)
            {
                if(Logger.IsErrorEnabled)
                    Logger.Error("Clear idle session error!", e);
            }
            finally
            {
                Monitor.Exit(state!);
            }
        }
    }

    private KeyValuePair<string, TAppSession>[]? SessionSource
    {
        get
        {
            if (Config.DisableSessionSnapshot)
                return _sessionDict.ToArray();
            else
                return _sessionsSnapshot;
        }
    }

    

    

    private Timer? _sessionSnapshotTimer = null;

    private KeyValuePair<string, TAppSession>[]? _sessionsSnapshot = [];

    private void StartSessionSnapshotTimer()
    {
        int interval = Math.Max(Config.SessionSnapshotInterval, 1) * 1000;//in milliseconds
        _sessionSnapshotTimer = new Timer(TakeSessionSnapshot, new object(), interval, interval);
    }

    private void TakeSessionSnapshot(object? state)
    {
        if (Monitor.TryEnter(state!))
        {
            Interlocked.Exchange(ref _sessionsSnapshot, _sessionDict.ToArray());
            Monitor.Exit(state!);
        }
    }

    

    

    /// <summary>
    /// Gets the matched sessions from sessions snapshot.
    /// </summary>
    /// <param name="critera">The prediction critera.</param>
    /// <returns></returns>
    public override IEnumerable<TAppSession>? GetSessions(Func<TAppSession, bool> critera)
    {
        var sessionSource = SessionSource;

        if (sessionSource == null)
            return null;

        return sessionSource.Select(p => p.Value).Where(critera);
    }

    /// <summary>
    /// Gets all sessions in sessions snapshot.
    /// </summary>
    /// <returns></returns>
    public override IEnumerable<TAppSession>? GetAllSessions()
    {
        var sessionSource = SessionSource;

        if (sessionSource == null)
            return null;

        return sessionSource.Select(p => p.Value);
    }

    /// <summary>
    /// Stops this instance.
    /// </summary>
    public override void Stop()
    {
        base.Stop();

        if (_sessionSnapshotTimer != null)
        {
            _sessionSnapshotTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _sessionSnapshotTimer.Dispose();
            _sessionSnapshotTimer = null;
        }

        if (_clearIdleSessionTimer != null)
        {
            _clearIdleSessionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _clearIdleSessionTimer.Dispose();
            _clearIdleSessionTimer = null;
        }

        if (_collectSendSessionTimer != null)
        {
            _collectSendSessionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _collectSendSessionTimer.Dispose();
            _collectSendSessionTimer = null;
        }

        _sessionsSnapshot = null;

        var sessions = _sessionDict.ToArray();

        if (sessions.Length > 0)
        {
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i].Value.Close(CloseReason.ServerShutdown);
            }
        }
    }

    
}
