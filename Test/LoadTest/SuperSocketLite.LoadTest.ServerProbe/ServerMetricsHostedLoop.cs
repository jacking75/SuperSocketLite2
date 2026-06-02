namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class ServerMetricsHostedLoop : IDisposable
{
    private readonly ServerMetricsCollector _collector;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loopTask;

    internal ServerMetricsHostedLoop(ServerMetricsCollector collector, TimeSpan interval)
    {
        _collector = collector;
        _timer = new PeriodicTimer(interval);
        _loopTask = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                _collector.WriteSample();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _timer.Dispose();
        try
        {
            _loopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _collector.Flush();
        _stop.Dispose();
    }
}
