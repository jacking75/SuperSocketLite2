using System.Globalization;

namespace SuperSocketLite.LoadTest.Report;

/// <summary>
/// 한 실행을 판정에 쓸 수 있는 몇 개의 수치로 줄인 것입니다.
/// </summary>
public sealed record RunMetrics
{
    public required string RunId { get; init; }
    public string Scenario { get; init; } = "";
    public string Transport { get; init; } = "";

    /// <summary>
    /// 이 실행이 어떤 방식으로 부하를 걸었는지입니다.
    /// 페이싱이 다른 실행끼리는 지연 수치를 비교할 수 없으므로 판정 전에 확인해야 합니다.
    /// </summary>
    public string Pacing { get; init; } = "unknown";

    public long Clients { get; init; }
    public long DurationMs { get; init; }

    public long TotalSend { get; init; }
    public long TotalReceive { get; init; }
    public long TotalTimeout { get; init; }
    public long TotalSendFail { get; init; }
    public long TotalConnectFail { get; init; }
    public long SocketErrors { get; init; }
    public long LocalResourceExhaustion { get; init; }

    public double TargetRatePerSec { get; init; }
    public double SteadyRatePerSec { get; init; }
    public double RateAchievement { get; init; }

    public double RttP50Ms { get; init; }
    public double RttP90Ms { get; init; }
    public double RttP99Ms { get; init; }
    public double RttP999Ms { get; init; }
    public double RttMaxMs { get; init; }

    /// <summary>정상 구간의 평균 서버 처리량입니다. 무부하 구간은 빠집니다.</summary>
    public double ServerSteadyRps { get; init; }
    public long ServerExceptions { get; init; }
    public long FinalActiveSessions { get; init; }
    public double MemoryGrowthMb { get; init; }
    public double PeakWorkingSetMb { get; init; }

    public int SteadySampleCount { get; init; }

    /// <summary>오류율입니다. 보낸 요청 대비 타임아웃과 송신 실패의 비율입니다.</summary>
    public double ErrorRate => TotalSend > 0 ? (TotalTimeout + TotalSendFail) / (double)TotalSend : 0;

    public static RunMetrics From(RunData run)
    {
        var steadyClient = run.ClientSamples.Where(IsLoadBearing).ToList();
        var steadyServer = run.ServerSamples.Where(IsLoadBearing).ToList();

        var working = run.ServerSamples.Where(s => s.WorkingSetBytes > 0).ToList();
        var memoryGrowthMb = working.Count > 0
            ? (working[^1].WorkingSetBytes - working[0].WorkingSetBytes) / 1024.0 / 1024.0
            : 0;

        return new RunMetrics
        {
            RunId = run.RunId,
            Scenario = Text(run, "scenario"),
            Transport = Text(run, "transport"),
            Pacing = Text(run, "pacing", "unknown"),
            Clients = Integer(run, "clients"),
            DurationMs = Integer(run, "duration_ms"),
            TotalSend = Integer(run, "total_send_success"),
            TotalReceive = Integer(run, "total_receive"),
            TotalTimeout = Integer(run, "total_timeout"),
            TotalSendFail = Integer(run, "total_send_fail"),
            TotalConnectFail = Integer(run, "total_connect_fail"),
            SocketErrors = Integer(run, "socket_error_total"),
            LocalResourceExhaustion = Integer(run, "local_resource_exhaustion"),
            TargetRatePerSec = Number(run, "target_send_rate_per_sec"),
            SteadyRatePerSec = Number(run, "steady_send_rate_per_sec"),
            RateAchievement = Number(run, "steady_rate_achievement"),
            RttP50Ms = Number(run, "rtt_total_p50_us") / 1000.0,
            RttP90Ms = Number(run, "rtt_total_p90_us") / 1000.0,
            RttP99Ms = Number(run, "rtt_total_p99_us") / 1000.0,
            RttP999Ms = Number(run, "rtt_total_p999_us") / 1000.0,
            RttMaxMs = Number(run, "rtt_total_max_us") / 1000.0,
            ServerSteadyRps = steadyServer.Count > 0 ? steadyServer.Average(s => s.RequestsPerSec) : 0,
            ServerExceptions = run.ServerSamples.Count > 0 ? run.ServerSamples.Max(s => s.ExceptionTotal) : 0,
            FinalActiveSessions = run.ServerSamples.Count > 0 ? run.ServerSamples[^1].ActiveSessions : 0,
            MemoryGrowthMb = memoryGrowthMb,
            PeakWorkingSetMb = working.Count > 0 ? working.Max(s => s.WorkingSetBytes) / 1024.0 / 1024.0 : 0,
            SteadySampleCount = steadyClient.Count
        };
    }

    /// <summary>
    /// 부하가 실린 구간인지입니다.
    /// phase 컬럼이 없던 시절의 실행은 unknown이며, 집계에서 사라지지 않도록 부하 구간으로 봅니다.
    /// </summary>
    private static bool IsLoadBearing(ClientSample sample) => sample.Phase is "steady" or "unknown";

    private static bool IsLoadBearing(ServerSample sample) => sample.Phase is "steady" or "unknown";

    private static string Text(RunData run, string key, string fallback = "")
        => run.Summary.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static long Integer(RunData run, string key)
        => run.Summary.TryGetValue(key, out var value)
           && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static double Number(RunData run, string key)
        => run.Summary.TryGetValue(key, out var value)
           && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
}
