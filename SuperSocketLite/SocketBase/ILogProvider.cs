using SuperSocketLite.SocketBase.Logging;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// The interface for who provides logger
/// </summary>
/// <remarks>
/// Named <c>ILogProvider</c>, not <c>ILogProvider</c>, so that it does not collide by simple name
/// with <c>Microsoft.Extensions.Logging.ILogProvider</c> in files that use both namespaces.
/// </remarks>
public interface ILogProvider
{
    /// <summary>
    /// Gets the logger assosiated with this object.
    /// </summary>
    ILog Logger { get; }
}
