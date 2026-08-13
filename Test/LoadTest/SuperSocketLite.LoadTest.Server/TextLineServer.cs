using System.Buffers;
using System.Text;
using SuperSocketLite.LoadTest.ServerProbe;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.LoadTest.Server;

/// <summary>
/// 줄바꿈으로 요청을 구분하는 텍스트 에코 서버입니다.
/// </summary>
/// <remarks>
/// 클라이언트에는 <c>--protocol text-line</c>이 예전부터 있었지만 받아 줄 서버가 없어
/// 실행할 수 없는 옵션이었습니다. 이 서버가 그 자리를 채웁니다.
///
/// 라이브러리에는 구분자 기반 수신 필터가 없으므로(문자열 프로토콜 계열이 제거되었습니다)
/// 여기서 <see cref="IReceiveFilter{TRequestInfo}"/>를 직접 구현합니다.
/// 바이너리 서버와는 프로토콜이 다르므로 같은 포트를 쓸 수 없고 별도 인스턴스로 띄웁니다.
/// </remarks>
public sealed class TextLineServer : AppServer<TextLineSession, TextLineRequestInfo>, IDisposable
{
    private ServerMetricsCollector? _metrics;
    private bool _stopped;

    public TextLineServer()
        : base(new DefaultReceiveFilterFactory<TextLineReceiveFilter, TextLineRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    /// <summary>
    /// 바이너리 서버와 같은 수집기를 공유합니다.
    /// GC·메모리·CPU는 프로세스 단위 값이므로 리스너마다 따로 재는 것이 의미가 없고,
    /// 세션과 요청 수는 합산해 보는 편이 서버 전체 부하를 읽기 쉽습니다.
    /// </summary>
    public bool Configure(LoadTestServerOptions options, ServerMetricsCollector metrics)
    {
        _metrics = metrics;

        var config = new ServerConfig
        {
            Port = options.TextPort,
            Ip = "Any",
            MaxConnectionNumber = options.MaxConnections,
            Mode = SocketMode.Tcp,
            Name = "LoadTestTextServer",
            MaxRequestLength = 1024 * 1024,
            NoDelay = true
        };

        return Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());
    }

    private void OnConnected(TextLineSession session)
    {
        _metrics?.OnConnected(session.SessionID, session.RemoteEndPoint?.ToString());
    }

    private void OnClosed(TextLineSession session, CloseReason reason)
    {
        _metrics?.OnClosed(session.SessionID, reason.ToString(), session.RemoteEndPoint?.ToString());
    }

    private void OnRequestReceived(TextLineSession session, TextLineRequestInfo request)
    {
        if (_metrics is null)
            return;

        using var recorder = _metrics.BeginRequest(session.SessionID, packetId: 0, bytesIn: request.Line.Length);

        // 클라이언트가 줄 단위로 응답을 읽으므로 종결자를 붙여 되돌려준다.
        var response = Encoding.UTF8.GetBytes(request.Line + "\r\n");

        try
        {
            session.Send(response, 0, response.Length);
            _metrics.OnBytesOut(response.Length);
        }
        catch (Exception ex)
        {
            _metrics.OnSendFailed(session.SessionID, response.Length, ex.Message);
            throw;
        }
    }

    public new void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        base.Stop();
    }

    public new void Dispose()
    {
        Stop();
        _metrics = null;
    }
}

public sealed class TextLineSession : AppSession<TextLineSession, TextLineRequestInfo>
{
}

public sealed class TextLineRequestInfo : IRequestInfo
{
    public TextLineRequestInfo(string line)
    {
        Line = line;
    }

    public string Key => "line";

    /// <summary>종결자를 뺀 한 줄입니다.</summary>
    public string Line { get; }
}

/// <summary>
/// <c>\n</c>까지를 한 요청으로 잘라 내는 필터입니다. 앞의 <c>\r</c>는 버립니다.
/// </summary>
public sealed class TextLineReceiveFilter : IReceiveFilter<TextLineRequestInfo>
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    public IReceiveFilter<TextLineRequestInfo>? NextReceiveFilter => null;

    public FilterState State { get; private set; }

    public TextLineRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        var reader = new SequenceReader<byte>(buffer);

        // 줄이 아직 다 오지 않았다. 살펴본 위치만 끝으로 옮겨 두면
        // 나머지가 도착할 때까지 파이프에 그대로 남는다.
        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, LineFeed, advancePastDelimiter: true))
        {
            consumed = buffer.Start;
            examined = buffer.End;
            return null;
        }

        consumed = reader.Position;
        examined = consumed;

        var bytes = line.ToArray();
        var length = bytes.Length;
        if (length > 0 && bytes[length - 1] == CarriageReturn)
            length--;

        return new TextLineRequestInfo(Encoding.UTF8.GetString(bytes, 0, length));
    }

    public void Reset()
    {
        State = FilterState.Normal;
    }
}
