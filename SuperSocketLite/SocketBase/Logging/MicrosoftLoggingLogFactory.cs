using MsILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;

namespace SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Adapts a <c>Microsoft.Extensions.Logging.ILoggerFactory</c> to <see cref="ILogFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the recommended way to plug any modern logging library into the server: configure the
/// library through its own <c>ILoggerFactory</c> / DI setup and hand the factory over.
/// </para>
/// <example>
/// Plain setup:
/// <code>
/// Setup(new RootConfig(), config,
///       logFactory: new MicrosoftLoggingLogFactory(loggerFactory));
/// </code>
/// With the generic host, register it once and let DI supply the <c>ILoggerFactory</c>:
/// <code>
/// services.AddSingleton&lt;ILogFactory, MicrosoftLoggingLogFactory&gt;();
/// </code>
/// </example>
/// </remarks>
public sealed class MicrosoftLoggingLogFactory : ILogFactory
{
    private readonly MsILoggerFactory m_LoggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MicrosoftLoggingLogFactory"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to create loggers from.</param>
    public MicrosoftLoggingLogFactory(MsILoggerFactory loggerFactory)
    {
        m_LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Gets the log by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public ILog GetLog(string name)
    {
        return new MicrosoftLoggingLog(m_LoggerFactory.CreateLogger(name));
    }
}
