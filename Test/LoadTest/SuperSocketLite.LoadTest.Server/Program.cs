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

using var server = new LoadTestServer();

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
Console.WriteLine(options.Duration is null ? "Press Ctrl+C to stop." : $"Stopping after {options.Duration}.");

var stopped = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopped.Set();
};

if (options.Duration is { } duration)
    stopped.Wait(duration);
else
    stopped.Wait();

server.Stop();
return 0;
