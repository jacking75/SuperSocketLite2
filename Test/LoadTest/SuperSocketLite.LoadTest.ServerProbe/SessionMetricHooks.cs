namespace SuperSocketLite.LoadTest.ServerProbe;

public static class SessionMetricHooks
{
    /// <summary>
    /// <see cref="ServerMetricsCollector.BeginRequest"/>를 그대로 넘깁니다.
    /// </summary>
    /// <remarks>
    /// 반환 타입이 <see cref="IDisposable"/>이면 안 됩니다.
    /// <see cref="RequestMetricRecorder"/>는 요청당 할당을 없애려고 구조체로 두었는데,
    /// 인터페이스로 반환하면 그 자리에서 박싱되어 없앤 할당이 그대로 되살아납니다.
    /// </remarks>
    public static RequestMetricRecorder BeginRequest(ServerMetricsCollector collector, object? session, int packetId, long bytesIn)
    {
        return collector.BeginRequest(session, packetId, bytesIn);
    }
}
