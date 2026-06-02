namespace SuperSocketLite.LoadTest.Shared.Metrics;

public sealed class LatencyHistogram
{
    private readonly object _syncRoot = new();
    private readonly List<long> _values = new();

    public void Record(long elapsedMicroseconds)
    {
        if (elapsedMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMicroseconds), "Latency cannot be negative.");

        lock (_syncRoot)
        {
            _values.Add(elapsedMicroseconds);
        }
    }

    public void MergeFrom(LatencyHistogram other)
    {
        var snapshotValues = other.CopyValues();
        lock (_syncRoot)
        {
            _values.AddRange(snapshotValues);
        }
    }

    public HistogramSnapshot Snapshot(bool reset)
    {
        long[] values;
        lock (_syncRoot)
        {
            values = _values.ToArray();
            if (reset)
                _values.Clear();
        }

        if (values.Length == 0)
            return default;

        Array.Sort(values);
        return new HistogramSnapshot(
            values.Length,
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99),
            values[^1]);
    }

    private long[] CopyValues()
    {
        lock (_syncRoot)
        {
            return _values.ToArray();
        }
    }

    private static long Percentile(long[] sortedValues, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sortedValues.Length);
        var index = Math.Clamp(rank - 1, 0, sortedValues.Length - 1);
        return sortedValues[index];
    }
}
