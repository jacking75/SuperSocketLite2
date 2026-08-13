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

**ReuseLockBaseBuffer** (CollectSend)
`Config.CollectSendIntervalMillSec > 0`이면 활성화.
여러 Send 호출을 하나의 버퍼에 모아 한 번에 전송한다.
  

## 이벤트 스레드 모델

| 이벤트 | 호출 방식 |
|---|---|
| `NewSessionConnected` | `Task.Run()` — 비동기 (`SyncSessionConnectedEvent=true`면 동기) |
| `NewRequestReceived` | 파이프 리더 태스크에서 동기 호출 |
| `SessionClosed` | `Task.Run()` — 비동기 |
  

## 수신 필터 두 경로

| 인터페이스 | 경로 | 비고 |
|---|---|---|
| `ISequenceReceiveFilter<T>` | zero-copy. `ReadOnlySequence`를 그대로 파싱 | 기본 필터 전부 구현 |
| `IReceiveFilter<T>` | 세션 캐리 버퍼(`ArrayPool`)로 복사 후 `byte[]` 파싱 | fallback |

`RawDataReceived` 핸들러를 등록하면 zero-copy 경로가 꺼지고 항상 `byte[]` 경로를 탄다.
  

## 기본 구현 패턴

```csharp
// 1. ReceiveFilter (고정 헤더 12바이트 예시)
public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    public ReceiveFilter() : base(12) { }

    protected override int GetBodyLengthFromHeader(byte[] header, int offset, int length)
        => BitConverter.ToInt32(header, offset + 8);

    protected override EFBinaryRequestInfo ResolveRequestInfo(
        ArraySegment<byte> header, byte[] bodyBuffer, int offset, int length)
        => new EFBinaryRequestInfo(
            BitConverter.ToInt32(header.Array, 0),
            BitConverter.ToInt16(header.Array, 4),
            BitConverter.ToInt16(header.Array, 6),
            bodyBuffer.CloneRange(offset, length));
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
