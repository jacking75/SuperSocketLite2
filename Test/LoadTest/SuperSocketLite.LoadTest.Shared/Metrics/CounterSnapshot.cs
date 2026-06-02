namespace SuperSocketLite.LoadTest.Shared.Metrics;

public readonly record struct CounterSnapshot(long ElapsedMs, long Total)
{
    public long DeltaFrom(CounterSnapshot previous)
    {
        return Total - previous.Total;
    }

    public double PerSecondFrom(CounterSnapshot previous)
    {
        var elapsedDeltaMs = ElapsedMs - previous.ElapsedMs;
        if (elapsedDeltaMs <= 0)
            return 0;

        return DeltaFrom(previous) * 1000.0 / elapsedDeltaMs;
    }
}
