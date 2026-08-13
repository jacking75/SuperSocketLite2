using System.Numerics;

namespace SuperSocketLite.LoadTest.Shared.Metrics;

/// <summary>
/// 고정 버킷 지연 히스토그램입니다.
/// 값을 개별 보관하지 않고 로그-선형 버킷에 세므로 기록 비용과 메모리가 표본 수와 무관합니다.
/// 기록은 스레드별 슬롯으로 분산해 고부하에서 측정기 자체가 병목이 되지 않게 합니다.
/// </summary>
/// <remarks>
/// 버킷 구조는 HdrHistogram과 같은 방식입니다. <see cref="SubBucketCount"/> 미만의 값은 1:1로 저장해 정확하고,
/// 그 이상은 2의 거듭제곱 구간마다 <see cref="SubBucketCount"/>/2개로 분할하므로 상대 오차가 약 0.4% 이내입니다.
/// 분위수는 버킷의 상한값을 돌려주므로 실제보다 작게 보고하지 않습니다.
/// </remarks>
public sealed class LatencyHistogram
{
    private const int SubBucketBits = 8;
    private const int SubBucketCount = 1 << SubBucketBits;
    private const int SubBucketHalf = SubBucketCount / 2;
    private const int BucketCount = 24;
    private const int CountsLength = SubBucketCount + ((BucketCount - 1) * SubBucketHalf);

    private static readonly int SlotCount = CalculateSlotCount();
    private static readonly int SlotMask = SlotCount - 1;

    private readonly long[][] _slots;
    private readonly long[] _windowBase = new long[CountsLength];
    private readonly object _snapshotSync = new();

    public LatencyHistogram()
    {
        _slots = new long[SlotCount][];
        for (var i = 0; i < SlotCount; i++)
            _slots[i] = new long[CountsLength];
    }

    /// <summary>표본 하나를 기록합니다.</summary>
    public void Record(long elapsedMicroseconds)
    {
        if (elapsedMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMicroseconds), "Latency cannot be negative.");

        var slot = _slots[Environment.CurrentManagedThreadId & SlotMask];
        Interlocked.Increment(ref slot[IndexOf(elapsedMicroseconds)]);
    }

    /// <summary>다른 히스토그램이 지금까지 누적한 표본을 이 히스토그램에 더합니다.</summary>
    public void MergeFrom(LatencyHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var totals = other.ReadTotals();
        var slot = _slots[0];
        for (var i = 0; i < CountsLength; i++)
        {
            if (totals[i] != 0)
                Interlocked.Add(ref slot[i], totals[i]);
        }
    }

    /// <summary>
    /// 직전 창(window) 구간의 스냅샷을 돌려줍니다.
    /// <paramref name="reset"/>가 참이면 창을 닫아 다음 호출이 이후 표본만 보게 합니다.
    /// 실행 전체 누적은 창과 무관하게 유지되므로 <see cref="SnapshotTotal"/>로 언제든 얻을 수 있습니다.
    /// </summary>
    public HistogramSnapshot Snapshot(bool reset)
    {
        var totals = ReadTotals();

        lock (_snapshotSync)
        {
            var window = new long[CountsLength];
            for (var i = 0; i < CountsLength; i++)
                window[i] = totals[i] - _windowBase[i];

            if (reset)
                Array.Copy(totals, _windowBase, CountsLength);

            return Summarize(window);
        }
    }

    /// <summary>실행 시작부터 지금까지 누적한 모든 표본의 스냅샷을 돌려줍니다.</summary>
    public HistogramSnapshot SnapshotTotal()
    {
        return Summarize(ReadTotals());
    }

    private long[] ReadTotals()
    {
        var totals = new long[CountsLength];
        for (var slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            var slot = _slots[slotIndex];
            for (var i = 0; i < CountsLength; i++)
            {
                var value = Volatile.Read(ref slot[i]);
                if (value != 0)
                    totals[i] += value;
            }
        }

        return totals;
    }

    private static HistogramSnapshot Summarize(long[] counts)
    {
        long total = 0;
        var highestIndex = -1;
        for (var i = 0; i < CountsLength; i++)
        {
            if (counts[i] == 0)
                continue;

            total += counts[i];
            highestIndex = i;
        }

        if (total == 0)
            return default;

        return new HistogramSnapshot(
            total,
            ValueAtPercentile(counts, total, 0.50),
            ValueAtPercentile(counts, total, 0.90),
            ValueAtPercentile(counts, total, 0.95),
            ValueAtPercentile(counts, total, 0.99),
            ValueAtPercentile(counts, total, 0.999),
            ValueOf(highestIndex));
    }

    private static long ValueAtPercentile(long[] counts, long total, double percentile)
    {
        var rank = (long)Math.Ceiling(percentile * total);
        if (rank < 1)
            rank = 1;

        long running = 0;
        for (var i = 0; i < CountsLength; i++)
        {
            running += counts[i];
            if (running >= rank)
                return ValueOf(i);
        }

        return 0;
    }

    /// <summary>버킷 인덱스가 표현하는 값 범위의 상한을 돌려줍니다.</summary>
    private static long ValueOf(int index)
    {
        if (index < SubBucketCount)
            return index;

        var bucketIndex = (index / SubBucketHalf) - 1;
        var subIndex = (index % SubBucketHalf) + SubBucketHalf;
        return ((long)(subIndex + 1) << bucketIndex) - 1;
    }

    private static int IndexOf(long value)
    {
        if (value < SubBucketCount)
            return (int)value;

        var bucketIndex = 63 - BitOperations.LeadingZeroCount((ulong)value) - (SubBucketBits - 1);
        if (bucketIndex >= BucketCount)
            bucketIndex = BucketCount - 1;

        var subIndex = (int)(value >> bucketIndex);
        if (subIndex > SubBucketCount - 1)
            subIndex = SubBucketCount - 1;

        return ((bucketIndex + 1) * SubBucketHalf) + (subIndex - SubBucketHalf);
    }

    private static int CalculateSlotCount()
    {
        var target = (uint)Math.Clamp(Environment.ProcessorCount, 1, 64);
        return (int)BitOperations.RoundUpToPowerOf2(target);
    }
}
