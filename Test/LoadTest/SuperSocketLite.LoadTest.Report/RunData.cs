namespace SuperSocketLite.LoadTest.Report;

/// <summary>한 실행에서 읽어 들인 원본 데이터입니다.</summary>
public sealed class RunData
{
    public required string RunId { get; init; }

    /// <summary>이 실행의 CSV가 들어 있던 디렉토리들입니다. 서버와 클라이언트가 따로 있을 수 있습니다.</summary>
    public required IReadOnlyList<string> Directories { get; init; }

    /// <summary>client_summary.csv의 key/value입니다. 여러 머신이면 마지막 값이 남습니다.</summary>
    public required IReadOnlyDictionary<string, string> Summary { get; init; }

    public required IReadOnlyList<ClientSample> ClientSamples { get; init; }

    public required IReadOnlyList<ServerSample> ServerSamples { get; init; }
}

public sealed record ClientSample(
    long ElapsedMs,
    string Phase,
    long ActiveClients,
    double SendPerSec,
    double ReceivePerSec,
    long RttP99Us,
    long TotalTimeout,
    long TotalSendFail);

/// <summary>
/// 서버가 남긴 한 행입니다.
/// 뒤쪽 네 항목은 라이브러리 계기에서 온 값이며, 계기가 없던 실행과 계측을 끈 실행에서는 -1입니다.
/// </summary>
public sealed record ServerSample(
    long ElapsedMs,
    string Phase,
    long ActiveSessions,
    double RequestsPerSec,
    long WorkingSetBytes,
    long GcHeapBytes,
    long ExceptionTotal,
    long HandlerLatencyP99Us,
    double CpuPercent,
    long SendQueueDepthTotal = -1,
    long SendQueueDepthMax = -1,
    long ReceivePoolAvailable = -1,
    long SendPoolAvailable = -1);
