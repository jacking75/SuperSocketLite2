using System.Globalization;
using System.Threading.Channels;
using SuperSocketLite.LoadTest.Shared.Csv;

namespace SuperSocketLite.LoadTest.Client.Metrics;

public sealed class ClientCsvWriters : IDisposable
{
    private readonly CsvMetricWriter _samples;
    private readonly CsvMetricWriter _operations;
    private readonly CsvMetricWriter _summary;
    private readonly string _machineId;
    private readonly Channel<QueuedOperation> _operationChannel;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _operationWriterTask;
    private readonly object _operationWriterSync = new();
    private long _operationId;
    private long _pendingOperationRows;
    private long _droppedOperationRows;

    public ClientCsvWriters(string outputDirectory, string? machineId = null, int operationChannelCapacity = 65536, bool autoStartOperationWriter = true)
    {
        Directory.CreateDirectory(outputDirectory);
        _machineId = string.IsNullOrWhiteSpace(machineId) ? "unknown" : machineId;
        _samples = new CsvMetricWriter(Path.Combine(outputDirectory, "client_samples.csv"), new CsvSchema(
            "timestamp_utc", "elapsed_ms", "run_id", "machine_id", "active_clients", "connecting_clients", "connected_clients",
            "closed_clients", "reconnecting_clients", "total_connect_success", "total_connect_fail", "total_disconnect",
            "total_send_success", "total_send_fail", "total_receive", "total_timeout", "send_per_sec", "receive_per_sec",
            "bytes_sent_per_sec", "bytes_received_per_sec", "rtt_p50_us", "rtt_p95_us", "rtt_p99_us", "rtt_max_us",
            "socket_error_total", "protocol_error_total", "runtime_error_total", "dropped_operation_rows"));
        _operations = new CsvMetricWriter(Path.Combine(outputDirectory, "client_operations.csv"), new CsvSchema(
            "timestamp_utc", "elapsed_ms", "run_id", "machine_id", "client_id", "operation_id", "operation_type", "packet_id",
            "payload_bytes", "send_start_ms", "response_end_ms", "rtt_us", "success", "error_type", "socket_error"));
        _summary = new CsvMetricWriter(Path.Combine(outputDirectory, "client_summary.csv"), new CsvSchema(
            "timestamp_utc", "run_id", "machine_id", "key", "value"));
        _operationChannel = Channel.CreateBounded<QueuedOperation>(new BoundedChannelOptions(Math.Max(1, operationChannelCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        if (autoStartOperationWriter)
            _operationWriterTask = Task.Run(WriteOperationsLoopAsync);
    }

    public long NextOperationId() => Interlocked.Increment(ref _operationId);
    public long DroppedOperationRows => Volatile.Read(ref _droppedOperationRows);

    public void WriteSample(ClientMetricsSnapshot sample)
    {
        _samples.WriteRow(
            sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            sample.ElapsedMs,
            sample.RunId,
            _machineId,
            sample.ActiveClients,
            sample.ConnectingClients,
            sample.ConnectedClients,
            sample.ClosedClients,
            sample.ReconnectingClients,
            sample.TotalConnectSuccess,
            sample.TotalConnectFail,
            sample.TotalDisconnect,
            sample.TotalSendSuccess,
            sample.TotalSendFail,
            sample.TotalReceive,
            sample.TotalTimeout,
            sample.SendPerSec.ToString("F3", CultureInfo.InvariantCulture),
            sample.ReceivePerSec.ToString("F3", CultureInfo.InvariantCulture),
            sample.BytesSentPerSec.ToString("F3", CultureInfo.InvariantCulture),
            sample.BytesReceivedPerSec.ToString("F3", CultureInfo.InvariantCulture),
            sample.RttP50Us,
            sample.RttP95Us,
            sample.RttP99Us,
            sample.RttMaxUs,
            sample.SocketErrorTotal,
            sample.ProtocolErrorTotal,
            sample.RuntimeErrorTotal,
            DroppedOperationRows);
    }

    public void WriteOperation(string runId, long elapsedMs, int clientId, long operationId, string operationType, int packetId, int payloadBytes, long sendStartMs, long responseEndMs, long rttUs, bool success, string errorType, string socketError)
    {
        var operation = new QueuedOperation(DateTimeOffset.UtcNow, runId, elapsedMs, _machineId, clientId, operationId, operationType, packetId, payloadBytes, sendStartMs, responseEndMs, rttUs, success, errorType, socketError);
        Interlocked.Increment(ref _pendingOperationRows);
        if (_operationChannel.Writer.TryWrite(operation))
            return;

        Interlocked.Decrement(ref _pendingOperationRows);
        Interlocked.Increment(ref _droppedOperationRows);
    }

    public void WriteSummary(string runId, string key, object value)
    {
        _summary.WriteRow(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), runId, _machineId, key, value);
    }

    public void Flush()
    {
        WaitForOperationQueue();
        _samples.Flush();
        _operations.Flush();
        _summary.Flush();
    }

    public void Dispose()
    {
        _operationChannel.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            _operationWriterTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        DrainAvailableOperations();
        _samples.Dispose();
        _operations.Dispose();
        _summary.Dispose();
        _stop.Dispose();
    }

    private async Task WriteOperationsLoopAsync()
    {
        try
        {
            await foreach (var operation in _operationChannel.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                WriteOperationRow(operation);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WaitForOperationQueue()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Volatile.Read(ref _pendingOperationRows) > 0 && DateTime.UtcNow < deadline)
        {
            DrainAvailableOperations();
            if (Volatile.Read(ref _pendingOperationRows) > 0)
                Thread.Sleep(1);
        }
    }

    private void DrainAvailableOperations()
    {
        while (_operationChannel.Reader.TryRead(out var operation))
            WriteOperationRow(operation);
    }

    private void WriteOperationRow(QueuedOperation operation)
    {
        lock (_operationWriterSync)
        {
            _operations.WriteRow(
                operation.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                operation.ElapsedMs,
                operation.RunId,
                operation.MachineId,
                operation.ClientId,
                operation.OperationId,
                operation.OperationType,
                operation.PacketId,
                operation.PayloadBytes,
                operation.SendStartMs,
                operation.ResponseEndMs,
                operation.RttUs,
                operation.Success,
                operation.ErrorType,
                operation.SocketError);
        }

        Interlocked.Decrement(ref _pendingOperationRows);
    }

    private sealed record QueuedOperation(
        DateTimeOffset TimestampUtc,
        string RunId,
        long ElapsedMs,
        string MachineId,
        int ClientId,
        long OperationId,
        string OperationType,
        int PacketId,
        int PayloadBytes,
        long SendStartMs,
        long ResponseEndMs,
        long RttUs,
        bool Success,
        string ErrorType,
        string SocketError);
}
