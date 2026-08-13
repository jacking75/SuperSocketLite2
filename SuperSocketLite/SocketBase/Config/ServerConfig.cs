using SuperSocketLite.Common;

namespace SuperSocketLite.SocketBase.Config;

/// <summary>Server configruation model</summary>
[Serializable]
public class ServerConfig : IServerConfig
{
    /// <summary>Default ReceiveBufferSize</summary>
    public const int DefaultReceiveBufferSize = 4096;

    /// <summary>Default MaxConnectionNumber</summary>
    public const int DefaultMaxConnectionNumber = 100;


    /// <summary>Default sending queue size</summary>
    public const int DefaultSendingQueueSize = 5;

    /// <summary>Default MaxRequestLength</summary>
    public const int DefaultMaxRequestLength = 1024;


    /// <summary>Default send timeout value, in milliseconds</summary>
    public const int DefaultSendTimeout = 5000;


    /// <summary>Default clear idle session interval</summary>
    public const int DefaultClearIdleSessionInterval = 120;


    /// <summary>Default idle session timeout</summary>
    public const int DefaultIdleSessionTimeOut = 300;


    /// <summary>The default send buffer size</summary>
    public const int DefaultSendBufferSize = 2048;


    /// <summary>The default session snapshot interval</summary>
    public const int DefaultSessionSnapshotInterval = 5;

    /// <summary>The default keep alive time</summary>
    public const int DefaultKeepAliveTime = 600; // 60 * 10 = 10 minutes


    /// <summary>The default keep alive interval</summary>
    public const int DefaultKeepAliveInterval = 60; // 60 seconds


    /// <summary>The default keep alive retry count</summary>
    public const int DefaultKeepAliveRetryCount = 5;


    /// <summary>The default listen backlog</summary>
    public const int DefaultListenBacklog = 100;


    public ServerConfig(IServerConfig serverConfig)
    {
        serverConfig.CopyPropertiesTo(this);
               
        if (serverConfig.Listeners != null && serverConfig.Listeners.Any())
        {
            this.Listeners = serverConfig.Listeners.Select(l => l.CopyPropertiesTo(new ListenerConfig())).OfType<ListenerConfig>().ToArray();
        }
    }

    public ServerConfig()
    {
        MaxConnectionNumber = DefaultMaxConnectionNumber;
        Mode = SocketMode.Tcp;
        MaxRequestLength = DefaultMaxRequestLength;
        KeepAliveTime = DefaultKeepAliveTime;
        KeepAliveInterval = DefaultKeepAliveInterval;
        ListenBacklog = DefaultListenBacklog;
        ReceiveBufferSize = DefaultReceiveBufferSize;
        SendingQueueSize = DefaultSendingQueueSize;
        SendTimeOut = DefaultSendTimeout;
        ClearIdleSessionInterval = DefaultClearIdleSessionInterval;
        IdleSessionTimeOut = DefaultIdleSessionTimeOut;
        SendBufferSize = DefaultSendBufferSize;
        SessionSnapshotInterval = DefaultSessionSnapshotInterval;
    }

    /// <summary>Gets/sets the ip.</summary>
    public string? Ip { get; set; }

    /// <summary>Gets/sets the port.</summary>
    public int Port { get; set; }

    /// <summary>Gets the name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets/sets the mode.</summary>
    public SocketMode Mode { get; set; }

    /// <summary>Gets/sets the send time out.</summary>
    public int SendTimeOut { get; set; }

    /// <summary>Gets the max connection number.</summary>
    public int MaxConnectionNumber { get; set; }

    /// <summary>Gets the size of the receive buffer.</summary>
    public int ReceiveBufferSize { get; set; }

    /// <summary>Gets the size of the send buffer.</summary>
    public int SendBufferSize { get; set; }

    /// <summary>Gets a value indicating whether sending is in synchronous mode.</summary>
    public bool SyncSend { get; set; }


    /// <summary>Gets/sets a value indicating whether clear idle session.</summary>
    public bool ClearIdleSession { get; set; }

    /// <summary>Gets/sets the clear idle session interval, in seconds.</summary>
    public int ClearIdleSessionInterval { get; set; }

    /// <summary>Gets/sets the idle session timeout time length, in seconds.</summary>
    public int IdleSessionTimeOut { get; set; }
          
    /// <summary>Gets/sets the length of the max request.</summary>
    public int MaxRequestLength { get; set; }

    /// <summary>Gets/sets a value indicating whether [disable session snapshot].</summary>
    public bool DisableSessionSnapshot { get; set; }

    /// <summary>Gets/sets the interval to taking snapshot for all live sessions.</summary>
    public int SessionSnapshotInterval { get; set; }

    /// <summary>Gets/sets the start keep alive time, in seconds</summary>
    public int KeepAliveTime { get; set; }

    /// <summary>Gets/sets the keep alive interval, in seconds.</summary>
    public int KeepAliveInterval { get; set; }

    /// <summary>
    /// Gets/sets how many unacknowledged keep-alive probes are sent before the connection is
    /// considered dead. 0 or less leaves the OS default in place.
    /// </summary>
    /// <remarks>
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>,
    /// so custom <see cref="IServerConfig"/> implementations fall back to
    /// <see cref="DefaultKeepAliveRetryCount"/>.
    /// </remarks>
    public int KeepAliveRetryCount { get; set; } = DefaultKeepAliveRetryCount;

    /// <summary>Gets the backlog size of socket listening.</summary>
    public int ListenBacklog { get; set; }

    /// <summary>Gets and sets the listeners' configuration.</summary>
    public IEnumerable<IListenerConfig>? Listeners { get; set; }

    /// <summary>Gets/sets the size of the sending queue.</summary>
    public int SendingQueueSize { get; set; }

    /// <summary>Gets/sets a value indicating whether [log all socket exception].</summary>
    public bool LogAllSocketException { get; set; }

    /// <summary>Gets/sets the default text encoding.</summary>
    public string? TextEncoding { get; set; }

    public bool NoDelay { get; set; }


    /// <summary>
    /// Gets or sets a value indicating whether to pre-allocate SocketAsyncEventArgs objects at startup.
    /// true: Pre-allocate MaxConnectionNumber SAEA objects at startup (default, better performance)
    /// false: Start with MinPoolSize and grow dynamically as needed
    /// </summary>
    public bool PreAllocateSAEA { get; set; } = true;

    /// <summary>Gets or sets the minimum pool size for SAEA objects when PreAllocateSAEA is false.</summary>
    public int MinPoolSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether a completed receive is processed directly on the IOCP completion
    /// thread (default) instead of being dispatched to the thread pool.
    /// </summary>
    /// <remarks>
    /// Receive completion only advances the receive pipe and posts the next receive; the
    /// application's request handlers run on the separate pipe-reader task. Running it inline
    /// therefore saves a thread hop plus one closure and two Task allocations per received packet.
    /// Set to false only to restore the old dispatching behaviour.
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>,
    /// so custom <see cref="IServerConfig"/> implementations always get the inline behaviour.
    /// </remarks>
    public bool ReceiveInlineOnIocpThread { get; set; } = true;

    /// <summary>
    /// Gets or sets how many received bytes a session may buffer before the receive loop pauses,
    /// in bytes. 0 or less uses the System.IO.Pipelines default (65536).
    /// </summary>
    /// <remarks>
    /// This is the back-pressure threshold of the per-session receive pipe. Raise it when request
    /// handlers are slow and bursty traffic must not stall; lower it to cap per-session memory.
    /// The effective value is at least twice <see cref="ReceiveBufferSize"/>, because a pipe
    /// requires its pause threshold to be no smaller than one segment.
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>.
    /// </remarks>
    public int MaxReceivePipeBufferSize { get; set; } = 65536;

    /// <summary>The number of accept loops a listener runs when none is configured.</summary>
    public const int DefaultAcceptLoopCount = 1;

    /// <summary>The largest number of accept loops a single listener will run.</summary>
    public const int MaxAcceptLoopCount = 64;

    /// <summary>
    /// Gets or sets how many accept loops each listener runs concurrently on its listening socket.
    /// </summary>
    /// <remarks>
    /// Accepting a connection is not just the accept call: the socket options, the session object
    /// and its registration all happen on the accepting thread, so a single loop serialises them.
    /// Raising this to 2-4 helps a server that has to absorb a reconnect storm (a maintenance
    /// window ending, or the server itself restarting); it does nothing for steady traffic.
    /// Values are clamped to 1..<see cref="MaxAcceptLoopCount"/>.
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>,
    /// so custom <see cref="IServerConfig"/> implementations always get a single loop.
    /// </remarks>
    public int AcceptLoopCount { get; set; } = DefaultAcceptLoopCount;

    /// <summary>
    /// Gets or sets whether a session with no traffic waits on a zero-byte receive instead of
    /// holding a receive buffer.
    /// </summary>
    /// <remarks>
    /// By default a session always has a real receive buffer posted, which means every idle
    /// connection keeps <see cref="ReceiveBufferSize"/> bytes borrowed from its receive pipe and
    /// pinned for the socket. With this enabled a session first waits on an empty buffer and only
    /// takes a real one once data has actually arrived, so idle connections cost no receive buffer
    /// at all - worth a lot on a server whose sessions are mostly idle.
    /// The trade-off is one extra receive call per burst of data, so leave it off for servers where
    /// traffic is continuous.
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>.
    /// </remarks>
    public bool UseZeroByteReceive { get; set; } = false;

    /// <summary>Gets or sets whether the NewSessionConnected event is raised synchronously on the accept path.</summary>
    /// <remarks>
    /// By default the event is dispatched to the thread pool, so the first request of a fast client
    /// can reach NewRequestReceived before the connected handler has run. Setting this to true
    /// raises the event synchronously during session registration, which happens before receiving
    /// starts, and therefore guarantees the ordering. The handler then blocks the accept path, so
    /// it must stay short.
    /// This member only exists on <see cref="ServerConfig"/>, not on <see cref="IServerConfig"/>.
    /// </remarks>
    public bool SyncSessionConnectedEvent { get; set; } = false;
}
