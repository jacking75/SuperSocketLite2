using System.Globalization;
using System.Threading.Channels;
using SuperSocketLite.LoadTest.Shared.Csv;

namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class ServerMetricCsvWriter : IDisposable
{
    private static readonly CsvSchema SampleSchema = new(
        "timestamp_utc",
        "elapsed_ms",
        "run_id",
        "server_name",
        "process_id",
        "active_sessions",
        "total_connected",
        "total_closed",
        "total_requests",
        "requests_per_sec",
        "total_bytes_in",
        "bytes_in_per_sec",
        "total_bytes_out",
        "bytes_out_per_sec",
        "send_fail_total",
        "exception_total",
        "protocol_error_total",
        "gc_gen0_total",
        "gc_gen1_total",
        "gc_gen2_total",
        "gc_gen0_delta",
        "gc_gen1_delta",
        "gc_gen2_delta",
        "gc_heap_bytes",
        "working_set_bytes",
        "private_memory_bytes",
        "thread_count",
        "threadpool_worker_available",
        "threadpool_worker_max",
        "threadpool_io_available",
        "threadpool_io_max",
        "cpu_percent",
        "handler_latency_p50_us",
        "handler_latency_p95_us",
        "handler_latency_p99_us",
        "handler_latency_max_us",
        "dropped_metric_rows",
        "phase",
        "send_queue_depth_total",
        "send_queue_depth_max",
        "receive_saea_pool_available",
        "receive_saea_pool_total",
        "send_saea_pool_available",
        "send_saea_pool_total");

    private static readonly CsvSchema EventSchema = new(
        "timestamp_utc",
        "elapsed_ms",
        "run_id",
        "event_type",
        "session_id",
        "remote_endpoint",
        "packet_id",
        "bytes_in",
        "bytes_out",
        "close_reason",
        "error_type",
        "message");

    private readonly CsvMetricWriter _sampleWriter;
    private readonly CsvMetricWriter _eventWriter;
    private readonly Channel<QueuedRow> _channel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _writerTask;

    public ServerMetricCsvWriter(string outputDirectory, int channelCapacity, bool autoStart)
    {
        Directory.CreateDirectory(outputDirectory);
        _sampleWriter = new CsvMetricWriter(Path.Combine(outputDirectory, "server_samples.csv"), SampleSchema);
        _eventWriter = new CsvMetricWriter(Path.Combine(outputDirectory, "server_events.csv"), EventSchema);
        _channel = Channel.CreateBounded<QueuedRow>(new BoundedChannelOptions(Math.Max(1, channelCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        if (autoStart)
            _writerTask = Task.Run(WriteLoopAsync);
    }

    public bool TryWriteSample(ServerMetricsSnapshot snapshot)
    {
        return _channel.Writer.TryWrite(QueuedRow.ForSample(snapshot));
    }

    public bool TryWriteEvent(ServerMetricEvent metricEvent)
    {
        return _channel.Writer.TryWrite(QueuedRow.ForEvent(metricEvent));
    }

    public void Flush()
    {
        DrainAvailableRows();
        _sampleWriter.Flush();
        _eventWriter.Flush();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var row in _channel.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                Write(row);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DrainAvailableRows()
    {
        while (_channel.Reader.TryRead(out var row))
            Write(row);
    }

    private void Write(QueuedRow row)
    {
        if (row.Sample is { } sample)
        {
            _sampleWriter.WriteRow(
                sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                sample.ElapsedMs,
                sample.RunId,
                sample.ServerName,
                sample.ProcessId,
                sample.ActiveSessions,
                sample.TotalConnected,
                sample.TotalClosed,
                sample.TotalRequests,
                sample.RequestsPerSec.ToString("F3", CultureInfo.InvariantCulture),
                sample.TotalBytesIn,
                sample.BytesInPerSec.ToString("F3", CultureInfo.InvariantCulture),
                sample.TotalBytesOut,
                sample.BytesOutPerSec.ToString("F3", CultureInfo.InvariantCulture),
                sample.SendFailTotal,
                sample.ExceptionTotal,
                sample.ProtocolErrorTotal,
                sample.GcGen0Total,
                sample.GcGen1Total,
                sample.GcGen2Total,
                sample.GcGen0Delta,
                sample.GcGen1Delta,
                sample.GcGen2Delta,
                sample.GcHeapBytes,
                sample.WorkingSetBytes,
                sample.PrivateMemoryBytes,
                sample.ThreadCount,
                sample.ThreadPoolWorkerAvailable,
                sample.ThreadPoolWorkerMax,
                sample.ThreadPoolIoAvailable,
                sample.ThreadPoolIoMax,
                sample.CpuPercent.ToString("F3", CultureInfo.InvariantCulture),
                sample.HandlerLatencyP50Us,
                sample.HandlerLatencyP95Us,
                sample.HandlerLatencyP99Us,
                sample.HandlerLatencyMaxUs,
                sample.DroppedMetricRows,
                sample.Phase,
                sample.SendQueueDepthTotal,
                sample.SendQueueDepthMax,
                sample.ReceiveSaeaPoolAvailable,
                sample.ReceiveSaeaPoolTotal,
                sample.SendSaeaPoolAvailable,
                sample.SendSaeaPoolTotal);
            return;
        }

        var metricEvent = row.Event!;
        _eventWriter.WriteRow(
            metricEvent.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            metricEvent.ElapsedMs,
            metricEvent.RunId,
            metricEvent.EventType,
            metricEvent.SessionId,
            metricEvent.RemoteEndpoint,
            metricEvent.PacketId,
            metricEvent.BytesIn,
            metricEvent.BytesOut,
            metricEvent.CloseReason,
            metricEvent.ErrorType,
            metricEvent.Message);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            _writerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        DrainAvailableRows();
        _sampleWriter.Dispose();
        _eventWriter.Dispose();
        _stop.Dispose();
    }

    private sealed record QueuedRow(ServerMetricsSnapshot? Sample, ServerMetricEvent? Event)
    {
        public static QueuedRow ForSample(ServerMetricsSnapshot snapshot) => new(snapshot, null);
        public static QueuedRow ForEvent(ServerMetricEvent metricEvent) => new(null, metricEvent);
    }
}
