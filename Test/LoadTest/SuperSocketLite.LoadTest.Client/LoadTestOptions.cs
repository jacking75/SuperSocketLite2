using System.Globalization;

namespace SuperSocketLite.LoadTest.Client;

public sealed class LoadTestOptions
{
    public static string HelpText { get; } = string.Join(Environment.NewLine, [
        "SuperSocketLite LoadTest Client",
        "",
        "Options:",
        "  --transport <tcp|text|udp>",
        "  --protocol <echo-binary|game-binary|text-line|udp-echo>",
        "  --host <host>",
        "  --port <1-65535>",
        "  --clients <count>",
        "  --ramp-up <hh:mm:ss>",
        "  --duration <hh:mm:ss>",
        "  --send-rate-per-client <rate>",
        "  --scenario <echo|game-like|idle-heartbeat|reconnect-storm|burst>",
        "  --scenario-file <path.json>   (--scenario 대신 요청 조합을 파일로 기술한다)",
        "  --payload <small|medium|large|huge|mixed|mixed-huge>",
        "  --pacing <open|closed>",
        "  --max-in-flight <count>",
        "  --abort-percent <0-100>",
        "  --burst-every <hh:mm:ss>",
        "  --burst-size <count>",
        "  --operation-sampling <0.0-1.0>",
        "  --reconnect-on-drop",
        "  --reconnect-delay-ms <milliseconds>",
        "  --machine-id <id>",
        "  --run-id <id>",
        "  --output <directory>",
        "  --help"
    ]);

    public string Transport { get; set; } = "tcp";
    public string Protocol { get; set; } = "echo-binary";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2012;
    public int Clients { get; set; } = 1;
    public TimeSpan RampUp { get; set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);
    public double SendRatePerClient { get; set; } = 1.0;
    public string Payload { get; set; } = "small";
    public string Output { get; set; } = Path.Combine("logs", "loadtest", "client");
    public string Scenario { get; set; } = "echo";

    /// <summary>
    /// 송신 페이싱 방식입니다.
    /// <c>open</c>은 미리 정한 절대 시각 일정대로 보내므로 응답이 늦어도 부하량이 줄지 않습니다.
    /// <c>closed</c>는 응답을 받은 뒤 다음 지연을 시작하는 예전 방식입니다.
    /// 열린 루프는 TCP 바이너리 프로토콜에만 적용되며, UDP와 text-line은 항상 닫힌 루프로 동작합니다.
    /// </summary>
    public string Pacing { get; set; } = "open";

    /// <summary>
    /// 응답을 기다리는 동안 동시에 떠 있을 수 있는 요청 수입니다.
    /// 0이면 송신 레이트와 수신 타임아웃으로 자동 산정합니다.
    /// </summary>
    public int MaxInFlight { get; set; }

    /// <summary>
    /// 실행이 끝날 때 정상 종료 대신 RST로 끊을 클라이언트 비율입니다.
    /// 서버가 비정상 종료를 어떻게 처리하는지 보기 위한 것입니다.
    /// </summary>
    public int AbortPercent { get; set; }

    /// <summary>순간 폭주 간격입니다. <c>--scenario burst</c>에서만 씁니다.</summary>
    public TimeSpan BurstEvery { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>폭주 한 번에 몰아서 보낼 요청 수입니다.</summary>
    public int BurstSize { get; set; } = 20;
    public int HeartbeatMinSec { get; set; } = 5;
    public int HeartbeatMaxSec { get; set; } = 15;
    public int ChatMinSec { get; set; } = 10;
    public int ChatMaxSec { get; set; } = 45;
    public int ReconnectPercent { get; set; } = 2;
    public TimeSpan StormAt { get; set; } = TimeSpan.Zero;
    public int StormPercent { get; set; } = 0;
    public TimeSpan StormWindow { get; set; } = TimeSpan.FromSeconds(20);
    public int RoomCycleEvery { get; set; } = 120;
    public bool PartialPacket { get; set; }
    public bool CoalescedPacket { get; set; }
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public double UdpLossPercent { get; set; }
    public double OperationSampling { get; set; } = 1.0;
    public TimeSpan SlowReceiverDelay { get; set; }

    /// <summary>
    /// 연결이 예기치 않게 끊겼을 때 실행이 끝날 때까지 다시 붙을지 여부입니다.
    /// 서버 장애 주입에서 씁니다. 이것 없이는 서버가 죽는 순간 클라이언트가 그대로 빠져
    /// 서버가 살아난 뒤의 회복을 볼 수 없습니다.
    /// </summary>
    public bool ReconnectOnDrop { get; set; }

    /// <summary>재접속을 시도하기 전에 기다리는 시간입니다.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>
    /// 선언적 시나리오 파일의 경로입니다.
    /// 지정하면 <see cref="Scenario"/> 대신 이 파일이 요청 조합과 간격을 정합니다.
    /// </summary>
    public string ScenarioFile { get; set; } = string.Empty;

    /// <summary>읽어 들인 선언적 시나리오입니다. 파일을 주지 않았으면 null입니다.</summary>
    public Scenarios.DeclarativeScenario? DeclarativeScenario { get; set; }

    public string RunId { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    public string MachineId { get; set; } = string.IsNullOrWhiteSpace(Environment.MachineName) ? "unknown" : Environment.MachineName;

    public static bool IsHelpRequest(string[] args)
    {
        return args.Any(arg => arg is "--help" or "-h" or "/?");
    }

    /// <summary>
    /// 시나리오 파일을 아직 읽지 않았다면 읽습니다.
    /// 실행이 시작되기 전에 부르므로, 파일이 잘못되었으면 부하를 걸기 전에 알 수 있습니다.
    /// </summary>
    public void EnsureDeclarativeScenarioLoaded()
    {
        if (DeclarativeScenario is not null || string.IsNullOrWhiteSpace(ScenarioFile))
            return;

        DeclarativeScenario = Scenarios.DeclarativeScenario.Load(ScenarioFile);
    }

    public static LoadTestOptions Parse(string[] args)
    {
        var options = new LoadTestOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");

            switch (arg)
            {
                case "--transport":
                    options.Transport = Next();
                    break;
                case "--protocol":
                    options.Protocol = Next();
                    break;
                case "--host":
                    options.Host = Next();
                    break;
                case "--port":
                    options.Port = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--clients":
                    options.Clients = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--ramp-up":
                    options.RampUp = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--duration":
                    options.Duration = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--send-rate-per-client":
                    options.SendRatePerClient = double.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--payload":
                    options.Payload = Next();
                    break;
                case "--output":
                    options.Output = Next();
                    break;
                case "--scenario":
                    options.Scenario = Next();
                    break;
                case "--scenario-file":
                    options.ScenarioFile = Next();
                    break;
                case "--pacing":
                    options.Pacing = Next();
                    break;
                case "--max-in-flight":
                    options.MaxInFlight = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--abort-percent":
                    options.AbortPercent = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--burst-every":
                    options.BurstEvery = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--burst-size":
                    options.BurstSize = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--heartbeat-min-sec":
                    options.HeartbeatMinSec = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--heartbeat-max-sec":
                    options.HeartbeatMaxSec = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--chat-min-sec":
                    options.ChatMinSec = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--chat-max-sec":
                    options.ChatMaxSec = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--reconnect-percent":
                    options.ReconnectPercent = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--reconnect-on-drop":
                    options.ReconnectOnDrop = true;
                    break;
                case "--reconnect-delay-ms":
                    options.ReconnectDelay = TimeSpan.FromMilliseconds(int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                case "--storm-at":
                    options.StormAt = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--storm-percent":
                    options.StormPercent = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--storm-window":
                    options.StormWindow = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--room-cycle-every":
                    options.RoomCycleEvery = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--partial-packet":
                    options.PartialPacket = true;
                    break;
                case "--coalesced-packet":
                    options.CoalescedPacket = true;
                    break;
                case "--receive-timeout":
                    options.ReceiveTimeout = TimeSpan.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--udp-loss-percent":
                    options.UdpLossPercent = double.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--operation-sampling":
                case "--client-operation-sampling":
                    options.OperationSampling = double.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--slow-receiver-delay-ms":
                    options.SlowReceiverDelay = TimeSpan.FromMilliseconds(int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                case "--run-id":
                    options.RunId = Next();
                    break;
                case "--machine-id":
                    options.MachineId = Next();
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
        if (Clients < 0)
            throw new ArgumentOutOfRangeException(nameof(Clients), "Clients must be greater than or equal to zero.");
        if (Duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Duration), "Duration must be greater than zero.");
        if (RampUp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RampUp), "RampUp must be greater than or equal to zero.");
        if (SendRatePerClient < 0)
            throw new ArgumentOutOfRangeException(nameof(SendRatePerClient), "SendRatePerClient must be greater than or equal to zero.");
        if (OperationSampling is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(OperationSampling), "OperationSampling must be between 0.0 and 1.0.");
        if (UdpLossPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(UdpLossPercent), "UdpLossPercent must be between 0 and 100.");
        if (ReceiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReceiveTimeout), "ReceiveTimeout must be greater than zero.");
        if (HeartbeatMinSec < 0 || HeartbeatMaxSec < HeartbeatMinSec)
            throw new ArgumentOutOfRangeException(nameof(HeartbeatMaxSec), "Heartbeat range is invalid.");
        if (ChatMinSec < 0 || ChatMaxSec < ChatMinSec)
            throw new ArgumentOutOfRangeException(nameof(ChatMaxSec), "Chat range is invalid.");
        if (ReconnectPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ReconnectPercent), "ReconnectPercent must be between 0 and 100.");
        if (StormPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(StormPercent), "StormPercent must be between 0 and 100.");
        if (StormWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(StormWindow), "StormWindow must be greater than or equal to zero.");
        if (RoomCycleEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(RoomCycleEvery), "RoomCycleEvery must be greater than zero.");
        if (string.IsNullOrWhiteSpace(MachineId))
            throw new ArgumentException("MachineId must not be empty.", nameof(MachineId));
        if (Pacing is not ("open" or "closed"))
            throw new ArgumentException("Pacing must be 'open' or 'closed'.", nameof(Pacing));
        if (MaxInFlight < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxInFlight), "MaxInFlight must be greater than or equal to zero.");
        if (AbortPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(AbortPercent), "AbortPercent must be between 0 and 100.");
        if (BurstEvery <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(BurstEvery), "BurstEvery must be greater than zero.");
        if (BurstSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BurstSize), "BurstSize must be greater than zero.");
        if (ReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelay), "ReconnectDelay must be greater than zero.");

        EnsureDeclarativeScenarioLoaded();
    }

    /// <summary>이 클라이언트가 실행 종료 시 RST로 끊을 대상인지입니다.</summary>
    public bool ShouldAbort(int clientId)
    {
        return AbortPercent > 0 && Math.Abs(clientId) % 100 < AbortPercent;
    }

    /// <summary>
    /// 실제로 적용할 동시 요청 한도입니다.
    /// 명시하지 않으면 타임아웃 안에 떠 있을 수 있는 최대 요청 수로 잡습니다.
    /// 한도가 너무 낮으면 열린 루프가 다시 응답 대기에 막히므로 최소 8은 확보합니다.
    /// </summary>
    public int ResolveMaxInFlight()
    {
        if (MaxInFlight > 0)
            return MaxInFlight;

        var expected = SendRatePerClient * ReceiveTimeout.TotalSeconds;
        return Math.Max(8, (int)Math.Ceiling(expected));
    }

    /// <summary>열린 루프로 동작하는 조합인지입니다. TCP 바이너리 프로토콜에만 적용합니다.</summary>
    public bool UsesOpenLoop()
    {
        return Pacing == "open" && Transport != "udp" && Protocol != "text-line";
    }
}
