namespace SuperSocketLite.LoadTest.Server;

/// <summary>지정한 파일이 나타나면 종료를 요청하는 신호입니다.</summary>
/// <remarks>
/// 부하 스크립트가 서버를 강제 종료하면 세션 정리도, 마지막 CSV 표본 기록도 일어나지 않습니다.
/// 마지막 표본은 <c>ServerMetricsHostedLoop.Dispose()</c>에서만 쓰이므로, 강제 종료한 실행은
/// 클라이언트가 이미 다 빠져나갔는데도 CSV의 끝 행이 "활성 세션 N개"로 남습니다.
/// 리포트는 그 마지막 행으로 세션 누수를 판정하므로 멀쩡한 실행이 불합격이 됩니다.
///
/// 스크립트가 이 파일을 만들어 종료를 요청하면 서버가 스스로 정리하고 끝내므로 그 오판이 사라집니다.
///
/// 왜 파일인가: 콘솔 없이 띄운 자식 프로세스에 Ctrl+C를 보내는 것은 Windows에서 까다롭고,
/// 이름 있는 동기화 객체는 유닉스에서 지원되지 않습니다. 파일은 양쪽 모두에서 단순하게 동작하고
/// 스크립트에서도 한 줄로 만들 수 있습니다.
/// </remarks>
public sealed class StopFileSignal : IDisposable
{
    private readonly string _path;
    private readonly Action _onSignal;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watchTask;
    private bool _disposed;

    private StopFileSignal(string path, TimeSpan pollInterval, Action onSignal)
    {
        _path = path;
        _onSignal = onSignal;
        _watchTask = Task.Run(() => WatchAsync(pollInterval));
    }

    /// <summary>감시를 시작합니다.</summary>
    /// <param name="path">이 경로에 파일이 생기면 종료를 요청합니다. null이거나 비면 감시하지 않습니다.</param>
    /// <param name="onSignal">파일이 생겼을 때 부를 동작입니다.</param>
    /// <param name="pollInterval">확인 주기입니다. 기본 200ms.</param>
    /// <returns>감시하지 않는 경우 null.</returns>
    /// <remarks>
    /// 시작할 때 남아 있는 파일은 지웁니다. 이전 실행이 남긴 파일 때문에 새 서버가 뜨자마자
    /// 끝나 버리는 일을 막습니다.
    /// </remarks>
    public static StopFileSignal? Start(string? path, Action onSignal, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(onSignal);

        if (string.IsNullOrWhiteSpace(path))
            return null;

        TryDelete(path);
        return new StopFileSignal(path, pollInterval ?? TimeSpan.FromMilliseconds(200), onSignal);
    }

    private async Task WatchAsync(TimeSpan pollInterval)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                // 기다리기 전에 먼저 본다. 감시를 시작하기 직전에 만들어진 파일도 곧바로 잡힌다.
                if (File.Exists(_path))
                {
                    _onSignal();
                    return;
                }

                await Task.Delay(pollInterval, _stop.Token).ConfigureAwait(false);
            }
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

        try
        {
            _watchTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();

        // 남겨 두면 같은 경로를 쓰는 다음 실행이 뜨자마자 끝난다.
        TryDelete(_path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
