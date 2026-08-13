namespace SuperSocketLite.LoadTest.Report;

public sealed class ReportOptions
{
    public static string HelpText { get; } = string.Join(Environment.NewLine, [
        "SuperSocketLite LoadTest Report",
        "",
        "CSV로 남은 부하 테스트 결과를 HTML 리포트로 만들고, 기준 실행과 견주어 회귀를 판정한다.",
        "",
        "Options:",
        "  --input <directory>      실행 CSV가 모인 루트. 기본 logs/loadtest",
        "  --run <prefix>           리포트 대상. run_id가 이 접두사로 시작하는 실행을 모은다.",
        "  --baseline <prefix>      비교 기준. 같은 방식으로 모은다.",
        "  --thresholds <file>      판정 임계값 JSON. 생략하면 기본값을 쓴다.",
        "  --output <file>          HTML 리포트 경로. 기본 logs/loadtest/report.html",
        "  --fail-on-regression     판정이 불합격이면 종료 코드 1을 반환한다.",
        "  --list                   찾은 실행 목록만 출력한다.",
        "  --print-thresholds       기본 임계값을 JSON으로 출력한다.",
        "  --help",
        "",
        "여러 회 반복한 실행은 접두사로 함께 묶인다. 꼬리 지연은 실행마다 흔들리므로",
        "지표별 중앙값으로 비교한다. 반복 실행을 권장한다.",
        "",
        "예:",
        "  --run cand- --baseline base- --fail-on-regression"
    ]);

    public string Input { get; set; } = Path.Combine("logs", "loadtest");
    public string? RunPrefix { get; set; }
    public string? BaselinePrefix { get; set; }
    public string? ThresholdsPath { get; set; }
    public string Output { get; set; } = Path.Combine("logs", "loadtest", "report.html");
    public bool FailOnRegression { get; set; }
    public bool ListOnly { get; set; }
    public bool PrintThresholds { get; set; }

    public static bool IsHelpRequest(string[] args) => args.Any(a => a is "--help" or "-h" or "/?");

    public static ReportOptions Parse(string[] args)
    {
        var options = new ReportOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");

            switch (arg)
            {
                case "--input":
                    options.Input = Next();
                    break;
                case "--run":
                    options.RunPrefix = Next();
                    break;
                case "--baseline":
                    options.BaselinePrefix = Next();
                    break;
                case "--thresholds":
                    options.ThresholdsPath = Next();
                    break;
                case "--output":
                    options.Output = Next();
                    break;
                case "--fail-on-regression":
                    options.FailOnRegression = true;
                    break;
                case "--list":
                    options.ListOnly = true;
                    break;
                case "--print-thresholds":
                    options.PrintThresholds = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        return options;
    }
}
