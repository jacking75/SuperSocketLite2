using System.Net.Sockets;
using SuperSocketLite.SocketBase;

namespace SuperSocketLite.SocketEngine;

interface IAsyncSocketSessionBase : ILoggerProvider
{
    SocketAsyncEventArgsProxy SocketAsyncProxy { get; }
    
    SocketAsyncEventArgs? SendSAEA { get; }
    
    Socket? Client { get; }
}

interface IAsyncSocketSession : IAsyncSocketSessionBase
{
    void ProcessReceive(SocketAsyncEventArgs e);

    /// <summary>
    /// When true the IOCP completion thread runs <see cref="ProcessReceive"/> directly instead of
    /// dispatching it to the thread pool. See <c>ServerConfig.ReceiveInlineOnIocpThread</c>.
    /// </summary>
    bool ReceiveInlineOnIocpThread { get; }
}
