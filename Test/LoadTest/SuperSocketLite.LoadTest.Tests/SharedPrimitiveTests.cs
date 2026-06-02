using SuperSocketLite.LoadTest.Shared.Metrics;
using SuperSocketLite.LoadTest.Shared.Time;

namespace SuperSocketLite.LoadTest.Tests;

internal static class SharedPrimitiveTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(CounterSnapshotComputesDeltasAndRates), CounterSnapshotComputesDeltasAndRates);
        yield return new TestCase(nameof(MonotonicClockElapsedMillisecondsNeverGoesBackward), MonotonicClockElapsedMillisecondsNeverGoesBackward);
    }

    private static void CounterSnapshotComputesDeltasAndRates()
    {
        var previous = new CounterSnapshot(ElapsedMs: 1000, Total: 10);
        var current = new CounterSnapshot(ElapsedMs: 3000, Total: 30);

        AssertEx.Equal(20L, current.DeltaFrom(previous));
        AssertEx.Equal(10.0, current.PerSecondFrom(previous));
    }

    private static void MonotonicClockElapsedMillisecondsNeverGoesBackward()
    {
        var clock = MonotonicClock.StartNew();
        var first = clock.ElapsedMilliseconds;
        Thread.Sleep(5);
        var second = clock.ElapsedMilliseconds;

        AssertEx.True(second >= first, "Monotonic elapsed time should not go backward.");
        AssertEx.True(clock.GetTimestampMicroseconds() > 0, "Timestamp should be positive.");
    }
}
