using System.Diagnostics;

namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class RequestMetricRecorder : IDisposable
{
    private readonly ServerMetricsCollector _collector;
    private readonly string _sessionId;
    private readonly int _packetId;
    private readonly long _bytesIn;
    private readonly long _started;
    private bool _disposed;

    internal RequestMetricRecorder(ServerMetricsCollector collector, string sessionId, int packetId, long bytesIn)
    {
        _collector = collector;
        _sessionId = sessionId;
        _packetId = packetId;
        _bytesIn = bytesIn;
        _started = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var elapsedUs = (long)((Stopwatch.GetTimestamp() - _started) * 1_000_000.0 / Stopwatch.Frequency);
        _collector.RecordRequest(_sessionId, _packetId, _bytesIn, elapsedUs);
    }
}
