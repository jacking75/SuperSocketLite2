namespace SuperSocketLite.LoadTest.ServerProbe;

public static class SessionMetricHooks
{
    public static IDisposable BeginRequest(ServerMetricsCollector collector, object? session, int packetId, long bytesIn)
    {
        return collector.BeginRequest(session, packetId, bytesIn);
    }
}
