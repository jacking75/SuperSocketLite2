using System.Collections.Concurrent;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>AppServer basic class</summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class AppServer<TAppSession, TRequestInfo> : AppServerBase<TAppSession, TRequestInfo>
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{
    public AppServer()
        : base()
    {
        
    }

    protected AppServer(IReceiveFilterFactory<TRequestInfo> protocol)
        : base(protocol)
    {

    }

    /// <summary>Starts this AppServer instance.</summary>
    public override bool Start()
    {
        if (!base.Start())
            return false;

        if (!Config.DisableSessionSnapshot)
            StartSessionSnapshotTimer();

        if (Config.ClearIdleSession)
            StartClearSessionTimer();

        return true;
    }

    private ConcurrentDictionary<string, TAppSession> _sessionDict = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers the session into the session container.</summary>
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

    /// <summary>Gets the app session by ID.</summary>
    public override TAppSession? GetSessionByID(string sessionID)
    {
        if (string.IsNullOrEmpty(sessionID))
            return null;

        TAppSession? targetSession;
        _sessionDict.TryGetValue(sessionID, out targetSession);
        return targetSession;
    }

    /// <summary>Called when [socket session closed].</summary>
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

    /// <summary>Gets the total session count.</summary>
    public override int SessionCount => _sessionDict.Count;


    /// <summary>
    /// Starts a periodic timer whose callback never re-enters itself.
    /// </summary>
    /// <remarks>
    /// A tick that arrives while the previous one is still running is dropped rather than queued,
    /// so a slow sweep over the session snapshot cannot pile up timer callbacks.
    /// </remarks>
    private static Timer StartPeriodicTimer(Action body, int intervalMillSec)
    {
        var gate = new object();

        return new Timer(_ =>
        {
            if (!Monitor.TryEnter(gate))
                return;

            try
            {
                body();
            }
            finally
            {
                Monitor.Exit(gate);
            }
        }, gate, intervalMillSec, intervalMillSec);
    }

    private static void StopTimer(ref Timer? timer)
    {
        if (timer == null)
            return;

        timer.Change(Timeout.Infinite, Timeout.Infinite);
        timer.Dispose();
        timer = null;
    }


    private Timer? _clearIdleSessionTimer = null;

    private void StartClearSessionTimer()
    {
        int interval = Config.ClearIdleSessionInterval * 1000;//in milliseconds
        _clearIdleSessionTimer = StartPeriodicTimer(ClearIdleSession, interval);
    }

    /// <summary>Clears the idle session.</summary>
    private void ClearIdleSession()
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
            if (Logger.IsErrorEnabled)
                Logger.Error("Clear idle session error!", e);
        }
    }

    private KeyValuePair<string, TAppSession>[]? SessionSource
        => Config.DisableSessionSnapshot ? _sessionDict.ToArray() : _sessionsSnapshot;

    

    

    private Timer? _sessionSnapshotTimer = null;

    private KeyValuePair<string, TAppSession>[]? _sessionsSnapshot = [];

    private void StartSessionSnapshotTimer()
    {
        int interval = Math.Max(Config.SessionSnapshotInterval, 1) * 1000;//in milliseconds
        _sessionSnapshotTimer = StartPeriodicTimer(TakeSessionSnapshot, interval);
    }

    private void TakeSessionSnapshot()
    {
        Interlocked.Exchange(ref _sessionsSnapshot, _sessionDict.ToArray());
    }

    

    

    /// <summary>Gets the matched sessions from sessions snapshot.</summary>
    /// <param name="critera">The prediction critera.</param>
    public override IEnumerable<TAppSession>? GetSessions(Func<TAppSession, bool> critera)
    {
        var sessionSource = SessionSource;

        if (sessionSource == null)
            return null;

        return sessionSource.Select(p => p.Value).Where(critera);
    }

    /// <summary>Gets all sessions in sessions snapshot.</summary>
    public override IEnumerable<TAppSession>? GetAllSessions()
    {
        var sessionSource = SessionSource;

        if (sessionSource == null)
            return null;

        return sessionSource.Select(p => p.Value);
    }

    /// <summary>Stops this instance.</summary>
    public override void Stop()
    {
        base.Stop();

        StopTimer(ref _sessionSnapshotTimer);
        StopTimer(ref _clearIdleSessionTimer);

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
