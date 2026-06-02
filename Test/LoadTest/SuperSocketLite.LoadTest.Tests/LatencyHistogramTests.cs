using SuperSocketLite.LoadTest.Shared.Metrics;

namespace SuperSocketLite.LoadTest.Tests;

internal static class LatencyHistogramTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(CalculatesPercentilesAndMax), CalculatesPercentilesAndMax);
        yield return new TestCase(nameof(SnapshotCanResetHistogram), SnapshotCanResetHistogram);
        yield return new TestCase(nameof(MergesLocalHistograms), MergesLocalHistograms);
    }

    private static void CalculatesPercentilesAndMax()
    {
        var histogram = new LatencyHistogram();
        for (var i = 1; i <= 100; i++)
            histogram.Record(i);

        var snapshot = histogram.Snapshot(reset: false);

        AssertEx.Equal(100, snapshot.Count);
        AssertEx.Equal(50, snapshot.P50Us);
        AssertEx.Equal(95, snapshot.P95Us);
        AssertEx.Equal(99, snapshot.P99Us);
        AssertEx.Equal(100, snapshot.MaxUs);
    }

    private static void SnapshotCanResetHistogram()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(10);

        AssertEx.Equal(1, histogram.Snapshot(reset: true).Count);
        AssertEx.Equal(0, histogram.Snapshot(reset: false).Count);
    }

    private static void MergesLocalHistograms()
    {
        var left = new LatencyHistogram();
        var right = new LatencyHistogram();
        left.Record(10);
        right.Record(20);
        right.Record(30);

        left.MergeFrom(right);

        var snapshot = left.Snapshot(reset: false);
        AssertEx.Equal(3, snapshot.Count);
        AssertEx.Equal(30, snapshot.MaxUs);
    }
}
