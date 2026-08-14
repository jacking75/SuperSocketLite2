namespace SuperSocketLite.LoadTest.Server;

/// <summary>
/// 서버가 자신을 얼마나 재는지입니다.
/// 계측이 결과에 주는 영향을 보려면 같은 부하를 세 단계로 돌려 비교합니다.
/// </summary>
public enum ServerMetricsMode
{
    /// <summary>요청 계측 · 주기 표본 · 런타임 게이지를 모두 켭니다.</summary>
    Full,

    /// <summary>런타임 게이지(송신 큐·SAEA 풀)만 끕니다. 게이지가 더한 비용을 가려냅니다.</summary>
    NoGauges,

    /// <summary>서버 계측을 전부 끕니다. 서버 CSV도 남지 않습니다.</summary>
    Off
}

/// <summary>
/// 서버가 패킷당 버퍼를 어떻게 다루는지입니다.
/// <c>Docs/GC_Copy_Minimization.md</c>의 개선 1·3을 켜고 끄는 스위치로,
/// 같은 부하를 두 번 돌려 alloc-rate와 GC 횟수를 비교하는 데 씁니다.
/// 이진 TCP 경로에만 적용됩니다. 부가 리스너(text-line·UDP)는 언제나 풀 경로로 동작합니다.
/// </summary>
public enum AllocationMode
{
    /// <summary>
    /// 수신은 파이프 메모리를 그대로 넘기고(요청 인스턴스도 재사용),
    /// 송신은 스택·풀 버퍼에 직렬화합니다. 패킷당 할당이 없습니다.
    /// </summary>
    Pooled,

    /// <summary>
    /// 개선 전 방식입니다. 패킷마다 본문 배열과 요청 인스턴스를 새로 만들고
    /// 응답도 새 배열에 담습니다. 개선 전 수치를 재는 데만 씁니다.
    /// </summary>
    Legacy
}

public sealed class LoadTestServerOptions
{
    public static string HelpText { get; } = string.Join(Environment.NewLine, [
        "SuperSocketLite LoadTest Server",
        "",
        "Options:",
        "  --port <1-65535>",
        "  --text-port <1-65535>   (0 to disable)",
        "  --udp-port <1-65535>    (0 to disable)",
        "  --max-connections <count>",
        "  --output <directory>",
        "  --sample-interval-ms <milliseconds>",
        "  --server-event-request-sampling <0.0-1.0>",
        "  --metrics <full|no-gauges|off>",
        "  --alloc-mode <pooled|legacy>",
        "  --stop-file <path>      (이 경로에 파일이 생기면 정상 종료한다)",
        "  --duration <hh:mm:ss>",
        "  --run-id <id>",
        "  --help"
    ]);

    public int Port { get; set; } = 2012;

    /// <summary>text-line 프로토콜 리슨 포트입니다. 0이면 리스너를 열지 않습니다.</summary>
    public int TextPort { get; set; }

    /// <summary>UDP 에코 리슨 포트입니다. 0이면 리스너를 열지 않습니다.</summary>
    public int UdpPort { get; set; }

    public int MaxConnections { get; set; } = 1000;
    public string Output { get; set; } = Path.Combine("logs", "loadtest", "local-server");
    public int SampleIntervalMs { get; set; } = 1000;
    public string RunId { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    public TimeSpan? Duration { get; set; }
    public double RequestEventSampling { get; set; }

    /// <summary>서버 계측 수준입니다. 기본은 전부 켬입니다.</summary>
    public ServerMetricsMode Metrics { get; set; } = ServerMetricsMode.Full;

    /// <summary>패킷당 버퍼 처리 방식입니다. 기본은 할당이 없는 풀 경로입니다.</summary>
    public AllocationMode Allocation { get; set; } = AllocationMode.Pooled;

    /// <summary>
    /// 이 경로에 파일이 생기면 정상 종료합니다. null이면 감시하지 않습니다.
    /// </summary>
    /// <remarks>
    /// 부하 스크립트가 서버를 강제로 죽이면 세션 정리와 마지막 표본 기록을 건너뛰어,
    /// 멀쩡한 실행이 세션 누수로 기록됩니다. 스크립트는 이 파일로 종료를 요청합니다.
    /// 자세한 내용은 <see cref="StopFileSignal"/>.
    /// </remarks>
    public string? StopFile { get; set; }

    public static bool IsHelpRequest(string[] args)
    {
        return args.Any(arg => arg is "--help" or "-h" or "/?");
    }

    public static LoadTestServerOptions Parse(string[] args)
    {
        var options = new LoadTestServerOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");

            switch (arg)
            {
                case "--port":
                    options.Port = int.Parse(value);
                    break;
                case "--text-port":
                    options.TextPort = int.Parse(value);
                    break;
                case "--udp-port":
                    options.UdpPort = int.Parse(value);
                    break;
                case "--max-connections":
                    options.MaxConnections = int.Parse(value);
                    break;
                case "--output":
                    options.Output = value;
                    break;
                case "--sample-interval-ms":
                case "--server-metrics-interval-ms":
                    options.SampleIntervalMs = int.Parse(value);
                    break;
                case "--run-id":
                    options.RunId = value;
                    break;
                case "--duration":
                    options.Duration = TimeSpan.Parse(value);
                    break;
                case "--server-event-request-sampling":
                    options.RequestEventSampling = double.Parse(value);
                    break;
                case "--metrics":
                    options.Metrics = ParseMetricsMode(value);
                    break;
                case "--alloc-mode":
                    options.Allocation = ParseAllocationMode(value);
                    break;
                case "--stop-file":
                    options.StopFile = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        options.Validate();
        return options;
    }

    private static ServerMetricsMode ParseMetricsMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "full" or "on" => ServerMetricsMode.Full,
            "no-gauges" or "nogauges" => ServerMetricsMode.NoGauges,
            "off" or "none" => ServerMetricsMode.Off,
            _ => throw new ArgumentException($"Unknown metrics mode '{value}'. Use full, no-gauges, or off.")
        };
    }

    private static AllocationMode ParseAllocationMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "pooled" or "zero" => AllocationMode.Pooled,
            "legacy" or "copy" => AllocationMode.Legacy,
            _ => throw new ArgumentException($"Unknown allocation mode '{value}'. Use pooled or legacy.")
        };
    }

    public void Validate()
    {
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
        if (TextPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(TextPort), "TextPort must be between 0 and 65535.");
        if (UdpPort is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(UdpPort), "UdpPort must be between 0 and 65535.");
        if (TextPort != 0 && TextPort == Port)
            throw new ArgumentException("TextPort must differ from Port.", nameof(TextPort));
        if (MaxConnections < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConnections), "MaxConnections must be greater than or equal to zero.");
        if (SampleIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(SampleIntervalMs), "SampleIntervalMs must be greater than zero.");
        if (Duration is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Duration), "Duration must be greater than zero.");
        if (RequestEventSampling is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(RequestEventSampling), "RequestEventSampling must be between 0.0 and 1.0.");
    }
}
