using System.Net;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Receive filter factory interface</summary>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public interface IReceiveFilterFactory<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>Creates the Receive filter.</summary>
    /// <returns>the new created request filer assosiated with this socketSession</returns>
    IReceiveFilter<TRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, IPEndPoint? remoteEndPoint);
}
