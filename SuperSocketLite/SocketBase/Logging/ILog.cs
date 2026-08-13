using System;

namespace SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Log interface.
/// </summary>
/// <remarks>
/// <para>
/// Only the six level flags and the eight plain methods are required; everything else has a default
/// implementation that degrades to them, so a minimal adapter stays short.
/// </para>
/// <para>
/// An adapter over a library that supports structured logging (Serilog, NLog, ZLogger, anything
/// behind <c>Microsoft.Extensions.Logging</c>) should additionally override
/// <see cref="Log(LogEventLevel, in LogSessionContext, string, Exception?)"/> so that the session
/// identity is emitted as separate properties. See <c>MicrosoftLoggingLog</c> for a worked example.
/// </para>
/// <para>
/// Always guard a call with the matching <c>IsXxxEnabled</c> flag (or <see cref="IsEnabled"/>) when
/// building the message costs anything - that is what keeps a disabled level free.
/// </para>
/// </remarks>
public interface ILog
{
    /// <summary>Gets whether <see cref="LogEventLevel.Trace"/> is enabled.</summary>
    /// <remarks>Defaults to false: the library never emits Trace unless an adapter opts in.</remarks>
    bool IsTraceEnabled => false;

    /// <summary>Gets whether <see cref="LogEventLevel.Debug"/> is enabled.</summary>
    bool IsDebugEnabled { get; }

    /// <summary>Gets whether <see cref="LogEventLevel.Info"/> is enabled.</summary>
    bool IsInfoEnabled { get; }

    /// <summary>Gets whether <see cref="LogEventLevel.Warn"/> is enabled.</summary>
    bool IsWarnEnabled { get; }

    /// <summary>Gets whether <see cref="LogEventLevel.Error"/> is enabled.</summary>
    bool IsErrorEnabled { get; }

    /// <summary>Gets whether <see cref="LogEventLevel.Fatal"/> is enabled.</summary>
    bool IsFatalEnabled { get; }

    /// <summary>
    /// Gets whether the given level is enabled.
    /// </summary>
    /// <param name="level">The level.</param>
    bool IsEnabled(LogEventLevel level) => level switch
    {
        LogEventLevel.Trace => IsTraceEnabled,
        LogEventLevel.Debug => IsDebugEnabled,
        LogEventLevel.Info => IsInfoEnabled,
        LogEventLevel.Warn => IsWarnEnabled,
        LogEventLevel.Error => IsErrorEnabled,
        LogEventLevel.Fatal => IsFatalEnabled,
        _ => false,
    };

    /// <summary>Writes a Trace entry. Defaults to writing it as Debug.</summary>
    /// <param name="message">The message.</param>
    void Trace(string message) => Debug(message);

    /// <summary>Writes a Debug entry.</summary>
    /// <param name="message">The message.</param>
    void Debug(string message);

    /// <summary>Writes an Info entry.</summary>
    /// <param name="message">The message.</param>
    void Info(string message);

    /// <summary>Writes a Warn entry.</summary>
    /// <param name="message">The message.</param>
    void Warn(string message);

    /// <summary>Writes an Error entry.</summary>
    /// <param name="message">The message.</param>
    void Error(string message);

    /// <summary>Writes a Fatal entry.</summary>
    /// <param name="message">The message.</param>
    void Fatal(string message);

    /// <summary>Writes a Trace entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Trace(string message, Exception exception) => Trace(Combine(message, exception));

    /// <summary>Writes a Debug entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Debug(string message, Exception exception) => Debug(Combine(message, exception));

    /// <summary>Writes an Info entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Info(string message, Exception exception) => Info(Combine(message, exception));

    /// <summary>Writes a Warn entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Warn(string message, Exception exception) => Warn(Combine(message, exception));

    /// <summary>Writes an Error entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Error(string message, Exception exception);

    /// <summary>Writes a Fatal entry with an exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    void Fatal(string message, Exception exception);

    /// <summary>
    /// Writes an entry that belongs to a specific session.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="session">The session the entry belongs to.</param>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception, or null.</param>
    /// <remarks>
    /// The default implementation flattens <paramref name="session"/> into the message text and
    /// forwards to the plain method for <paramref name="level"/>. Override it to emit the session ID
    /// and the remote end point as structured properties.
    /// </remarks>
    void Log(LogEventLevel level, in LogSessionContext session, string message, Exception? exception = null)
    {
        var text = session.IsEmpty ? message : string.Concat("[", session.ToString(), "] ", message);

        switch (level)
        {
            case LogEventLevel.Trace:
                if (exception == null) Trace(text); else Trace(text, exception);
                break;
            case LogEventLevel.Debug:
                if (exception == null) Debug(text); else Debug(text, exception);
                break;
            case LogEventLevel.Info:
                if (exception == null) Info(text); else Info(text, exception);
                break;
            case LogEventLevel.Warn:
                if (exception == null) Warn(text); else Warn(text, exception);
                break;
            case LogEventLevel.Error:
                if (exception == null) Error(text); else Error(text, exception);
                break;
            case LogEventLevel.Fatal:
                if (exception == null) Fatal(text); else Fatal(text, exception);
                break;
        }
    }

    /// <summary>
    /// Appends an exception to a message for adapters that cannot carry it separately.
    /// </summary>
    private static string Combine(string message, Exception exception)
    {
        return string.Concat(message, " | ", exception.ToString());
    }
}
