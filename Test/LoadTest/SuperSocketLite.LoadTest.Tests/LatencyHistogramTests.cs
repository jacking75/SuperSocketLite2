using SuperSocketLite.LoadTest.Shared.Metrics;

namespace SuperSocketLite.LoadTest.Tests;

internal static class LatencyHistogramTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(CalculatesPercentilesAndMax), CalculatesPercentilesAndMax);
        yield return new TestCase(nameof(SnapshotCanResetHistogram), SnapshotCanResetHistogram);
        yield return new TestCase(nameof(MergesLocalHistograms), MergesLocalHistograms);
        yield return new TestCase(nameof(SnapshotTotalSurvivesWindowResets), SnapshotTotalSurvivesWindowResets);
        yield return new TestCase(nameof(LargeValuesStayWithinRelativeError), LargeValuesStayWithinRelativeError);
        yield return new TestCase(nameof(PercentilesDoNotUnderreport), PercentilesDoNotUnderreport);
        yield return new TestCase(nameof(ConcurrentRecordsAreNotLost), ConcurrentRecordsAreNotLost);
    }

    private static void CalculatesPercentilesAndMax()
    {
        var histogram = new LatencyHistogram();
        for (var i = 1; i <= 100; i++)
            histogram.Record(i);

        var snapshot = histogram.Snapshot(reset: false);

        AssertEx.Equal(100, snapshot.Count);
        AssertEx.Equal(50, snapshot.P50Us);
        AssertEx.Equal(90, snapshot.P90Us);
        AssertEx.Equal(95, snapshot.P95Us);
        AssertEx.Equal(99, snapshot.P99Us);
        AssertEx.Equal(100, snapshot.P999Us);
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

    /// <summary>창을 몇 번 닫아도 실행 전체 누적은 남아야 한다. 실행 전체 p99를 얻는 근거다.</summary>
    private static void SnapshotTotalSurvivesWindowResets()
    {
        var histogram = new LatencyHistogram();

        for (var i = 1; i <= 100; i++)
        {
            histogram.Record(i);
            histogram.Snapshot(reset: true);
        }

        var window = histogram.Snapshot(reset: false);
        var total = histogram.SnapshotTotal();

        AssertEx.Equal(0, window.Count, "창을 닫은 뒤에는 창 표본이 남지 않아야 한다.");
        AssertEx.Equal(100, total.Count, "누적 표본은 창 리셋과 무관하게 유지되어야 한다.");
        AssertEx.Equal(100, total.MaxUs);
        AssertEx.Equal(50, total.P50Us);
        AssertEx.Equal(99, total.P99Us);
    }

    /// <summary>서브버킷 경계를 넘는 큰 값도 1% 이내 오차로 복원되어야 한다.</summary>
    private static void LargeValuesStayWithinRelativeError()
    {
        long[] samples = [1_000, 25_000, 400_000, 7_500_000, 120_000_000];

        foreach (var sample in samples)
        {
            var histogram = new LatencyHistogram();
            histogram.Record(sample);

            var max = histogram.Snapshot(reset: false).MaxUs;
            var error = Math.Abs(max - sample) / (double)sample;

            AssertEx.True(
                error <= 0.01,
                $"{sample}µs를 기록했을 때 복원값 {max}µs의 상대 오차 {error:P3}가 1%를 넘었다.");
        }
    }

    /// <summary>분위수는 버킷 상한을 쓰므로 실제 값보다 작게 보고되면 안 된다.</summary>
    private static void PercentilesDoNotUnderreport()
    {
        var histogram = new LatencyHistogram();
        for (var i = 0; i < 1_000; i++)
            histogram.Record(5_000);
        histogram.Record(9_999_999);

        var snapshot = histogram.Snapshot(reset: false);

        AssertEx.True(snapshot.P50Us >= 5_000, $"p50 {snapshot.P50Us}가 실제 값 5000보다 작다.");
        AssertEx.True(snapshot.MaxUs >= 9_999_999, $"max {snapshot.MaxUs}가 실제 값 9999999보다 작다.");
    }

    /// <summary>여러 스레드가 동시에 기록해도 표본이 유실되지 않아야 한다.</summary>
    private static void ConcurrentRecordsAreNotLost()
    {
        var histogram = new LatencyHistogram();
        const int threadCount = 8;
        const int perThread = 5_000;

        Parallel.For(0, threadCount, _ =>
        {
            for (var i = 0; i < perThread; i++)
                histogram.Record(100);
        });

        AssertEx.Equal(threadCount * perThread, histogram.SnapshotTotal().Count);
    }
}
