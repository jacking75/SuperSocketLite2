using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite2.GameServerTemplate;

/// <summary>세션 하나. 세션별 상태는 여기에 둔다.</summary>
public sealed class NetworkSession : AppSession<NetworkSession, PacketRequestInfo>
{
    /// <summary>로그인한 유저 ID. 아직 로그인 전이면 null 이다.</summary>
    /// <remarks>
    /// 접속 핸들러와 요청 핸들러는 서로 다른 스레드에서 동시에 돌 수 있다.
    /// 이 템플릿은 <c>ServerConfig.SyncSessionConnectedEvent = true</c> 로 두어
    /// "접속 → 첫 요청" 순서를 구조적으로 보장한다.
    /// </remarks>
    public string? UserId { get; set; }
}

/// <summary>서버 본체.</summary>
public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>
{
    private readonly Dictionary<short, Action<NetworkSession, PacketRequestInfo>> _handlers = new();

    public MainServer()
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;

        RegisterHandlers();
    }

    /// <summary>패킷 ID와 핸들러를 이어 붙인다. 패킷을 추가하면 여기에 한 줄 넣는다.</summary>
    private void RegisterHandlers()
    {
        _handlers[(short)PacketId.ReqEcho] = PacketHandlers.HandleEcho;
    }

    private void OnConnected(NetworkSession session)
    {
        Logger.Info($"connected. session: {session.SessionID}, remote: {session.RemoteEndPoint}");
    }

    private void OnClosed(NetworkSession session, CloseReason reason)
    {
        Logger.Info($"closed. session: {session.SessionID}, reason: {reason}");
    }

    /// <summary>
    /// 요청 하나를 처리한다.
    /// </summary>
    /// <remarks>
    /// 이 메서드는 반드시 동기로 끝나야 한다. <c>async void</c> 를 쓰거나 <c>await</c> 하지 않는
    /// 비동기 호출을 넣으면 메서드가 먼저 리턴하고, 그 뒤에 <paramref name="request"/> 의 본문을
    /// 읽게 되어 데이터가 깨진다. 비동기 작업이 필요하면 핸들러 안에서 값을 복사해 넘긴다.
    /// </remarks>
    private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
    {
        if (!_handlers.TryGetValue(request.PacketId, out var handler))
        {
            Logger.Warn($"unknown packet id: {request.PacketId}, session: {session.SessionID}");
            return;
        }

        try
        {
            handler(session, request);
        }
        catch (Exception ex)
        {
            // 핸들러 예외가 수신 루프를 죽이지 않게 여기서 막는다.
            Logger.Error($"handler failed. packet id: {request.PacketId}, session: {session.SessionID}", ex);
            session.Close(CloseReason.ApplicationError);
        }
    }

    /// <summary>접속 중인 모든 세션에 보낸다.</summary>
    /// <remarks>
    /// 느린 클라이언트 하나가 <c>SendTimeOut</c> 만큼 전체 루프를 세우지 않도록
    /// <c>Send</c> 가 아니라 <c>TrySendCopied</c> 를 쓴다.
    /// </remarks>
    public void Broadcast(ReadOnlySpan<byte> packet, string? exceptSessionId = null)
    {
        // 서버가 아직 안 떴거나 내려가는 중이면 null 이 온다.
        var sessions = GetAllSessions();

        if (sessions is null)
        {
            return;
        }

        foreach (var session in sessions)
        {
            if (!session.Connected || session.SessionID == exceptSessionId)
            {
                continue;
            }

            session.TrySendCopied(packet);
        }
    }
}
