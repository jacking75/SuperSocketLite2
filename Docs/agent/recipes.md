# 레시피

복사해서 고쳐 쓰는 형태로 정리했다. 전부 `Docs/agent/cautions.md`의 규칙을 지킨 코드다.

| # | 레시피 |
|---|---|
| 1 | [최소 TCP 서버](#1-최소-tcp-서버) |
| 2 | [ReceiveFilter — 길이 프리픽스](#2-receivefilter--길이-프리픽스-가장-흔함) |
| 3 | [ReceiveFilter — 고정 길이](#3-receivefilter--고정-길이) |
| 4 | [ReceiveFilter — 직접 구현](#4-receivefilter--직접-구현) |
| 5 | [패킷 ID → 핸들러 디스패치](#5-패킷-id--핸들러-디스패치) |
| 6 | [브로드캐스트](#6-브로드캐스트) |
| 7 | [Generic Host + 실서비스 로거](#7-generic-host--실서비스-로거) |
| 8 | [MemoryPack 직렬화](#8-memorypack-직렬화) |
| 9 | [로직 스레드로 패킷 넘기기](#9-로직-스레드로-패킷-넘기기) |
| 10 | [UDP 서버](#10-udp-서버) |
| 11 | [우아한 종료](#11-우아한-종료) |

---

## 1. 최소 TCP 서버

파일 3개면 끝난다. 프로토콜은 `[2바이트 전체 길이][2바이트 패킷 ID][본문]`.

**`.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- 서버는 Server GC로 돌린다 -->
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SuperSocketLite2" Version="0.21.1" />
  </ItemGroup>
</Project>
```

**`Protocol.cs`**

```csharp
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace MyGameServer;

public sealed class PacketRequestInfo : IRequestInfo
{
    public const int HeaderSize = 4;   // [2] totalSize + [2] packetId

    public string Key => string.Empty;

    public short TotalSize { get; private set; }
    public short PacketId { get; private set; }

    /// <summary>헤더를 뺀 본문. 핸들러가 리턴하면 무효가 된다.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(short totalSize, short packetId, ReadOnlySequence<byte> body)
    {
        TotalSize = totalSize;
        PacketId = packetId;
        Body = body;
    }
}

public sealed class PacketReceiveFilter : FixedHeaderReceiveFilter<PacketRequestInfo>
{
    // 필터는 세션마다 하나이고, 다음 패킷 파싱은 이전 핸들러가 리턴한 뒤에 일어난다.
    // 그래서 인스턴스 하나를 돌려 써도 안전하다 = 패킷당 할당 0.
    private readonly PacketRequestInfo _reusable = new();

    public PacketReceiveFilter() : base(PacketRequestInfo.HeaderSize) { }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);                                   // .First.Span 금지 — 쪼개질 수 있다
        return BinaryPrimitives.ReadInt16LittleEndian(buffer) - PacketRequestInfo.HeaderSize;
    }

    protected override PacketRequestInfo ResolveRequestInfo(
        ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);

        _reusable.Set(
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2)),
            body);

        return _reusable;
    }
}
```

**`MainServer.cs`**

```csharp
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace MyGameServer;

public sealed class NetworkSession : AppSession<NetworkSession, PacketRequestInfo> { }

public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>
{
    public MainServer()
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    private void OnConnected(NetworkSession session)
    {
        Logger.Info($"connected: {session.SessionID}");
    }

    private void OnClosed(NetworkSession session, CloseReason reason)
    {
        Logger.Info($"closed: {session.SessionID}, {reason}");
    }

    private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
    {
        // 에코. Body는 이 메서드가 리턴하면 무효다.
        PacketWriter.Send(session, request.PacketId, request.Body);
    }
}
```

**`Program.cs`**

```csharp
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;

namespace MyGameServer;

internal static class Program
{
    private static async Task Main()
    {
        var config = new ServerConfig
        {
            Ip = "Any",
            Port = 32452,
            Mode = SocketMode.Tcp,
            Name = "MyGameServer",
            MaxConnectionNumber = 2000,
            MaxRequestLength = 8192,     // 기본 1024는 게임 패킷에 대개 모자란다
            NoDelay = true,              // 실시간이면 켠다
            SyncSessionConnectedEvent = true,   // "접속 → 첫 요청" 순서 보장
        };

        var server = new MainServer();

        if (!server.Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory()))
        {
            Console.Error.WriteLine("server setup failed");
            return;
        }

        if (!server.Start())
        {
            Console.Error.WriteLine("server start failed");
            return;
        }

        Console.WriteLine($"listening on {config.Port}. press Ctrl+C to stop.");

        var stopping = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.TrySetResult(); };
        await stopping.Task;

        await server.StopAsync(TimeSpan.FromSeconds(5));   // 큐에 남은 응답을 흘려보낸다
    }
}
```

**응답 쓰기 헬퍼** — 응답마다 배열을 새로 만들지 않는 형태다.

```csharp
using System.Buffers;
using System.Buffers.Binary;

namespace MyGameServer;

internal static class PacketWriter
{
    private const int StackBufferSize = 512;

    public static void Send(NetworkSession session, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            Write(packet, packetId, body);
            session.SendCopied(packet.Slice(0, totalSize));   // 스택 버퍼 → 반드시 SendCopied
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            Write(rented, packetId, body);
            session.SendCopied(rented.AsSpan(0, totalSize));  // 대여 버퍼 → 반드시 SendCopied
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void Write(Span<byte> destination, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), packetId);
        body.CopyTo(destination.Slice(PacketRequestInfo.HeaderSize));
    }
}
```

> `stackalloc`이나 `ArrayPool` 버퍼를 `Send`로 넘기면 **안 된다.** zero-copy라 전송 전에
> 버퍼가 회수/해제된다. 이 경우는 항상 `SendCopied`다.

---

## 2. ReceiveFilter — 길이 프리픽스 (가장 흔함)

레시피 1의 `PacketReceiveFilter`가 그대로 답이다. 변형만 정리한다.

**길이 필드가 본문 길이만 담을 때** (헤더 크기를 빼지 않는다)

```csharp
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buffer = stackalloc byte[HeaderSize];
    header.CopyTo(buffer);
    return BinaryPrimitives.ReadInt32LittleEndian(buffer);   // 그대로 본문 길이
}
```

**빅엔디안 프로토콜일 때**

```csharp
return BinaryPrimitives.ReadInt32BigEndian(buffer);
```

**길이 상한을 직접 걸 때** — 기본 구현은 `MaxRequestLength`를 본다. 더 좁히려면 오버라이드한다.

```csharp
protected override bool ValidateBodyLength(int bodyLength)
{
    if (bodyLength < 0 || bodyLength > 4096)
    {
        return false;    // FilterState.Error가 되고 세션이 닫힌다
    }

    return base.ValidateBodyLength(bodyLength);
}
```

---

## 3. ReceiveFilter — 고정 길이

모든 패킷이 같은 크기일 때. 구현할 게 하나뿐이다.

```csharp
using System.Buffers;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

public sealed class TelemetryFilter : FixedSizeReceiveFilter<TelemetryRequestInfo>
{
    private readonly TelemetryRequestInfo _reusable = new();

    public TelemetryFilter() : base(32) { }   // 패킷 하나가 항상 32바이트

    protected override TelemetryRequestInfo ProcessMatchedRequest(ReadOnlySequence<byte> buffer)
    {
        Span<byte> packet = stackalloc byte[32];
        buffer.CopyTo(packet);                 // 세그먼트에 걸칠 수 있다
        _reusable.Parse(packet);
        return _reusable;
    }
}
```

---

## 4. ReceiveFilter — 직접 구현

구분자 기반(예: `\r\n`)처럼 위 둘로 안 되는 프로토콜일 때만 쓴다.

```csharp
using System.Buffers;
using SuperSocketLite.SocketBase.Protocol;

public sealed class LineFilter : IReceiveFilter<LineRequestInfo>
{
    private readonly LineRequestInfo _reusable = new();

    public IReceiveFilter<LineRequestInfo>? NextReceiveFilter => null;
    public FilterState State { get; private set; }

    public LineRequestInfo? Filter(ReadOnlySequence<byte> buffer,
                                   out SequencePosition consumed, out SequencePosition examined)
    {
        // 아직 한 줄이 안 됐으면 consumed를 전진시키지 않는다.
        // 데이터는 파이프에 남고 다음 수신 때 이어서 온다 — 캐리 버퍼를 두면 안 된다.
        consumed = buffer.Start;
        examined = buffer.End;

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n'))
        {
            return null;
        }

        consumed = reader.Position;
        examined = consumed;

        _reusable.Set(line);
        return _reusable;
    }

    public void Reset() => State = FilterState.Normal;
}
```

`Filter`의 계약은 이렇다.

- 완성된 요청이 있으면 → 요청을 리턴하고 `consumed`를 그 요청 끝으로 옮긴다
- 아직 없으면 → `null`을 리턴하고 `consumed`는 `buffer.Start` 그대로, `examined`는 `buffer.End`
- 프로토콜이 깨졌으면 → `State = FilterState.Error`로 두면 세션이 닫힌다

---

## 5. 패킷 ID → 핸들러 디스패치

패킷이 늘어나면 `switch` 대신 맵으로 간다.

```csharp
public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>
{
    private readonly Dictionary<short, Action<NetworkSession, PacketRequestInfo>> _handlers = new();

    public MainServer()
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        NewRequestReceived += OnRequestReceived;
        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        _handlers[(short)PacketId.ReqLogin] = HandleLogin;
        _handlers[(short)PacketId.ReqChat] = HandleChat;
    }

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
            Logger.Error($"handler failed. id: {request.PacketId}", ex);
            session.Close(CloseReason.ApplicationError);
        }
    }
}
```

**핸들러는 동기로 끝내야 한다.** `async void`나 `await` 없는 `async` 호출을 넣으면
메서드가 먼저 리턴하고, 그 뒤에 `request.Body`를 읽게 되어 데이터가 깨진다.
비동기 작업이 필요하면 레시피 9처럼 값을 복사해서 넘긴다.

---

## 6. 브로드캐스트

```csharp
private void Broadcast(ReadOnlySpan<byte> packet, string? exceptSessionId = null)
{
    var sessions = GetAllSessions();

    if (sessions is null)      // 서버가 안 떴거나 내려가는 중이면 null이다
    {
        return;
    }

    foreach (var session in sessions)
    {
        if (!session.Connected || session.SessionID == exceptSessionId)
        {
            continue;
        }

        // 한 세션의 큐가 막혀도 나머지 브로드캐스트를 멈추지 않는다.
        session.TrySendCopied(packet);
    }
}
```

- 브로드캐스트에는 `Send`가 아니라 **`TrySendCopied`**를 쓴다. 느린 클라이언트 하나가
  `SendTimeOut`만큼 전체 루프를 세우는 걸 막는다.
- 방(room) 단위라면 `GetAllSessions()`를 매번 도는 대신 방이 자기 세션 목록을 들고 있게 한다.
  세션 수가 늘면 전체 순회가 병목이 된다.

---

## 7. Generic Host + 실서비스 로거

`MicrosoftLoggingLogFactory` 하나로 Serilog / NLog / ZLogger / log4net이 전부 붙는다.
**라이브러리별 어댑터 클래스를 새로 만들 필요가 없다.**

```csharp
var host = new HostBuilder()
    .ConfigureAppConfiguration((ctx, config) =>
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true))
    .ConfigureLogging((ctx, logging) =>
    {
        logging.ClearProviders();
        logging.AddZLoggerConsole();      // 또는 AddSerilog(), AddNLog() ...
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<ServerOption>(ctx.Configuration.GetSection("ServerOption"));
        services.AddHostedService<MainServer>();

        // 호스트의 ILoggerFactory를 SuperSocketLite의 ILogFactory로 넘겨 준다.
        services.AddSingleton<SuperSocketLite.SocketBase.Logging.ILogFactory>(
            sp => new SuperSocketLite.SocketBase.Logging.MicrosoftLoggingLogFactory(
                sp.GetRequiredService<ILoggerFactory>()));
    })
    .Build();

await host.RunAsync();
```

서버 쪽은 `IHostedService`를 같이 구현한다.

```csharp
public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>, IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ServerOption _option;
    private readonly SuperSocketLite.SocketBase.Logging.ILogFactory _logFactory;

    public MainServer(IHostApplicationLifetime lifetime,
                      IOptions<ServerOption> option,
                      SuperSocketLite.SocketBase.Logging.ILogFactory logFactory)
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        _lifetime = lifetime;
        _option = option.Value;
        _logFactory = logFactory;

        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = new ServerConfig { /* _option에서 채운다 */ };

        if (!Setup(new RootConfig(), config, logFactory: _logFactory) || !Start())
        {
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => base.StopAsync(TimeSpan.FromSeconds(5));
}
```

---

## 8. MemoryPack 직렬화

`Docs/agent/cautions.md` 4번의 "안전 A"에 해당한다. 핸들러 안에서 역직렬화해 값만 남긴다.

```csharp
using MemoryPack;

[MemoryPackable]
public partial class ReqLogin
{
    public string UserId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

private void HandleLogin(NetworkSession session, PacketRequestInfo request)
{
    // Body는 ReadOnlySequence<byte>라 MemoryPack에 그대로 넘어간다 — 중간 배열이 없다.
    var req = MemoryPackSerializer.Deserialize<ReqLogin>(request.Body);

    if (req is null)
    {
        session.Close(CloseReason.ProtocolError);
        return;
    }

    // req는 새로 만들어진 객체다. 핸들러 밖으로 넘겨도 안전하다.
    _loginQueue.Enqueue(new LoginWork(session.SessionID, req));
}
```

응답을 보낼 때는 헤더를 앞에 붙여야 하므로 풀 버퍼에 직접 쓴다.

```csharp
private static void SendPacket<T>(NetworkSession session, short packetId, T payload)
{
    var writer = new ArrayBufferWriter<byte>();   // IDisposable이 아니다 — using 붙이지 않는다
    MemoryPackSerializer.Serialize(writer, payload);

    var bodyLength = writer.WrittenCount;
    var totalSize = PacketRequestInfo.HeaderSize + bodyLength;
    var rented = ArrayPool<byte>.Shared.Rent(totalSize);

    try
    {
        BinaryPrimitives.WriteInt16LittleEndian(rented.AsSpan(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(rented.AsSpan(2, 2), packetId);
        writer.WrittenSpan.CopyTo(rented.AsSpan(PacketRequestInfo.HeaderSize));

        session.SendCopied(rented.AsSpan(0, totalSize));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(rented);
    }
}
```

동작하는 전체 예제는 `Template/GameServer_MemoryPack`에 있다.

---

## 9. 로직 스레드로 패킷 넘기기

게임 서버는 대개 네트워크 스레드와 로직 스레드를 분리한다.
이때는 **바이트를 복사해서 넘겨야 한다.** `RequestInfo`도 `Body`도 핸들러 밖에서는 무효다.

```csharp
public readonly record struct PacketWork(string SessionId, short PacketId, byte[] Buffer, int Length);

private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
{
    var length = checked((int)request.Body.Length);
    var rented = ArrayPool<byte>.Shared.Rent(length);

    request.Body.CopyTo(rented);   // 여기서 복사해야 한다 — 리턴하면 Body는 무효

    if (!_logicQueue.Writer.TryWrite(new PacketWork(session.SessionID, request.PacketId, rented, length)))
    {
        ArrayPool<byte>.Shared.Return(rented);   // 큐가 막히면 즉시 반납한다
        Logger.Warn($"logic queue full. dropped packet {request.PacketId}");
    }
}

// 로직 스레드
private async Task LogicLoopAsync(CancellationToken token)
{
    await foreach (var work in _logicQueue.Reader.ReadAllAsync(token))
    {
        try
        {
            Process(work);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(work.Buffer);   // 반납은 한 곳에서만
        }
    }
}
```

`ArrayPool`은 **빌린 곳과 반납하는 곳을 반드시 짝지어 둔다.** 큐에 못 넣은 경로에서
반납을 빠뜨리는 게 흔한 실수다. 실제 예제는 `Tutorials/PvPGameServer`.

---

## 10. UDP 서버

`Mode`만 바꾸면 나머지는 TCP와 같다.

```csharp
var config = new ServerConfig
{
    Ip = "Any",
    Port = 555,
    Mode = SocketMode.Udp,          // 이것만 다르다
    MaxConnectionNumber = 1000,
    Name = "UdpServer",
};
```

세션 매핑은 두 가지다.

1. **기본** — remote endpoint 하나당 세션 하나. 별도 작업이 없다.
2. **세션 ID 내장** — `RequestInfo`가 `UdpRequestInfo`를 상속하면, 페이로드에 실어 보낸
   sessionID로 세션을 찾는다. NAT가 바뀌어도 같은 논리 세션을 유지할 수 있다.

```csharp
public sealed class MyUdpRequestInfo : UdpRequestInfo
{
    public MyUdpRequestInfo(string key, string sessionId) : base(key, sessionId) { }
}
```

2번을 쓸 때 sessionID 파싱용 필터는 **수신 스레드당 하나가 재사용된다.**
데이터그램 간 상태를 갖지 말고, `CreateFilter`의 remote endpoint를 캡처하지 않는다.
전체 예제는 `Tutorials/SimpleUDPServer`.

---

## 11. 우아한 종료

```csharp
// 나쁘지 않지만 큐에 남은 응답이 버려진다
server.Stop();

// 권장 — 새 접속을 막고, 이미 큐에 있는 응답을 흘려보낸 뒤 닫는다
await server.StopAsync(TimeSpan.FromSeconds(5));
```

게임 서버라면 종료 공지 패킷을 보내고 잠시 기다린 뒤 `StopAsync`를 부른다.

```csharp
Broadcast(shutdownNotice);
await Task.Delay(TimeSpan.FromSeconds(1));
await server.StopAsync(TimeSpan.FromSeconds(5));
```
