namespace SuperSocketLite.SocketBase.Logging;

/// <summary>Severity of a log entry.</summary>
/// <remarks>
/// Deliberately named <c>LogEventLevel</c> rather than <c>LogLevel</c> so that it does not collide
/// by simple name with <c>Microsoft.Extensions.Logging.LogLevel</c> in files that use both
/// namespaces.
/// </remarks>
public enum LogEventLevel : byte
{
    /// <summary>Most verbose. Not used by the library itself.</summary>
    Trace = 0,

    /// <summary>Diagnostic detail useful while developing.</summary>
    Debug = 1,

    /// <summary>Normal operational events.</summary>
    Info = 2,

    /// <summary>Something unexpected that the server recovered from.</summary>
    Warn = 3,

    /// <summary>A failure affecting one operation or one session.</summary>
    Error = 4,

    /// <summary>A failure affecting the whole server instance.</summary>
    Fatal = 5,
}
