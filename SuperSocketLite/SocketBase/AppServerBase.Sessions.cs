using System.Diagnostics;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;
/// <summary>세션과 요청: 이벤트, 명령 실행, 세션 생성/등록/종료.</summary>

public abstract partial class AppServerBase<TAppSession, TRequestInfo>
    where TRequestInfo : class, IRequestInfo
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
{

    private RequestHandler<TAppSession, TRequestInfo>? _requestHandler;

    /// <summary>Occurs when a full request item received.</summary>
    public virtual event RequestHandler<TAppSession, TRequestInfo> NewRequestReceived
    {
        add { _requestHandler += value; }
        remove { _requestHandler -= value; }
    }


    /// <summary>Executes the command.</summary>
    protected virtual void ExecuteCommand(TAppSession session, TRequestInfo requestInfo)
    {
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

        session.MarkActive();

        Interlocked.Increment(ref _totalHandledRequests);

        // Track total requests for metrics
        s_TotalRequestsCounter.Add(1, ServerTag);
    }


    /// <summary>Executes the command for the session.</summary>
    internal void ExecuteCommand(IAppSession session, TRequestInfo requestInfo)
        => ExecuteCommand((TAppSession)session, requestInfo);

    void IRequestHandler<TRequestInfo>.ExecuteCommand(IAppSession session, TRequestInfo requestInfo)
        => ExecuteCommand(session, requestInfo);

    /// <summary>Creates the app session.</summary>
    IAppSession IAppServer.CreateAppSession(ISocketSession socketSession)
    {
        var appSession = CreateAppSession(socketSession);
        
        appSession.Initialize(this, socketSession);

        return appSession;
    }

    /// <summary>create a new TAppSession instance, you can override it to create the session instance in your own way</summary>
    /// <returns>the new created session instance</returns>
    protected virtual TAppSession CreateAppSession(ISocketSession socketSession)
    {
        return new TAppSession();
    }

    /// <summary>Registers the new created app session into the appserver's session container.</summary>
    bool IAppServer.RegisterSession(IAppSession session)
    {
        var appSession = (session as TAppSession)!;

        if (!RegisterSession(appSession.SessionID, appSession))
            return false;

        appSession.SocketSession.Closed += OnSocketSessionClosed;

        // Track active connections
        s_ActiveConnectionsCounter?.Add(1, new KeyValuePair<string, object?>("server", Name));

        OnNewSessionConnected(appSession);
        return true;
    }

    /// <summary>Registers the session into session container.</summary>
    protected virtual bool RegisterSession(string sessionID, TAppSession appSession)
    {
        return true;
    }


    private SessionHandler<TAppSession>? _newSessionConnected;

    /// <summary>The action which will be executed after a new session connect</summary>
    public event SessionHandler<TAppSession> NewSessionConnected
    {
        add { _newSessionConnected += value; }
        remove { _newSessionConnected -= value; }
    }

    /// <summary>Called when [new session connected].</summary>
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

    /// <summary>Called when [socket session closed].</summary>
    /// <param name="session">The socket session.</param>
    private void OnSocketSessionClosed(ISocketSession session, CloseReason reason)
    {
        s_ActiveConnectionsCounter?.Add(-1, new KeyValuePair<string, object?>("server", Name));

        var appSession = (session.AppSession as TAppSession)!;
        appSession.Connected = false;
        OnSessionClosed(appSession, reason);
    }

    private SessionHandler<TAppSession, CloseReason>? _sessionClosed;
    /// <summary>Gets/sets the session closed event handler.</summary>
    public event SessionHandler<TAppSession, CloseReason> SessionClosed
    {
        add { _sessionClosed += value; }
        remove { _sessionClosed -= value; }
    }

    /// <summary>Called when [session closed].</summary>
    /// <param name="session">The appSession.</param>
    protected virtual void OnSessionClosed(TAppSession session, CloseReason reason)
    {
        var handler = _sessionClosed;

        if (handler != null)
        {
            Task.Run(() => handler(session, reason)); 
        }

        session.OnSessionClosed(reason);
    }

    /// <summary>Gets the app session by ID.</summary>
    public abstract TAppSession? GetSessionByID(string sessionID);

    /// <summary>Gets the app session by ID.</summary>
    IAppSession? IAppServer.GetSessionByID(string sessionID)
    {
        return this.GetSessionByID(sessionID);
    }

    /// <summary>Gets the matched sessions from sessions snapshot.</summary>
    /// <param name="critera">The prediction critera.</param>
    public virtual IEnumerable<TAppSession>? GetSessions(Func<TAppSession, bool> critera)
    {
        throw new NotSupportedException();
    }

    /// <summary>Gets all sessions in sessions snapshot.</summary>
    public virtual IEnumerable<TAppSession>? GetAllSessions()
    {
        throw new NotSupportedException();
    }

    /// <summary>Gets the total session count.</summary>
    public abstract int SessionCount { get; }

}
