using SuperSocketLite.LoadTest.Server;

namespace SuperSocketLite.LoadTest.Tests;

/// <summary>
/// 부하 스크립트가 서버에 정상 종료를 요청하는 통로를 확인합니다.
/// 이 신호가 없으면 스크립트가 서버를 강제로 죽이고, 그러면 마지막 표본이 기록되지 않아
/// 멀쩡한 실행이 "종료 후 활성 세션 N개"로 남아 세션 누수 판정이 불합격이 됩니다.
/// </summary>
internal static class StopFileSignalTests
{
    /// <summary>폴링 주기의 몇 배까지 기다려 줄지입니다. 느린 CI에서도 흔들리지 않을 만큼 넉넉히 둡니다.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(SignalsWhenTheFileAppears), SignalsWhenTheFileAppears);
        yield return new TestCase(nameof(DoesNotSignalWhileTheFileIsAbsent), DoesNotSignalWhileTheFileIsAbsent);
        yield return new TestCase(nameof(DeletesAStaleFileSoTheNewRunKeepsGoing), DeletesAStaleFileSoTheNewRunKeepsGoing);
        yield return new TestCase(nameof(DisposeRemovesTheFile), DisposeRemovesTheFile);
        yield return new TestCase(nameof(DisposeIsIdempotent), DisposeIsIdempotent);
        yield return new TestCase(nameof(NoPathMeansNoWatcher), NoPathMeansNoWatcher);
    }

    private static void SignalsWhenTheFileAppears()
    {
        using var temp = TempDirectory.Create();
        var stopFile = Path.Combine(temp.Path, "server.stop");
        using var signalled = new ManualResetEventSlim();

        using var signal = StopFileSignal.Start(stopFile, signalled.Set, PollInterval);
        AssertEx.True(signal is not null, "A path should produce a watcher.");
        AssertEx.False(signalled.IsSet, "Nothing should fire before the file exists.");

        File.WriteAllText(stopFile, string.Empty);

        AssertEx.True(signalled.Wait(WaitTimeout), "The watcher should signal once the file appears.");
    }

    private static void DoesNotSignalWhileTheFileIsAbsent()
    {
        using var temp = TempDirectory.Create();
        var stopFile = Path.Combine(temp.Path, "server.stop");
        using var signalled = new ManualResetEventSlim();

        using var signal = StopFileSignal.Start(stopFile, signalled.Set, PollInterval);

        // 폴링을 여러 번 돌 만큼 기다려도 조용해야 한다.
        AssertEx.False(signalled.Wait(TimeSpan.FromMilliseconds(200)), "The watcher must stay quiet without the file.");
    }

    /// <summary>
    /// 이전 실행이 남긴 파일 때문에 새 서버가 뜨자마자 끝나면, 그 실행은 표본이 거의 없는 채로 남는다.
    /// </summary>
    private static void DeletesAStaleFileSoTheNewRunKeepsGoing()
    {
        using var temp = TempDirectory.Create();
        var stopFile = Path.Combine(temp.Path, "server.stop");
        File.WriteAllText(stopFile, string.Empty);

        using var signalled = new ManualResetEventSlim();
        using var signal = StopFileSignal.Start(stopFile, signalled.Set, PollInterval);

        AssertEx.False(File.Exists(stopFile), "A stale stop file should be removed at start.");
        AssertEx.False(signalled.Wait(TimeSpan.FromMilliseconds(200)), "A stale file must not stop the new run.");
    }

    private static void DisposeRemovesTheFile()
    {
        using var temp = TempDirectory.Create();
        var stopFile = Path.Combine(temp.Path, "server.stop");
        using var signalled = new ManualResetEventSlim();

        var signal = StopFileSignal.Start(stopFile, signalled.Set, PollInterval);
        File.WriteAllText(stopFile, string.Empty);
        AssertEx.True(signalled.Wait(WaitTimeout), "The watcher should signal once the file appears.");

        signal!.Dispose();

        AssertEx.False(File.Exists(stopFile), "Dispose should clean the file up for the next run.");
    }

    private static void DisposeIsIdempotent()
    {
        using var temp = TempDirectory.Create();
        var stopFile = Path.Combine(temp.Path, "server.stop");
        using var signalled = new ManualResetEventSlim();

        var signal = StopFileSignal.Start(stopFile, signalled.Set, PollInterval);

        signal!.Dispose();
        signal.Dispose();
    }

    private static void NoPathMeansNoWatcher()
    {
        AssertEx.True(StopFileSignal.Start(null, () => { }) is null, "A null path should not start a watcher.");
        AssertEx.True(StopFileSignal.Start("   ", () => { }) is null, "A blank path should not start a watcher.");
    }
}
