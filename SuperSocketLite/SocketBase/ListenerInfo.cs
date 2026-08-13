using System.Net;


namespace SuperSocketLite.SocketBase;

/// <summary>Listener inforamtion</summary>
[Serializable]
public class ListenerInfo
{
    /// <summary>Gets or sets the listen endpoint.</summary>
    public IPEndPoint EndPoint { get; set; } = null!;

    /// <summary>Gets or sets the listen backlog.</summary>
    public int BackLog { get; set; }
}
