namespace SuperSocketLite.LoadTest.Report;

public enum CheckOutcome
{
    Pass,
    Fail,
    /// <summary>판정할 수 없었습니다. 통과로도 실패로도 세지 않고 이유를 남깁니다.</summary>
    Inconclusive
}

public sealed record Check(string Name, CheckOutcome Outcome, string Detail);

/// <summary>실행 하나 또는 기준 대비 비교의 판정 결과입니다.</summary>
public sealed class Verdict
{
    private readonly List<Check> _checks = [];

    public IReadOnlyList<Check> Checks => _checks;

    public bool Failed => _checks.Any(c => c.Outcome == CheckOutcome.Fail);

    public bool HasInconclusive => _checks.Any(c => c.Outcome == CheckOutcome.Inconclusive);

    public void Add(string name, CheckOutcome outcome, string detail) => _checks.Add(new Check(name, outcome, detail));

    /// <summary>기준 실행 없이도 확인할 수 있는 조건들입니다.</summary>
    public static Verdict Evaluate(RunGroup candidate, Thresholds thresholds)
    {
        var verdict = new Verdict();
        var metrics = candidate.Median;

        verdict.Add(
            "오류율",
            metrics.ErrorRate <= thresholds.MaxErrorRate ? CheckOutcome.Pass : CheckOutcome.Fail,
            $"{metrics.ErrorRate:P3} (상한 {thresholds.MaxErrorRate:P3}, 타임아웃 {metrics.TotalTimeout} · 송신 실패 {metrics.TotalSendFail})");

        if (metrics.TargetRatePerSec > 0)
        {
            verdict.Add(
                "목표 레이트 달성",
                metrics.RateAchievement >= thresholds.MinRateAchievement ? CheckOutcome.Pass : CheckOutcome.Fail,
                $"{metrics.RateAchievement:P1} (하한 {thresholds.MinRateAchievement:P1}, 목표 {metrics.TargetRatePerSec:F0}/s · 실측 {metrics.SteadyRatePerSec:F1}/s)");
        }
        else
        {
            verdict.Add("목표 레이트 달성", CheckOutcome.Inconclusive, "목표 레이트가 기록되지 않은 실행이다.");
        }

        // 서버 계측을 끈 실행에는 서버 표본이 없다. 0으로 읽어 통과시키면
        // 확인하지 않은 것을 확인했다고 말하는 셈이 된다.
        if (thresholds.RequireZeroServerExceptions)
        {
            verdict.Add(
                "서버 예외",
                metrics.HasServerSamples
                    ? metrics.ServerExceptions == 0 ? CheckOutcome.Pass : CheckOutcome.Fail
                    : CheckOutcome.Inconclusive,
                metrics.HasServerSamples ? $"{metrics.ServerExceptions}건" : "서버 표본이 없는 실행이다.");
        }

        if (thresholds.RequireZeroSessionLeak)
        {
            verdict.Add(
                "세션 정리",
                metrics.HasServerSamples
                    ? metrics.FinalActiveSessions == 0 ? CheckOutcome.Pass : CheckOutcome.Fail
                    : CheckOutcome.Inconclusive,
                metrics.HasServerSamples
                    ? $"종료 후 활성 세션 {metrics.FinalActiveSessions}개"
                    : "서버 표본이 없는 실행이다.");
        }

        if (thresholds.RequireNoLocalResourceExhaustion)
        {
            verdict.Add(
                "부하 발생기 여유",
                metrics.LocalResourceExhaustion == 0 ? CheckOutcome.Pass : CheckOutcome.Fail,
                metrics.LocalResourceExhaustion == 0
                    ? "임시 포트·소켓 한계에 닿지 않았다."
                    : $"{metrics.LocalResourceExhaustion}건. 이 실행의 수치는 서버 성능을 말해 주지 않는다.");
        }

        if (metrics.SteadySampleCount == 0)
        {
            verdict.Add("정상 구간", CheckOutcome.Inconclusive, "정상 구간 표본이 없다. 비교할 수 있는 측정이 아니다.");
        }

        return verdict;
    }

    /// <summary>기준 실행과 견주어 회귀가 있는지 봅니다.</summary>
    public static Verdict Compare(RunGroup baseline, RunGroup candidate, Thresholds thresholds)
    {
        var verdict = Evaluate(candidate, thresholds);
        var a = baseline.Median;
        var b = candidate.Median;

        // 페이싱이 다르면 지연 비교가 성립하지 않는다.
        // 닫힌 루프는 응답을 기다렸다 보내므로 부하가 덜 걸리고 지연도 낮게 나온다.
        if (!string.Equals(a.Pacing, b.Pacing, StringComparison.Ordinal))
        {
            verdict.Add(
                "페이싱 일치",
                CheckOutcome.Inconclusive,
                $"기준 '{a.Pacing}' 대 대상 '{b.Pacing}'. 페이싱이 다르면 지연을 견줄 수 없다.");
        }

        AddRatioCheck(verdict, "p99 지연", a.RttP99Ms, b.RttP99Ms, thresholds.MaxRttP99IncreaseRatio, "ms");
        AddRatioCheck(verdict, "p99.9 지연", a.RttP999Ms, b.RttP999Ms, thresholds.MaxRttP999IncreaseRatio, "ms");

        AddThroughputCheck(verdict, a, b, thresholds);

        // 메모리도 서버 표본에서 온다. 한쪽이라도 없으면 비교할 것이 없다.
        if (a.HasServerSamples && b.HasServerSamples)
        {
            var memoryDelta = b.MemoryGrowthMb - a.MemoryGrowthMb;
            verdict.Add(
                "메모리 증가",
                memoryDelta <= thresholds.MaxMemoryGrowthIncreaseMb ? CheckOutcome.Pass : CheckOutcome.Fail,
                $"{a.MemoryGrowthMb:F1} → {b.MemoryGrowthMb:F1} MB (차이 {memoryDelta:+0.0;-0.0;0} MB, 상한 +{thresholds.MaxMemoryGrowthIncreaseMb:F0} MB)");
        }
        else
        {
            verdict.Add("메모리 증가", CheckOutcome.Inconclusive, "서버 표본이 없는 실행이 있어 메모리를 견줄 수 없다.");
        }

        return verdict;
    }

    /// <summary>
    /// 처리량을 견줍니다.
    /// 서버 표본이 양쪽에 다 있으면 서버가 센 요청 수로, 한쪽이라도 없으면
    /// 클라이언트가 보낸 정상 구간 송신 레이트로 봅니다.
    /// 계측을 끈 실행을 비교할 때가 후자이며, 그때도 부하가 줄었는지는 알 수 있어야 합니다.
    /// </summary>
    private static void AddThroughputCheck(Verdict verdict, RunMetrics baseline, RunMetrics candidate, Thresholds thresholds)
    {
        var useServer = baseline.HasServerSamples && candidate.HasServerSamples;
        var name = useServer ? "처리량" : "처리량 (클라이언트 기준)";
        var a = useServer ? baseline.ServerSteadyRps : baseline.SteadyRatePerSec;
        var b = useServer ? candidate.ServerSteadyRps : candidate.SteadyRatePerSec;

        if (a <= 0)
        {
            verdict.Add(name, CheckOutcome.Inconclusive, "기준 실행의 처리량이 없다.");
            return;
        }

        var ratio = b / a;
        verdict.Add(
            name,
            ratio >= thresholds.MinThroughputRatio ? CheckOutcome.Pass : CheckOutcome.Fail,
            $"{a:F1} → {b:F1}/s ({ratio:P1}, 하한 {thresholds.MinThroughputRatio:P0})");
    }

    private static void AddRatioCheck(Verdict verdict, string name, double baseline, double candidate, double maxIncrease, string unit)
    {
        if (baseline <= 0)
        {
            verdict.Add(name, CheckOutcome.Inconclusive, "기준 실행에 값이 없다.");
            return;
        }

        var increase = (candidate - baseline) / baseline;
        verdict.Add(
            name,
            increase <= maxIncrease ? CheckOutcome.Pass : CheckOutcome.Fail,
            $"{baseline:F3} → {candidate:F3} {unit} ({increase:+0.0%;-0.0%;0%}, 상한 +{maxIncrease:P0})");
    }
}
