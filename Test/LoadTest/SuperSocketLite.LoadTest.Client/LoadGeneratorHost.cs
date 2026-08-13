using System.Net.Sockets;

namespace SuperSocketLite.LoadTest.Client;

/// <summary>
/// 부하 발생기가 스스로 병목이 되지 않도록 실행 환경을 준비하고,
/// 한계에 닿았을 때 그 사실을 조용히 넘기지 않고 드러냅니다.
/// </summary>
public static class LoadGeneratorHost
{
    /// <summary>
    /// 스레드풀이 워커를 늘리는 속도는 초당 한두 개 수준입니다.
    /// 수천 개 클라이언트가 한꺼번에 접속을 시작하면 그 증가 속도를 기다리느라
    /// 램프업이 길어지고 송신 일정도 밀립니다. 시작 전에 미리 확보해 둡니다.
    /// </summary>
    public static void PrepareThreadPool(LoadTestOptions options)
    {
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);

        // 클라이언트마다 송신·수신 두 루프가 돌 수 있으므로 그만큼을 기준으로 잡되,
        // 논리 프로세서 수보다 아래로는 내리지 않는다.
        var desired = Math.Max(Environment.ProcessorCount * 4, Math.Min(options.Clients * 2, 2000));
        var worker = Math.Min(Math.Max(minWorker, desired), maxWorker);
        var io = Math.Min(Math.Max(minIo, desired), maxIo);

        if (worker <= minWorker && io <= minIo)
            return;

        ThreadPool.SetMinThreads(worker, io);
    }

    /// <summary>
    /// 연결 실패가 부하 발생기 쪽 한계에서 온 것인지 판별합니다.
    /// 서버가 거부한 것과 클라이언트 머신이 더 이상 소켓을 낼 수 없는 것은
    /// 대응이 전혀 다르므로 구분해서 보고해야 합니다.
    /// </summary>
    public static bool IsLocalResourceExhaustion(Exception exception)
    {
        return FindSocketError(exception) is
            SocketError.AddressAlreadyInUse or
            SocketError.TooManyOpenSockets or
            SocketError.NoBufferSpaceAvailable;
    }

    public static string DescribeExhaustion(SocketError error)
    {
        return error switch
        {
            SocketError.AddressAlreadyInUse =>
                "임시 포트가 부족하다. TIME_WAIT 소켓이 쌓였을 수 있으니 클라이언트 수를 줄이거나 여러 머신에 나눠 실행한다.",
            SocketError.TooManyOpenSockets =>
                "열 수 있는 소켓 수를 넘었다. 클라이언트 수를 줄이거나 여러 머신에 나눠 실행한다.",
            SocketError.NoBufferSpaceAvailable =>
                "소켓 버퍼를 더 할당할 수 없다. 클라이언트 머신이 한계에 닿았다.",
            _ => error.ToString()
        };
    }

    public static SocketError? FindSocketError(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException)
                return socketException.SocketErrorCode;
        }

        return null;
    }
}
