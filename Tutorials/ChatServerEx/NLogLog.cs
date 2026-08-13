#nullable enable

using System;
using SuperSocketLite.SocketBase.Logging;

namespace ChatServer;

/// <summary>
/// Adapts an NLog logger to SuperSocketLite's ILog.
/// </summary>
public class NLogLog : ILog
{
    private readonly NLog.ILogger _logger;

    public NLogLog(NLog.ILogger log)
    {
        _logger = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsTraceEnabled => _logger.IsTraceEnabled;

    public bool IsDebugEnabled => _logger.IsDebugEnabled;

    public bool IsInfoEnabled => _logger.IsInfoEnabled;

    public bool IsWarnEnabled => _logger.IsWarnEnabled;

    public bool IsErrorEnabled => _logger.IsErrorEnabled;

    public bool IsFatalEnabled => _logger.IsFatalEnabled;

    public void Trace(string message) => _logger.Trace(message);

    public void Debug(string message) => _logger.Debug(message);

    public void Info(string message) => _logger.Info(message);

    public void Warn(string message) => _logger.Warn(message);

    public void Error(string message) => _logger.Error(message);

    public void Fatal(string message) => _logger.Fatal(message);

    // The exception goes to NLog as an exception, not as text, so ${exception} layouts work.
    public void Trace(string message, Exception exception) => _logger.Trace(exception, message);

    public void Debug(string message, Exception exception) => _logger.Debug(exception, message);

    public void Info(string message, Exception exception) => _logger.Info(exception, message);

    public void Warn(string message, Exception exception) => _logger.Warn(exception, message);

    public void Error(string message, Exception exception) => _logger.Error(exception, message);

    public void Fatal(string message, Exception exception) => _logger.Fatal(exception, message);

    /// <summary>
    /// Emits the session identity as NLog event properties instead of baking it into the message,
    /// so structured targets (JSON, database, ...) get separate SessionId / RemoteEndPoint fields.
    /// </summary>
    public void Log(LogEventLevel level, in LogSessionContext session, string message, Exception? exception = null)
    {
        var nlogLevel = ToNLogLevel(level);

        if (!_logger.IsEnabled(nlogLevel))
            return;

        var logEvent = new NLog.LogEventInfo(nlogLevel, _logger.Name, message)
        {
            Exception = exception
        };

        if (!session.IsEmpty)
        {
            logEvent.Properties["SessionId"] = session.SessionId;
            logEvent.Properties["RemoteEndPoint"] = session.RemoteEndPoint;
        }

        _logger.Log(logEvent);
    }

    private static NLog.LogLevel ToNLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Trace => NLog.LogLevel.Trace,
        LogEventLevel.Debug => NLog.LogLevel.Debug,
        LogEventLevel.Info => NLog.LogLevel.Info,
        LogEventLevel.Warn => NLog.LogLevel.Warn,
        LogEventLevel.Error => NLog.LogLevel.Error,
        LogEventLevel.Fatal => NLog.LogLevel.Fatal,
        _ => NLog.LogLevel.Off,
    };
}
