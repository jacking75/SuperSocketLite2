using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>The interface for socket server factory</summary>
public interface ISocketServerFactory
{
    /// <summary>Creates the socket server instance.</summary>
    /// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
    ISocketServer CreateSocketServer<TRequestInfo>(IAppServer appServer, ListenerInfo[] listeners, IServerConfig config)
        where TRequestInfo : IRequestInfo;
}
