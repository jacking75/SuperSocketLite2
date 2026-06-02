namespace SuperSocketLite.LoadTest.Client.Scenarios;

public static class ReconnectStormScenario
{
    public static bool IsStormClient(int clientId, LoadTestOptions options)
    {
        if (options.StormPercent <= 0)
            return false;

        return clientId % 100 < options.StormPercent;
    }

    public static TimeSpan DisconnectAt(int clientId, LoadTestOptions options)
    {
        if (!IsStormClient(clientId, options))
            return TimeSpan.MaxValue;

        var slotMs = options.StormWindow.TotalMilliseconds * (clientId % 100) / Math.Max(1, options.StormPercent);
        return options.StormAt + TimeSpan.FromMilliseconds(slotMs);
    }
}
