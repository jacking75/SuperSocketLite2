using System.Globalization;

namespace SuperSocketLite.SmokeClient;

/// <summary>명령줄 옵션.</summary>
internal sealed class Options
{
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; }

    /// <summary>길이 필드 바이트 수. 2 또는 4.</summary>
    public int LengthBytes { get; private set; } = 2;

    /// <summary>패킷 ID 바이트 수. 0이면 ID 필드가 없는 프로토콜이다.</summary>
    public int IdBytes { get; private set; } = 2;

    /// <summary>길이 필드가 헤더까지 포함한 전체 길이인가. false면 본문 길이만 담는다.</summary>
    public bool LengthIncludesHeader { get; private set; } = true;

    public bool BigEndian { get; private set; }

    public short PacketId { get; private set; } = 101;

    /// <summary>보낼 본문. --text / --hex / --size 중 하나로 만들어진다.</summary>
    public byte[] Body { get; private set; } = "ping"u8.ToArray();

    public int Count { get; private set; } = 1;
    public int Connections { get; private set; } = 1;
    public int TimeoutMs { get; private set; } = 5000;

    /// <summary>응답 본문이 요청 본문과 같아야 한다.</summary>
    public bool ExpectEcho { get; private set; }

    /// <summary>기대하는 응답 패킷 ID. 지정하지 않으면 검사하지 않는다.</summary>
    public short? ExpectId { get; private set; }

    /// <summary>응답을 기다리지 않고 보내기만 한다.</summary>
    public bool NoWaitResponse { get; private set; }

    public bool Verbose { get; private set; }

    public int HeaderSize => LengthBytes + IdBytes;

    public static Options? Parse(string[] args, out string? error)
    {
        var options = new Options();
        var bodySetters = 0;
        error = null;

        for (var i = 0; i < args.Length; ++i)
        {
            var name = args[i];

            switch (name)
            {
                case "--expect-echo":
                    options.ExpectEcho = true;
                    continue;

                case "--no-wait-response":
                    options.NoWaitResponse = true;
                    continue;

                case "--big-endian":
                    options.BigEndian = true;
                    continue;

                case "--length-excludes-header":
                    options.LengthIncludesHeader = false;
                    continue;

                case "-v" or "--verbose":
                    options.Verbose = true;
                    continue;

                case "-h" or "--help":
                    error = null;
                    return null;
            }

            if (i + 1 >= args.Length)
            {
                error = $"{name} 에 값이 없습니다.";
                return null;
            }

            var value = args[++i];

            switch (name)
            {
                case "--host":
                    options.Host = value;
                    break;

                case "--port" or "-p":
                    if (!TryPort(value, out var port))
                    {
                        error = $"--port 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.Port = port;
                    break;

                case "--len-bytes":
                    if (value is not ("2" or "4"))
                    {
                        error = "--len-bytes 는 2 또는 4 여야 합니다.";
                        return null;
                    }

                    options.LengthBytes = int.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case "--id-bytes":
                    if (value is not ("0" or "2"))
                    {
                        error = "--id-bytes 는 0 또는 2 여야 합니다.";
                        return null;
                    }

                    options.IdBytes = int.Parse(value, CultureInfo.InvariantCulture);
                    break;

                case "--packet-id":
                    if (!short.TryParse(value, CultureInfo.InvariantCulture, out var packetId))
                    {
                        error = $"--packet-id 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.PacketId = packetId;
                    break;

                case "--expect-id":
                    if (!short.TryParse(value, CultureInfo.InvariantCulture, out var expectId))
                    {
                        error = $"--expect-id 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.ExpectId = expectId;
                    break;

                case "--text":
                    options.Body = System.Text.Encoding.UTF8.GetBytes(value);
                    ++bodySetters;
                    break;

                case "--hex":
                    if (!TryParseHex(value, out var hexBody))
                    {
                        error = $"--hex 값이 16진수가 아닙니다: {value}";
                        return null;
                    }

                    options.Body = hexBody;
                    ++bodySetters;
                    break;

                case "--size":
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out var size) || size < 0)
                    {
                        error = $"--size 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.Body = CreateFilledBody(size);
                    ++bodySetters;
                    break;

                case "--count" or "-c":
                    if (!TryPositive(value, out var count))
                    {
                        error = $"--count 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.Count = count;
                    break;

                case "--connections" or "-n":
                    if (!TryPositive(value, out var connections))
                    {
                        error = $"--connections 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.Connections = connections;
                    break;

                case "--timeout":
                    if (!TryPositive(value, out var timeout))
                    {
                        error = $"--timeout 값이 잘못되었습니다: {value}";
                        return null;
                    }

                    options.TimeoutMs = timeout;
                    break;

                default:
                    error = $"알 수 없는 옵션입니다: {name}";
                    return null;
            }
        }

        if (options.Port == 0)
        {
            error = "--port 는 필수입니다.";
            return null;
        }

        if (bodySetters > 1)
        {
            error = "--text / --hex / --size 는 하나만 지정합니다.";
            return null;
        }

        if (options.IdBytes == 0 && options.ExpectId is not null)
        {
            error = "--id-bytes 0 인 프로토콜에는 --expect-id 를 쓸 수 없습니다.";
            return null;
        }

        return options;
    }

    /// <summary>검증하기 쉽도록 0,1,2,... 패턴으로 채운 본문을 만든다.</summary>
    private static byte[] CreateFilledBody(int size)
    {
        var body = new byte[size];

        for (var i = 0; i < size; ++i)
        {
            body[i] = (byte)(i % 251);
        }

        return body;
    }

    private static bool TryParseHex(string value, out byte[] result)
    {
        var text = value.Replace(" ", string.Empty).Replace("-", string.Empty);

        if (text.Length % 2 != 0)
        {
            result = [];
            return false;
        }

        try
        {
            result = Convert.FromHexString(text);
            return true;
        }
        catch (FormatException)
        {
            result = [];
            return false;
        }
    }

    private static bool TryPort(string value, out int result)
        => int.TryParse(value, CultureInfo.InvariantCulture, out result) && result is > 0 and <= 65535;

    private static bool TryPositive(string value, out int result)
        => int.TryParse(value, CultureInfo.InvariantCulture, out result) && result > 0;

    public const string Usage = """
        smokeclient — SuperSocketLite2 서버에 실제로 붙어 패킷 왕복을 확인한다.

        사용법:
          dotnet run --project Test/SmokeClient -- --port <포트> [옵션]

        접속
          --host <호스트>            기본 127.0.0.1
          --port, -p <포트>          필수
          --timeout <ms>             응답 대기 상한. 기본 5000

        프로토콜 (기본값은 Template/dotnet-new 서버와 같다)
          --len-bytes <2|4>          길이 필드 크기. 기본 2
          --id-bytes <0|2>           패킷 ID 필드 크기. 0이면 ID 없음. 기본 2
          --length-excludes-header   길이 필드가 본문 길이만 담는 프로토콜
          --big-endian               길이·ID를 빅엔디안으로 읽고 쓴다

        보낼 것
          --packet-id <n>            기본 101
          --text <문자열>            본문을 UTF-8로. 기본 "ping"
          --hex <16진수>             본문을 16진수로
          --size <n>                 n바이트짜리 패턴 본문
          --count, -c <n>            연결당 보낼 패킷 수. 기본 1
          --connections, -n <n>      동시 연결 수. 기본 1

        검증
          --expect-echo              응답 본문이 요청 본문과 같아야 한다
          --expect-id <n>            응답 패킷 ID가 이 값이어야 한다
          --no-wait-response         응답을 기다리지 않는다 (단방향 프로토콜)
          -v, --verbose              패킷마다 로그를 찍는다

        종료 코드는 성공 0, 실패 1 이다.

        예)
          dotnet run --project Test/SmokeClient -- --port 32452 --expect-echo
          dotnet run --project Test/SmokeClient -- --port 32452 -n 50 -c 20 --size 512 --expect-echo
        """;
}
