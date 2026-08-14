# Tutorial
여기에는 있는 서버를 순서대로 만들어 보면서 SuperSocketLite 사용 방법을 배운다.  
대부분의 서버 프로젝트는 빌드하면 `Tutorials/00_server_bins` 디렉토리에 출력한다. 다만 예외가 있다.  
- `BinaryPacketServer`, `SimpleUDPServer`, `sendFailTestServer`는 `<OutputPath>`를 지정하지 않아서 기본 경로(`<프로젝트>/bin/<Configuration>/net10.0/`)로 나간다.  
- `GateServer_GameServer/GateServer`는 자기 상위인 `Tutorials/GateServer_GameServer/00_server_bins`로 나간다.  
- 클라이언트는 따로다. `ChatClient`는 `Tutorials/00_client_bin`, `EchoClient`와 `PvPGameServer_Client`는 `Tutorials/bin`으로 나간다.  
    
  
## 중요
- 네트워크 이벤트 중 동일 세션이라도 `NewSessionConnected` 와 `NewRequestReceived` 다른 스레드에서 동시에 발생할 수 있다. 즉 클라이언트에서 접속하자말자 바로 패킷을 보내면 `NewSessionConnected`을 처리하는 중에 `NewRequestReceived`이 호출될 수 있다.
  
  
  
## EchoServer
![EchoServer](./01_images/001.png)      
  
- 가장 간단한 서버이다.
- 클라이언트가 보낸 것을 그대로 클라이언트에게 보낸다.
- 간단하게 SuperSocketLite를 애플리케이션에서 어떻게 사용하는지 배운다.  
- SuperSocketLite 라이브러리 프로젝트를 참조하고 있다.
  
- 빌드 후 run_EchoServer.bat 배치 파일로 실행한다.    
- 클라이언트는 EchoClient 프로젝틀 사용한다.  
  
   
## EchoServerEx
![EchoServerEx](./01_images/002.png)        
  
- [유튜브 영상](https://youtu.be/ZgzMuHE43hU )
- EchoServer를 좀 더 고도화 한 것이다.
- 서버 옵션을 프로그램 실행 시 인자로 받는다.
- NLog를 사용한다.
- SuperSocketLite 프로젝트를 참조한다.
  
### 프로젝트에 추가할 것 
- SuperSocketLite 프로젝트를 참조에 추가한다.
- Nuget 추가 
    - CommandLineParser
	- NLog.Extensions.Logging
      
- 빌드 후 run_EchoServerEx.bat 배치 파일로 실행한다. 
- 클라이언트는 EchoClient 프로젝틀 사용한다.    
  
  
  
## EchoServer_GenericHost
![EchoServerGenericHost](./01_images/003.png)          
  
[Generic Host(일반 호스트) 소개 및 사용](https://jacking75.github.io/NET_GenericHost/)  | [MS Docs](https://learn.microsoft.com/ko-kr/dotnet/core/extensions/generic-host?tabs=appbuilder)   
    
- `EchoServer`에 `GenericHost` 기능을 사용하여 프로그램화 한 것이다.  
- 빌드 후 EchoServer_GenericHost.bat 배치 파일로 실행한다. 
- 클라이언트는 EchoClient 프로젝틀 사용한다.    
```
class Program
{
    static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;
                //config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddConsole();
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<ServerOption>(hostContext.Configuration.GetSection("ServerOption"));
                services.AddHostedService<MainServer>();
            })
            .Build();

        await host.RunAsync();
    }
}
```    
  
```
namespace EchoServer_GenericHost
{
    class MainServer : AppServer<NetworkSession, EFBinaryRequestInfo>, IHostedService
    {
        ...

        public MainServer(IHostApplicationLifetime appLifetime, IOptions<ServerOption> serverConfig, ILogger<MainServer> logger)
            : base(new DefaultReceiveFilterFactory<ReceiveFilter, EFBinaryRequestInfo>())
        {
            
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            AppLifetime.ApplicationStarted.Register(AppOnStarted);
            AppLifetime.ApplicationStarted.Register(AppOnStopped);
                        
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void AppOnStarted()
        {
            AppLogger.LogInformation("OnStarted");
            
        }
```  
    
      
  
## MultiPortServer  
![MultiPortServer](./01_images/004.png)          
  
- 서버가 복수의 port 번호를 사용하는 경우에 대한 예제이다.
- 이런 방식이 사용되는 경우는 이 서버에 내부 서버와 외부 클라이언트가 접속하는 경우 보안 상의 이유 등으로 port 1은 내부 서버에서만 접속하고, port 2는 외부 클라이언트만 접속하도록 할 때 이렇게 사용한다.  

```
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello SuperSocketLite");

        var serverOption = ParseCommandLine(args);
        if (serverOption == null)
        {
            return;
        }

        
        var server1 = new MainServer();
        server1.InitConfig(serverOption.Name1, serverOption.Port1, serverOption.MaxConnectionNumber1);
        server1.CreateServer();

        var IsResult = server1.Start();

        if (IsResult)
        {
            MainServer.MainLogger.Info("C2S 서버 네트워크 시작");
        }
        else
        {
            Console.WriteLine("[ERROR] C2S 서버 네트워크 시작 실패");
            return;
        }


        var server2 = new MainServer();
        server2.InitConfig(serverOption.Name2, serverOption.Port2, serverOption.MaxConnectionNumber2);
        server2.CreateServer();

        IsResult = server2.Start();

        if (IsResult)
        {
            MainServer.MainLogger.Info("S2S 서버 네트워크 시작");
        }
        else
        {
            Console.WriteLine("[ERROR] S2S 서버 네트워크 시작 실패");
            return;
        }


        MainServer.MainLogger.Info("key를 누르면 종료한다....");
        Console.ReadKey();
    }  
```  
   
- 빌드 후 run_MultiPortServer.bat 배치 파일로 실행한다. 
   
   
  
## sendFailTestServer 
![sendFailTestServer](./01_images/005.png)          

- 서버는 연결된 클라이언트에 패킷을 보내었는데 클라이언트가 계속 받지 않아서 send가 타임 아웃이 발생한 경우를 테스트한 것이다  
  
```
class Program
{
    static void Main(string[] args)
    {
        ....

        var server = new MainServer();
        server.InitConfig(serverOption);
        server.CreateServer();

        var IsResult = server.Start();
        ...

        var timer = new Timer(64);

        timer.Elapsed += (s, e) =>
        {
            var packet = TempPacket();

            foreach (var session in server.GetAllSessions())
            {
                try
                {
                    session.Send(packet);
                }
                catch (TimeoutException ex)
                {
                    MainServer.MainLogger.Error($"{ex.ToString()},  {ex.StackTrace}");

                    // TimeoutException 발생 후 세션을 짜르고 싶으면 꼭 SendEndWhenSendingTimeOut()를 호출해야 한다.
                    session.SendEndWhenSendingTimeOut(); 
                    session.Close();
                    break;
                }
                catch (Exception ex)
                {
                    MainServer.MainLogger.Error($"{ex.ToString()},  {ex.StackTrace}");
                }
            }
        };

        ...
    }
```    
  
  
## SimpleUDPServer  
![SimpleUDPServer](./01_images/007.png)    
  
- UDP 통신을 하는 간단한 예제 코드
   
      
## BinaryPacketServer
![BinaryPacketServer](./01_images/009.png)     
  
- `ReceiveFilter`를 바이너리 포맷의 패킷으로 주고 받을 때의 예제 코드  
```
public class NetworkSession : AppSession<NetworkSession, EFBinaryRequestInfo>
{
}

void RequestReceived(NetworkSession session, EFBinaryRequestInfo reqInfo)
{
    DevLog.Write(string.Format("세션 번호 {0} 받은 데이터 크기: {1}, ThreadId: {2}", session.SessionID, reqInfo.Body.Length, System.Threading.Thread.CurrentThread.ManagedThreadId), LOG_LEVEL.INFO);
    
    var PacketID = reqInfo.PacketID;
    var value1 = reqInfo.Value1;
    var value2 = reqInfo.Value2;

    if (_handlerMap.ContainsKey(PacketID))
    {
        _handlerMap[PacketID](session, reqInfo);
    }
    else
    {
        DevLog.Write(string.Format("세션 번호 {0} 받은 데이터 크기: {1}", session.SessionID, reqInfo.Body.Length), LOG_LEVEL.INFO);
    }
}
```  
  
필터는 수신 파이프의 `ReadOnlySequence<byte>`를 직접 파싱한다. 본문을 배열로 복사하지 않고
그대로 가리키고, 요청 인스턴스도 세션마다 하나를 돌려 쓰므로 **패킷을 받는 데 드는 할당이 없다**.
대신 핸들러가 리턴하면 요청과 본문은 모두 무효가 된다.
자세한 근거와 다른 방식(패킷을 로직 스레드로 넘길 때)은 [`Docs/GC_Copy_Minimization.md`](../Docs/GC_Copy_Minimization.md)를 보라.

```csharp
public class EFBinaryRequestInfo : IRequestInfo
{
    public int PacketID { get; private set; }
    public short Value1 { get; private set; }
    public short Value2 { get; private set; }

    public string Key => string.Empty;

    /// 헤더를 뺀 본문. 핸들러가 리턴하면 무효가 된다.
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(int packetID, short value1, short value2, ReadOnlySequence<byte> body)
    {
        PacketID = packetID;
        Value1 = value1;
        Value2 = value2;
        Body = body;
    }
}

public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    private const int FrameHeaderSize = 12;

    // 필터는 세션마다 하나이고 요청 처리는 동기로 끝나므로 인스턴스를 돌려 써도 된다.
    private readonly EFBinaryRequestInfo _reusable = new();

    public ReceiveFilter()
        : base(FrameHeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[FrameHeaderSize];
        header.CopyTo(headerBuffer);
        return BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.Slice(8, 4));
    }

    protected override EFBinaryRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[FrameHeaderSize];
        header.CopyTo(headerBuffer);

        _reusable.Set(
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.Slice(0, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(6, 2)),
            body);

        return _reusable;
    }
}
```  
  


## ChatServer
![ChatServer](./01_images/010.png)  
  
- [유튜브 영상](https://youtu.be/eiwvQ8NV2h8 )
- 채팅 서버
- 패킷 처리는 1개의 스레드만으로 처리한다.

- 클라이언트는 `ChatClient` 이다. WPF로 만들었다
    
    

## ChatServerEx  
- 채팅 서버
- ChatServer와 달리 패킷 처리를 멀티스레드로 한다는 것만 빼고는 같음
    - 각 스레드 별로 접근할 수 있는 Room 객체를 할당한다
  
```
public PacketDistributor GetPacketDistributor() { return Distributor; }
        
void OnConnected(ClientSession session)
{
    //옵션의 최대 연결 수를 넘으면 SuperSocket이 바로 접속을 짤라버린다. 즉 이 OnConneted 함수가 호출되지 않는다

    session.AllocSessionIndex();
    s_MainLogger.Info(string.Format("세션 번호 {0} 접속", session.SessionID));
                
    var packet = ServerPacketData.MakeNTFInConnectOrDisConnectClientPacket(true, session.SessionID, session.SessionIndex);            
    Distributor.DistributeCommon(false, packet);
}

void OnClosed(ClientSession session, CloseReason reason)
{
    s_MainLogger.Info(string.Format("세션 번호 {0} 접속해제: {1}", session.SessionID, reason.ToString()));


    var packet = ServerPacketData.MakeNTFInConnectOrDisConnectClientPacket(false, session.SessionID, session.SessionIndex);
    Distributor.DistributeCommon(false, packet);

    session.FreeSessionIndex(session.SessionIndex);
}

void OnPacketReceived(ClientSession session, EFBinaryRequestInfo reqInfo)
{
    s_MainLogger.Debug(string.Format("세션 번호 {0} 받은 데이터 크기: {1}, ThreadId: {2}", session.SessionID, reqInfo.Body.Length, System.Threading.Thread.CurrentThread.ManagedThreadId));

    var packet = new ServerPacketData();
    packet.SessionID = session.SessionID;
    packet.SessionIndex = session.SessionIndex;
    packet.PacketSize = reqInfo.Size;            
    packet.PacketID = reqInfo.PacketID;
    packet.Type = reqInfo.Type;
    packet.BodyData = reqInfo.Body;
            
    Distributor.Distribute(packet);
}
```  
  
```
public class PacketDistributor
{
    ConnectSessionManager SessionManager = new ConnectSessionManager();
    PacketProcessor CommonPacketProcessor = null;
    List<PacketProcessor> PacketProcessorList = new List<PacketProcessor>();

    DBProcessor DBWorker = new DBProcessor();

    RoomManager RoomMgr = new RoomManager();


    public ErrorCode Create(MainServer mainServer)
    {
        var roomThreadCount = MainServer.s_ServerOption.RoomThreadCount;
        
        Room.NetSendFunc = mainServer.SendData;

        SessionManager.CreateSession(ClientSession.s_MaxSessionCount);

        RoomMgr.CreateRooms();

        CommonPacketProcessor = new PacketProcessor();
        CommonPacketProcessor.CreateAndStart(true, null, mainServer, SessionManager);
                    
        for (int i = 0; i < roomThreadCount; ++i)
        {
            var packetProcess = new PacketProcessor();
            packetProcess.CreateAndStart(false, RoomMgr.GetRoomList(i), mainServer, SessionManager);
            PacketProcessorList.Add(packetProcess);
        }

        DBWorker.MainLogger = MainServer.s_MainLogger;
        var error = DBWorker.CreateAndStart(MainServer.s_ServerOption.DBWorkerThreadCount, DistributeDBJobResult, MainServer.s_ServerOption.RedisAddres);
        if (error != ErrorCode.None)
        {
            return error;
        }

        return ErrorCode.None;
    }

    public void Destory()
    {
        DBWorker.Destory();

        CommonPacketProcessor.Destory();

        PacketProcessorList.ForEach(preocess => preocess.Destory());
        PacketProcessorList.Clear();
    }

    public void Distribute(ServerPacketData requestPacket)
    {
        var packetId = (PacketId)requestPacket.PacketID;
        var sessionIndex = requestPacket.SessionIndex;
                    
        if(IsClientRequestPacket(packetId) == false)
        {
            MainServer.s_MainLogger.Debug("[Distribute] - 클라리언트의 요청 패킷이 아니다.");
            return; 
        }

        if(IsClientRequestCommonPacket(packetId))
        {
            DistributeCommon(true, requestPacket);
            return;
        }


        var roomNumber = SessionManager.GetRoomNumber(sessionIndex);
        if(DistributeRoomProcessor(true, false, roomNumber, requestPacket) == false)
        {
            return;
        }            
    }

    public void DistributeCommon(bool isClientPacket, ServerPacketData requestPacket)
    {
        CommonPacketProcessor.InsertMsg(isClientPacket, requestPacket);
    }

    public bool DistributeRoomProcessor(bool isClientPacket, bool isPreRoomEnter, int roomNumber, ServerPacketData requestPacket)
    {
        var sessionIndex = requestPacket.SessionIndex;
        var processor = PacketProcessorList.Find(x => x.관리중인_Room(roomNumber));
        if (processor != null)
        {
            if (isPreRoomEnter == false && SessionManager.IsStateRoom(sessionIndex) == false)
            {
                MainServer.s_MainLogger.Debug("[DistributeRoomProcessor] - 방에 입장하지 않은 유저 - 1");
                return false;
            }

            processor.InsertMsg(isClientPacket, requestPacket);
            return true;
        }

        MainServer.s_MainLogger.Debug("[DistributeRoomProcessor] - 방에 입장하지 않은 유저 - 2");
        return false;
    }


    public void DistributeDBJobRequest(DBQueue dbQueue)
    {
        DBWorker.InsertMsg(dbQueue);
    }

    public void DistributeDBJobResult(DBResultQueue resultData)
    {
        var sessionIndex = resultData.SessionIndex;

        var requestPacket = new ServerPacketData();
        requestPacket.Assign(resultData);

        DistributeCommon(false, requestPacket);            
    }

    bool IsClientRequestCommonPacket(PacketId packetId )
    {
        if ( packetId == PacketId.ReqLogin || packetId == PacketId.ReqRoomEnter)
        {
            return true;
        }

        return false;
    }

    bool IsClientRequestPacket(PacketId packetId)
    {
        return (PacketId.CsBegin < packetId && packetId < PacketId.CsEnd);
     }
}
```   
       
- 클라이언트는 `ChatClient` 이다. WPF로 만들었다



## GateServer_GameServer 
![GateServer_GameServer](./01_images/011.png)    
    
- 분산 서버 아키텍처에서 `클라이언트` 와 `백엔드서버(예 게임서버)` 간의 통신을 담당하는 서버이다  
    - 클라이언트는 GateServer에 접속하고 요청을 보낸다
    - GateServer는 클라이언트 접속을 관리하고, 클라이언트의 요청을 적절한 게임서버에 보낸다
    - 게임서버는 직접 클라이언트와 패킷을 주고 받지 않고, 모두 GateServer를 통해서 한다
- GateServer 서버와 연결하는 게임서버는 만들어지지 않았다
  


## PvPGameServer
![PvPGameServer](./01_images/012.png)    
  
- PvP 게임서버를 목적으로 만든 것이다. 그러나 미완성 상태
- Generic Host 구조 사용
- 패킷 데이터 직렬화 라이브러리로 `MemoryPack` 사용  
    
- 클라이언트는 `PvPGameServer_Client` 이다
  


## GameServer_MoDedicated
![GameServer_MoDedicated](./01_images/014.png)  
    
- 게임 서버에서 게임 로직을 주도적으로 처리하는 게임서버이다  
- 게임서버가 게임로직을 업데이트 주기에 맞추어서 실행하는 경우를 가정한 서버이다.  
- async/await 방식으로 스레드를 동작시킨다  
- 일반적인 스레드풀 방식보다 CPU를 덜 사용. 50% 정도
- 간격을 짧게 주면 맞지 않음.간격을 크게 주면 거의 맞음
     


## GameServer_MoDedicated2 
![GameServer_MoDedicated2](./01_images/014.png)  
  
- `GameServer_MoDedicated` 와 구조적으로 같은 것이다
- 게임로직 업데이트를 직접 스레드를 만들어서 실행한다
   
  
  
