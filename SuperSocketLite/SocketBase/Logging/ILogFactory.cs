namespace SuperSocketLite.SocketBase.Logging;

/// <summary>LogFactory Interface</summary>
public interface ILogFactory
{
    /// <summary>Gets the log by name.</summary>
    ILog GetLog(string name);
}
