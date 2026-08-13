using System.Net;
using System.Net.Sockets;
using SuperSocketLite.LoadTest.Client;
using SuperSocketLite.LoadTest.Server;
using SuperSocketLite.LoadTest.ServerProbe;

namespace SuperSocketLite.LoadTest.Tests;

internal static class LoadTestIntegrationTests
{
    public static IEnumerable<TestCase> Cases()
    {
        yield return new TestCase(nameof(OneTcpClientRecordsEchoOperation), OneTcpClientRecordsEchoOperation);
        yield return new TestCase(nameof(PartialTcpPacketSendRecordsSuccessfulOperation), PartialTcpPacketSendRecordsSuccessfulOperation);
        yield return new TestCase(nameof(ClientOperationSamplingCanSuppressOperationRows), ClientOperationSamplingCanSuppressOperationRows);
        yield return new TestCase(nameof(ClientRuntimeCancellationStopsActors), ClientRuntimeCancellationStopsActors);
        yield return new TestCase(nameof(ConnectFailuresDoNotCountAsDisconnects), ConnectFailuresDoNotCountAsDisconnects);
        yield return new TestCase(nameof(SlowReceiverDelayStillRecordsSuccessfulTcpOperation), SlowReceiverDelayStillRecordsSuccessfulTcpOperation);
        yield return new TestCase(nameof(ServerRequestSamplingWritesRequestEvents), ServerRequestSamplingWritesRequestEvents);
        yield return new TestCase(nameof(CoalescedTcpClientSendsTwoRequestsInOneLoop), CoalescedTcpClientSendsTwoRequestsInOneLoop);
        yield return new TestCase(nameof(GameLikeRuntimeRecordsRoomLifecycleOperations), GameLikeRuntimeRecordsRoomLifecycleOperations);
        yield return new TestCase(nameof(TextLineClientRecordsPingOperations), TextLineClientRecordsPingOperations);
        yield return new TestCase(nameof(UdpClientRecordsEchoOperations), UdpClientRecordsEchoOperations);
        yield return new TestCase(nameof(UdpClientRecordsReceiveTimeouts), UdpClientRecordsReceiveTimeouts);
        yield return new TestCase(nameof(UdpLossSimulationRecordsSendFailures), UdpLossSimulationRecordsSendFailures);
        yield return new TestCase(nameof(ReconnectStormDoesNotCrashAndDrainsSessions), ReconnectStormDoesNotCrashAndDrainsSessions);
        yield return new TestCase(nameof(OpenLoopKeepsSendingWhenResponsesAreSlow), OpenLoopKeepsSendingWhenResponsesAreSlow);
        yield return new TestCase(nameof(OpenLoopRespectsInFlightLimit), OpenLoopRespectsInFlightLimit);
        yield return new TestCase(nameof(TextLineServerEchoesClientLines), TextLineServerEchoesClientLines);
        yield return new TestCase(nameof(UdpEchoServerEchoesClientDatagrams), UdpEchoServerEchoesClientDatagrams);
        yield return new TestCase(nameof(BurstScenarioSendsMoreThanSteadyRate), BurstScenarioSendsMoreThanSteadyRate);
        yield return new TestCase(nameof(AbortedConnectionsDoNotBreakServer), AbortedConnectionsDoNotBreakServer);
        yield return new TestCase(nameof(HugePayloadRoundTripsThroughServer), HugePayloadRoundTripsThroughServer);
        yield return new TestCase(nameof(ServerSamplesReportSendQueueAndPoolGauges), ServerSamplesReportSendQueueAndPoolGauges);
        yield return new TestCase(nameof(MetricsOffKeepsEchoingWithoutServerCsv), MetricsOffKeepsEchoingWithoutServerCsv);
        yield return new TestCase(nameof(MetricsNoGaugesLeavesRuntimeColumnsUnavailable), MetricsNoGaugesLeavesRuntimeColumnsUnavailable);
        yield return new TestCase(nameof(ClientReconnectsAfterServerRestart), ClientReconnectsAfterServerRestart);
        yield return new TestCase(nameof(ClientWithoutReconnectOnDropStopsAtServerLoss), ClientWithoutReconnectOnDropStopsAtServerLoss);
        yield return new TestCase(nameof(DeclarativeScenarioFileDrivesTheRun), DeclarativeScenarioFileDrivesTheRun);
    }

    /// <summary>
    /// 시나리오 파일로 돌리면 파일이 정한 요청들이 실제로 나가고, 요약의 시나리오 이름도 그것이어야 한다.
    /// 코드를 고치지 않고 부하 조합을 바꿀 수 있다는 것이 이 기능의 목적이다.
    /// </summary>
    private static void DeclarativeScenarioFileDrivesTheRun()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "declarative-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");
        var scenarioPath = Path.Combine(temp.Path, "scenario.json");

        File.WriteAllText(scenarioPath, """
            {
              "name": "file-driven",
              "prologue": [ { "type": "login", "packetId": 201 } ],
              "operations": [ { "type": "heartbeat", "packetId": 203, "weight": 1, "payloadBytes": 0 } ]
            }
            """);

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                SampleIntervalMs = 200,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 10,
                Output = clientOutput,
                RunId = runId,
                ScenarioFile = scenarioPath
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",login,")), "The prologue request should be sent.");
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",heartbeat,")), "The weighted request should be sent.");
        AssertEx.False(operations.Skip(1).Any(line => line.Contains(",echo,")), "The built-in echo scenario should not run when a file is given.");

        var summary = ReadSummary(Path.Combine(clientOutput, "client_summary.csv"));
        AssertEx.Equal("file-driven", summary["scenario"], "The summary should name the scenario from the file.");
    }

    /// <summary>
    /// 부하 중 서버가 죽었다 살아나면 클라이언트가 다시 붙어 응답을 받아야 한다.
    /// 회복 시간은 끊긴 시각부터 응답을 다시 받기까지로 재며 요약에 남는다.
    /// </summary>
    private static void ClientReconnectsAfterServerRestart()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "fault-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        var options = new LoadTestOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Clients = 2,
            Duration = TimeSpan.FromSeconds(6),
            SendRatePerClient = 10,
            Output = clientOutput,
            RunId = runId,
            ReconnectOnDrop = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(100),
            ReceiveTimeout = TimeSpan.FromMilliseconds(500)
        };

        var first = StartLoadTestServer(port, Path.Combine(temp.Path, "server-1"), runId);
        var runtime = new ClientRuntime(options);
        var clientTask = Task.Run(() => runtime.RunAsync());

        // 부하가 자리를 잡은 뒤 서버를 내리고, 잠시 두었다가 같은 포트로 다시 띄운다.
        Thread.Sleep(1500);
        first.Stop();
        first.Dispose();

        Thread.Sleep(1000);
        using var second = StartLoadTestServer(port, Path.Combine(temp.Path, "server-2"), runId);

        AssertEx.Equal(0, clientTask.GetAwaiter().GetResult());
        second.Stop();

        var summary = ReadSummary(Path.Combine(clientOutput, "client_summary.csv"));
        AssertEx.True(long.Parse(summary["outage_total"]) > 0, "Killing the server should register an outage.");
        AssertEx.True(long.Parse(summary["reconnect_total"]) > 0, "Clients should reconnect after the server comes back.");
        AssertEx.True(long.Parse(summary["max_outage_ms"]) > 0, "Recovery time should be measured once responses resume.");

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        var successAfterRestart = operations
            .Skip(1)
            .Where(line => line.Contains(",True,"))
            .Select(line => long.Parse(line.Split(',')[1]))
            .Any(elapsedMs => elapsedMs > 2500);

        AssertEx.True(successAfterRestart, "Clients should complete operations again after the restart.");
    }

    /// <summary>
    /// 재접속 옵션이 없으면 서버가 사라진 시점에 클라이언트가 그대로 빠져야 한다.
    /// 기본 동작이 바뀌지 않았음을 고정한다.
    /// </summary>
    private static void ClientWithoutReconnectOnDropStopsAtServerLoss()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "no-reconnect-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        var options = new LoadTestOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Clients = 2,
            Duration = TimeSpan.FromSeconds(4),
            SendRatePerClient = 10,
            Output = clientOutput,
            RunId = runId,
            ReconnectPercent = 0,
            ReceiveTimeout = TimeSpan.FromMilliseconds(500)
        };

        var server = StartLoadTestServer(port, Path.Combine(temp.Path, "server"), runId);
        var runtime = new ClientRuntime(options);
        var clientTask = Task.Run(() => runtime.RunAsync());

        Thread.Sleep(1000);
        server.Stop();
        server.Dispose();

        AssertEx.Equal(0, clientTask.GetAwaiter().GetResult());

        var summary = ReadSummary(Path.Combine(clientOutput, "client_summary.csv"));
        AssertEx.Equal(0L, long.Parse(summary["reconnect_total"]), "Without --reconnect-on-drop the clients should not come back.");
    }

    private static LoadTestServer StartLoadTestServer(int port, string output, string runId)
    {
        var server = new LoadTestServer();
        AssertEx.True(server.Configure(new LoadTestServerOptions
        {
            Port = port,
            MaxConnections = 10,
            Output = output,
            SampleIntervalMs = 200,
            RunId = runId
        }), "Server should configure.");
        AssertEx.True(server.StartWithMetrics(), "Server should start.");
        return server;
    }

    private static Dictionary<string, string> ReadSummary(string path)
    {
        var lines = File.ReadAllLines(path);
        var headers = lines[0].Split(',');
        var keyIndex = Array.IndexOf(headers, "key");
        var valueIndex = Array.IndexOf(headers, "value");
        AssertEx.True(keyIndex >= 0 && valueIndex >= 0, "client_summary.csv should have key/value columns.");

        var summary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length > Math.Max(keyIndex, valueIndex))
                summary[fields[keyIndex]] = fields[valueIndex];
        }

        return summary;
    }

    /// <summary>
    /// 서버 표본이 라이브러리 계기에서 읽은 송신 큐 깊이와 SAEA 풀 잔량을 담아야 한다.
    /// 세션이 붙어 있는 동안에는 풀에서 꺼내 간 만큼 잔량이 총수보다 적어야 한다.
    /// </summary>
    private static void ServerSamplesReportSendQueueAndPoolGauges()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "gauge-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 2,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 5,
                Output = Path.Combine(temp.Path, "client"),
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        AssertEx.True(sampleLines.Length >= 2, "Server samples should be written.");

        // 마지막 표본은 서버가 멈춘 뒤에 찍힌다. 그때는 관측할 대상이 없으므로 -1이 맞다.
        var rows = sampleLines
            .Skip(1)
            .Where(row => ParseColumnAsLong(sampleLines, "active_sessions", row) > 0)
            .ToArray();

        AssertEx.True(rows.Length > 0, "The run should produce samples taken while sessions were connected.");
        AssertEx.True(
            rows.All(row => ParseColumnAsLong(sampleLines, "send_queue_depth_total", row) >= 0),
            "Runtime gauges should be observed while the server runs, not reported as unavailable.");

        AssertEx.True(
            rows.Any(row => ParseColumnAsLong(sampleLines, "receive_saea_pool_total", row) > 0),
            "Receive SAEA pool should report the items it created.");

        AssertEx.True(
            rows.Any(row =>
                ParseColumnAsLong(sampleLines, "receive_saea_pool_available", row)
                < ParseColumnAsLong(sampleLines, "receive_saea_pool_total", row)),
            "While sessions are connected the pool should hold fewer items than it created.");
    }

    /// <summary>
    /// <c>--metrics off</c>은 서버 계측을 통째로 끈다.
    /// 서버 CSV는 남지 않지만 에코는 그대로 돌아야 한다. 클라이언트 쪽 수치를 두 실행에서 비교하기 때문이다.
    /// </summary>
    private static void MetricsOffKeepsEchoingWithoutServerCsv()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "metrics-off-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId,
                Metrics = ServerMetricsMode.Off
            }), "Server should configure with metrics off.");
            AssertEx.True(server.StartWithMetrics(), "Server should start with metrics off.");
            AssertEx.True(server.Metrics is null, "Metrics collector should not exist when metrics are off.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 5,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",True,")), "Echo should still work with metrics off.");
        AssertEx.False(
            File.Exists(Path.Combine(serverOutput, "server_samples.csv")),
            "Metrics off should not write server samples.");
    }

    /// <summary>
    /// <c>--metrics no-gauges</c>는 주기 표본은 남기되 런타임 게이지만 끈다.
    /// 그 실행의 게이지 컬럼은 0이 아니라 -1이어야 한다. 0이면 "큐가 비었다"로 오독된다.
    /// </summary>
    private static void MetricsNoGaugesLeavesRuntimeColumnsUnavailable()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "no-gauges-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId,
                Metrics = ServerMetricsMode.NoGauges
            }), "Server should configure without gauges.");
            AssertEx.True(server.StartWithMetrics(), "Server should start without gauges.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 5,
                Output = Path.Combine(temp.Path, "client"),
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        AssertEx.True(sampleLines.Length >= 2, "Server samples should still be written without gauges.");
        AssertEx.True(
            sampleLines.Skip(1).All(row => ParseColumnAsLong(sampleLines, "send_queue_depth_total", row) == -1),
            "Runtime gauge columns should read as unavailable when gauges are off.");
    }

    /// <summary>
    /// 폭주 시나리오는 기본 레이트 위에 주기적으로 한 뭉치를 얹는다.
    /// 열린 루프이므로 그 뭉치가 응답 대기에 막히지 않고 실제로 몰려 나가야 한다.
    /// </summary>
    private static void BurstScenarioSendsMoreThanSteadyRate()
    {
        var steady = RunScenarioAndCountSends("echo");
        var burst = RunScenarioAndCountSends("burst");

        AssertEx.True(
            burst > steady * 2,
            $"폭주 시나리오는 기본 레이트보다 훨씬 많이 보내야 한다. burst={burst}, steady={steady}");
    }

    private static long RunScenarioAndCountSends(string scenario)
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = scenario + "-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 2,
                Scenario = scenario,
                BurstEvery = TimeSpan.FromMilliseconds(400),
                BurstSize = 20,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        return ParseColumnAsLong(samples, "total_send_success", samples[^1]);
    }

    /// <summary>
    /// 클라이언트가 FIN 대신 RST로 끊어도 서버는 예외 없이 세션을 정리해야 한다.
    /// 모바일 환경에서 흔한 끊김이라 서버가 이 경로에서 무너지면 안 된다.
    /// </summary>
    private static void AbortedConnectionsDoNotBreakServer()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "abort-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 20,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 8,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 5,
                AbortPercent = 100,
                Output = Path.Combine(temp.Path, "client"),
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        var exceptions = ParseColumnAsLong(sampleLines, "exception_total", sampleLines[^1]);
        var activeSessions = ParseColumnAsLong(sampleLines, "active_sessions", sampleLines[^1]);
        var connected = ParseColumnAsLong(sampleLines, "total_connected", sampleLines[^1]);

        AssertEx.True(connected > 0, "서버는 클라이언트 접속을 받아야 한다.");
        AssertEx.Equal(0L, exceptions, "RST로 끊겨도 서버에 예외가 발생하면 안 된다.");
        AssertEx.Equal(0L, activeSessions, "RST로 끊긴 세션도 남김없이 정리되어야 한다.");
    }

    /// <summary>60KB 페이로드가 서버의 조립 경로를 온전히 통과하는지 확인한다.</summary>
    private static void HugePayloadRoundTripsThroughServer()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "huge-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 2,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 5,
                Payload = "huge",
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        var received = ParseColumnAsLong(samples, "total_receive", samples[^1]);
        var timeouts = ParseColumnAsLong(samples, "total_timeout", samples[^1]);

        AssertEx.True(received > 0, "60KB 요청도 응답을 받아야 한다.");
        AssertEx.Equal(0L, timeouts, "대용량 페이로드에서 타임아웃이 나면 안 된다.");
    }

    /// <summary>
    /// --protocol text-line은 오래 전부터 클라이언트에 있었지만 받아 줄 서버가 없어
    /// 실행할 수 없는 옵션이었다. 이제 서버가 있으므로 실제로 완주하는지 확인한다.
    /// </summary>
    private static void TextLineServerEchoesClientLines()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "textline-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var metrics = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = runId,
            OutputDirectory = Path.Combine(temp.Path, "server"),
            ServerName = "text-test",
            SampleInterval = TimeSpan.FromMilliseconds(100)
        }))
        using (var server = new TextLineServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                TextPort = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                RunId = runId
            }, metrics), "Text-line server should configure.");
            AssertEx.True(server.Start(), "Text-line server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Transport = "text",
                Protocol = "text-line",
                Host = "127.0.0.1",
                Port = port,
                Clients = 2,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 5,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        var received = ParseColumnAsLong(samples, "total_receive", samples[^1]);
        var timeouts = ParseColumnAsLong(samples, "total_timeout", samples[^1]);

        AssertEx.True(received > 0, "text-line 서버는 보낸 줄을 되돌려줘야 한다.");
        AssertEx.Equal(0L, timeouts, "text-line 실행에 타임아웃이 있으면 안 된다.");
    }

    /// <summary>
    /// UDP도 마찬가지로 받아 줄 서버가 없던 시나리오다.
    /// 데이터그램 하나가 곧 요청 하나이며 세션은 본문의 GUID로 식별된다.
    /// </summary>
    private static void UdpEchoServerEchoesClientDatagrams()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeUdpPort();
        var runId = "udpecho-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var metrics = ServerMetricsCollector.Create(new ServerMetricsOptions
        {
            RunId = runId,
            OutputDirectory = Path.Combine(temp.Path, "server"),
            ServerName = "udp-test",
            SampleInterval = TimeSpan.FromMilliseconds(100)
        }))
        using (var server = new UdpEchoServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                UdpPort = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                RunId = runId
            }, metrics), "UDP server should configure.");
            AssertEx.True(server.Start(), "UDP server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Transport = "udp",
                Protocol = "udp-echo",
                Host = "127.0.0.1",
                Port = port,
                Clients = 2,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 5,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        var received = ParseColumnAsLong(samples, "total_receive", samples[^1]);

        AssertEx.True(received > 0, "UDP 서버는 보낸 데이터그램을 되돌려줘야 한다.");
    }

    /// <summary>
    /// 열린 루프가 존재하는 이유를 확인하는 시험이다.
    /// 응답 처리를 느리게 만들면 닫힌 루프는 송신까지 함께 느려지지만,
    /// 열린 루프는 예정된 일정대로 계속 보내야 한다.
    /// </summary>
    private static void OpenLoopKeepsSendingWhenResponsesAreSlow()
    {
        var openSends = RunWithSlowResponses("open");
        var closedSends = RunWithSlowResponses("closed");

        AssertEx.True(
            openSends >= closedSends * 2,
            $"열린 루프는 느린 응답에도 부하를 유지해야 한다. open={openSends}, closed={closedSends}");
    }

    private static long RunWithSlowResponses(string pacing)
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "pacing-" + pacing + "-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 20,
                // 응답을 읽기 전에 매번 멈춘다. 닫힌 루프에서는 이 지연이 곧 송신 주기가 된다.
                SlowReceiverDelay = TimeSpan.FromMilliseconds(200),
                Pacing = pacing,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        return ParseColumnAsLong(samples, "total_send_success", samples[^1]);
    }

    /// <summary>
    /// 동시 요청 한도는 지켜져야 한다. 한도를 넘겨 보내는 대신 건너뛰고 그 사실을 남긴다.
    /// 부하가 조용히 사라지면 결과를 잘못 읽게 되기 때문이다.
    /// </summary>
    private static void OpenLoopRespectsInFlightLimit()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "inflight-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = Path.Combine(temp.Path, "server"),
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 50,
                SlowReceiverDelay = TimeSpan.FromMilliseconds(200),
                Pacing = "open",
                MaxInFlight = 2,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var observed = ReadSummaryValue(clientOutput, "max_in_flight_observed");
        var skipped = ReadSummaryValue(clientOutput, "send_skipped_in_flight");

        AssertEx.True(observed <= 2, $"동시 요청은 한도 2를 넘지 않아야 한다. 관측값={observed}");
        AssertEx.True(skipped > 0, "한도에 걸려 건너뛴 송신은 기록되어야 한다.");
    }

    private static double ReadSummaryValue(string clientOutput, string key)
    {
        var lines = File.ReadAllLines(Path.Combine(clientOutput, "client_summary.csv"));
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length >= 5 && parts[3] == key)
                return double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException($"client_summary.csv에 '{key}' 항목이 없다.");
    }

    private static void OneTcpClientRecordsEchoOperation()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "integration-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        int result;
        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId
            });

            result = runtime.RunAsync().GetAwaiter().GetResult();
            server.Stop();
        }

        AssertEx.Equal(0, result);
        var operationsPath = Path.Combine(clientOutput, "client_operations.csv");
        var operations = File.ReadAllLines(operationsPath);
        AssertEx.True(operations.Length >= 2, "Client should record at least one operation.");
        AssertEx.True(operations.Any(line => line.Contains(",True,")), "At least one operation should succeed.");
        AssertEx.True(File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv")).Length >= 2, "Server samples should be written.");
    }

    private static void ReconnectStormDoesNotCrashAndDrainsSessions()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "reconnect-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        int result;
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        try
        {
            using var server = new LoadTestServer();
            try
            {
                AssertEx.True(server.Configure(new LoadTestServerOptions
                {
                    Port = port,
                    MaxConnections = 50,
                    Output = serverOutput,
                    SampleIntervalMs = 100,
                    RunId = runId
                }), "Server should configure.");
                AssertEx.True(server.StartWithMetrics(), "Server should start.");

                var runtime = new ClientRuntime(new LoadTestOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    Clients = 8,
                    Duration = TimeSpan.FromSeconds(2),
                    SendRatePerClient = 5,
                    Output = clientOutput,
                    RunId = runId,
                    Scenario = "reconnect-storm",
                    ReconnectPercent = 100,
                    StormAt = TimeSpan.FromMilliseconds(300),
                    StormPercent = 50,
                    StormWindow = TimeSpan.FromMilliseconds(400)
                });

                result = runtime.RunAsync().GetAwaiter().GetResult();
                server.Stop();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        catch
        {
            Console.SetOut(originalOut);
            throw;
        }

        var logs = capturedOut.ToString();

        AssertEx.Equal(0, result);
        AssertEx.False(logs.Contains("Object reference not set to an instance of an object", StringComparison.OrdinalIgnoreCase), "Reconnect storm should not trigger listener null-reference errors.");
        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Length >= 2, "Reconnect storm should record operations.");
        AssertEx.True(operations.Any(line => line.Contains(",True,")), "Reconnect storm should have successful operations.");

        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        AssertEx.True(sampleLines.Length >= 2, "Server samples should be written.");
        var finalActiveSessions = ParseColumnAsLong(sampleLines, "active_sessions", sampleLines[^1]);
        AssertEx.Equal(0L, finalActiveSessions, "Final server sample should show drained active sessions.");
    }

    private static void CoalescedTcpClientSendsTwoRequestsInOneLoop()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "coalesced-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 0.1,
                Output = clientOutput,
                RunId = runId,
                CoalescedPacket = true
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.Equal(3, operations.Length, "Coalesced loop should record two operation rows plus header.");
        AssertEx.True(operations.Skip(1).All(line => line.Contains(",True,")), "Both coalesced operations should succeed.");

        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        var totalRequests = ParseColumnAsLong(sampleLines, "total_requests", sampleLines[^1]);
        AssertEx.Equal(2L, totalRequests, "Server should receive two requests from one coalesced loop.");
    }

    private static void ServerRequestSamplingWritesRequestEvents()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "request-events-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId,
                RequestEventSampling = 1.0
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var events = File.ReadAllLines(Path.Combine(serverOutput, "server_events.csv"));
        AssertEx.True(events.Any(line => line.Contains(",request,")), "Request sampling 1.0 should write request events.");
    }

    private static void ClientOperationSamplingCanSuppressOperationRows()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "sampling-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId,
                OperationSampling = 0.0
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        AssertEx.Equal(1, File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv")).Length);
        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        AssertEx.True(ParseColumnAsLong(samples, "total_receive", samples[^1]) > 0, "Aggregate metrics should still count receives when operation rows are sampled out.");
    }

    private static void PartialTcpPacketSendRecordsSuccessfulOperation()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "partial-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId,
                PartialPacket = true
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",True,")), "Partial TCP packet send should record successful operations.");
        var sampleLines = File.ReadAllLines(Path.Combine(serverOutput, "server_samples.csv"));
        AssertEx.True(ParseColumnAsLong(sampleLines, "total_requests", sampleLines[^1]) > 0, "Server should receive requests split across writes.");
    }

    private static void SlowReceiverDelayStillRecordsSuccessfulTcpOperation()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "slow-receiver-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId,
                SlowReceiverDelay = TimeSpan.FromMilliseconds(25)
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",True,")), "Slow receiver should still record successful operations.");
    }

    private static void ClientRuntimeCancellationStopsActors()
    {
        using var temp = TempDirectory.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var runtime = new ClientRuntime(new LoadTestOptions
        {
            Host = "127.0.0.1",
            Port = GetFreeTcpPort(),
            Clients = 5,
            RampUp = TimeSpan.Zero,
            Duration = TimeSpan.FromMinutes(5),
            SendRatePerClient = 1,
            ReceiveTimeout = TimeSpan.FromMilliseconds(100),
            Output = Path.Combine(temp.Path, "client"),
            RunId = "cancel-" + Guid.NewGuid().ToString("N")
        });

        var task = Task.Run(() => runtime.RunAsync(cts.Token));
        AssertEx.True(task.Wait(TimeSpan.FromSeconds(5)), "Client runtime should stop promptly after cancellation.");
        AssertEx.Equal(0, task.Result);
    }

    private static void ConnectFailuresDoNotCountAsDisconnects()
    {
        using var temp = TempDirectory.Create();
        var runtime = new ClientRuntime(new LoadTestOptions
        {
            Host = "127.0.0.1",
            Port = 9,
            Clients = 1,
            Duration = TimeSpan.FromMilliseconds(500),
            SendRatePerClient = 1,
            ReceiveTimeout = TimeSpan.FromMilliseconds(50),
            Output = Path.Combine(temp.Path, "client"),
            RunId = "connect-fail-" + Guid.NewGuid().ToString("N")
        });

        AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());

        var samples = File.ReadAllLines(Path.Combine(temp.Path, "client", "client_samples.csv"));
        AssertEx.True(ParseColumnAsLong(samples, "total_connect_fail", samples[^1]) > 0, "Connect failures should be counted.");
        AssertEx.Equal(0L, ParseColumnAsLong(samples, "total_disconnect", samples[^1]), "Failed connects should not be counted as disconnects.");
    }

    private static void GameLikeRuntimeRecordsRoomLifecycleOperations()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        var runId = "game-like-" + Guid.NewGuid().ToString("N");
        var serverOutput = Path.Combine(temp.Path, "server");
        var clientOutput = Path.Combine(temp.Path, "client");

        using (var server = new LoadTestServer())
        {
            AssertEx.True(server.Configure(new LoadTestServerOptions
            {
                Port = port,
                MaxConnections = 10,
                Output = serverOutput,
                SampleIntervalMs = 100,
                RunId = runId
            }), "Server should configure.");
            AssertEx.True(server.StartWithMetrics(), "Server should start.");

            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Protocol = "game-binary",
                Scenario = "game-like",
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 8,
                RoomCycleEvery = 4,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
            server.Stop();
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Any(line => line.Contains(",login,")), "Game-like runtime should record login.");
        AssertEx.True(operations.Any(line => line.Contains(",room-enter,")), "Game-like runtime should record room enter.");
        AssertEx.True(operations.Any(line => line.Contains(",heartbeat,")), "Game-like runtime should record heartbeat.");
        AssertEx.True(operations.Any(line => line.Contains(",room-leave,")), "Game-like runtime should record room leave.");
        AssertEx.True(operations.Skip(1).All(line => line.Contains(",True,")), "Game-like operations should succeed against LoadTestServer.");
    }

    private static void UdpClientRecordsEchoOperations()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeUdpPort();
        using var cts = new CancellationTokenSource();
        var echoTask = RunUdpEchoLoopAsync(port, cts.Token);
        Thread.Sleep(50);
        var runId = "udp-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        try
        {
            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Transport = "udp",
                Protocol = "simple-udp",
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(2),
                SendRatePerClient = 2,
                ReceiveTimeout = TimeSpan.FromSeconds(2),
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
        }
        finally
        {
            cts.Cancel();
            try
            {
                echoTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Length >= 2, "UDP client should record at least one operation.");
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",True,")), "UDP echo should record at least one successful operation.");
        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        AssertEx.True(ParseColumnAsLong(samples, "total_receive", samples[^1]) > 0, "UDP receive counter should be positive.");
        AssertEx.Equal(0L, ParseColumnAsLong(samples, "total_send_fail", samples[^1]), "UDP echo should not record send failures.");
    }

    private static void TextLineClientRecordsPingOperations()
    {
        using var temp = TempDirectory.Create();
        var port = GetFreeTcpPort();
        using var cts = new CancellationTokenSource();
        var echoTask = RunTextLineEchoServerAsync(port, cts.Token);
        var runId = "text-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        try
        {
            var runtime = new ClientRuntime(new LoadTestOptions
            {
                Transport = "tcp",
                Protocol = "text-line",
                Host = "127.0.0.1",
                Port = port,
                Clients = 1,
                Duration = TimeSpan.FromSeconds(1),
                SendRatePerClient = 2,
                Output = clientOutput,
                RunId = runId
            });

            AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());
        }
        finally
        {
            cts.Cancel();
            try
            {
                echoTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }

        var operations = File.ReadAllLines(Path.Combine(clientOutput, "client_operations.csv"));
        AssertEx.True(operations.Length >= 2, "Text-line client should record at least one operation.");
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",text-ping,")), "Text-line client should record text-ping operations.");
        AssertEx.True(operations.Skip(1).Any(line => line.Contains(",True,")), "Text-line client should record successful operations.");
    }

    private static void UdpLossSimulationRecordsSendFailures()
    {
        using var temp = TempDirectory.Create();
        var runId = "udp-loss-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        var runtime = new ClientRuntime(new LoadTestOptions
        {
            Transport = "udp",
            Protocol = "simple-udp",
            Host = "127.0.0.1",
            Port = GetFreeUdpPort(),
            Clients = 1,
            Duration = TimeSpan.FromSeconds(1),
            SendRatePerClient = 5,
            Output = clientOutput,
            RunId = runId,
            UdpLossPercent = 100
        });

        AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        var totalSendFail = ParseColumnAsLong(samples, "total_send_fail", samples[^1]);
        var totalTimeout = ParseColumnAsLong(samples, "total_timeout", samples[^1]);
        AssertEx.True(totalSendFail > 0, "Simulated UDP loss should increment send-fail counter.");
        AssertEx.Equal(0L, totalTimeout, "Simulated UDP loss should skip receive timeout waits.");
    }

    private static void UdpClientRecordsReceiveTimeouts()
    {
        using var temp = TempDirectory.Create();
        var runId = "udp-timeout-" + Guid.NewGuid().ToString("N");
        var clientOutput = Path.Combine(temp.Path, "client");

        var runtime = new ClientRuntime(new LoadTestOptions
        {
            Transport = "udp",
            Protocol = "simple-udp",
            Host = "127.0.0.1",
            Port = GetFreeUdpPort(),
            Clients = 1,
            Duration = TimeSpan.FromSeconds(1),
            SendRatePerClient = 2,
            ReceiveTimeout = TimeSpan.FromMilliseconds(50),
            Output = clientOutput,
            RunId = runId
        });

        AssertEx.Equal(0, runtime.RunAsync().GetAwaiter().GetResult());

        var samples = File.ReadAllLines(Path.Combine(clientOutput, "client_samples.csv"));
        var totalTimeout = ParseColumnAsLong(samples, "total_timeout", samples[^1]);
        var totalSendSuccess = ParseColumnAsLong(samples, "total_send_success", samples[^1]);
        AssertEx.True(totalSendSuccess > 0, "UDP timeout run should send datagrams.");
        AssertEx.True(totalTimeout > 0, "UDP timeout run should increment timeout counter.");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int GetFreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static async Task RunUdpEchoLoopAsync(int port, CancellationToken cancellationToken)
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await server.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            await server.SendAsync(result.Buffer, result.Buffer.Length, result.RemoteEndPoint).ConfigureAwait(false);
        }
    }

    private static async Task RunTextLineEchoServerAsync(int port, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    using var _ = client;
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { NewLine = "\r\n", AutoFlush = true };
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line is null)
                            return;
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static long ParseColumnAsLong(string[] csvLines, string columnName, string row)
    {
        var headers = csvLines[0].Split(',');
        var values = row.Split(',');
        var index = Array.IndexOf(headers, columnName);
        AssertEx.True(index >= 0, $"Missing column {columnName}.");
        return long.Parse(values[index]);
    }
}
