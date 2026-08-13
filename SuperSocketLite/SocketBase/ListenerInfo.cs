using System.Net;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// Listener inforamtion
/// </summary>
[Serializable]
public class ListenerInfo
{
    /// <summary>
    /// Gets or sets the listen endpoint.
    /// </summary>
    /// <value>
    /// The end point.
    /// </value>
    public IPEndPoint EndPoint { get; set; } = null!;

    /// <summary>
    /// Gets or sets the listen backlog.
    /// </summary>
    /// <value>
    /// The back log.
    /// </value>
    public int BackLog { get; set; }
}
