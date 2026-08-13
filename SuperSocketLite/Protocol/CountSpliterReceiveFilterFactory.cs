using System.Net;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.SocketEngine.Protocol;

/// <summary>ReceiveFilterFactory for CountSpliterReceiveFilter</summary>
/// <typeparam name="TRequestFilter">The type of the Receive filter.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public class CountSpliterReceiveFilterFactory<TRequestFilter, TRequestInfo> : IReceiveFilterFactory<TRequestInfo>
    where TRequestFilter : CountSpliterReceiveFilter<TRequestInfo>, new()
    where TRequestInfo : IRequestInfo
{
    /// <summary>Creates the filter.</summary>
    public IReceiveFilter<TRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, IPEndPoint? remoteEndPoint)
    {
        var config = appServer.Config;

        if(config.MaxRequestLength > config.ReceiveBufferSize)
            throw new Exception("ReceiveBufferSize cannot smaller than MaxRequestLength in this protocol.");

        return new TRequestFilter();
    }
}

/// <summary>ReceiveFilterFactory for CountSpliterReceiveFilter</summary>
/// <typeparam name="TRequestFilter">The type of the Receive filter.</typeparam>
public class CountSpliterReceiveFilterFactory<TRequestFilter> : CountSpliterReceiveFilterFactory<TRequestFilter, StringRequestInfo>
    where TRequestFilter : CountSpliterReceiveFilter<StringRequestInfo>, new()
{

}

/// <summary>receiveFilterFactory for CountSpliterRequestFilter</summary>
public class  CountSpliterReceiveFilterFactory : IReceiveFilterFactory<StringRequestInfo>
{
    private readonly byte _spliter;

    private readonly int _spliterCount;

    public CountSpliterReceiveFilterFactory(byte spliter, int count)
    {
        _spliter = spliter;
        _spliterCount = count;
    }

    /// <summary>Creates the filter.</summary>
    public IReceiveFilter<StringRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, IPEndPoint? remoteEndPoint)
    {
        return new CountSpliterReceiveFilter(_spliter, _spliterCount);
    }
}
