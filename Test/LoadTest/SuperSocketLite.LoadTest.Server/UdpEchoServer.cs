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
        var payload = Encoding.UTF8.GetBytes(request.Value);

        // 계측기가 없어도(--metrics off) 에코는 그대로 한다.
        using var recorder = _metrics?.BeginRequest(session.SessionID, packetId: 0, bytesIn: payload.Length);

        // 클라이언트는 받은 바이트 수만 세므로 페이로드만 돌려주면 된다.
        try
        {
            session.Send(payload, 0, payload.Length);
            _metrics?.OnBytesOut(payload.Length);
        }
        catch (Exception ex)
        {
            _metrics?.OnSendFailed(session.SessionID, payload.Length, ex.Message);
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

public sealed class UdpEchoRequestInfo : UdpRequestInfo
{
    public UdpEchoRequestInfo(string key, string sessionId, string value)
        : base(key, sessionId)
    {
        Value = value;
    }

    public string Value { get; }
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

        var data = buffer.ToArray();
        var key = Encoding.ASCII.GetString(data, 0, KeyLength);
        var sessionId = Encoding.ASCII.GetString(data, KeyLength, SessionIdLength);
        var value = Encoding.UTF8.GetString(data, HeaderLength, data.Length - HeaderLength);

        return new UdpEchoRequestInfo(key, sessionId, value);
    }

    public void Reset()
    {
        State = FilterState.Normal;
    }
}
