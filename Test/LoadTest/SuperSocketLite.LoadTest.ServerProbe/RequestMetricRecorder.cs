using System.Diagnostics;

namespace SuperSocketLite.LoadTest.ServerProbe;

/// <summary>요청 하나의 처리 시간을 재서 <see cref="Dispose"/>에서 수집기에 넘깁니다.</summary>
/// <remarks>
/// 구조체입니다. 요청마다 만들어지므로 클래스로 두면 계측 자체가 초당 수만 건의 할당을 만들어
/// 정작 재려는 alloc-rate를 가려 버립니다.
///
/// <c>using var</c>로만 쓰세요. 복사해서 <see cref="Dispose"/>를 두 번 부르면 요청이 두 번 기록됩니다.
/// <c>default</c>는 아무것도 기록하지 않으므로, 계측기가 없을 때 그대로 쓸 수 있습니다.
/// </remarks>
public readonly struct RequestMetricRecorder : IDisposable
{
    private readonly ServerMetricsCollector? _collector;
    private readonly string _sessionId;
    private readonly int _packetId;
    private readonly long _bytesIn;
    private readonly long _started;

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
        if (_collector is null)
            return;

        var elapsedUs = (long)((Stopwatch.GetTimestamp() - _started) * 1_000_000.0 / Stopwatch.Frequency);
        _collector.RecordRequest(_sessionId, _packetId, _bytesIn, elapsedUs);
    }
}
