using System.Net;

namespace SuperSocketLite.SocketBase;

/// <summary>The basic interface of connection filter</summary>
public interface IConnectionFilter
{
    /// <summary>Gets the name of the filter.</summary>
    string Name { get; }

    /// <summary>Whether allows the connect according the remote endpoint</summary>
    bool AllowConnect(IPEndPoint? remoteAddress);
}

