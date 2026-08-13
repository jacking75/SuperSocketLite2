using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// CloseReason enum
/// </summary>
public enum CloseReason : int
{
    /// <summary>
    /// The socket is closed for unknown reason
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Close for server shutdown
    /// </summary>
    ServerShutdown = 1,

    /// <summary>
    /// The client close the socket
    /// </summary>
    ClientClosing = 2,

    /// <summary>
    /// The server side close the socket
    /// </summary>
    ServerClosing = 3,

    /// <summary>
    /// Application error
    /// </summary>
    ApplicationError = 4,

    /// <summary>
    /// The socket is closed for a socket error
    /// </summary>
    SocketError = 5,

    /// <summary>
    /// The socket is closed by server for timeout
    /// </summary>
    TimeOut = 6,

    /// <summary>
    /// Protocol error 
    /// </summary>
    ProtocolError = 7,

    /// <summary>
    /// SuperSocket internal error
    /// </summary>
    InternalError = 8,
}

/// <summary>
/// The interface for socket session
/// </summary>
public interface ISocketSession : ISessionBase
{
    /// <summary>
    /// Initializes the specified app session.
    /// </summary>
    /// <param name="appSession">The app session.</param>
    void Initialize(IAppSession appSession);

    /// <summary>
    /// Starts this instance.
    /// </summary>
    void Start();

    /// <summary>
    /// Closes the socket session for the specified reason.
    /// </summary>
    /// <param name="reason">The reason.</param>
    void Close(CloseReason reason);


    bool CollectSend(byte[] source, int pos, int count);


    ArraySegment<byte> GetCollectSendData();

    void CommitCollectSend(int size);
    

    /// <summary>
    /// Tries to send array segment.
    /// </summary>
    /// <param name="segments">The segments.</param>
    bool TrySend(IList<ArraySegment<byte>> segments);

    /// <summary>
    /// Tries to send array segment.
    /// </summary>
    /// <param name="segment">The segment.</param>
    bool TrySend(ArraySegment<byte> segment);

    /// <summary>
    /// Tries to send memory.
    /// </summary>
    /// <param name="memory">The memory.</param>
    bool TrySend(ReadOnlyMemory<byte> memory);

    /// <summary>
    /// Tries to send a copy of the data, so the caller may reuse its buffer immediately.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    /// <remarks>
    /// Unlike <see cref="TrySend(ArraySegment{byte})"/>, which keeps a reference to the caller's
    /// array until the data has actually been sent, this copies into a pooled buffer owned by the
    /// session. The default implementation allocates; <c>SocketSession</c> overrides it to rent
    /// from <see cref="System.Buffers.ArrayPool{T}"/>.
    /// </remarks>
    bool TrySendCopied(ReadOnlySpan<byte> data)
    {
        return TrySend(new ArraySegment<byte>(data.ToArray()));
    }

    /// <summary>
    /// Queues the data, waiting asynchronously when the sending queue is full.
    /// </summary>
    /// <param name="data">The data to send. Array-backed memory is sent without copying.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space.</param>
    /// <returns>false if the session is closed or was closed while waiting.</returns>
    /// <remarks>
    /// The configured <c>SendTimeOut</c> does not apply; bound the wait with a cancellation token.
    /// The default implementation degrades to a non-waiting <see cref="TrySend(ReadOnlyMemory{byte})"/>;
    /// <c>SocketSession</c> overrides it with a real asynchronous wait.
    /// </remarks>
    ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<bool>(TrySend(data));
    }


    /// <summary>
    /// Gets whether the session has nothing left to send: its sending queue is empty and no send
    /// operation is in progress. Used to drain sessions during a graceful shutdown.
    /// </summary>
    bool IsSendIdle => true;

    /// <summary>
    /// Gets the client socket.
    /// </summary>
    Socket? Client { get; }

    /// <summary>
    /// Gets the local listening endpoint.
    /// </summary>
    IPEndPoint? LocalEndPoint { get; }

    /// <summary>
    /// Occurs when [closed].
    /// </summary>
    Action<ISocketSession, CloseReason>? Closed { get; set; }

    /// <summary>
    /// Gets the app session assosiated with this socket session.
    /// </summary>
    IAppSession AppSession { get; }


    /// <summary>
    /// 최흥배. 보내기 실패 예외가 발생한 경우 보내기 상태를 취소 시켜서 즉각 접속이 종료 되도록 한다.
    /// 클라이언트에게 Send를 할 때 ‘Send Time Out’ 예외가 발생 후 서버에서 해당 클라이언트를 Disconnect 할 수 없다. 단 클라이언트에서 접속을 끊으면 접속이 끊어진다.
    /// 서버에서 Disconnect 할 수 없는 이유는 close 시킬 때 클라이언트의 소켓 상태가 ‘보내는 중’으로 되어 있어서 끝날 때까지 기다리기 때문이다.즉 이런 상태가 되면 해당 클라이언트가 접속을 끊어 줄 때까지 어떻게 할 방법이 없다.
    /// 그래서 코드를 수정하여 강제적으로 끊을 수 있게 해야 한다.
    /// timeout 이 발생하는 사유는 send 큐에 데이터를 못 넣는 경우고, 이후 넣을 때까지 무한 시도를 한다.
    /// 즉 timeout 크면 그만큼 성능에 나쁜 영향을 줄 수 있다.
    /// timeout가 발생하면 OnSendEnd(false); 호출 후 소켓을 짤라야 한다.
    /// </summary>
    void SendEndWhenSendingTimeOut();
}
