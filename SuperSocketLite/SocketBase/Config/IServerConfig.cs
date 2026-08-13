using System.Collections.Generic;

namespace SuperSocketLite.SocketBase.Config;

/// <summary>
/// Server instance configuation interface
/// </summary>
public interface IServerConfig
{
    /// <summary>
    /// Gets the ip.
    /// </summary>
    string? Ip { get; }

    /// <summary>
    /// Gets the port.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the mode.
    /// </summary>
    SocketMode Mode { get; }

    /// <summary>
    /// Gets the send time out.
    /// </summary>
    int SendTimeOut { get; }

    /// <summary>
    /// Gets the max connection number.
    /// </summary>
    int MaxConnectionNumber { get; }

    /// <summary>
    /// Gets the size of the receive buffer.
    /// </summary>
    /// <value>
    /// The size of the receive buffer.
    /// </value>
    int ReceiveBufferSize { get; }

    /// <summary>
    /// Gets the size of the send buffer.
    /// </summary>
    /// <value>
    /// The size of the send buffer.
    /// </value>
    int SendBufferSize { get; }

    /// <summary>
    /// Gets a value indicating whether sending is in synchronous mode.
    /// </summary>
    /// <value>
    ///   <c>true</c> if [sync send]; otherwise, <c>false</c>.
    /// </value>
    bool SyncSend { get; }

    /// <summary>
    /// Gets a value indicating whether log command in log file.
    /// </summary>
    /// <value><c>true</c> if log command; otherwise, <c>false</c>.</value>
    bool LogCommand { get; }

    /// <summary>
    /// Gets a value indicating whether clear idle session.
    /// </summary>
    /// <value><c>true</c> if clear idle session; otherwise, <c>false</c>.</value>
    bool ClearIdleSession { get; }

    /// <summary>
    /// Gets the clear idle session interval, in seconds.
    /// </summary>
    /// <value>The clear idle session interval.</value>
    int ClearIdleSessionInterval { get; }

    /// <summary>
    /// Gets the idle session timeout time length, in seconds.
    /// </summary>
    /// <value>The idle session time out.</value>
    int IdleSessionTimeOut { get; }
           
    /// <summary>
    /// Gets the length of the max request.
    /// </summary>
    /// <value>
    /// The length of the max request.
    /// </value>
    int MaxRequestLength { get; }

    /// <summary>
    /// Gets a value indicating whether [disable session snapshot].
    /// </summary>
    /// <value>
    /// 	<c>true</c> if [disable session snapshot]; otherwise, <c>false</c>.
    /// </value>
    bool DisableSessionSnapshot { get; }

    /// <summary>
    /// Gets the interval to taking snapshot for all live sessions.
    /// </summary>
    int SessionSnapshotInterval { get; }
    
    /// <summary>
    /// Gets the start keep alive time, in seconds
    /// </summary>
    int KeepAliveTime { get; }

    /// <summary>
    /// Gets the keep alive interval, in seconds.
    /// </summary>
    int KeepAliveInterval { get; }

    /// <summary>
    /// Gets the backlog size of socket listening.
    /// </summary>
    int ListenBacklog { get; }

    /// <summary>
    /// Gets the listeners' configuration.
    /// </summary>
    IEnumerable<IListenerConfig>? Listeners { get; }

    /// <summary>
    /// Gets the size of the sending queue.
    /// </summary>
    /// <value>
    /// The size of the sending queue.
    /// </value>
    int SendingQueueSize { get; }

    /// <summary>
    /// Gets a value indicating whether [log all socket exception].
    /// </summary>
    /// <value>
    /// <c>true</c> if [log all socket exception]; otherwise, <c>false</c>.
    /// </value>
    bool LogAllSocketException { get; }

    /// <summary>
    /// Gets the default text encoding.
    /// </summary>
    /// <value>
    /// The text encoding.
    /// </value>
    string? TextEncoding { get; }

    /// <summary>
    /// Nodelay
    /// </summary>
    /// <value>
    /// 	<c>true</c> if [disable nagel]; otherwise, <c>false</c>.
    /// </value>
    bool NoDelay { get; }

    /// <summary>
    /// Gets the interval for collect send, in milliseconds.
    /// </summary>
    /// <value>
    /// default is 0
    /// </value>
    int CollectSendIntervalMillSec { get; }

    /// <summary>
    /// Gets a value indicating whether to pre-allocate SocketAsyncEventArgs objects at startup.
    /// true: Pre-allocate MaxConnectionNumber SAEA objects at startup (default, better performance)
    /// false: Start with MinPoolSize and grow dynamically as needed
    /// </summary>
    bool PreAllocateSAEA { get; }

    /// <summary>
    /// Gets the minimum pool size for SAEA objects when PreAllocateSAEA is false.
    /// Default is 100.
    /// </summary>
    int MinPoolSize { get; }
}