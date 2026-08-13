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
