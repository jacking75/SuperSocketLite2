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
        yield return new TestCase(nameof(DeclarativeScenarioParsesPrologueAndWeights), DeclarativeScenarioParsesPrologueAndWeights);
        yield return new TestCase(nameof(DeclarativeScenarioRejectsBrokenDefinitions), DeclarativeScenarioRejectsBrokenDefinitions);
        yield return new TestCase(nameof(DeclarativeScenarioPicksByWeight), DeclarativeScenarioPicksByWeight);
        yield return new TestCase(nameof(DeclarativeScenarioRunnerRepeatsPrologueAfterReset), DeclarativeScenarioRunnerRepeatsPrologueAfterReset);
        yield return new TestCase(nameof(DeclarativeScenarioThinkTimeOverridesSendRate), DeclarativeScenarioThinkTimeOverridesSendRate);
        yield return new TestCase(nameof(ShippedScenarioFileLoads), ShippedScenarioFileLoads);
    }

    private const string SampleScenarioJson = """
        {
          "name": "mix",
          "prologue": [ { "type": "login", "packetId": 201 } ],
          "operations": [
            { "type": "heartbeat", "packetId": 203, "weight": 3, "payloadBytes": 0 },
            { "type": "chat", "packetId": 205, "weight": 1, "payload": "medium" }
          ],
          "thinkTime": { "minMs": 100, "maxMs": 200 }
        }
        """;

    private static void DeclarativeScenarioParsesPrologueAndWeights()
    {
        var scenario = DeclarativeScenario.Parse(SampleScenarioJson);

        AssertEx.Equal("mix", scenario.Name);
        AssertEx.Equal(1, scenario.Prologue.Count);
        AssertEx.Equal((short)201, scenario.Prologue[0].PacketId);
        AssertEx.Equal(2, scenario.Operations.Count);
        AssertEx.Equal(TimeSpan.FromMilliseconds(100), scenario.ThinkTimeMin!.Value);
        AssertEx.Equal(TimeSpan.FromMilliseconds(200), scenario.ThinkTimeMax!.Value);

        // payloadBytes 0은 본문 없는 요청이다. 프로필 이름보다 우선한다.
        var heartbeat = scenario.Operations.Single(o => o.Type == "heartbeat");
        AssertEx.Equal(0, heartbeat.CreatePacket(1, 1).Body.Length);
    }

    /// <summary>
    /// 잘못된 정의는 부하를 걸기 전에 걸러야 한다.
    /// 실행 중에 드러나면 이미 결과가 망가진 뒤다.
    /// </summary>
    private static void DeclarativeScenarioRejectsBrokenDefinitions()
    {
        AssertThrows("""{ "operations": [ { "type": "a", "packetId": 1, "weight": 1 } ] }""", "이름이 없으면 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [] }""", "요청이 하나도 없으면 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [ { "type": "a", "packetId": 1, "weight": 0 } ] }""", "weight 합이 0이면 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [ { "type": "a", "packetId": 999999, "weight": 1 } ] }""", "Int16을 넘는 packetId는 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [ { "type": "a", "packetId": 1, "weight": 1, "payloadBytes": 999999 } ] }""", "프로토콜 상한을 넘는 본문은 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [ { "type": "a", "packetId": 1, "weight": 1 } ], "thinkTime": { "minMs": 500, "maxMs": 100 } }""", "min이 max보다 크면 거부해야 한다.");
        AssertThrows("""{ "name": "x", "operations": [ { "type": "a", "packetId": 1, "weight": 1 } ], "thinkTime": { "minMs": 100 } }""", "thinkTime은 두 값을 함께 적어야 한다.");

        static void AssertThrows(string json, string because)
        {
            try
            {
                DeclarativeScenario.Parse(json);
            }
            catch (ArgumentException)
            {
                return;
            }

            throw new Exception(because);
        }
    }

    private static void DeclarativeScenarioPicksByWeight()
    {
        var scenario = DeclarativeScenario.Parse(SampleScenarioJson);
        var random = new Random(1234);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < 4000; i++)
        {
            var picked = scenario.PickWeighted(random);
            counts[picked.Type] = counts.GetValueOrDefault(picked.Type) + 1;
        }

        // 3:1 비율이므로 heartbeat가 대략 75%다. 난수이므로 폭을 넉넉히 둔다.
        var heartbeatShare = counts["heartbeat"] / 4000.0;
        AssertEx.True(heartbeatShare is > 0.70 and < 0.80, $"weight 3:1이면 heartbeat가 75% 근처여야 한다. 실제 {heartbeatShare:P1}");
    }

    /// <summary>재접속하면 로그인부터 다시 해야 하므로 도입 단계가 되풀이되어야 한다.</summary>
    private static void DeclarativeScenarioRunnerRepeatsPrologueAfterReset()
    {
        var runner = new DeclarativeScenarioRunner(DeclarativeScenario.Parse(SampleScenarioJson));
        var random = new Random(7);

        AssertEx.Equal("login", runner.Next(random).Type);
        AssertEx.True(runner.Next(random).Type != "login", "도입 단계는 접속당 한 번이어야 한다.");

        runner.Reset();
        AssertEx.Equal("login", runner.Next(random).Type, "재접속하면 도입 단계를 다시 밟아야 한다.");
    }

    private static void DeclarativeScenarioThinkTimeOverridesSendRate()
    {
        var options = new LoadTestOptions
        {
            SendRatePerClient = 100,
            DeclarativeScenario = DeclarativeScenario.Parse(SampleScenarioJson)
        };

        var random = new Random(11);
        for (var i = 0; i < 50; i++)
        {
            var thinkTime = LoadScenario.NextThinkTime(options, random);
            AssertEx.True(
                thinkTime >= TimeSpan.FromMilliseconds(100) && thinkTime <= TimeSpan.FromMilliseconds(200),
                $"시나리오가 정한 간격 안이어야 한다. 실제 {thinkTime.TotalMilliseconds}ms");
        }
    }

    /// <summary>저장소에 함께 넣은 예제 파일이 실제로 읽히는지 본다.</summary>
    private static void ShippedScenarioFileLoads()
    {
        var scenario = DeclarativeScenario.Load(RepoPaths.Combine("Test", "LoadTest", "scenarios", "game-mix.json"));

        AssertEx.Equal("game-mix", scenario.Name);
        AssertEx.True(scenario.Prologue.Count > 0, "예제는 도입 단계를 보여 주어야 한다.");
        AssertEx.True(scenario.Operations.Count > 1, "예제는 요청 조합을 보여 주어야 한다.");
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
