using System.Net;

namespace SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Identifies the session a log entry belongs to, so that an adapter can emit the session identity
/// as structured properties instead of receiving it pre-baked into the message text.
/// </summary>
/// <remarks>
/// This is a <c>readonly struct</c> of two reference fields: creating one at a call site costs no
/// heap allocation and passing it by <c>in</c> costs no copy, so a disabled log level stays free.
/// It carries no <c>object</c> parameters on purpose - nothing here is boxed.
/// </remarks>
public readonly struct LogSessionContext
{
    /// <summary>An entry that is not tied to any session.</summary>
    public static readonly LogSessionContext None = default;

    /// <summary>Initializes a new instance of the <see cref="LogSessionContext"/> struct.</summary>
    /// <param name="remoteEndPoint">The remote end point of the session.</param>
    public LogSessionContext(string? sessionId, IPEndPoint? remoteEndPoint)
    {
        SessionId = sessionId;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>Gets the session ID, or null when the entry is not tied to a session.</summary>
    public string? SessionId { get; }

    /// <summary>Gets the remote end point of the session, or null when it is unknown.</summary>
    public IPEndPoint? RemoteEndPoint { get; }

    /// <summary>Gets whether this context carries no session identity at all.</summary>
    public bool IsEmpty => SessionId == null && RemoteEndPoint == null;

    /// <summary>
    /// Renders the context as <c>sessionId/remoteEndPoint</c>. Used by adapters that cannot emit
    /// structured properties.
    /// </summary>
    public override string ToString()
    {
        if (IsEmpty)
            return string.Empty;

        return string.Concat(SessionId, "/", RemoteEndPoint?.ToString());
    }
}
