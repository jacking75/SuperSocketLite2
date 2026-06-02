using System.Diagnostics;

namespace SuperSocketLite.LoadTest.Shared.Time;

public sealed class MonotonicClock
{
    private readonly long _startedTimestamp;

    private MonotonicClock(long startedTimestamp)
    {
        _startedTimestamp = startedTimestamp;
    }

    public static MonotonicClock StartNew()
    {
        return new MonotonicClock(Stopwatch.GetTimestamp());
    }

    public long ElapsedMilliseconds
    {
        get
        {
            return (long)((Stopwatch.GetTimestamp() - _startedTimestamp) * 1000.0 / Stopwatch.Frequency);
        }
    }

    public long GetTimestampMicroseconds()
    {
        return (long)(Stopwatch.GetTimestamp() * 1_000_000.0 / Stopwatch.Frequency);
    }
}
