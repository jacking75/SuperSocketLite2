using System.Globalization;
using System.Text;

namespace SuperSocketLite.LoadTest.Report;

/// <summary>부하 테스트 결과를 파일 하나로 열리는 HTML로 씁니다.</summary>
public static class HtmlReportWriter
{
    public static void Write(
        string path,
        RunGroup candidate,
        RunGroup? baseline,
        Verdict verdict,
        IReadOnlyList<RunData> runData,
        DateTimeOffset generatedAt)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"ko\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append($"<title>부하 테스트 리포트 · {Html.Escape(candidate.Name)}</title>\n");
        html.Append(Styles);
        html.Append("</head>\n<body>\n<main>\n");

        WriteHeader(html, candidate, baseline, verdict, generatedAt);
        WriteVerdict(html, verdict);
        WriteSummaryTable(html, candidate, baseline);

        if (candidate.Runs.Count > 1)
            WriteRunSpread(html, candidate);

        WriteCharts(html, runData, candidate);

        html.Append("<footer>SuperSocketLite2 · LoadTest.Report</footer>\n");
        html.Append("</main>\n</body>\n</html>\n");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, html.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteHeader(StringBuilder html, RunGroup candidate, RunGroup? baseline, Verdict verdict, DateTimeOffset generatedAt)
    {
        var status = verdict.Failed ? "불합격" : verdict.HasInconclusive ? "판정 보류 포함" : "합격";
        var statusClass = verdict.Failed ? "bad" : verdict.HasInconclusive ? "warn" : "good";

        html.Append("<header>\n");
        html.Append($"<h1>부하 테스트 리포트</h1>\n");
        html.Append($"<p class=\"lead\">대상 <strong>{Html.Escape(candidate.Name)}</strong>");
        if (baseline is not null)
            html.Append($" · 기준 <strong>{Html.Escape(baseline.Name)}</strong>");
        html.Append("</p>\n");
        html.Append($"<p class=\"meta\"><span class=\"pill {statusClass}\">{status}</span> 생성 {generatedAt:yyyy-MM-dd HH:mm:ss K}</p>\n");
        html.Append("</header>\n");
    }

    private static void WriteVerdict(StringBuilder html, Verdict verdict)
    {
        html.Append("<h2>판정</h2>\n<table>\n<thead><tr><th style=\"width:22%\">항목</th><th style=\"width:10%\">결과</th><th>내용</th></tr></thead>\n<tbody>\n");
        foreach (var check in verdict.Checks)
        {
            var (label, cls) = check.Outcome switch
            {
                CheckOutcome.Pass => ("통과", "good"),
                CheckOutcome.Fail => ("불합격", "bad"),
                _ => ("보류", "warn")
            };

            html.Append($"<tr><td>{Html.Escape(check.Name)}</td><td><span class=\"pill {cls}\">{label}</span></td><td>{Html.Escape(check.Detail)}</td></tr>\n");
        }

        html.Append("</tbody>\n</table>\n");
    }

    private static void WriteSummaryTable(StringBuilder html, RunGroup candidate, RunGroup? baseline)
    {
        html.Append("<h2>지표</h2>\n<table>\n<thead><tr><th style=\"width:30%\">지표</th>");
        if (baseline is not null)
            html.Append("<th class=\"num\">기준</th>");
        html.Append("<th class=\"num\">대상</th>");
        if (baseline is not null)
            html.Append("<th class=\"num\">변화</th>");
        html.Append("</tr></thead>\n<tbody>\n");

        AddRow(html, baseline, candidate, "시나리오", m => m.Scenario, raw: true);
        AddRow(html, baseline, candidate, "전송 방식", m => m.Transport, raw: true);
        AddRow(html, baseline, candidate, "페이싱", m => m.Pacing, raw: true);
        AddRow(html, baseline, candidate, "클라이언트 수", m => m.Clients.ToString("N0", CultureInfo.InvariantCulture), raw: true);

        AddNumeric(html, baseline, candidate, "목표 레이트 (req/s)", m => m.TargetRatePerSec, "F0", higherIsBetter: null);
        AddNumeric(html, baseline, candidate, "정상 구간 송신 (req/s)", m => m.SteadyRatePerSec, "F1", higherIsBetter: true);
        AddNumeric(html, baseline, candidate, "목표 달성률", m => m.RateAchievement * 100, "F1", higherIsBetter: true, suffix: "%");
        AddNumeric(html, baseline, candidate, "서버 처리량 (req/s)", m => m.ServerSteadyRps, "F1", higherIsBetter: true);

        AddNumeric(html, baseline, candidate, "RTT p50 (ms)", m => m.RttP50Ms, "F3", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "RTT p90 (ms)", m => m.RttP90Ms, "F3", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "RTT p99 (ms)", m => m.RttP99Ms, "F3", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "RTT p99.9 (ms)", m => m.RttP999Ms, "F3", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "RTT 최대 (ms)", m => m.RttMaxMs, "F3", higherIsBetter: false);

        AddNumeric(html, baseline, candidate, "타임아웃", m => m.TotalTimeout, "F0", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "송신 실패", m => m.TotalSendFail, "F0", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "접속 실패", m => m.TotalConnectFail, "F0", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "서버 예외", m => m.ServerExceptions, "F0", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "종료 후 활성 세션", m => m.FinalActiveSessions, "F0", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "메모리 증가 (MB)", m => m.MemoryGrowthMb, "F1", higherIsBetter: false);
        AddNumeric(html, baseline, candidate, "최대 워킹셋 (MB)", m => m.PeakWorkingSetMb, "F1", higherIsBetter: false);

        html.Append("</tbody>\n</table>\n");
    }

    private static void WriteRunSpread(StringBuilder html, RunGroup group)
    {
        html.Append("<h2>반복 실행의 흩어짐</h2>\n");
        html.Append("<p class=\"muted\">꼬리 지연은 실행마다 흔들리므로 위 지표는 지표별 중앙값이다. 아래는 각 실행의 실제 값이다.</p>\n");
        html.Append("<table>\n<thead><tr><th>실행</th><th class=\"num\">송신 (req/s)</th><th class=\"num\">p99 (ms)</th><th class=\"num\">p99.9 (ms)</th><th class=\"num\">달성률</th></tr></thead>\n<tbody>\n");

        foreach (var run in group.Runs)
        {
            html.Append("<tr>");
            html.Append($"<td>{Html.Escape(run.RunId)}</td>");
            html.Append($"<td class=\"num\">{run.SteadyRatePerSec.ToString("F1", CultureInfo.InvariantCulture)}</td>");
            html.Append($"<td class=\"num\">{run.RttP99Ms.ToString("F3", CultureInfo.InvariantCulture)}</td>");
            html.Append($"<td class=\"num\">{run.RttP999Ms.ToString("F3", CultureInfo.InvariantCulture)}</td>");
            html.Append($"<td class=\"num\">{(run.RateAchievement * 100).ToString("F1", CultureInfo.InvariantCulture)}%</td>");
            html.Append("</tr>\n");
        }

        html.Append("</tbody>\n</table>\n");
    }

    private static void WriteCharts(StringBuilder html, IReadOnlyList<RunData> runData, RunGroup candidate)
    {
        // 여러 회 실행이면 첫 실행의 시계열을 보인다. 중앙값 시계열은 시점이 어긋나 의미가 흐려진다.
        var runId = candidate.Runs[0].RunId;
        var run = runData.FirstOrDefault(r => r.RunId == runId);
        if (run is null)
            return;

        html.Append("<h2>시계열</h2>\n");
        if (candidate.Runs.Count > 1)
            html.Append($"<p class=\"muted\">반복 실행 중 <code>{Html.Escape(runId)}</code>의 흐름이다.</p>\n");

        html.Append("<div class=\"charts\">\n");

        var clientSteady = run.ClientSamples.Where(s => s.Phase is "steady" or "unknown").ToList();
        html.Append(SparkChart.Render(
            "클라이언트 송신",
            [.. run.ClientSamples.Select(s => ((double)s.ElapsedMs, s.SendPerSec))],
            "req/s",
            "#0f766e"));

        html.Append(SparkChart.Render(
            "RTT p99 (1초 창)",
            [.. clientSteady.Select(s => ((double)s.ElapsedMs, s.RttP99Us / 1000.0))],
            "ms",
            "#b45309"));

        html.Append(SparkChart.Render(
            "서버 처리량",
            [.. run.ServerSamples.Select(s => ((double)s.ElapsedMs, s.RequestsPerSec))],
            "req/s",
            "#1d4ed8"));

        html.Append(SparkChart.Render(
            "서버 워킹셋",
            [.. run.ServerSamples.Select(s => ((double)s.ElapsedMs, s.WorkingSetBytes / 1024.0 / 1024.0))],
            "MB",
            "#7c3aed"));

        html.Append(SparkChart.Render(
            "활성 세션",
            [.. run.ServerSamples.Select(s => ((double)s.ElapsedMs, (double)s.ActiveSessions))],
            "개",
            "#be123c"));

        html.Append("</div>\n");
    }

    private static void AddRow(StringBuilder html, RunGroup? baseline, RunGroup candidate, string label, Func<RunMetrics, string> selector, bool raw)
    {
        html.Append($"<tr><td>{Html.Escape(label)}</td>");
        if (baseline is not null)
            html.Append($"<td class=\"num\">{Html.Escape(selector(baseline.Median))}</td>");
        html.Append($"<td class=\"num\">{Html.Escape(selector(candidate.Median))}</td>");
        if (baseline is not null)
        {
            var same = string.Equals(selector(baseline.Median), selector(candidate.Median), StringComparison.Ordinal);
            html.Append($"<td class=\"num\">{(same ? "—" : "<span class=\"pill warn\">다름</span>")}</td>");
        }

        html.Append("</tr>\n");
    }

    private static void AddNumeric(
        StringBuilder html,
        RunGroup? baseline,
        RunGroup candidate,
        string label,
        Func<RunMetrics, double> selector,
        string format,
        bool? higherIsBetter,
        string suffix = "")
    {
        var value = selector(candidate.Median);
        html.Append($"<tr><td>{Html.Escape(label)}</td>");

        if (baseline is not null)
        {
            var baseValue = selector(baseline.Median);
            html.Append($"<td class=\"num\">{baseValue.ToString(format, CultureInfo.InvariantCulture)}{suffix}</td>");
            html.Append($"<td class=\"num\">{value.ToString(format, CultureInfo.InvariantCulture)}{suffix}</td>");
            html.Append($"<td class=\"num\">{FormatDelta(baseValue, value, higherIsBetter)}</td>");
        }
        else
        {
            html.Append($"<td class=\"num\">{value.ToString(format, CultureInfo.InvariantCulture)}{suffix}</td>");
        }

        html.Append("</tr>\n");
    }

    private static string FormatDelta(double baseline, double candidate, bool? higherIsBetter)
    {
        if (Math.Abs(baseline) < 1e-9)
            return candidate == 0 ? "—" : "신규";

        var ratio = (candidate - baseline) / Math.Abs(baseline);
        if (Math.Abs(ratio) < 0.005)
            return "—";

        var text = ratio.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
        if (higherIsBetter is null)
            return text;

        var better = higherIsBetter.Value ? ratio > 0 : ratio < 0;
        return $"<span class=\"pill {(better ? "good" : "bad")}\">{text}</span>";
    }

    private const string Styles = """
<style>
  :root {
    color-scheme: light;
    --bg: #f7f9fb; --panel: #fff; --text: #1f2937; --muted: #64748b;
    --line: #d8e0ea; --accent: #156f6b;
    --good: #15803d; --good-soft: #e8f5ec;
    --warn: #b45309; --warn-soft: #fef3e2;
    --bad: #b91c1c; --bad-soft: #fdeaea;
  }
  * { box-sizing: border-box; }
  body { margin: 0; font-family: "Segoe UI", "Malgun Gothic", Arial, sans-serif;
         background: var(--bg); color: var(--text); line-height: 1.6; }
  main { max-width: 1000px; margin: 0 auto; padding: 40px 24px 64px; }
  header { border-bottom: 1px solid var(--line); padding-bottom: 20px; margin-bottom: 28px; }
  h1 { margin: 0 0 8px; font-size: 28px; }
  h2 { margin: 36px 0 12px; font-size: 20px; padding-bottom: 6px; border-bottom: 2px solid #e7f5f2; }
  .lead { margin: 0; color: var(--muted); }
  .meta { margin: 10px 0 0; font-size: 13px; color: var(--muted); }
  .muted { color: var(--muted); font-size: 14px; }
  table { width: 100%; border-collapse: collapse; margin: 12px 0 20px; font-size: 14px; background: var(--panel); }
  th, td { border: 1px solid var(--line); padding: 8px 10px; text-align: left; vertical-align: top; }
  th { background: #e7f5f2; font-weight: 600; }
  td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
  code { background: #f1f5f9; padding: 1px 5px; border-radius: 3px; font-family: Consolas, monospace; font-size: 0.92em; }
  .pill { display: inline-block; padding: 1px 9px; border-radius: 11px; font-size: 12px; font-weight: 600; white-space: nowrap; }
  .pill.good { background: var(--good-soft); color: var(--good); }
  .pill.warn { background: var(--warn-soft); color: var(--warn); }
  .pill.bad { background: var(--bad-soft); color: var(--bad); }
  .charts { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 16px; }
  .chart { border: 1px solid var(--line); border-radius: 6px; background: var(--panel); padding: 12px 14px; }
  .chart-title { display: block; font-size: 13px; font-weight: 600; margin-bottom: 6px; }
  .chart-unit { display: block; font-size: 12px; color: var(--muted); margin-top: 4px; }
  .chart svg { width: 100%; height: auto; }
  .chart .grid { stroke: #e2e8f0; stroke-width: 1; }
  .chart .series { fill: none; stroke-width: 1.6; }
  .chart .axis { font-size: 10px; fill: var(--muted); }
  footer { margin-top: 48px; padding-top: 16px; border-top: 1px solid var(--line); font-size: 13px; color: var(--muted); }
</style>
""";
}
