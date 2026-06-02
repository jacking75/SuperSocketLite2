using SuperSocketLite.LoadTest.Client.Metrics;
using SuperSocketLite.LoadTest.ServerProbe;

namespace SuperSocketLite.LoadTest.Tests;

internal static class MetricsCollectorTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(TracksConnectCloseAndRequestCounters), TracksConnectCloseAndRequestCounters);
        yield return new TestCase(nameof(RequestScopeRecordsHandlerLatency), RequestScopeRecordsHandlerLatency);
        yield return new TestCase(nameof(HostedLoopWritesServerSampleRows), HostedLoopWritesServerSampleRows);
        yield return new TestCase(nameof(ProcessMetricReaderReadsRuntimeValues), ProcessMetricReaderReadsRuntimeValues);
        yield return new TestCase(nameof(BoundedWriterOverflowIncrementsDroppedRowsWithoutBlocking), BoundedWriterOverflowIncrementsDroppedRowsWithoutBlocking);
        yield return new TestCase(nameof(RequestSamplingBelowOneWritesDeterministicSubset), RequestSamplingBelowOneWritesDeterministicSubset);
        yield return new TestCase(nameof(ClientRatesUseSampleDeltas), ClientRatesUseSampleDeltas);
    }

    private static void TracksConnectCloseAndRequestCounters()
    {
        using var temp = TempDirectory.Create();
        using var collector = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = "counter-test",
            OutputDirectory = temp.Path,
            ServerName = "test"
        });

        collector.OnConnected("s1", "127.0.0.1:10000");
        using (collector.BeginRequest("s1", packetId: 101, bytesIn: 5))
        {
        }

        collector.OnClosed("s1", "Normal");

        var snapshot = collector.CaptureSnapshot(resetLatency: false);
        AssertEx.Equal(0L, snapshot.ActiveSessions);
        AssertEx.Equal(1L, snapshot.TotalConnected);
        AssertEx.Equal(1L, snapshot.TotalClosed);
        AssertEx.Equal(1L, snapshot.TotalRequests);
        AssertEx.Equal(5L, snapshot.TotalBytesIn);
    }

    private static void RequestScopeRecordsHandlerLatency()
    {
        using var temp = TempDirectory.Create();
        using var collector = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = "latency-test",
            OutputDirectory = temp.Path,
            ServerName = "test"
        });

        using (collector.BeginRequest("s1", packetId: 101, bytesIn: 5))
        {
            Thread.Sleep(1);
        }

        var snapshot = collector.CaptureSnapshot(resetLatency: true);
        AssertEx.True(snapshot.HandlerLatencyMaxUs > 0, "Request scope should record elapsed latency.");
    }

    private static void HostedLoopWritesServerSampleRows()
    {
        using var temp = TempDirectory.Create();
        using (var collector = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = "sample-test",
            OutputDirectory = temp.Path,
            ServerName = "test",
            SampleInterval = TimeSpan.FromMilliseconds(10)
        }))
        {
            using (collector.Start())
            {
                Thread.Sleep(80);
            }

            collector.Flush();
        }

        var path = Path.Combine(temp.Path, "server_samples.csv");
        AssertEx.True(File.Exists(path), "server_samples.csv should exist.");
        AssertEx.True(File.ReadAllLines(path).Length >= 2, "server_samples.csv should contain a header and at least one sample.");
    }

    private static void ProcessMetricReaderReadsRuntimeValues()
    {
        var reader = new ProcessMetricReader();
        var first = reader.Read();
        Thread.Sleep(10);
        var second = reader.Read();

        AssertEx.True(second.ProcessId > 0, "Process id should be populated.");
        AssertEx.True(second.WorkingSetBytes > 0, "Working set should be populated.");
        AssertEx.True(second.ThreadPoolWorkerMax > 0, "Thread pool max worker count should be populated.");
        AssertEx.True(second.CpuPercent >= 0, "CPU percent should not be negative.");
        AssertEx.True(first.ProcessId == second.ProcessId, "Reader should target the current process.");
    }

    private static void BoundedWriterOverflowIncrementsDroppedRowsWithoutBlocking()
    {
        using var temp = TempDirectory.Create();
        using var collector = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = "drop-test",
            OutputDirectory = temp.Path,
            ServerName = "test",
            WriterChannelCapacity = 1,
            AutoStartWriter = false
        });

        collector.OnConnected("s1", "127.0.0.1:10000");
        collector.OnConnected("s2", "127.0.0.1:10001");
        collector.OnClosed("s1", "Normal");

        var snapshot = collector.CaptureSnapshot(resetLatency: false);
        AssertEx.True(snapshot.DroppedMetricRows > 0, "Overflow should increment dropped metric rows.");
    }

    private static void RequestSamplingBelowOneWritesDeterministicSubset()
    {
        using var temp = TempDirectory.Create();
        using (var collector = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = "request-sampling",
            OutputDirectory = temp.Path,
            ServerName = "test",
            RequestEventSampling = 0.5
        }))
        {
            for (var i = 0; i < 4; i++)
            {
                using (collector.BeginRequest("s1", packetId: 101, bytesIn: 5))
                {
                }
            }

            collector.Flush();
        }

        var requestEvents = File.ReadAllLines(Path.Combine(temp.Path, "server_events.csv"))
            .Count(line => line.Contains(",request,"));
        AssertEx.Equal(2, requestEvents, "Sampling 0.5 should write every second request event.");
    }

    private static void ClientRatesUseSampleDeltas()
    {
        var collector = new ClientMetricsCollector();

        for (var i = 0; i < 10; i++)
            collector.OnSendSuccess(100);
        for (var i = 0; i < 5; i++)
            collector.OnReceive(50, 100);

        var first = collector.Snapshot("run", elapsedMs: 1000, resetLatency: true);
        var second = collector.Snapshot("run", elapsedMs: 2000, resetLatency: true);

        AssertEx.Equal(10.0, first.SendPerSec);
        AssertEx.Equal(5.0, first.ReceivePerSec);
        AssertEx.Equal(0.0, second.SendPerSec);
        AssertEx.Equal(0.0, second.ReceivePerSec);
    }
}
