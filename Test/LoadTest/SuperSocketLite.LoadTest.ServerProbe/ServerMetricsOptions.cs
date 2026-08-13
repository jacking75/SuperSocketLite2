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

    /// <summary>
    /// SuperSocketLite Meter의 런타임 게이지(송신 큐 깊이·SAEA 풀)를 함께 읽을지 여부입니다.
    /// 끄면 계기를 구독하지 않으므로, 계측 자체의 비용을 재는 실행에서 기준으로 씁니다.
    /// </summary>
    public bool CollectRuntimeGauges { get; set; } = true;
}
