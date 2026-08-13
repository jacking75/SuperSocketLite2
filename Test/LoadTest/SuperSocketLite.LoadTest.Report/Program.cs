using SuperSocketLite.LoadTest.Report;

if (ReportOptions.IsHelpRequest(args))
{
    Console.WriteLine(ReportOptions.HelpText);
    return 0;
}

ReportOptions options;
try
{
    options = ReportOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ReportOptions.HelpText);
    return 2;
}

if (options.PrintThresholds)
{
    Console.WriteLine(Thresholds.ToJsonTemplate());
    return 0;
}

var runs = RunLoader.LoadAll(options.Input);
if (runs.Count == 0)
{
    Console.Error.WriteLine($"'{options.Input}' 아래에서 실행 결과를 찾지 못했다.");
    return 1;
}

if (options.ListOnly)
{
    Console.WriteLine($"{options.Input} 아래 실행 {runs.Count}개:");
    foreach (var run in runs)
    {
        var metrics = RunMetrics.From(run);
        Console.WriteLine($"  {run.RunId,-32} pacing={metrics.Pacing,-6} clients={metrics.Clients,-6} send={metrics.TotalSend,-8} p99={metrics.RttP99Ms:F3}ms");
    }

    return 0;
}

Thresholds thresholds;
try
{
    thresholds = Thresholds.Load(options.ThresholdsPath);
}
catch (Exception ex) when (ex is FileNotFoundException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"임계값을 읽지 못했다: {ex.Message}");
    return 2;
}

var candidateGroup = BuildGroup(runs, options.RunPrefix, "대상");
if (candidateGroup is null)
{
    Console.Error.WriteLine(options.RunPrefix is null
        ? "리포트 대상 실행이 없다."
        : $"'{options.RunPrefix}'로 시작하는 실행이 없다.");
    return 1;
}

RunGroup? baselineGroup = null;
if (options.BaselinePrefix is not null)
{
    baselineGroup = BuildGroup(runs, options.BaselinePrefix, "기준");
    if (baselineGroup is null)
    {
        Console.Error.WriteLine($"'{options.BaselinePrefix}'로 시작하는 기준 실행이 없다.");
        return 1;
    }
}

var verdict = baselineGroup is null
    ? Verdict.Evaluate(candidateGroup, thresholds)
    : Verdict.Compare(baselineGroup, candidateGroup, thresholds);

HtmlReportWriter.Write(options.Output, candidateGroup, baselineGroup, verdict, runs, DateTimeOffset.Now);

Console.WriteLine($"대상: {candidateGroup.Name} ({candidateGroup.Runs.Count}회)");
if (baselineGroup is not null)
    Console.WriteLine($"기준: {baselineGroup.Name} ({baselineGroup.Runs.Count}회)");
Console.WriteLine();

foreach (var check in verdict.Checks)
{
    var label = check.Outcome switch
    {
        CheckOutcome.Pass => "PASS",
        CheckOutcome.Fail => "FAIL",
        _ => "----"
    };

    Console.WriteLine($"  {label}  {check.Name,-18} {check.Detail}");
}

Console.WriteLine();
Console.WriteLine($"리포트: {Path.GetFullPath(options.Output)}");

if (verdict.Failed)
{
    Console.WriteLine("판정: 불합격");
    // 자동화에 물릴 수 있도록 종료 코드로 알린다.
    // 옵션을 켜지 않으면 리포트만 만들고 성공으로 끝난다.
    return options.FailOnRegression ? 1 : 0;
}

Console.WriteLine(verdict.HasInconclusive ? "판정: 합격 (보류 항목 있음)" : "판정: 합격");
return 0;

static RunGroup? BuildGroup(IReadOnlyList<RunData> runs, string? prefix, string label)
{
    var matched = (prefix is null
            ? runs
            : runs.Where(r => r.RunId.StartsWith(prefix, StringComparison.Ordinal)))
        .Select(RunMetrics.From)
        .OrderBy(m => m.RunId, StringComparer.Ordinal)
        .ToList();

    if (matched.Count == 0)
        return null;

    var name = prefix ?? label;
    var group = RunGroup.Create(name, matched);

    if (group.HasMixedPacing)
    {
        Console.Error.WriteLine(
            $"경고: '{name}' 묶음의 실행들이 서로 다른 페이싱을 썼다. 지연 수치를 함께 묶을 수 없다.");
    }

    return group;
}
