namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>UdpRequestInfo, it is designed for passing in business session ID to udp request info</summary>
public class UdpRequestInfo : IRequestInfo
{
    public UdpRequestInfo(string key, string sessionID)
    {
        Key = key;
        SessionID = sessionID;
    }

    /// <summary>Gets the key of this request.</summary>
    public string Key { get; private set; }

    /// <summary>Gets the session ID.</summary>
    public string SessionID { get; private set; }
}
