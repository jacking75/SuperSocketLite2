using System.Buffers;
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
    /// <summary>줄 끝에 붙여 돌려보내는 종결자입니다.</summary>
    private static ReadOnlySpan<byte> Terminator => "\r\n"u8;

    /// <summary>스택에 담아 보낼 응답의 상한입니다. 이보다 크면 풀에서 빌립니다.</summary>
    private const int StackBufferSize = 512;

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
    public bool Configure(LoadTestServerOptions options, ServerMetricsCollector? metrics)
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
        // 종료 중에 Dispose가 _metrics를 null로 만들 수 있으므로 한 번만 읽어서 쓴다.
        // 검사와 사용 사이에 다시 읽으면 그 틈에 null이 되어 NRE가 난다.
        var metrics = _metrics;

        // 계측기가 없어도(--metrics off) 에코는 그대로 한다.
        // default 레코더는 아무것도 기록하지 않으므로 null 검사를 여기 한 번만 둔다.
        using var recorder = metrics is null
            ? default
            : metrics.BeginRequest(session.SessionID, packetId: 0, bytesIn: request.Line.Length);

        var lineLength = checked((int)request.Line.Length);
        var totalSize = lineLength + Terminator.Length;

        if (totalSize <= StackBufferSize)
        {
            Span<byte> buffer = stackalloc byte[StackBufferSize];
            Echo(session, request.Line, buffer.Slice(0, totalSize), metrics);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            Echo(session, request.Line, rented.AsSpan(0, totalSize), metrics);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// 받은 줄에 종결자를 붙여 돌려보냅니다. 클라이언트가 줄 단위로 응답을 읽기 때문입니다.
    /// 문자열을 거치지 않으므로 줄마다 생기는 할당이 없습니다.
    /// </summary>
    private static void Echo(TextLineSession session, ReadOnlySequence<byte> line, Span<byte> buffer, ServerMetricsCollector? metrics)
    {
        line.CopyTo(buffer);
        Terminator.CopyTo(buffer.Slice(buffer.Length - Terminator.Length));

        try
        {
            session.SendCopied(buffer);
            metrics?.OnBytesOut(buffer.Length);
        }
        catch (Exception ex)
        {
            metrics?.OnSendFailed(session.SessionID, buffer.Length, ex.Message);
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

/// <summary>텍스트 한 줄 요청입니다.</summary>
/// <remarks>
/// <see cref="Line"/>은 수신 파이프의 메모리를 그대로 가리키고 인스턴스도 필터가 돌려 쓰므로,
/// 핸들러가 리턴하면 둘 다 유효하지 않습니다. 자세한 내용은 <c>Docs/GC_Copy_Minimization.md</c>의 개선 1을 보세요.
/// </remarks>
public sealed class TextLineRequestInfo : IRequestInfo
{
    public string Key => "line";

    /// <summary>종결자를 뺀 한 줄입니다. 핸들러가 리턴하면 무효가 됩니다.</summary>
    public ReadOnlySequence<byte> Line { get; private set; }

    public void Set(ReadOnlySequence<byte> line)
    {
        Line = line;
    }
}

/// <summary>
/// <c>\n</c>까지를 한 요청으로 잘라 내는 필터입니다. 앞의 <c>\r</c>는 버립니다.
/// </summary>
public sealed class TextLineReceiveFilter : IReceiveFilter<TextLineRequestInfo>
{
    private const byte LineFeed = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    // 필터는 세션마다 하나이고 요청 처리는 동기로 끝나므로 인스턴스를 돌려 써도 된다.
    private readonly TextLineRequestInfo _reusable = new();

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

        // 줄을 배열로 펴지 않고 마지막 바이트만 들여다본다.
        if (line.Length > 0 && line.Slice(line.Length - 1).FirstSpan[0] == CarriageReturn)
            line = line.Slice(0, line.Length - 1);

        _reusable.Set(line);
        return _reusable;
    }

    public void Reset()
    {
        State = FilterState.Normal;
    }
}
