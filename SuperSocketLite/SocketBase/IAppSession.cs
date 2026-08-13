using System.Buffers;
using System.Net;
using System.Text;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>The basic interface for appSession</summary>
public interface IAppSession : ISessionBase
{
    /// <summary>Gets the app server.</summary>
    IAppServer AppServer { get; }
    /// <summary>Gets the socket session of the AppSession.</summary>
    ISocketSession SocketSession { get; }

    /// <summary>Gets the config of the server.</summary>
    IServerConfig Config { get; }

    /// <summary>Gets the local listening endpoint.</summary>
    IPEndPoint? LocalEndPoint { get; }

    /// <summary>Gets or sets the last active time of the session.</summary>
    DateTime LastActiveTime { get; set; }

    /// <summary>Gets the start time of the session.</summary>
    DateTime StartTime { get; }

    /// <summary>Closes this session.</summary>
    void Close();

    /// <summary>Closes the session by the specified reason.</summary>
    /// <param name="reason">The close reason.</param>
    void Close(CloseReason reason);

    /// <summary>Gets a value indicating whether this <see cref="IAppSession"/> is connected.</summary>
    bool Connected { get; }

    /// <summary>Gets or sets the charset which is used for transfering text message.</summary>
    Encoding Charset { get; set; }

    /// <summary>Gets the logger assosiated with this session.</summary>
    ILog Logger { get; }

    /// <summary>Processes the request data from the receive pipe.</summary>
    /// <param name="buffer">The read-only sequence buffer from PipeReader.</param>
    /// <returns>The consumed and examined positions to advance the PipeReader.</returns>
    ProcessReceiveResult ProcessRequest(ReadOnlySequence<byte> buffer);

    /// <summary>Starts the session.</summary>
    void StartSession();
}

/// <summary>The interface for appSession</summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface IAppSession<TAppSession, TRequestInfo> : IAppSession
    where TRequestInfo : IRequestInfo
    where TAppSession : IAppSession, IAppSession<TAppSession, TRequestInfo>, new()
{
    /// <summary>Initializes the specified session.</summary>
    void Initialize(IAppServer<TAppSession, TRequestInfo> server, ISocketSession socketSession);
}
