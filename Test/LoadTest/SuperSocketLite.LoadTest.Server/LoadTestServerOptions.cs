namespace SuperSocketLite.LoadTest.Server;

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
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        options.Validate();
        return options;
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
