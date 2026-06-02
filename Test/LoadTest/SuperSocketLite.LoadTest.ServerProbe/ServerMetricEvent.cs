namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed record ServerMetricEvent(
    DateTimeOffset TimestampUtc,
    long ElapsedMs,
    string RunId,
    string EventType,
    string SessionId,
    string RemoteEndpoint,
    int PacketId,
    long BytesIn,
    long BytesOut,
    string CloseReason,
    string ErrorType,
    string Message);
