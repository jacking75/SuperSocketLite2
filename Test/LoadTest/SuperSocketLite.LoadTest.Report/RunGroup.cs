namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 같은 조건으로 반복한 실행들을 하나로 묶습니다.
/// </summary>
/// <remarks>
/// 꼬리 지연(p99·p99.9)은 실행마다 크게 흔들립니다. 로컬 측정에서 p99가 실행 간 44%,
/// p99.9가 124%까지 차이 난 적이 있습니다. 한 번의 실행으로 회귀를 판정하면
/// 그 변동을 성능 변화로 오인하게 되므로, 지표마다 중앙값을 취해 비교합니다.
///
/// 중앙값은 지표별로 따로 구합니다. 그래서 이 묶음의 수치 조합이 실제 어느 한 실행과
/// 똑같지는 않습니다. 목적이 "대표 실행 고르기"가 아니라 "지표마다 흔들림 걷어내기"이기 때문입니다.
/// </remarks>
public sealed class RunGroup
{
    private RunGroup(string name, IReadOnlyList<RunMetrics> runs, RunMetrics median)
    {
        Name = name;
        Runs = runs;
        Median = median;
    }

    public string Name { get; }

    public IReadOnlyList<RunMetrics> Runs { get; }

    /// <summary>지표별 중앙값입니다. 실행이 하나뿐이면 그 실행의 값과 같습니다.</summary>
    public RunMetrics Median { get; }

    /// <summary>묶인 실행들이 서로 다른 페이싱을 썼는지입니다. 그렇다면 비교 자체가 성립하지 않습니다.</summary>
    public bool HasMixedPacing => Runs.Select(r => r.Pacing).Distinct(StringComparer.Ordinal).Count() > 1;

    public string Pacing => Median.Pacing;

    public static RunGroup Create(string name, IReadOnlyList<RunMetrics> runs)
    {
        if (runs.Count == 0)
            throw new ArgumentException("A run group needs at least one run.", nameof(runs));

        var first = runs[0];
        var median = new RunMetrics
        {
            RunId = runs.Count == 1 ? first.RunId : $"{name} (중앙값 {runs.Count}회)",
            Scenario = first.Scenario,
            Transport = first.Transport,
            Pacing = first.Pacing,
            Clients = (long)MedianOf(runs, r => r.Clients),
            DurationMs = (long)MedianOf(runs, r => r.DurationMs),
            TotalSend = (long)MedianOf(runs, r => r.TotalSend),
            TotalReceive = (long)MedianOf(runs, r => r.TotalReceive),
            TotalTimeout = (long)MedianOf(runs, r => r.TotalTimeout),
            TotalSendFail = (long)MedianOf(runs, r => r.TotalSendFail),
            TotalConnectFail = (long)MedianOf(runs, r => r.TotalConnectFail),
            SocketErrors = (long)MedianOf(runs, r => r.SocketErrors),
            LocalResourceExhaustion = (long)MedianOf(runs, r => r.LocalResourceExhaustion),
            TargetRatePerSec = MedianOf(runs, r => r.TargetRatePerSec),
            SteadyRatePerSec = MedianOf(runs, r => r.SteadyRatePerSec),
            RateAchievement = MedianOf(runs, r => r.RateAchievement),
            RttP50Ms = MedianOf(runs, r => r.RttP50Ms),
            RttP90Ms = MedianOf(runs, r => r.RttP90Ms),
            RttP99Ms = MedianOf(runs, r => r.RttP99Ms),
            RttP999Ms = MedianOf(runs, r => r.RttP999Ms),
            RttMaxMs = MedianOf(runs, r => r.RttMaxMs),
            ServerSteadyRps = MedianOf(runs, r => r.ServerSteadyRps),
            // 한 번이라도 서버 표본이 빠졌다면 이 묶음의 서버 수치는 믿을 수 없다.
            HasServerSamples = runs.All(r => r.HasServerSamples),
            // 예외와 세션 누수는 중앙값을 쓰면 한 번이라도 일어난 사고가 묻힌다.
            // 안전한 쪽으로 최악값을 남긴다.
            ServerExceptions = runs.Max(r => r.ServerExceptions),
            FinalActiveSessions = runs.Max(r => r.FinalActiveSessions),
            OutageTotal = runs.Max(r => r.OutageTotal),
            ReconnectTotal = runs.Max(r => r.ReconnectTotal),
            MaxOutageMs = runs.Max(r => r.MaxOutageMs),
            MemoryGrowthMb = MedianOf(runs, r => r.MemoryGrowthMb),
            PeakWorkingSetMb = MedianOf(runs, r => r.PeakWorkingSetMb),
            SteadySampleCount = (int)MedianOf(runs, r => r.SteadySampleCount),
            // 적체와 풀 소진도 사고에 가까우므로 최악값을 남긴다.
            // 계측이 없던 실행(-1)은 빼고 계산하며, 전부 없으면 -1이 그대로 남는다.
            MaxSendQueueDepthTotal = WorstOf(runs, r => r.MaxSendQueueDepthTotal, takeMax: true),
            MaxSendQueueDepthSession = WorstOf(runs, r => r.MaxSendQueueDepthSession, takeMax: true),
            MinReceivePoolAvailable = WorstOf(runs, r => r.MinReceivePoolAvailable, takeMax: false),
            MinSendPoolAvailable = WorstOf(runs, r => r.MinSendPoolAvailable, takeMax: false)
        };

        return new RunGroup(name, runs, median);
    }

    private static long WorstOf(IReadOnlyList<RunMetrics> runs, Func<RunMetrics, long> selector, bool takeMax)
    {
        var observed = runs.Select(selector).Where(value => value >= 0).ToList();
        if (observed.Count == 0)
            return -1;

        return takeMax ? observed.Max() : observed.Min();
    }

    private static double MedianOf(IReadOnlyList<RunMetrics> runs, Func<RunMetrics, double> selector)
    {
        var values = runs.Select(selector).OrderBy(v => v).ToArray();
        if (values.Length == 0)
            return 0;

        var middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
    }
}
