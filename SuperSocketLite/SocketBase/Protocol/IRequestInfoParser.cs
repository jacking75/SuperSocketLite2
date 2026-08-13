namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>The interface for request info parser</summary>
public interface IRequestInfoParser<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    /// <summary>Parses the request info from the source string.</summary>
    TRequestInfo ParseRequestInfo(string source);
}
