using System;
using System.Collections.Generic;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// The interface for AppServer
/// </summary>
public interface IAppServer : ILogProvider
{
    /// <summary>
    /// Gets the name of the server instance.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the server's config.
    /// </summary>
    IServerConfig Config { get; }

    /// <summary>
    /// Gets the current state of the server instance.
    /// </summary>
    ServerState State { get; }

    /// <summary>
    /// Gets the total session count.
    /// </summary>
    int SessionCount { get; }

    /// <summary>
    /// Starts this server instance.
    /// </summary>
    /// <returns>return true if start successfull, else false</returns>
    bool Start();

    /// <summary>
    /// Stops this server instance.
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets the started time.
    /// </summary>
    /// <value>
    /// The started time.
    /// </value>
    DateTime StartedTime { get; }


    /// <summary>
    /// Gets or sets the listeners.
    /// </summary>
    /// <value>
    /// The listeners.
    /// </value>
    ListenerInfo[]? Listeners { get; }

    /// <summary>
    /// Gets the Receive filter factory.
    /// </summary>
    object ReceiveFilterFactory { get; }

    /// <summary>
    /// Creates the app session.
    /// </summary>
    /// <param name="socketSession">The socket session.</param>
    /// <returns></returns>
    IAppSession CreateAppSession(ISocketSession socketSession);


    /// <summary>
    /// Registers the new created app session into the appserver's session container.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns></returns>
    bool RegisterSession(IAppSession session);

    /// <summary>
    /// Gets the app session by ID.
    /// </summary>
    /// <param name="sessionID">The session ID.</param>
    /// <returns></returns>
    IAppSession? GetSessionByID(string sessionID);

    /// <summary>
    /// Gets the log factory.
    /// </summary>
    ILogFactory LogFactory { get; }

    /// <summary>
    /// Records bytes received for metrics.
    /// </summary>
    /// <param name="count">The number of bytes received.</param>
    void RecordBytesReceived(int count);

    /// <summary>
    /// Records bytes sent for metrics.
    /// </summary>
    /// <param name="count">The number of bytes sent.</param>
    void RecordBytesSent(int count);

    /// <summary>
    /// Records a connection that was refused because the connection limit was reached.
    /// </summary>
    void RecordSessionRejected() { }

    /// <summary>
    /// Records a send that was dropped because the session's sending queue was full.
    /// </summary>
    void RecordSendQueueFull() { }

    /// <summary>
    /// Records a failed send.
    /// </summary>
    void RecordSendError() { }
}

/// <summary>
/// The raw data processor
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
public interface IRawDataProcessor<TAppSession>
    where TAppSession : IAppSession
{
    /// <summary>
    /// Gets or sets the raw binary data received event handler.
    /// TAppSession: session
    /// byte[]: receive buffer
    /// int: receive buffer offset
    /// int: receive lenght
    /// bool: whether process the received data further
    /// </summary>
    event Func<TAppSession, byte[], int, int, bool> RawDataReceived;
}

/// <summary>
/// The interface for AppServer
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
public interface IAppServer<TAppSession> : IAppServer
    where TAppSession : IAppSession
{
    /// <summary>
    /// Gets the matched sessions from sessions snapshot.
    /// </summary>
    /// <param name="critera">The prediction critera.</param>
    /// <returns></returns>
    IEnumerable<TAppSession>? GetSessions(Func<TAppSession, bool> critera);

    /// <summary>
    /// Gets all sessions in sessions snapshot.
    /// </summary>
    /// <returns></returns>
    IEnumerable<TAppSession>? GetAllSessions();

    /// <summary>
    /// Gets/sets the new session connected event handler.
    /// </summary>
    event SessionHandler<TAppSession> NewSessionConnected;

    /// <summary>
    /// Gets/sets the session closed event handler.
    /// </summary>
    event SessionHandler<TAppSession, CloseReason> SessionClosed;
}

/// <summary>
/// The interface for AppServer
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface IAppServer<TAppSession, TRequestInfo> : IAppServer<TAppSession>
    where TRequestInfo : IRequestInfo
    where TAppSession : IAppSession, IAppSession<TAppSession, TRequestInfo>, new()
{
    /// <summary>
    /// Occurs when [request comming].
    /// </summary>
    event RequestHandler<TAppSession, TRequestInfo> NewRequestReceived;
}

/// <summary>
/// The interface for handler of session request
/// </summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface IRequestHandler<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="requestInfo">The request info.</param>
    void ExecuteCommand(IAppSession session, TRequestInfo requestInfo);
}