using System.Net;
using System.Net.Sockets;


namespace SuperSocketLite.SocketBase;

/// <summary>CloseReason enum</summary>
public enum CloseReason : int
{
    /// <summary>The socket is closed for unknown reason</summary>
    Unknown = 0,

    /// <summary>Close for server shutdown</summary>
    ServerShutdown = 1,

    /// <summary>The client close the socket</summary>
    ClientClosing = 2,

    /// <summary>The server side close the socket</summary>
    ServerClosing = 3,

    /// <summary>Application error</summary>
    ApplicationError = 4,

    /// <summary>The socket is closed for a socket error</summary>
    SocketError = 5,

    /// <summary>The socket is closed by server for timeout</summary>
    TimeOut = 6,

    /// <summary>Protocol error</summary>
    ProtocolError = 7,

    /// <summary>SuperSocket internal error</summary>
    InternalError = 8,
}

/// <summary>The interface for socket session</summary>
public interface ISocketSession : ISessionBase
{
    /// <summary>Initializes the specified app session.</summary>
    void Initialize(IAppSession appSession);

    /// <summary>Starts this instance.</summary>
    void Start();

    /// <summary>Closes the socket session for the specified reason.</summary>
    void Close(CloseReason reason);


    /// <summary>Tries to send array segment.</summary>
    bool TrySend(IList<ArraySegment<byte>> segments);

    /// <summary>Tries to send array segment.</summary>
    bool TrySend(ArraySegment<byte> segment);

    /// <summary>Tries to send memory.</summary>
    bool TrySend(ReadOnlyMemory<byte> memory);

    /// <summary>Tries to send a copy of the data, so the caller may reuse its buffer immediately.</summary>
    /// <param name="data">The data to send.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    /// <remarks>
    /// Unlike <see cref="TrySend(ArraySegment{byte})"/>, which keeps a reference to the caller's
    /// array until the data has actually been sent, this copies into a pooled buffer owned by the
    /// session.
    /// </remarks>
    bool TrySendCopied(ReadOnlySpan<byte> data);

    /// <summary>Queues the data, waiting asynchronously when the sending queue is full.</summary>
    /// <param name="data">The data to send. Array-backed memory is sent without copying.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space.</param>
    /// <returns>false if the session is closed or was closed while waiting.</returns>
    /// <remarks>
    /// The configured <c>SendTimeOut</c> does not apply; bound the wait with a cancellation token.
    /// </remarks>
    ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the session has nothing left to send: its sending queue is empty and no send
    /// operation is in progress. Used to drain sessions during a graceful shutdown.
    /// </summary>
    bool IsSendIdle { get; }

    /// <summary>Gets the client socket.</summary>
    Socket? Client { get; }

    /// <summary>Gets the local listening endpoint.</summary>
    IPEndPoint? LocalEndPoint { get; }

    /// <summary>Occurs when [closed].</summary>
    Action<ISocketSession, CloseReason>? Closed { get; set; }

    /// <summary>Gets the app session assosiated with this socket session.</summary>
    IAppSession AppSession { get; }


    /// <summary>
    /// Begins closing after a send-queue timeout and aborts the private TCP socket to unblock I/O.
    /// In-flight operations keep ownership of their buffers and event args until completion;
    /// Closed is raised only after those operations finish. The shared UDP listener stays open.
    /// </summary>
    void SendEndWhenSendingTimeOut();
}
