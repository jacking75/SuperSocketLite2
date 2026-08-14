using SuperSocketLite.LoadTest.Server;

if (LoadTestServerOptions.IsHelpRequest(args))
{
    Console.WriteLine(LoadTestServerOptions.HelpText);
    return 0;
}

LoadTestServerOptions options;
try
{
    options = LoadTestServerOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(LoadTestServerOptions.HelpText);
    return 2;
}

// 종료 신호는 서버를 띄우기 전에 준비한다. 남아 있던 stop 파일을 먼저 지워야 새 서버가
// 뜨자마자 끝나는 일이 없고, 기동 도중에 들어온 종료 요청도 놓치지 않는다.
var stopped = new ManualResetEventSlim();
using var stopFileSignal = StopFileSignal.Start(options.StopFile, stopped.Set);

using var server = new LoadTestServer(options.Allocation);

if (!server.Configure(options))
{
    Console.Error.WriteLine("Failed to configure LoadTestServer.");
    return 1;
}

if (!server.StartWithMetrics())
{
    Console.Error.WriteLine("Failed to start LoadTestServer.");
    return 1;
}

Console.WriteLine($"LoadTestServer listening on port {options.Port}. Output: {options.Output}");

if (options.Metrics != ServerMetricsMode.Full)
    Console.WriteLine($"  metrics: {options.Metrics.ToString().ToLowerInvariant()}");

if (options.Allocation != AllocationMode.Pooled)
    Console.WriteLine($"  alloc-mode: {options.Allocation.ToString().ToLowerInvariant()}");

if (options.StopFile is not null)
    Console.WriteLine($"  stop-file: {options.StopFile}");

// 부가 리스너는 바이너리 서버의 계측기를 함께 쓴다. 프로세스 자원은 하나이기 때문이다.
using var textServer = new TextLineServer();
if (options.TextPort > 0)
{
    if (!textServer.Configure(options, server.Metrics) || !textServer.Start())
    {
        Console.Error.WriteLine($"Failed to start text-line listener on port {options.TextPort}.");
        return 1;
    }

    Console.WriteLine($"  text-line listener on port {options.TextPort}");
}

using var udpServer = new UdpEchoServer();
if (options.UdpPort > 0)
{
    if (!udpServer.Configure(options, server.Metrics) || !udpServer.Start())
    {
        Console.Error.WriteLine($"Failed to start UDP listener on port {options.UdpPort}.");
        return 1;
    }

    Console.WriteLine($"  UDP listener on port {options.UdpPort}");
}

Console.WriteLine(options.Duration is null ? "Press Ctrl+C to stop." : $"Stopping after {options.Duration}.");

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopped.Set();
};

if (options.Duration is { } duration)
    stopped.Wait(duration);
else
    stopped.Wait();

// 부가 리스너를 먼저 닫아야 바이너리 서버가 마지막 샘플을 쓸 때
// 모든 세션이 정리된 상태가 된다.
udpServer.Stop();
textServer.Stop();
server.Stop();
return 0;
