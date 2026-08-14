using System.Buffers;
using System.Text;
using SuperSocketLite.LoadTest.ServerProbe;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.LoadTest.Server;

/// <summary>
/// UDP 에코 서버입니다.
/// </summary>
/// <remarks>
/// 클라이언트의 <c>--transport udp</c>가 보내는 데이터그램은
/// 4바이트 키 + 36바이트 세션 GUID + 페이로드로 이루어집니다.
/// 앞의 40바이트는 라이브러리가 UDP 세션을 식별하는 데 쓰는 규약이라 그대로 따릅니다.
///
/// UDP는 연결이 없으므로 요청·응답을 짝지을 상관 ID 자리가 프로토콜에 없습니다.
/// 그래서 클라이언트는 UDP에서 닫힌 루프로 동작합니다.
/// </remarks>
public sealed class UdpEchoServer : AppServer<UdpEchoSession, UdpEchoRequestInfo>, IDisposable
{
    private ServerMetricsCollector? _metrics;
    private bool _stopped;

    public UdpEchoServer()
        : base(new DefaultReceiveFilterFactory<UdpEchoReceiveFilter, UdpEchoRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    /// <summary>TCP 서버와 같은 수집기를 공유합니다. 프로세스 자원은 하나이기 때문입니다.</summary>
    public bool Configure(LoadTestServerOptions options, ServerMetricsCollector? metrics)
    {
        _metrics = metrics;

        var config = new ServerConfig
        {
            Port = options.UdpPort,
            Ip = "Any",
            MaxConnectionNumber = options.MaxConnections,
            Mode = SocketMode.Udp,
            Name = "LoadTestUdpServer",
            MaxRequestLength = 60 * 1024
        };

        return Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());
    }

    private void OnConnected(UdpEchoSession session)
    {
        _metrics?.OnConnected(session.SessionID, session.RemoteEndPoint?.ToString());
    }

    private void OnClosed(UdpEchoSession session, CloseReason reason)
    {
        _metrics?.OnClosed(session.SessionID, reason.ToString(), session.RemoteEndPoint?.ToString());
    }

    private void OnRequestReceived(UdpEchoSession session, UdpEchoRequestInfo request)
    {
        var payloadLength = checked((int)request.Payload.Length);

        // 종료 중에 Dispose가 _metrics를 null로 만들 수 있으므로 한 번만 읽어서 쓴다.
        // 검사와 사용 사이에 다시 읽으면 그 틈에 null이 되어 NRE가 난다.
        var metrics = _metrics;

        // 계측기가 없어도(--metrics off) 에코는 그대로 한다.
        // default 레코더는 아무것도 기록하지 않으므로 null 검사를 여기 한 번만 둔다.
        using var recorder = metrics is null
            ? default
            : metrics.BeginRequest(session.SessionID, packetId: 0, bytesIn: payloadLength);

        // 클라이언트는 받은 바이트 수만 세므로 페이로드만 돌려주면 된다.
        // 데이터그램은 한 덩어리로 오므로 대개 여기서 끝난다.
        if (request.Payload.IsSingleSegment)
        {
            Send(session, request.Payload.FirstSpan, metrics);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(payloadLength);

        try
        {
            request.Payload.CopyTo(rented);
            Send(session, rented.AsSpan(0, payloadLength), metrics);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void Send(UdpEchoSession session, ReadOnlySpan<byte> payload, ServerMetricsCollector? metrics)
    {
        try
        {
            session.SendCopied(payload);
            metrics?.OnBytesOut(payload.Length);
        }
        catch (Exception ex)
        {
            metrics?.OnSendFailed(session.SessionID, payload.Length, ex.Message);
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

public sealed class UdpEchoSession : AppSession<UdpEchoSession, UdpEchoRequestInfo>
{
}

/// <summary>데이터그램 하나입니다.</summary>
/// <remarks>
/// <see cref="Payload"/>는 수신 버퍼를 그대로 가리키므로 핸들러가 리턴하면 무효가 됩니다.
/// 키와 세션 ID는 라이브러리가 UDP 세션을 문자열로 찾으므로 문자열로 남습니다.
/// </remarks>
public sealed class UdpEchoRequestInfo : UdpRequestInfo
{
    public UdpEchoRequestInfo(string key, string sessionId, ReadOnlySequence<byte> payload)
        : base(key, sessionId)
    {
        Payload = payload;
    }

    /// <summary>헤더를 뺀 페이로드입니다. 핸들러가 리턴하면 무효가 됩니다.</summary>
    public ReadOnlySequence<byte> Payload { get; }
}

/// <summary>
/// 데이터그램 하나가 곧 요청 하나입니다. TCP처럼 경계를 찾을 필요가 없습니다.
/// </summary>
public sealed class UdpEchoReceiveFilter : IReceiveFilter<UdpEchoRequestInfo>
{
    private const int KeyLength = 4;
    private const int SessionIdLength = 36;
    private const int HeaderLength = KeyLength + SessionIdLength;

    public IReceiveFilter<UdpEchoRequestInfo>? NextReceiveFilter => null;

    public FilterState State { get; private set; }

    public UdpEchoRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length <= HeaderLength)
            return null;

        consumed = buffer.End;
        examined = consumed;

        // 헤더만 스택으로 옮긴다. 데이터그램 전체를 배열로 펴지 않는다.
        Span<byte> header = stackalloc byte[HeaderLength];
        buffer.Slice(0, HeaderLength).CopyTo(header);

        var key = Encoding.ASCII.GetString(header.Slice(0, KeyLength));
        var sessionId = Encoding.ASCII.GetString(header.Slice(KeyLength, SessionIdLength));

        return new UdpEchoRequestInfo(key, sessionId, buffer.Slice(HeaderLength));
    }

    public void Reset()
    {
        State = FilterState.Normal;
    }
}
