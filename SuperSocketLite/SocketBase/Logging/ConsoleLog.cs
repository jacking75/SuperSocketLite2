namespace SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Console Log. The default log used when no <see cref="ILogFactory"/> is supplied.
/// </summary>
/// <remarks>
/// Every entry is written as a single line so that line-oriented log collectors see one event per
/// line. An exception is appended after the message rather than on its own line.
/// </remarks>
public class ConsoleLog : ILog
{
    private readonly string m_Name;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleLog"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    public ConsoleLog(string name)
    {
        m_Name = name;
    }

    /// <inheritdoc />
    public bool IsTraceEnabled => true;

    /// <inheritdoc />
    public bool IsDebugEnabled => true;

    /// <inheritdoc />
    public bool IsInfoEnabled => true;

    /// <inheritdoc />
    public bool IsWarnEnabled => true;

    /// <inheritdoc />
    public bool IsErrorEnabled => true;

    /// <inheritdoc />
    public bool IsFatalEnabled => true;

    /// <inheritdoc />
    public void Trace(string message) => Write(LogEventLevel.Trace, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Debug(string message) => Write(LogEventLevel.Debug, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Info(string message) => Write(LogEventLevel.Info, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Warn(string message) => Write(LogEventLevel.Warn, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Error(string message) => Write(LogEventLevel.Error, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Fatal(string message) => Write(LogEventLevel.Fatal, LogSessionContext.None, message, null);

    /// <inheritdoc />
    public void Trace(string message, Exception exception) => Write(LogEventLevel.Trace, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Debug(string message, Exception exception) => Write(LogEventLevel.Debug, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Info(string message, Exception exception) => Write(LogEventLevel.Info, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Warn(string message, Exception exception) => Write(LogEventLevel.Warn, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Error(string message, Exception exception) => Write(LogEventLevel.Error, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Fatal(string message, Exception exception) => Write(LogEventLevel.Fatal, LogSessionContext.None, message, exception);

    /// <inheritdoc />
    public void Log(LogEventLevel level, in LogSessionContext session, string message, Exception? exception = null)
    {
        Write(level, session, message, exception);
    }

    private void Write(LogEventLevel level, in LogSessionContext session, string message, Exception? exception)
    {
        var line = string.Concat(m_Name, "-", LevelName(level), ": ");

        if (!session.IsEmpty)
            line = string.Concat(line, "[", session.ToString(), "] ");

        line = string.Concat(line, message);

        if (exception != null)
            line = string.Concat(line, " | ", exception.ToString().Replace(Environment.NewLine, " "));

        Console.WriteLine(line);
    }

    private static string LevelName(LogEventLevel level) => level switch
    {
        LogEventLevel.Trace => "TRACE",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Info => "INFO",
        LogEventLevel.Warn => "WARN",
        LogEventLevel.Error => "ERROR",
        LogEventLevel.Fatal => "FATAL",
        _ => "NONE",
    };
}
