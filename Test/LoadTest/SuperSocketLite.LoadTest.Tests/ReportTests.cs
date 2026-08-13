using SuperSocketLite.LoadTest.Report;

namespace SuperSocketLite.LoadTest.Tests;

internal static class ReportTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(GroupTakesMedianPerMetric), GroupTakesMedianPerMetric);
        yield return new TestCase(nameof(GroupKeepsWorstCaseForFaults), GroupKeepsWorstCaseForFaults);
        yield return new TestCase(nameof(LatencyRegressionFailsVerdict), LatencyRegressionFailsVerdict);
        yield return new TestCase(nameof(LatencyWithinThresholdPasses), LatencyWithinThresholdPasses);
        yield return new TestCase(nameof(ThroughputDropFailsVerdict), ThroughputDropFailsVerdict);
        yield return new TestCase(nameof(MismatchedPacingIsInconclusive), MismatchedPacingIsInconclusive);
        yield return new TestCase(nameof(LowRateAchievementFails), LowRateAchievementFails);
        yield return new TestCase(nameof(LoadsRunsFromCsvDirectories), LoadsRunsFromCsvDirectories);
        yield return new TestCase(nameof(WritesSelfContainedHtmlReport), WritesSelfContainedHtmlReport);
        yield return new TestCase(nameof(MissingServerSamplesLeaveServerChecksInconclusive), MissingServerSamplesLeaveServerChecksInconclusive);
        yield return new TestCase(nameof(MissingServerSamplesCompareThroughputFromClient), MissingServerSamplesCompareThroughputFromClient);
    }

    /// <summary>
    /// 계측을 끈 실행에는 서버 표본이 없다. 그 실행의 서버 쪽 판정은 통과가 아니라 보류여야 한다.
    /// 0으로 읽어 통과시키면 확인하지 않은 것을 확인했다고 말하게 된다.
    /// </summary>
    private static void MissingServerSamplesLeaveServerChecksInconclusive()
    {
        var group = RunGroup.Create("off", [Metrics("off01", hasServerSamples: false)]);

        var verdict = Verdict.Evaluate(group, new Thresholds());

        var serverException = verdict.Checks.Single(c => c.Name == "서버 예외");
        var sessionCleanup = verdict.Checks.Single(c => c.Name == "세션 정리");

        AssertEx.True(serverException.Outcome == CheckOutcome.Inconclusive, "서버 표본이 없으면 서버 예외 판정은 보류여야 한다.");
        AssertEx.True(sessionCleanup.Outcome == CheckOutcome.Inconclusive, "서버 표본이 없으면 세션 정리 판정은 보류여야 한다.");
        AssertEx.False(verdict.Failed, "판정할 수 없는 것을 불합격으로 세면 안 된다.");
    }

    /// <summary>
    /// 계측 오버헤드 비교는 한쪽에 서버 표본이 없다.
    /// 그때 처리량은 클라이언트가 보낸 정상 구간 레이트로 견주어야 하며, 서버 수치가 없다고
    /// 처리량이 0으로 떨어진 것처럼 불합격이 나오면 안 된다.
    /// </summary>
    private static void MissingServerSamplesCompareThroughputFromClient()
    {
        // 계측을 끈 실행이 기준. 서버 표본이 없지만 클라이언트 송신 레이트는 남아 있다.
        var baseline = RunGroup.Create("off", [Metrics("off01", rps: 500, hasServerSamples: false)]);
        var candidate = RunGroup.Create("full", [Metrics("full01", rps: 495)]);

        var verdict = Verdict.Compare(baseline, candidate, new Thresholds { MinThroughputRatio = 0.95 });

        var throughput = verdict.Checks.Single(c => c.Name.StartsWith("처리량", StringComparison.Ordinal));
        AssertEx.True(throughput.Name.Contains("클라이언트", StringComparison.Ordinal), "서버 표본이 없으면 클라이언트 기준으로 견주어야 한다.");
        AssertEx.True(throughput.Outcome == CheckOutcome.Pass, "1% 차이는 통과여야 한다.");
        AssertEx.False(verdict.Failed, "계측 오버헤드 비교가 서버 표본 부재로 불합격이 되면 안 된다.");
    }

    private static RunMetrics Metrics(
        string runId,
        double p99 = 1.0,
        double p999 = 3.0,
        double rps = 500,
        double achievement = 0.99,
        long exceptions = 0,
        long activeSessions = 0,
        string pacing = "open",
        double memoryGrowth = 10,
        bool hasServerSamples = true)
    {
        return new RunMetrics
        {
            HasServerSamples = hasServerSamples,
            RunId = runId,
            Pacing = pacing,
            Scenario = "echo",
            Transport = "tcp",
            Clients = 100,
            TotalSend = 10000,
            TotalReceive = 10000,
            TargetRatePerSec = 500,
            SteadyRatePerSec = rps,
            RateAchievement = achievement,
            RttP99Ms = p99,
            RttP999Ms = p999,
            ServerSteadyRps = rps,
            ServerExceptions = exceptions,
            FinalActiveSessions = activeSessions,
            MemoryGrowthMb = memoryGrowth,
            SteadySampleCount = 20
        };
    }

    /// <summary>꼬리 지연은 실행마다 흔들리므로 지표별 중앙값으로 비교해야 한다.</summary>
    private static void GroupTakesMedianPerMetric()
    {
        var group = RunGroup.Create("g", [
            Metrics("g01", p99: 1.0),
            Metrics("g02", p99: 5.0),
            Metrics("g03", p99: 2.0)
        ]);

        AssertEx.Equal(2.0, group.Median.RttP99Ms, "세 값 1·5·2의 중앙값은 2여야 한다.");
    }

    /// <summary>예외나 세션 누수는 중앙값을 쓰면 한 번 일어난 사고가 묻힌다.</summary>
    private static void GroupKeepsWorstCaseForFaults()
    {
        var group = RunGroup.Create("g", [
            Metrics("g01", exceptions: 0, activeSessions: 0),
            Metrics("g02", exceptions: 3, activeSessions: 2),
            Metrics("g03", exceptions: 0, activeSessions: 0)
        ]);

        AssertEx.Equal(3L, group.Median.ServerExceptions, "예외는 최악값이 남아야 한다.");
        AssertEx.Equal(2L, group.Median.FinalActiveSessions, "세션 누수는 최악값이 남아야 한다.");
    }

    private static void LatencyRegressionFailsVerdict()
    {
        var baseline = RunGroup.Create("base", [Metrics("base01", p99: 1.0)]);
        var candidate = RunGroup.Create("cand", [Metrics("cand01", p99: 2.0)]);

        var verdict = Verdict.Compare(baseline, candidate, new Thresholds { MaxRttP99IncreaseRatio = 0.10 });

        AssertEx.True(verdict.Failed, "p99가 두 배가 되면 불합격이어야 한다.");
        var check = verdict.Checks.First(c => c.Name == "p99 지연");
        AssertEx.Equal(CheckOutcome.Fail, check.Outcome);
    }

    private static void LatencyWithinThresholdPasses()
    {
        var baseline = RunGroup.Create("base", [Metrics("base01", p99: 1.0)]);
        var candidate = RunGroup.Create("cand", [Metrics("cand01", p99: 1.05)]);

        var verdict = Verdict.Compare(baseline, candidate, new Thresholds { MaxRttP99IncreaseRatio = 0.10 });

        AssertEx.False(verdict.Failed, "5% 증가는 10% 상한 안이므로 통과해야 한다.");
    }

    private static void ThroughputDropFailsVerdict()
    {
        var baseline = RunGroup.Create("base", [Metrics("base01", rps: 500)]);
        var candidate = RunGroup.Create("cand", [Metrics("cand01", rps: 400)]);

        var verdict = Verdict.Compare(baseline, candidate, new Thresholds { MinThroughputRatio = 0.95 });

        AssertEx.True(verdict.Failed, "처리량이 20% 떨어지면 불합격이어야 한다.");
    }

    /// <summary>
    /// 닫힌 루프는 응답을 기다렸다 보내므로 부하가 덜 걸리고 지연도 낮게 나온다.
    /// 페이싱이 다른 실행을 견주면 잘못된 결론을 낸다.
    /// </summary>
    private static void MismatchedPacingIsInconclusive()
    {
        var baseline = RunGroup.Create("base", [Metrics("base01", pacing: "closed")]);
        var candidate = RunGroup.Create("cand", [Metrics("cand01", pacing: "open")]);

        var verdict = Verdict.Compare(baseline, candidate, new Thresholds());

        var check = verdict.Checks.First(c => c.Name == "페이싱 일치");
        AssertEx.Equal(CheckOutcome.Inconclusive, check.Outcome);
    }

    /// <summary>요청한 부하를 걸지 못한 실행은 지연 수치를 믿을 수 없다.</summary>
    private static void LowRateAchievementFails()
    {
        var candidate = RunGroup.Create("cand", [Metrics("cand01", achievement: 0.70)]);

        var verdict = Verdict.Evaluate(candidate, new Thresholds { MinRateAchievement = 0.95 });

        AssertEx.True(verdict.Failed, "달성률 70%는 불합격이어야 한다.");
    }

    private static void LoadsRunsFromCsvDirectories()
    {
        using var temp = TempDirectory.Create();
        var serverDir = Path.Combine(temp.Path, "demo-server");
        var clientDir = Path.Combine(temp.Path, "demo-client");
        Directory.CreateDirectory(serverDir);
        Directory.CreateDirectory(clientDir);

        File.WriteAllText(Path.Combine(clientDir, "client_summary.csv"),
            "timestamp_utc,run_id,machine_id,key,value\n" +
            "2026-01-01T00:00:00Z,demo,m1,clients,100\n" +
            "2026-01-01T00:00:00Z,demo,m1,pacing,open\n" +
            "2026-01-01T00:00:00Z,demo,m1,rtt_total_p99_us,1500\n");

        File.WriteAllText(Path.Combine(clientDir, "client_samples.csv"),
            "timestamp_utc,elapsed_ms,run_id,send_per_sec,rtt_p99_us,phase\n" +
            "2026-01-01T00:00:01Z,1000,demo,100.0,1200,rampup\n" +
            "2026-01-01T00:00:02Z,2000,demo,500.0,1500,steady\n");

        File.WriteAllText(Path.Combine(serverDir, "server_samples.csv"),
            "timestamp_utc,elapsed_ms,run_id,active_sessions,requests_per_sec,working_set_bytes,exception_total,phase\n" +
            "2026-01-01T00:00:02Z,2000,demo,100,500.0,104857600,0,steady\n");

        var runs = RunLoader.LoadAll(temp.Path);

        AssertEx.Equal(1, runs.Count, "서버와 클라이언트 디렉토리가 run_id로 하나의 실행으로 묶여야 한다.");
        var metrics = RunMetrics.From(runs[0]);
        AssertEx.Equal("demo", metrics.RunId);
        AssertEx.Equal("open", metrics.Pacing);
        AssertEx.Equal(1.5, metrics.RttP99Ms, "1500µs는 1.5ms여야 한다.");
        AssertEx.Equal(500.0, metrics.ServerSteadyRps, "정상 구간 표본만 평균해야 한다.");
    }

    /// <summary>리포트는 파일 하나로 열려야 하므로 외부 자원을 참조하면 안 된다.</summary>
    private static void WritesSelfContainedHtmlReport()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "report.html");
        var candidate = RunGroup.Create("cand", [Metrics("cand01")]);
        var verdict = Verdict.Evaluate(candidate, new Thresholds());

        HtmlReportWriter.Write(path, candidate, baseline: null, verdict, [], DateTimeOffset.UnixEpoch);

        var html = File.ReadAllText(path);
        AssertEx.True(html.Contains("<!doctype html>", StringComparison.OrdinalIgnoreCase), "HTML 문서여야 한다.");
        AssertEx.True(html.Contains("판정", StringComparison.Ordinal), "판정 표가 있어야 한다.");
        AssertEx.False(html.Contains("http://", StringComparison.OrdinalIgnoreCase), "외부 자원을 참조하면 안 된다.");
        AssertEx.False(html.Contains("https://", StringComparison.OrdinalIgnoreCase), "외부 자원을 참조하면 안 된다.");
    }
}
