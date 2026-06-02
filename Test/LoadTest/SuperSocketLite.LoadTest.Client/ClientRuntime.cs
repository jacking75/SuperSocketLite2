using System.Diagnostics;
using SuperSocketLite.LoadTest.Client.Metrics;

namespace SuperSocketLite.LoadTest.Client;

public sealed class ClientRuntime
{
    private readonly LoadTestOptions _options;
    private readonly ClientMetricsCollector _metrics = new();

    public ClientRuntime(LoadTestOptions options)
    {
        _options = options;
    }

    public static void EnsureOutputFiles(LoadTestOptions options)
    {
        using var writers = new ClientCsvWriters(options.Output, options.MachineId);
        writers.WriteSummary(options.RunId, "created", true);
        writers.Flush();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var writers = new ClientCsvWriters(_options.Output, _options.MachineId);
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        duration.CancelAfter(_options.Duration);

        var runStartMs = Environment.TickCount64;
        var stopwatch = Stopwatch.StartNew();
        var actors = LoadScenario.CreateConnectSchedule(_options)
            .Select((delay, clientId) => new ClientActor(clientId, _options, _metrics, writers, runStartMs).RunAsync(delay, duration.Token))
            .ToArray();

        var sampler = Task.Run(async () =>
        {
            while (!duration.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), duration.Token).ConfigureAwait(false);
                writers.WriteSample(_metrics.Snapshot(_options.RunId, stopwatch.ElapsedMilliseconds, resetLatency: true));
                writers.Flush();
            }
        }, duration.Token);

        try
        {
            await Task.WhenAll(actors).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        duration.Cancel();
        try
        {
            await sampler.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        writers.WriteSample(_metrics.Snapshot(_options.RunId, stopwatch.ElapsedMilliseconds, resetLatency: true));
        writers.WriteSummary(_options.RunId, "clients", _options.Clients);
        writers.WriteSummary(_options.RunId, "duration_ms", stopwatch.ElapsedMilliseconds);
        writers.WriteSummary(_options.RunId, "dropped_client_operation_rows", writers.DroppedOperationRows);
        writers.Flush();
        return 0;
    }
}
