namespace SuperSocketLite.SocketBase.Config;

/// <summary>Listener configuration model</summary>
[Serializable]
public class ListenerConfig : IListenerConfig
{
    public ListenerConfig()
    {
        Backlog = 100;
    }

    /// <summary>Gets the ip of listener</summary>
    public string? Ip { get; set; }

    /// <summary>Gets the port of listener</summary>
    public int Port { get; set; }


    /// <summary>Gets the backlog.</summary>
    public int Backlog { get; set; }

    /// <summary>Gets/sets the security option, None/Default/Tls/Ssl/...</summary>
    public string? Security { get; set; }
}
