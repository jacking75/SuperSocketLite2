// Needed for the LoggerExtensions.Log(level, exception, template, args) extension methods.
using Microsoft.Extensions.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using MsILogger = Microsoft.Extensions.Logging.ILogger;

namespace SuperSocketLite.SocketBase.Logging;

/// <summary>Adapts a <c>Microsoft.Extensions.Logging.ILogger</c> to <see cref="ILog"/>.</summary>
/// <remarks>
/// Because Serilog, NLog, ZLogger, log4net and others all ship an
/// <c>Microsoft.Extensions.Logging</c> provider, this single adapter is enough to run the server on
/// any of them - there is no need to write a per-library adapter.
/// </remarks>
public sealed class MicrosoftLoggingLog : ILog
{
    // A constant template, so braces that happen to appear in the message are never re-parsed as
    // placeholders, and "Message" arrives at the sink as a named property rather than as prose.
    private const string MessageTemplate = "{Message}";

    private const string SessionMessageTemplate = "[{SessionId}/{RemoteEndPoint}] {Message}";

    private readonly MsILogger _logger;

    /// <param name="logger">The logger to write to.</param>
    public MicrosoftLoggingLog(MsILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsTraceEnabled => _logger.IsEnabled(MsLogLevel.Trace);

    /// <inheritdoc />
    public bool IsDebugEnabled => _logger.IsEnabled(MsLogLevel.Debug);

    /// <inheritdoc />
    public bool IsInfoEnabled => _logger.IsEnabled(MsLogLevel.Information);

    /// <inheritdoc />
    public bool IsWarnEnabled => _logger.IsEnabled(MsLogLevel.Warning);

    /// <inheritdoc />
    public bool IsErrorEnabled => _logger.IsEnabled(MsLogLevel.Error);

    /// <inheritdoc />
    public bool IsFatalEnabled => _logger.IsEnabled(MsLogLevel.Critical);

    /// <inheritdoc />
    public bool IsEnabled(LogEventLevel level) => _logger.IsEnabled(ToMsLevel(level));

    /// <inheritdoc />
    public void Trace(string message) => Write(MsLogLevel.Trace, message, null);

    /// <inheritdoc />
    public void Debug(string message) => Write(MsLogLevel.Debug, message, null);

    /// <inheritdoc />
    public void Info(string message) => Write(MsLogLevel.Information, message, null);

    /// <inheritdoc />
    public void Warn(string message) => Write(MsLogLevel.Warning, message, null);

    /// <inheritdoc />
    public void Error(string message) => Write(MsLogLevel.Error, message, null);

    /// <inheritdoc />
    public void Fatal(string message) => Write(MsLogLevel.Critical, message, null);

    /// <inheritdoc />
    public void Trace(string message, Exception exception) => Write(MsLogLevel.Trace, message, exception);

    /// <inheritdoc />
    public void Debug(string message, Exception exception) => Write(MsLogLevel.Debug, message, exception);

    /// <inheritdoc />
    public void Info(string message, Exception exception) => Write(MsLogLevel.Information, message, exception);

    /// <inheritdoc />
    public void Warn(string message, Exception exception) => Write(MsLogLevel.Warning, message, exception);

    /// <inheritdoc />
    public void Error(string message, Exception exception) => Write(MsLogLevel.Error, message, exception);

    /// <inheritdoc />
    public void Fatal(string message, Exception exception) => Write(MsLogLevel.Critical, message, exception);

    /// <inheritdoc />
    /// <remarks>
    /// Emits the session identity as the <c>SessionId</c> and <c>RemoteEndPoint</c> properties, so a
    /// JSON sink gets separate fields instead of one opaque string.
    /// </remarks>
    public void Log(LogEventLevel level, in LogSessionContext session, string message, Exception? exception = null)
    {
        var msLevel = ToMsLevel(level);

        if (!_logger.IsEnabled(msLevel))
            return;

        if (session.IsEmpty)
        {
            _logger.Log(msLevel, exception, MessageTemplate, message);
            return;
        }

        _logger.Log(msLevel, exception, SessionMessageTemplate, session.SessionId, session.RemoteEndPoint, message);
    }

    private void Write(MsLogLevel level, string message, Exception? exception)
    {
        if (_logger.IsEnabled(level))
            _logger.Log(level, exception, MessageTemplate, message);
    }

    private static MsLogLevel ToMsLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Trace => MsLogLevel.Trace,
        LogEventLevel.Debug => MsLogLevel.Debug,
        LogEventLevel.Info => MsLogLevel.Information,
        LogEventLevel.Warn => MsLogLevel.Warning,
        LogEventLevel.Error => MsLogLevel.Error,
        LogEventLevel.Fatal => MsLogLevel.Critical,
        _ => MsLogLevel.None,
    };
}
