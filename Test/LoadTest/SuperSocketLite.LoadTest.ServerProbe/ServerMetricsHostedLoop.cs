namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class ServerMetricsHostedLoop : IDisposable
{
    private readonly ServerMetricsCollector _collector;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loopTask;
    private bool _disposed;

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
        if (_disposed)
            return;

        _disposed = true;
        _stop.Cancel();
        _timer.Dispose();
        try
        {
            _loopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        // 종료 시점의 상태를 남기는 마지막 샘플이다. 여기서만 기록하므로 중복되지 않는다.
        _collector.WriteSample();
        _collector.Flush();
        _stop.Dispose();
    }
}
