using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading;

using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketBase.Config;


namespace EchoServer;

/// <summary>
/// 메인 서버 클래스입니다.
/// </summary>
class MainServer : AppServer<NetworkSession, EFBinaryRequestInfo>
{
    /// <summary>
    /// 메인 로거 인스턴스입니다.
    /// </summary>
    public static ILog s_MainLogger;

    private IServerConfig _config;
    private bool _isRun = false;
    private Thread _threadCount;

    /// <summary>
    /// MainServer 클래스의 새 인스턴스를 초기화합니다.
    /// </summary>
    public MainServer()
        : base(new DefaultReceiveFilterFactory<ReceiveFilter, EFBinaryRequestInfo>())
    {
        NewSessionConnected += new SessionHandler<NetworkSession>(OnConnected);
        SessionClosed += new SessionHandler<NetworkSession, CloseReason>(OnClosed);
        NewRequestReceived += new RequestHandler<NetworkSession, EFBinaryRequestInfo>(RequestReceived);
    }

    /// <summary>
    /// 핸들러를 등록합니다.
    /// </summary>
    private void RegistHandler()
    {
        // 에코 서버라서 핸들러 등록은 하지 않음
        
        s_MainLogger.Info("핸들러 등록 완료");
    }

    /// <summary>
    /// 서버 설정을 초기화합니다.
    /// </summary>
    /// <param name="option">서버 옵션</param>
    public void InitConfig(ServerOption option)
    {
        _config = new ServerConfig
        {
            Port = option.Port,
            Ip = "Any",
            MaxConnectionNumber = option.MaxConnectionNumber,
            Mode = SocketMode.Tcp,
            Name = option.Name
        };
    }

    /// <summary>
    /// 서버를 생성합니다.
    /// </summary>
    public void CreateServer()
    {
        try
        {
            bool isResult = Setup(new RootConfig(), _config, logFactory: new ConsoleLogFactory());

            if (isResult == false)
            {
                Console.WriteLine("[ERROR] 서버 네트워크 설정 실패 ㅠㅠ");
                return;
            }

            s_MainLogger = base.Logger;

            RegistHandler();

            _isRun = true;
            _threadCount = new Thread(EchoCounter);
            _threadCount.Start();

            s_MainLogger.Info($"[{DateTime.Now}] 서버 생성 성공");
        }
        catch (Exception ex)
        {
            s_MainLogger.Error($"서버 생성 실패: {ex.ToString()}");
        }
    }

    /// <summary>
    /// 서버를 종료합니다.
    /// </summary>
    public void Destory()
    {
        base.Stop();

        _isRun = false;
        _threadCount.Join();
    }

    private Int64 Count = 0;

    /// <summary>
    /// Echo 카운터를 실행합니다.
    /// </summary>
    private void EchoCounter()
    {
        while (_isRun)
        {
            Thread.Sleep(1000);

            var value = Interlocked.Exchange(ref Count, 0);
            //Console.WriteLine($"{DateTime.Now} : {value}");
        }
    }

    /// <summary>
    /// 서버가 실행 중인지 확인합니다.
    /// </summary>
    /// <param name="curState">현재 서버 상태</param>
    /// <returns>서버가 실행 중인지 여부</returns>
    public bool IsRunning(ServerState curState)
    {
        if (curState == ServerState.Running)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 클라이언트가 접속했을 때 호출됩니다.
    /// </summary>
    /// <param name="session">접속한 세션</param>
    private void OnConnected(NetworkSession session)
    {
        s_MainLogger.Debug($"[{DateTime.Now}] 세션 번호 {session.SessionID} 접속 start, ThreadId: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

        //Thread.Sleep(3000);
        //MainLogger.Info($"세션 번호 {session.SessionID} 접속 end, ThreadId: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
    }

    /// <summary>
    /// 클라이언트가 접속을 해제했을 때 호출됩니다.
    /// </summary>
    /// <param name="session">해제된 세션</param>
    /// <param name="reason">해제 사유</param>
    private void OnClosed(NetworkSession session, CloseReason reason)
    {
        s_MainLogger.Info($"[{DateTime.Now}] 세션 번호 {session.SessionID},  접속해제: {reason.ToString()}");
    }

    /// <summary>
    /// 클라이언트로부터 요청을 받았을 때 호출됩니다.
    /// </summary>
    /// <param name="session">요청을 받은 세션</param>
    /// <param name="reqInfo">받은 요청 정보</param>
    private void RequestReceived(NetworkSession session, EFBinaryRequestInfo reqInfo)
    {
        s_MainLogger.Debug($"[{DateTime.Now}] 세션 번호 {session.SessionID},  받은 데이터 크기: {reqInfo.Body.Length}, ThreadId: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

        Interlocked.Increment(ref Count);

        EchoPacket(session, reqInfo);
    }

    /// <summary>스택에 담아 보낼 응답의 상한입니다. 이보다 크면 풀에서 빌립니다.</summary>
    private const int StackBufferSize = 512;

    /// <summary>
    /// 받은 패킷을 그대로 돌려보냅니다.
    /// </summary>
    /// <remarks>
    /// 응답 버퍼를 스택이나 <see cref="ArrayPool{T}"/>에서 마련하므로 응답마다 배열을 새로 만들지
    /// 않습니다. <c>SendCopied</c>는 라이브러리 풀 버퍼로 복사해 큐에 넣고 전송이 끝나면 그 버퍼를
    /// 스스로 반납하므로, 이 메서드가 리턴하는 즉시 여기 버퍼를 다시 써도 됩니다.
    /// 큐가 <c>SendTimeOut</c>동안 계속 가득 차 있으면 <c>Send</c>와 똑같이 예외를 던집니다.
    /// </remarks>
    private static void EchoPacket(NetworkSession session, EFBinaryRequestInfo reqInfo)
    {
        var totalSize = EFBinaryRequestInfo.HeaderSize + checked((int)reqInfo.Body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            session.SendCopied(packet.Slice(0, WritePacket(packet, reqInfo.PacketID, reqInfo.Body)));
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            session.SendCopied(rented.AsSpan(0, WritePacket(rented, reqInfo.PacketID, reqInfo.Body)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>헤더와 본문을 <paramref name="destination"/>에 써 넣고 쓴 바이트 수를 돌려줍니다.</summary>
    private static int WritePacket(Span<byte> destination, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = EFBinaryRequestInfo.HeaderSize + checked((int)body.Length);

        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), packetId);
        destination[4] = 0;
        body.CopyTo(destination.Slice(EFBinaryRequestInfo.HeaderSize));

        return totalSize;
    }
}


/// <summary>
/// 네트워크 세션 클래스입니다.
/// </summary>
public class NetworkSession : AppSession<NetworkSession, EFBinaryRequestInfo>
{
}
