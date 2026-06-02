using SuperSocketLite.LoadTest.Client;
using SuperSocketLite.LoadTest.Client.Scenarios;

namespace SuperSocketLite.LoadTest.Tests;

internal static class ScenarioScheduleTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(ParsesClientOptions), ParsesClientOptions);
        yield return new TestCase(nameof(ParsesClientMachineIdOption), ParsesClientMachineIdOption);
        yield return new TestCase(nameof(ParsesClientOperationSamplingOption), ParsesClientOperationSamplingOption);
        yield return new TestCase(nameof(RejectsInvalidClientSamplingOption), RejectsInvalidClientSamplingOption);
        yield return new TestCase(nameof(ClientHelpTextListsDistributedOptions), ClientHelpTextListsDistributedOptions);
        yield return new TestCase(nameof(ParsesSlowReceiverDelayOption), ParsesSlowReceiverDelayOption);
        yield return new TestCase(nameof(CreatesRampUpScheduleAcrossDuration), CreatesRampUpScheduleAcrossDuration);
        yield return new TestCase(nameof(CreatesOutputCsvFiles), CreatesOutputCsvFiles);
        yield return new TestCase(nameof(EncodesSimpleUdpPacketFormat), EncodesSimpleUdpPacketFormat);
        yield return new TestCase(nameof(GameLikeScenarioCreatesExpectedPacketIds), GameLikeScenarioCreatesExpectedPacketIds);
        yield return new TestCase(nameof(GameLikeScenarioAdvancesThroughRoomStates), GameLikeScenarioAdvancesThroughRoomStates);
        yield return new TestCase(nameof(PayloadProfilesProduceExpectedSizes), PayloadProfilesProduceExpectedSizes);
        yield return new TestCase(nameof(HeartbeatJitterStaysWithinConfiguredRange), HeartbeatJitterStaysWithinConfiguredRange);
        yield return new TestCase(nameof(ReconnectStormSelectsConfiguredPercentage), ReconnectStormSelectsConfiguredPercentage);
        yield return new TestCase(nameof(ParsesServerDurationOption), ParsesServerDurationOption);
        yield return new TestCase(nameof(ParsesServerRequestSamplingOption), ParsesServerRequestSamplingOption);
        yield return new TestCase(nameof(RejectsInvalidServerSamplingOption), RejectsInvalidServerSamplingOption);
        yield return new TestCase(nameof(ServerHelpTextListsSamplingOption), ServerHelpTextListsSamplingOption);
    }

    private static void ParsesClientOptions()
    {
        var options = LoadTestOptions.Parse([
            "--transport", "tcp",
            "--protocol", "echo-binary",
            "--host", "127.0.0.1",
            "--port", "2012",
            "--clients", "100",
            "--ramp-up", "00:00:10",
            "--duration", "00:05:00",
            "--send-rate-per-client", "2.5",
            "--payload", "mixed",
            "--output", "logs/loadtest/test"
        ]);

        AssertEx.Equal("tcp", options.Transport);
        AssertEx.Equal("echo-binary", options.Protocol);
        AssertEx.Equal(100, options.Clients);
        AssertEx.Equal(TimeSpan.FromSeconds(10), options.RampUp);
        AssertEx.Equal(TimeSpan.FromMinutes(5), options.Duration);
        AssertEx.Equal(2.5, options.SendRatePerClient);
        AssertEx.Equal("mixed", options.Payload);
    }

    private static void ParsesClientOperationSamplingOption()
    {
        var options = LoadTestOptions.Parse(["--operation-sampling", "0.25"]);

        AssertEx.Equal(0.25, options.OperationSampling);
    }

    private static void RejectsInvalidClientSamplingOption()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => LoadTestOptions.Parse(["--operation-sampling", "1.5"]));
    }

    private static void ClientHelpTextListsDistributedOptions()
    {
        AssertEx.True(LoadTestOptions.IsHelpRequest(["--help"]), "--help should be recognized.");
        AssertEx.True(LoadTestOptions.HelpText.Contains("--machine-id"), "Client help should include --machine-id.");
        AssertEx.True(LoadTestOptions.HelpText.Contains("--operation-sampling"), "Client help should include --operation-sampling.");
    }

    private static void ParsesClientMachineIdOption()
    {
        var options = LoadTestOptions.Parse(["--machine-id", "client-a"]);

        AssertEx.Equal("client-a", options.MachineId);
    }

    private static void ParsesSlowReceiverDelayOption()
    {
        var options = LoadTestOptions.Parse(["--slow-receiver-delay-ms", "25"]);

        AssertEx.Equal(TimeSpan.FromMilliseconds(25), options.SlowReceiverDelay);
    }

    private static void CreatesRampUpScheduleAcrossDuration()
    {
        var options = new LoadTestOptions
        {
            Clients = 5,
            RampUp = TimeSpan.FromSeconds(10)
        };

        var schedule = LoadScenario.CreateConnectSchedule(options).ToArray();

        AssertEx.Equal(5, schedule.Length);
        AssertEx.Equal(TimeSpan.Zero, schedule[0]);
        AssertEx.Equal(TimeSpan.FromSeconds(2), schedule[1]);
        AssertEx.Equal(TimeSpan.FromSeconds(8), schedule[4]);
    }

    private static void CreatesOutputCsvFiles()
    {
        using var temp = TempDirectory.Create();
        var options = new LoadTestOptions
        {
            Output = temp.Path,
            Clients = 0,
            Duration = TimeSpan.FromMilliseconds(1)
        };

        ClientRuntime.EnsureOutputFiles(options);

        AssertEx.True(File.Exists(Path.Combine(temp.Path, "client_samples.csv")), "client_samples.csv should exist.");
        AssertEx.True(File.Exists(Path.Combine(temp.Path, "client_operations.csv")), "client_operations.csv should exist.");
        AssertEx.True(File.Exists(Path.Combine(temp.Path, "client_summary.csv")), "client_summary.csv should exist.");
    }

    private static void EncodesSimpleUdpPacketFormat()
    {
        var sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var encoded = UdpEchoScenario.Encode("ECHO", sessionId, "hello");

        AssertEx.Equal(45, encoded.Length);
        AssertEx.Equal("ECHO", System.Text.Encoding.ASCII.GetString(encoded.AsSpan(0, 4)));
        AssertEx.Equal(sessionId.ToString("D"), System.Text.Encoding.ASCII.GetString(encoded.AsSpan(4, 36)));
        AssertEx.Equal("hello", System.Text.Encoding.UTF8.GetString(encoded.AsSpan(40)));
    }

    private static void GameLikeScenarioCreatesExpectedPacketIds()
    {
        var scenario = new GameLikeScenario();

        AssertEx.Equal((short)201, scenario.CreateLogin(7).PacketId);
        AssertEx.Equal((short)207, scenario.CreateRoomEnter(7).PacketId);
        AssertEx.Equal((short)203, scenario.CreateHeartbeat().PacketId);
        AssertEx.Equal((short)205, scenario.CreateChat(7, 1, new LoadTestOptions()).PacketId);
        AssertEx.Equal((short)209, scenario.CreateRoomLeave(7).PacketId);
    }

    private static void GameLikeScenarioAdvancesThroughRoomStates()
    {
        var scenario = new GameLikeScenario();
        var options = new LoadTestOptions { RoomCycleEvery = 4 };

        var first = scenario.NextOperation(7, 1, options);
        var second = scenario.NextOperation(7, 2, options);
        var third = scenario.NextOperation(7, 3, options);
        var fourth = scenario.NextOperation(7, 4, options);
        var fifth = scenario.NextOperation(7, 5, options);

        AssertEx.Equal("login", first.OperationType);
        AssertEx.Equal("room-enter", second.OperationType);
        AssertEx.Equal("heartbeat", third.OperationType);
        AssertEx.Equal("room-leave", fourth.OperationType);
        AssertEx.Equal("room-enter", fifth.OperationType);
    }

    private static void PayloadProfilesProduceExpectedSizes()
    {
        AssertEx.Equal(32, PayloadFactory.Create(1, 1, "small").Length);
        AssertEx.Equal(256, PayloadFactory.Create(1, 1, "medium").Length);
        AssertEx.Equal(4096, PayloadFactory.Create(1, 1, "large").Length);
        AssertEx.Equal(32, PayloadFactory.Create(1, 1, "mixed").Length);
        AssertEx.Equal(256, PayloadFactory.Create(1, 5, "mixed").Length);
        AssertEx.Equal(4096, PayloadFactory.Create(1, 20, "mixed").Length);
    }

    private static void HeartbeatJitterStaysWithinConfiguredRange()
    {
        var options = new LoadTestOptions { HeartbeatMinSec = 5, HeartbeatMaxSec = 15 };
        var random = new Random(1234);

        for (var i = 0; i < 100; i++)
        {
            var delay = IdleHeartbeatScenario.NextHeartbeatDelay(options, random);
            AssertEx.True(delay >= TimeSpan.FromSeconds(5), "Heartbeat delay should be >= min.");
            AssertEx.True(delay <= TimeSpan.FromSeconds(15), "Heartbeat delay should be <= max.");
        }
    }

    private static void ReconnectStormSelectsConfiguredPercentage()
    {
        var options = new LoadTestOptions
        {
            StormPercent = 40,
            StormAt = TimeSpan.FromMinutes(10),
            StormWindow = TimeSpan.FromSeconds(20)
        };

        var selected = Enumerable.Range(0, 100).Count(id => ReconnectStormScenario.IsStormClient(id, options));
        AssertEx.Equal(40, selected);
        AssertEx.True(ReconnectStormScenario.DisconnectAt(39, options) < options.StormAt + options.StormWindow, "Storm disconnect should fall inside window.");
        AssertEx.Equal(TimeSpan.MaxValue, ReconnectStormScenario.DisconnectAt(40, options));
    }

    private static void ParsesServerDurationOption()
    {
        var options = Server.LoadTestServerOptions.Parse(["--port", "2012", "--duration", "00:00:05"]);

        AssertEx.Equal(2012, options.Port);
        AssertEx.Equal(TimeSpan.FromSeconds(5), options.Duration);
    }

    private static void ParsesServerRequestSamplingOption()
    {
        var options = Server.LoadTestServerOptions.Parse(["--server-event-request-sampling", "1.0"]);

        AssertEx.Equal(1.0, options.RequestEventSampling);
    }

    private static void RejectsInvalidServerSamplingOption()
    {
        AssertEx.Throws<ArgumentOutOfRangeException>(() => Server.LoadTestServerOptions.Parse(["--server-event-request-sampling", "2"]));
    }

    private static void ServerHelpTextListsSamplingOption()
    {
        AssertEx.True(Server.LoadTestServerOptions.IsHelpRequest(["/?"]), "/? should be recognized.");
        AssertEx.True(Server.LoadTestServerOptions.HelpText.Contains("--server-event-request-sampling"), "Server help should include request sampling.");
    }
}
