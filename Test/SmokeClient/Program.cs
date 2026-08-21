using System.Diagnostics;
using System.Net.Sockets;

namespace SuperSocketLite.SmokeClient;

/// <summary>
/// SuperSocketLite2 서버에 실제로 접속해 패킷을 왕복시키는 헤드리스 클라이언트.
/// </summary>
/// <remarks>
/// 저장소의 다른 테스트 클라이언트는 WinForms라 CI나 에이전트가 돌릴 수 없다. 이 프로젝트는
/// 콘솔 전용이고 종료 코드로만 결과를 알린다 — 성공 0, 실패 1.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Options.Usage);
            return args.Length == 0 ? 1 : 0;
        }

        var options = Options.Parse(args, out var error);

        if (options is null)
        {
            Console.Error.WriteLine(error is null ? Options.Usage : $"[ERROR] {error}");
            return 1;
        }

        Console.WriteLine(
            $"smokeclient -> {options.Host}:{options.Port}  " +
            $"connections={options.Connections} count={options.Count} body={options.Body.Length}B");

        var codec = new PacketCodec(options);
        var stopwatch = Stopwatch.StartNew();

        var workers = new Task<ConnectionResult>[options.Connections];

        for (var i = 0; i < workers.Length; ++i)
        {
            var index = i;
            workers[i] = Task.Run(() => RunConnectionAsync(index, options, codec));
        }

        var results = await Task.WhenAll(workers);
        stopwatch.Stop();

        return Report(results, options, stopwatch.Elapsed);
    }

    private static async Task<ConnectionResult> RunConnectionAsync(int index, Options options, PacketCodec codec)
    {
        var result = new ConnectionResult(index);

        try
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            using (var connectCts = new CancellationTokenSource(options.TimeoutMs))
            {
                await socket.ConnectAsync(options.Host, options.Port, connectCts.Token);
            }

            result.Connected = true;

            await using var stream = new NetworkStream(socket, ownsSocket: false);

            var request = codec.Encode(options.PacketId, options.Body);
            var header = new byte[Math.Max(options.HeaderSize, 1)];

            for (var i = 0; i < options.Count; ++i)
            {
                using var cts = new CancellationTokenSource(options.TimeoutMs);

                await stream.WriteAsync(request, cts.Token);
                ++result.Sent;

                if (options.NoWaitResponse)
                {
                    continue;
                }

                await stream.ReadExactlyAsync(header.AsMemory(0, options.HeaderSize), cts.Token);

                var bodyLength = codec.GetRemainingBodyLength(header);

                if (bodyLength < 0 || bodyLength > 1 << 24)
                {
                    result.Fail($"헤더가 이상한 본문 길이를 담고 있습니다: {bodyLength}. " +
                                "프로토콜 옵션(--len-bytes / --id-bytes / --length-excludes-header / --big-endian)을 확인하세요.");
                    return result;
                }

                var body = new byte[bodyLength];

                if (bodyLength > 0)
                {
                    await stream.ReadExactlyAsync(body, cts.Token);
                }

                ++result.Received;

                if (options.ExpectId is { } expectedId)
                {
                    var actualId = codec.ReadPacketId(header);

                    if (actualId != expectedId)
                    {
                        result.Fail($"응답 패킷 ID가 다릅니다. 기대 {expectedId}, 실제 {actualId}");
                        return result;
                    }
                }

                if (options.ExpectEcho && !body.AsSpan().SequenceEqual(options.Body))
                {
                    result.Fail($"응답 본문이 요청과 다릅니다. 보낸 {options.Body.Length}B, 받은 {bodyLength}B" +
                                (bodyLength == options.Body.Length ? " (길이는 같고 내용이 다름)" : string.Empty));
                    return result;
                }

                if (options.Verbose)
                {
                    Console.WriteLine($"  [conn {index}] #{i + 1} ok, {bodyLength}B");
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Fail($"{options.TimeoutMs}ms 안에 응답이 오지 않았습니다. " +
                        $"(보냄 {result.Sent}, 받음 {result.Received})");
        }
        catch (EndOfStreamException)
        {
            result.Fail($"서버가 응답 도중 연결을 끊었습니다. (보냄 {result.Sent}, 받음 {result.Received})");
        }
        catch (SocketException ex)
        {
            result.Fail($"소켓 오류: {ex.SocketErrorCode} ({ex.Message})");
        }
        catch (IOException ex)
        {
            result.Fail($"입출력 오류: {ex.Message}");
        }

        return result;
    }

    private static int Report(ConnectionResult[] results, Options options, TimeSpan elapsed)
    {
        var connected = results.Count(r => r.Connected);
        var sent = results.Sum(r => r.Sent);
        var received = results.Sum(r => r.Received);
        var failures = results.Where(r => r.Error is not null).ToArray();

        Console.WriteLine($"  connected : {connected}/{options.Connections}");
        Console.WriteLine($"  sent      : {sent}");
        Console.WriteLine($"  received  : {received}");
        Console.WriteLine($"  elapsed   : {elapsed.TotalMilliseconds:F0}ms" +
                          (elapsed.TotalSeconds > 0 && received > 0
                              ? $"  ({received / elapsed.TotalSeconds:F0} packets/s)"
                              : string.Empty));

        if (failures.Length > 0)
        {
            Console.Error.WriteLine();

            // 같은 원인이 연결 수만큼 반복되므로 앞의 몇 개만 보여 준다.
            foreach (var failure in failures.Take(5))
            {
                Console.Error.WriteLine($"  [conn {failure.Index}] {failure.Error}");
            }

            if (failures.Length > 5)
            {
                Console.Error.WriteLine($"  ... 외 {failures.Length - 5}건");
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine($"FAIL ({failures.Length}/{options.Connections} 연결 실패)");
            return 1;
        }

        var expected = options.Connections * options.Count;

        if (sent != expected || (!options.NoWaitResponse && received != expected))
        {
            Console.Error.WriteLine($"FAIL (기대 {expected}, 보냄 {sent}, 받음 {received})");
            return 1;
        }

        Console.WriteLine("OK");
        return 0;
    }

    private sealed class ConnectionResult(int index)
    {
        public int Index { get; } = index;
        public bool Connected { get; set; }
        public int Sent { get; set; }
        public int Received { get; set; }
        public string? Error { get; private set; }

        public void Fail(string message) => Error ??= message;
    }
}
