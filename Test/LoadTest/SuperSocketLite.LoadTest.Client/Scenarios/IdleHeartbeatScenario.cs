namespace SuperSocketLite.LoadTest.Client.Scenarios;

public static class IdleHeartbeatScenario
{
    public static TimeSpan NextHeartbeatDelay(LoadTestOptions options, Random random)
    {
        var min = Math.Min(options.HeartbeatMinSec, options.HeartbeatMaxSec);
        var max = Math.Max(options.HeartbeatMinSec, options.HeartbeatMaxSec);
        return TimeSpan.FromSeconds(random.Next(min, max + 1));
    }
}
