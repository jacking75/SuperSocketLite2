using SuperSocketLite.LoadTest.Client;

if (LoadTestOptions.IsHelpRequest(args))
{
    Console.WriteLine(LoadTestOptions.HelpText);
    return 0;
}

try
{
    var options = LoadTestOptions.Parse(args);
    var runtime = new ClientRuntime(options);
    return await runtime.RunAsync();
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(LoadTestOptions.HelpText);
    return 2;
}
