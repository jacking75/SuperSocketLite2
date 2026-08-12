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
        ↓
[AppSession<TSession, TReq>]   애플리케이션 레벨 세션
        ↓
[IReceiveFilter<TRequestInfo>] 바이트 스트림 → 패킷 파싱
        ↓
[AppServerBase.ExecuteCommand] NewRequestReceived 이벤트 발생
```
  

## 핵심 컴포넌트

**BufferManager** (`Common/BufferManager.cs`)
연결 수 × 수신 버퍼 크기만큼 메모리를 미리 할당(pinned).
SocketAsyncEventArgs에 버퍼 슬롯을 배정한다.

**SmartPool<T>** (`Common/SmartPool.cs`)
SendingQueue 오브젝트 풀. ConcurrentStack 기반으로 메모리 할당을 최소화한다.

**SocketState 비트 플래그** (`SocketSession.cs`)
```csharp
Normal      = 0x00
InSending   = 0x01
InReceiving = 0x02
InClosing   = 0x10
Closed      = 0x01000000
```
상태 전환은 `Interlocked.CompareExchange`로 원자적으로 처리된다.

**SendingQueue**
세션별 송신 큐. TrackID로 ABA 문제를 방지한다.

**ReuseLockBaseBuffer** (CollectSend)
`Config.CollectSendIntervalMillSec > 0`이면 활성화.
여러 Send 호출을 하나의 버퍼에 모아 한 번에 전송한다.
  

## 이벤트 스레드 모델

| 이벤트 | 호출 방식 |
|---|---|
| `NewSessionConnected` | `Task.Run()` — 비동기 |
| `NewRequestReceived` | 수신 스레드에서 동기 호출 |
| `SessionClosed` | `Task.Run()` — 비동기 |
  

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