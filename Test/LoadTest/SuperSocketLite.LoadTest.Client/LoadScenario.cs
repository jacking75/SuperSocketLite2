namespace SuperSocketLite.LoadTest.Client;

public static class LoadScenario
{
    public static IEnumerable<TimeSpan> CreateConnectSchedule(LoadTestOptions options)
    {
        if (options.Clients <= 0)
            yield break;

        if (options.Clients == 1 || options.RampUp <= TimeSpan.Zero)
        {
            for (var i = 0; i < options.Clients; i++)
                yield return TimeSpan.Zero;
            yield break;
        }

        var stepTicks = options.RampUp.Ticks / options.Clients;
        for (var i = 0; i < options.Clients; i++)
            yield return TimeSpan.FromTicks(stepTicks * i);
    }

    public static TimeSpan NextThinkTime(LoadTestOptions options, Random random)
    {
        if (options.SendRatePerClient <= 0)
            return TimeSpan.FromSeconds(1);

        var baseMs = 1000.0 / options.SendRatePerClient;
        var jitter = 0.75 + random.NextDouble() * 0.5;
        return TimeSpan.FromMilliseconds(Math.Max(1, baseMs * jitter));
    }
}
