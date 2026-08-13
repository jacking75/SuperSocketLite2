namespace SuperSocketLite.SocketBase;

/// <summary>
/// It is the basic interface of SocketServer,
/// SocketServer is the abstract server who really listen the comming sockets directly.
/// </summary>
public interface ISocketServer
{
    /// <summary>Starts this instance.</summary>
    bool Start();

    /// <summary>Gets a value indicating whether this instance is running.</summary>
    bool IsRunning { get; }

    /// <summary>Stops this instance.</summary>
    void Stop();
}
