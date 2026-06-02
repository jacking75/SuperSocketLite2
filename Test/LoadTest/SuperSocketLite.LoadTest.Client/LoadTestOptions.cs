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
        "  --payload <small|medium|large|mixed>",
        "  --scenario <echo|game-like|idle-heartbeat|reconnect-storm>",
        "  --operation-sampling <0.0-1.0>",
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
    public string RunId { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    public string MachineId { get; set; } = string.IsNullOrWhiteSpace(Environment.MachineName) ? "unknown" : Environment.MachineName;

    public static bool IsHelpRequest(string[] args)
    {
        return args.Any(arg => arg is "--help" or "-h" or "/?");
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
    }
}
