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


## 연결과 수신 기동

1. `TcpAsyncSocketListener`의 accept 루프가 소켓을 받는다. `ServerConfig.AcceptLoopCount`
   (기본 1, 1~64)로 루프를 여러 개 돌릴 수 있고, `OnStopped`는 마지막 루프만 발생시킨다.
2. `AsyncSocketServer.ProcessNewClient()`가 **수신 proxy와 송신 SAEA를 각각 풀에서 꺼낸다.**
   둘 중 하나라도 없으면 접속을 거부하고 `NewSessionConnected`를 부르지 않는다.
   즉 `MaxConnectionNumber` 초과는 이벤트 없이 조용히 끊긴다.
3. `SocketSession.Initialize()`가 세션당 송신 큐와 수신 `Pipe`를 만든다.
   파이프의 백프레셔 임계값은 아래처럼 보정된다 — 큰 `MaxRequestLength`를 잡아 놓고
   파이프가 먼저 멈춰 요청이 영영 완성되지 못하는 상황을 막기 위해서다.

   ```
   pauseThreshold  = max(MaxReceivePipeBufferSize(기본 65536), ReceiveBufferSize * 2)
   MaxRequestLength > 0 이면
   pauseThreshold  = max(pauseThreshold, MaxRequestLength + ReceiveBufferSize * 2)
   resumeThreshold = pauseThreshold / 2
   ```
4. `AsyncSocketSession.Start()`가 `StartReceive()`와 `ProcessPipeAsync()` 태스크를 띄운다.
5. `StartReceive()`는 **while 루프**다. `_pipeWriter.GetMemory()`로 얻은 버퍼를
   `SetBuffer(Memory<byte>)`에 걸고 `ReceiveAsync()`를 건다. 동기 완료되면 재귀하지 않고
   루프를 한 번 더 돈다 — 루프백처럼 항상 데이터가 준비된 소켓에서 스택이 무한히 쌓이는 것을 막는다.
   `UseZeroByteReceive=true`면 유휴 세션은 길이 0 버퍼로 대기하다가, 읽을 것이 생긴 뒤에야
   실제 버퍼를 빌린다. 프로브인지 여부는 **게시한 버퍼 길이**로 판정하므로 스레드 간 공유 상태가 없다.
6. IOCP 완료 → `ProcessReceiveCore()`가 `Advance` → `FlushAsync` → 다시 `StartReceive()`.
   `ReceiveInlineOnIocpThread=true`(기본)면 이 전진을 IOCP 스레드에서 그대로 한다.
   **앱 핸들러까지 인라인으로 부르지는 않는다** — 그것은 `ProcessPipeAsync` 태스크의 몫이다.


## 송신 경로

```
AppSession.TrySend/Send → SocketSession.TrySend → ChannelSendingQueue.TryEnqueue
   → StartSend(true) → [InSending 플래그를 얻은 호출자만 진행]
   → DrainAvailable(_sendBatch, _pooledInFlight) → SendAsync/SendSync
   → 완료 → OnSendingCompleted → 큐가 비면 OnSendEnd, 남았으면 StartSend(false)
```

- **큐**: `ChannelSendingQueue`는 bounded `Channel<SendItem>` 하나다. lock이 없고,
  `Channel`의 원자적 write에 완료·용량 판정을 맡긴다. `SendItem`은 readonly struct라
  enqueue당 힙 할당이 없다. 용량은 세그먼트가 아니라 **전송 요청 수**를 센다 —
  다중 세그먼트 `Send` 하나가 슬롯 1개다.
- **single-flight**: 세션당 한 번에 하나의 송신만 진행한다. `InSending` 플래그를 얻은
  호출자만 실제로 보내고, 나머지는 큐에 넣고 돌아간다.
- **배치**: 드레인 대상 리스트(`_sendBatch`, `_pooledInFlight`)는 세션당 재사용한다.
  single-flight이라 이전 배치가 소켓에서 떨어진 뒤에만 다음 드레인이 채운다.
- **scatter-gather**: 세그먼트가 2개 이상이면 `SocketAsyncEventArgs.BufferList`로
  한 번에 보낸다. 복사해서 합치지 않는다.
- **부분 전송**: 보낸 바이트가 요청보다 적으면 `TrimSegments()`로 남은 구간만 잘라 재전송한다.
- **풀 버퍼 반납**: `TrySendCopied`/`SendCopied`가 빌린 ArrayPool 배열은
  배치가 끝나는 지점(`OnSendingCompleted` / `OnSendError`)에서 `ReturnPooledSendBuffers()`가
  돌려준다. 전송이 진행 중인 상태로 세션이 죽으면 반납하지 않고 GC에 맡긴다 —
  버퍼가 두 번 배포되는 것보다 낫기 때문이다.


## UDP 경로

TCP와 달리 파이프도, 연결도 없다. 데이터그램 하나가 곧 요청 하나다.

1. `UdpSocketListener`가 `ArrayPool`에서 빌린 버퍼로 `ReceiveFromAsync`를 건다.
   받은 것은 `UdpReceivePacket`에 담겨 `OnNewClientAccepted`로 넘어간다.
2. `UdpSocketServer`가 필터를 부른다. 요청 타입이 `UdpRequestInfo`를 상속하면
   그 안의 세션 ID로 세션을 찾고, 아니면 remote endpoint 문자열을 키로 쓴다.
   세션이 없으면 그 자리에서 만든다.
3. `_requestHandler.ExecuteCommand(...)`를 **동기로** 부른 뒤,
   `finally`에서 `UdpReceivePacket.Dispose()`가 버퍼를 풀에 돌려준다.
   즉 수신 버퍼 수명은 TCP와 같다 — 핸들러가 리턴하면 무효다.

주의할 점 둘:

- 세션 ID 파싱용 필터는 **수신 스레드당 하나를 재사용**한다(`[ThreadStatic]`, `Reset()` 후 재사용).
  데이터그램 간에 상태를 갖거나 `CreateFilter`에 넘어온 remote endpoint를 캡처하면 안 된다.
- `UdpSocketSession`은 리슨 소켓을 공유하므로 `Client`가 null이다.
  `TryValidateClosedBySocket`이 항상 false를 돌려주는 것도 그 때문이다.


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
필수 구현은 레벨 플래그 5개(`IsDebug/Info/Warn/Error/FatalEnabled`)와 메서드 7개
(평문 5개 + 예외를 받는 `Error`/`Fatal`)뿐이다. 나머지는 default 구현이 있다.

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

**먼저 정할 것**: 패킷을 `NewRequestReceived` 안에서 다 처리하는가, 아니면 로직 스레드로
넘기는가. 이 답이 요청 정보의 모양을 결정한다.

| 구조 | 요청 정보 | 기준 구현 |
|---|---|---|
| 핸들러에서 즉시 처리 | 인스턴스 재사용 + `Body`는 `ReadOnlySequence<byte>` (할당 0) | `Tutorials/EchoServer` |
| 로직 스레드로 전달 | `ArrayPool` 배열로 복사, 처리 후 한 곳에서 반납 | `Tutorials/PvPGameServer` |

아래는 앞쪽(즉시 처리)이다. 자세한 근거와 뒤쪽 패턴은 `Docs/GC_Copy_Minimization.md`.

```csharp
// 1. ReceiveFilter (고정 헤더 12바이트 예시)
public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    // 필터는 세션마다 하나이고 다음 패킷은 이전 핸들러가 리턴한 뒤에 파싱되므로,
    // 요청 인스턴스를 돌려 써도 된다. 대신 핸들러 밖으로 내보내면 안 된다.
    private readonly EFBinaryRequestInfo _reusable = new();

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

        _reusable.Set(
            BinaryPrimitives.ReadInt32LittleEndian(buf.Slice(0, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(6, 2)),
            body);                                // 복사하지 않고 파이프를 그대로 가리킨다

        return _reusable;
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

## 검토했지만 하지 않기로 한 최적화

성능 재점검(2026-08-14)에서 후보로 올랐다가 기각한 것들이다.
다시 제안되기 쉬운 항목이라 이유를 남긴다.

| 후보 | 기각 이유 |
|---|---|
| **세션/AppSession/Pipe 풀링(재사용)** | 최대 효과 후보지만 안정성 비용이 정확히 반대급부. 앱이 파생한 `AppSession` 서브클래스의 필드, 필터 인스턴스 상태, 이벤트 구독이 세션 간에 누출될 수 있고, 이는 라이브러리가 통제할 수 없는 앱 코드 계약 파괴다. |
| **PipeScheduler.Inline (reader 인라인 실행)** | `FlushAsync`가 IOCP 스레드에서 앱 요청 핸들러까지 실행하게 된다 → 수신 정지·데드락 위험. `ReceiveInlineOnIocpThread`가 "파이프 전진까지만 인라인"으로 선을 그은 설계 의도와 충돌한다. |
| **`ChannelSendingQueue`를 수제 lock-free 큐로 교체** | 세션당 큐라 경합이 거의 없고, 송신 1회의 syscall(µs) 대비 수십 ns 수준. `EnqueueAsync`(대기·취소·완료 의미) 재구현 위험 대비 이득이 없다. |
| **`SessionID`를 GUID 문자열이 아닌 값으로** | 접속당 할당 2회가 아깝지만 `SessionID: string`은 공개 계약이고 앱이 포맷에 의존할 수 있다. |
| **`_sessionDict` 비교자 `OrdinalIgnoreCase` → `Ordinal`** | 이득이 너무 작아 호환성 리스크를 정당화하지 못한다. |
| **송신 배치 코얼레싱** | scatter-gather(`BufferList`)가 이미 세그먼트 N개를 syscall 1회로 보낸다. 복사를 더하면 손해다. |
| **핫패스 공유 카운터 스트라이핑** | 실제로 구현해 측정했더니 처리량이 **일관되게 1.2% 떨어져** 철회했다. 이 부하에서는 캐시라인 경합이 거의 없어 `GetCurrentProcessorId` 비용만 남는다. |
| **UDP 경로 최적화** | 주 사용처가 TCP이고 UDP는 LoadTest 커버리지가 없다. 측정 없는 최적화는 하지 않는다는 원칙에 따라 보류. |
| **RIO / io_uring 등 커널 레벨 재작성** | 플랫폼 종속 대규모 재작성. 범용 .NET 소켓 서버라는 성격과 맞지 않는다. |

앱 경계(ReceiveFilter·핸들러·송신 호출부)에서 남은 할당을 없애는 방법은
`Docs/GC_Copy_Minimization.md`에 따로 정리되어 있다.
