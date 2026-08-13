using System.Diagnostics;
using System.Globalization;
using SuperSocketLite.LoadTest.Client.Metrics;

namespace SuperSocketLite.LoadTest.Client;

public sealed class ClientRuntime
{
    private readonly LoadTestOptions _options;
    private readonly ClientMetricsCollector _metrics = new();

    /// <summary>목표 동시 접속의 95%에 한 번이라도 도달했는지입니다. 정상 구간 판정의 기준입니다.</summary>
    private bool _reachedFullLoad;
    private bool _stopping;

    private long _steadyStartMs = -1;
    private long _steadyStartSend;
    private long _steadyEndMs = -1;
    private long _steadyEndSend;

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
        // 수천 클라이언트가 한꺼번에 뜰 때 스레드풀 증가 속도가 램프업을 늦추지 않도록 미리 확보한다.
        LoadGeneratorHost.PrepareThreadPool(_options);

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
                WriteSample(writers, stopwatch.ElapsedMilliseconds);
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

        _stopping = true;
        duration.Cancel();
        try
        {
            await sampler.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        WriteSample(writers, stopwatch.ElapsedMilliseconds);
        WriteSummary(writers, stopwatch.ElapsedMilliseconds);
        writers.Flush();
        return 0;
    }

    private void WriteSample(ClientCsvWriters writers, long elapsedMs)
    {
        var snapshot = _metrics.Snapshot(_options.RunId, elapsedMs, resetLatency: true, phase: DeterminePhase());
        writers.WriteSample(snapshot);
        TrackSteadyWindow(snapshot);
    }

    /// <summary>
    /// 현재 구간을 판정합니다.
    /// 목표 동시 접속에 도달하기 전은 rampup, 종료 절차에 들어가면 rampdown입니다.
    /// 한 번 만재에 도달한 뒤에는 재접속 등으로 잠시 줄어도 정상 구간으로 봅니다.
    /// </summary>
    private string DeterminePhase()
    {
        if (_stopping)
            return "rampdown";

        var active = _metrics.ActiveClients;
        if (active == 0)
            return "idle";

        if (!_reachedFullLoad)
        {
            if (active < _options.Clients * 0.95)
                return "rampup";

            _reachedFullLoad = true;
        }

        return "steady";
    }

    private void TrackSteadyWindow(ClientMetricsSnapshot snapshot)
    {
        if (snapshot.Phase != "steady")
            return;

        if (_steadyStartMs < 0)
        {
            _steadyStartMs = snapshot.ElapsedMs;
            _steadyStartSend = snapshot.TotalSendSuccess;
        }

        _steadyEndMs = snapshot.ElapsedMs;
        _steadyEndSend = snapshot.TotalSendSuccess;
    }

    private void WriteSummary(ClientCsvWriters writers, long elapsedMs)
    {
        var final = _metrics.Snapshot(_options.RunId, elapsedMs, resetLatency: false, phase: "rampdown");
        var latency = _metrics.TotalLatency;

        writers.WriteSummary(_options.RunId, "clients", _options.Clients);
        writers.WriteSummary(_options.RunId, "duration_ms", elapsedMs);
        writers.WriteSummary(_options.RunId, "scenario", _options.Scenario);
        writers.WriteSummary(_options.RunId, "transport", _options.Transport);
        writers.WriteSummary(_options.RunId, "pacing", _options.UsesOpenLoop() ? "open" : "closed");
        writers.WriteSummary(_options.RunId, "max_in_flight", _options.ResolveMaxInFlight());
        writers.WriteSummary(_options.RunId, "operation_sampling", Format(_options.OperationSampling));
        writers.WriteSummary(_options.RunId, "dropped_client_operation_rows", writers.DroppedOperationRows);

        writers.WriteSummary(_options.RunId, "total_connect_success", final.TotalConnectSuccess);
        writers.WriteSummary(_options.RunId, "total_connect_fail", final.TotalConnectFail);
        writers.WriteSummary(_options.RunId, "total_send_success", final.TotalSendSuccess);
        writers.WriteSummary(_options.RunId, "total_send_fail", final.TotalSendFail);
        writers.WriteSummary(_options.RunId, "total_receive", final.TotalReceive);
        writers.WriteSummary(_options.RunId, "total_timeout", final.TotalTimeout);
        writers.WriteSummary(_options.RunId, "socket_error_total", final.SocketErrorTotal);
        writers.WriteSummary(_options.RunId, "protocol_error_total", final.ProtocolErrorTotal);
        writers.WriteSummary(_options.RunId, "runtime_error_total", final.RuntimeErrorTotal);

        var attempted = final.TotalSendSuccess + final.TotalSendFail;
        writers.WriteSummary(_options.RunId, "send_success_rate", Format(Ratio(final.TotalSendSuccess, attempted)));
        writers.WriteSummary(_options.RunId, "response_rate", Format(Ratio(final.TotalReceive, final.TotalSendSuccess)));

        // 실행 전체 누적 분포다. 창 스냅샷과 달리 초기화되지 않으므로 표본 추출 비율과 무관하게 정확하다.
        writers.WriteSummary(_options.RunId, "rtt_total_count", latency.Count);
        writers.WriteSummary(_options.RunId, "rtt_total_p50_us", latency.P50Us);
        writers.WriteSummary(_options.RunId, "rtt_total_p90_us", latency.P90Us);
        writers.WriteSummary(_options.RunId, "rtt_total_p95_us", latency.P95Us);
        writers.WriteSummary(_options.RunId, "rtt_total_p99_us", latency.P99Us);
        writers.WriteSummary(_options.RunId, "rtt_total_p999_us", latency.P999Us);
        writers.WriteSummary(_options.RunId, "rtt_total_max_us", latency.MaxUs);

        // 목표 부하를 내지 못했을 때 원인이 클라이언트 쪽인지 가리는 지표다.
        // 스케줄 지연이 크거나 건너뛴 송신이 있으면 서버가 아니라 부하 발생기가 한계에 닿은 것이다.
        var scheduleDelay = _metrics.TotalScheduleDelay;
        writers.WriteSummary(_options.RunId, "send_schedule_delay_p50_us", scheduleDelay.P50Us);
        writers.WriteSummary(_options.RunId, "send_schedule_delay_p99_us", scheduleDelay.P99Us);
        writers.WriteSummary(_options.RunId, "send_schedule_delay_max_us", scheduleDelay.MaxUs);
        writers.WriteSummary(_options.RunId, "send_skipped_in_flight", _metrics.SendSkippedInFlight);
        writers.WriteSummary(_options.RunId, "max_in_flight_observed", _metrics.MaxInFlightObserved);
        writers.WriteSummary(_options.RunId, "local_resource_exhaustion", _metrics.LocalResourceExhaustion);

        var targetRate = _options.Clients * _options.SendRatePerClient;
        writers.WriteSummary(_options.RunId, "target_send_rate_per_sec", Format(targetRate));

        var steadyWindowMs = _steadyEndMs - _steadyStartMs;
        writers.WriteSummary(_options.RunId, "steady_window_ms", Math.Max(0, steadyWindowMs));

        if (steadyWindowMs > 0)
        {
            var achieved = (_steadyEndSend - _steadyStartSend) * 1000.0 / steadyWindowMs;
            writers.WriteSummary(_options.RunId, "steady_send_rate_per_sec", Format(achieved));
            writers.WriteSummary(_options.RunId, "steady_rate_achievement", Format(targetRate > 0 ? achieved / targetRate : 0.0));
        }
        else
        {
            // 정상 구간 샘플이 2개 미만이면 달성률을 계산할 수 없다. 0으로 적어 판정이 이 실행을 놓치지 않게 한다.
            writers.WriteSummary(_options.RunId, "steady_send_rate_per_sec", Format(0.0));
            writers.WriteSummary(_options.RunId, "steady_rate_achievement", Format(0.0));
        }
    }

    private static double Ratio(long numerator, long denominator)
    {
        return denominator > 0 ? numerator / (double)denominator : 0.0;
    }

    private static string Format(double value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }
}
