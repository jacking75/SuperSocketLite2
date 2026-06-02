using System.Diagnostics;

namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed record ProcessMetrics(
    int ProcessId,
    int GcGen0Total,
    int GcGen1Total,
    int GcGen2Total,
    int GcGen0Delta,
    int GcGen1Delta,
    int GcGen2Delta,
    long GcHeapBytes,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int ThreadCount,
    int ThreadPoolWorkerAvailable,
    int ThreadPoolWorkerMax,
    int ThreadPoolIoAvailable,
    int ThreadPoolIoMax,
    double CpuPercent);

public sealed class ProcessMetricReader
{
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTimeOffset? _lastTimestamp;
    private TimeSpan _lastProcessorTime;
    private int _lastGen0;
    private int _lastGen1;
    private int _lastGen2;

    public ProcessMetrics Read()
    {
        _process.Refresh();

        var now = DateTimeOffset.UtcNow;
        var processorTime = _process.TotalProcessorTime;
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var cpuPercent = 0.0;

        if (_lastTimestamp is { } lastTimestamp)
        {
            var wallDelta = now - lastTimestamp;
            var cpuDelta = processorTime - _lastProcessorTime;
            if (wallDelta.TotalMilliseconds > 0)
                cpuPercent = Math.Max(0, cpuDelta.TotalMilliseconds / wallDelta.TotalMilliseconds / Environment.ProcessorCount * 100.0);
        }

        ThreadPool.GetAvailableThreads(out var workerAvailable, out var ioAvailable);
        ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);

        var metrics = new ProcessMetrics(
            _process.Id,
            gen0,
            gen1,
            gen2,
            gen0 - _lastGen0,
            gen1 - _lastGen1,
            gen2 - _lastGen2,
            GC.GetTotalMemory(forceFullCollection: false),
            _process.WorkingSet64,
            _process.PrivateMemorySize64,
            _process.Threads.Count,
            workerAvailable,
            workerMax,
            ioAvailable,
            ioMax,
            cpuPercent);

        _lastTimestamp = now;
        _lastProcessorTime = processorTime;
        _lastGen0 = gen0;
        _lastGen1 = gen1;
        _lastGen2 = gen2;

        return metrics;
    }
}
