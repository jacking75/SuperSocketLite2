namespace SuperSocketLite.SocketBase.Logging;

/// <summary>Console log factory</summary>
public class ConsoleLogFactory : ILogFactory
{
    /// <summary>Gets the log by name.</summary>
    public ILog GetLog(string name)
    {
        return new ConsoleLog(name);
    }
}
