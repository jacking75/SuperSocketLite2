using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;

namespace SuperSocketLite2.GameServerTemplate;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var port = ParsePort(args, defaultPort: 32452);

        var config = new ServerConfig
        {
            Ip = "Any",
            Port = port,
            Mode = SocketMode.Tcp,
            Name = "SuperSocketLite2.GameServerTemplate",

            MaxConnectionNumber = 2000,

            // 기본값 1024는 게임 패킷에 대개 모자란다. 실제 최대 패킷보다 크게 둔다.
            MaxRequestLength = 8192,

            // 실시간 게임이면 Nagle을 끈다.
            NoDelay = true,

            // NewSessionConnected 를 accept 경로에서 동기 호출해서 "접속 → 첫 요청" 순서를
            // 구조적으로 보장한다. 대신 접속 핸들러가 accept를 블로킹하므로 가볍게 유지한다.
            SyncSessionConnectedEvent = true,
        };

        var server = new MainServer();

        // 실서비스에서는 ConsoleLogFactory 대신 MicrosoftLoggingLogFactory 로 바꾼다.
        // Serilog / NLog / ZLogger / log4net 이 전부 그 하나로 붙는다.
        if (!server.Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory()))
        {
            Console.Error.WriteLine("[FATAL] 서버 설정에 실패했습니다.");
            return 1;
        }

        if (!server.Start())
        {
            Console.Error.WriteLine("[FATAL] 서버 시작에 실패했습니다.");
            return 1;
        }

        Console.WriteLine($"listening on {config.Port}. Ctrl+C 로 종료합니다.");

        var stopping = new TaskCompletionSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.TrySetResult();
        };

        await stopping.Task;

        Console.WriteLine("종료 중...");

        // Stop() 과 달리 큐에 남은 응답을 흘려보낸 뒤 닫는다.
        await server.StopAsync(TimeSpan.FromSeconds(5));

        return 0;
    }

    /// <summary>--port 인자를 읽는다. 없으면 기본값을 쓴다.</summary>
    private static int ParsePort(string[] args, int defaultPort)
    {
        for (var i = 0; i < args.Length - 1; ++i)
        {
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var parsed))
            {
                return parsed;
            }
        }

        return defaultPort;
    }
}
