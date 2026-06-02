using System.Net;
using System.Net.Sockets;
using SuperSocketLite.LoadTest.Client;
using SuperSocketLite.LoadTest.Server;

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
