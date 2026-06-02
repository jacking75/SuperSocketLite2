namespace SuperSocketLite.LoadTest.ServerProbe;

public sealed class ServerMetricsOptions
{
    public string RunId { get; set; } = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
    public string OutputDirectory { get; set; } = Path.Combine("logs", "loadtest", "server");
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(1);
    public string ServerName { get; set; } = "LoadTestServer";
    public double RequestEventSampling { get; set; }
    public int WriterChannelCapacity { get; set; } = 4096;
    public bool AutoStartWriter { get; set; } = true;
}
