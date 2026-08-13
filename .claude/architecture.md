# 아키텍처

## 계층 구조

```
[클라이언트 TCP 연결]
        ↓
[TcpAsyncSocketListener]       Accept 루프
        ↓
[AsyncSocketServer]            SocketAsyncEventArgs 풀 관리, 세션 생성
        ↓
[SocketSession]                상태 머신 (InSending / InReceiving / Closed)
        ↓                      수신은 System.IO.Pipelines
[AppSession<TSession, TReq>]   애플리케이션 레벨 세션
        ↓
[IReceiveFilter<TRequestInfo>] 바이트 스트림 → 패킷 파싱
        ↓
[AppServerBase.ExecuteCommand] NewRequestReceived 이벤트 발생
```
  

## 핵심 컴포넌트

**수신 파이프** (`SocketSession.cs`)
세션마다 `System.IO.Pipelines.Pipe` 1개를 둔다.
IOCP 완료 스레드는 `PipeWriter`만 전진시키고, `ProcessPipeAsync()` 태스크가 `PipeReader`에서
읽어 `AppSession.ProcessRequest(ReadOnlySequence<byte>)`로 넘긴다.
back-pressure 임계값은 `ServerConfig.MaxReceivePipeBufferSize`로 조정한다.

**SmartPool\<T\>** (`Common/SmartPool.cs`)
`ConcurrentStack` 기반 오브젝트 풀. `AsyncSocketServer`가 수신용
`SocketAsyncEventArgsProxy` 풀과 송신용 `SocketAsyncEventArgs` 풀 2개를 만든다.
`PreAllocateSAEA`(기본 true)면 시작 시 `MaxConnectionNumber`개를 전부 만들고,
false면 `MinPoolSize`에서 시작해 2배씩 증설한다.

**ChannelSendingQueue** (`Common/ChannelSendingQueue.cs`)
세션별 송신 큐. bounded `Channel<SendItem>` 기반이라 별도 락이 없다.
용량은 **송신 호출 수** 기준이며 다중 세그먼트 송신도 슬롯 1개를 쓴다.

**SocketState 비트 플래그** (`SocketSession.cs`)
```csharp
Normal      = 0x00
InSending   = 0x01
InReceiving = 0x02
InClosing   = 0x10
Closed      = 0x01000000
```
상태 전환은 `Interlocked.CompareExchange`로 원자적으로 처리된다.


## 이벤트 스레드 모델

| 이벤트 | 호출 방식 |
|---|---|
| `NewSessionConnected` | `Task.Run()` — 비동기 (`SyncSessionConnectedEvent=true`면 동기) |
| `NewRequestReceived` | 파이프 리더 태스크에서 동기 호출 |
| `SessionClosed` | `Task.Run()` — 비동기 |
  

## 로깅

라이브러리는 특정 로그 라이브러리에 의존하지 않고 자체 `ILog` / `ILogFactory` 추상화만 쓴다.

**연동 방법 (권장)** — `MicrosoftLoggingLogFactory`
Serilog·NLog·ZLogger·log4net 모두 `Microsoft.Extensions.Logging` 프로바이더를 제공하므로,
내장 브리지 하나로 전부 커버된다. 라이브러리별 어댑터를 직접 만들 필요가 없다.

```csharp
// 일반 설정
Setup(new RootConfig(), config, logFactory: new MicrosoftLoggingLogFactory(loggerFactory));

// GenericHost
services.AddSingleton<ILogFactory>(
    sp => new MicrosoftLoggingLogFactory(sp.GetRequiredService<ILoggerFactory>()));
```

**직접 어댑터를 만들 때**
필수 구현은 레벨 플래그 6개 + 평문 메서드 8개뿐이다. 나머지는 default 구현이 있다.

| 멤버 | 필수 | 비고 |
|---|---|---|
| `IsDebug/Info/Warn/Error/FatalEnabled` | O | |
| `IsTraceEnabled` | X | 기본 false — 옵트인하지 않으면 Trace를 안 보낸다 |
| `Debug/Info/Warn/Error/Fatal(string)` | O | |
| `Trace(string)` | X | 기본 Debug로 접힘 |
| `Error/Fatal(string, Exception)` | O | |
| `Trace/Debug/Info/Warn(string, Exception)` | X | 기본 텍스트로 합침 |
| `Log(LogEventLevel, in LogSessionContext, string, Exception?)` | X | **구조적 로깅을 원하면 재정의** |

**구조적 로깅** — `LogSessionContext`
세션 정보를 메시지 문자열에 이어붙이지 않고 별도로 전달한다.
`readonly struct`(참조 2개)라서 호출 지점에서 힙 할당이 없고 박싱도 없다.

```csharp
Logger.Log(LogEventLevel.Error, session.SessionLogContext, "Max request length exceeded");
```

`Log`를 재정의하지 않은 어댑터는 default 구현이 `[sessionId/endpoint] message` 형태로
합쳐 평문 메서드로 넘긴다. 어느 경로든 **개행이 들어가지 않는다** — 줄 단위 수집기에서
한 이벤트가 쪼개지지 않게 하기 위함이다.

> default 인터페이스 멤버는 **인터페이스 타입으로 호출할 때만** 보인다.
> 라이브러리는 항상 `ILog`로 들고 있어서 문제없지만, 테스트에서 구현 클래스 타입으로
> 직접 호출하면 컴파일되지 않는다.

**이름 충돌 회피**
`ILogProvider`(MEL의 `ILoggerProvider`와 구분), `LogEventLevel`(MEL의 `LogLevel`과 구분).


## 수신 필터

경로는 하나다. 필터는 `ReadOnlySequence<byte>`를 파이프에서 직접 받아 파싱하고,
아직 요청이 되지 않은 데이터는 **파이프에 그대로 둔다**(`consumed`를 전진시키지 않는다).
세션 캐리 버퍼도, 오프셋 산술도 없다.

```csharp
public interface IReceiveFilter<TRequestInfo>
    where TRequestInfo : IRequestInfo
{
    TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);
    IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; }
    void Reset();
    FilterState State { get; }
}
```

| 반환 | consumed | examined | 의미 |
|---|---|---|---|
| RequestInfo | 요청 끝 | consumed와 동일 | 요청 1개 완성. 남은 바이트로 루프가 계속 돈다 |
| null | `buffer.Start` | `buffer.End` | 데이터 부족. 파이프가 더 받을 때까지 그대로 둔다 |

`State`를 `FilterState.Error`로 두면 세션이 `CloseReason.ProtocolError`로 닫힌다.

`MaxRequestLength` 판정은 **미소비 길이**(`sequence.Slice(consumed).Length`)로 한다.
파이프에는 완결된 파이프라인 요청이 여러 개 들어 있을 수 있으므로 버퍼 전체 길이로 재면 안 된다.
  

## 기본 구현 패턴

```csharp
// 1. ReceiveFilter (고정 헤더 12바이트 예시)
public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    public ReceiveFilter() : base(12) { }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buf = stackalloc byte[12];
        header.CopyTo(buf);                       // 세그먼트 경계에 걸려도 안전하다
        return BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(8, 4));
    }

    protected override EFBinaryRequestInfo ResolveRequestInfo(
        ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buf = stackalloc byte[12];
        header.CopyTo(buf);

        return new EFBinaryRequestInfo(
            BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(0, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(6, 2)),
            body.ToArray());
    }
}

// 2. AppServer
public class MainServer : AppServer<NetworkSession, EFBinaryRequestInfo>
{
    public MainServer()
        : base(new DefaultReceiveFilterFactory<ReceiveFilter, EFBinaryRequestInfo>()) { }

    protected override void OnStarted()
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnPacketReceived;
    }
}
```
