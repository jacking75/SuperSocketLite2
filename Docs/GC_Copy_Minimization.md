# GC·데이터 복사 최소화 가이드

> 대상: SuperSocketLite2를 게임 서버에 사용할 때 GC 압력과 데이터 복사를 줄이는 방법.
> 작성 기준 코드: 2026-08-14, `main` (커밋 `4758c1b` 이후).
> **개선 1~4는 이 저장소에 이미 적용되어 있다.** 적용 위치는 아래 [구현 현황](#구현-현황)을 보라.

## 구현 현황

이 문서의 개선안은 설명에 그치지 않고 저장소 코드에 반영되어 있다. 새 서버를 만들 때는
아래 파일을 그대로 본떠 쓰면 된다.

| 개선 | 상태 | 기준 구현(이걸 보고 따라 쓰면 된다) |
|---|---|---|
| 1. 수신 무할당 (동기 처리 서버) | 적용 | `Tutorials/EchoServer/ReceiveFilter.cs` + `MainServer.cs`<br>`Test/LoadTest/.../LoadTestRequestInfo.cs` + `ReceiveFilter.cs` |
| 2. 수신 ArrayPool (스레드 핸드오프 서버) | 적용 | `Tutorials/PvPGameServer/ReceiveFilter.cs` + `PacketProcessor.cs` |
| 3. 송신 무할당 | 적용 | `Tutorials/EchoServer/MainServer.cs`의 `EchoPacket`<br>`Test/LoadTest/.../PacketHandlers.cs` |
| 4. Server GC 설정 | 적용 | 서버 실행 프로젝트 csproj 14개 |
| 5. pinned 슬랩 풀 | **미적용(의도적)** | 측정에서 문제가 보이기 전에는 하지 않는다. [7장](#7-조건부--pipe에-pinned-슬랩-풀-주입) 참고 |

적용 범위와 남겨 둔 곳은 [10장](#10-저장소-적용-범위)에 정리했다.
개선 1·3의 동작(인스턴스 재사용·본문 무복사·버퍼 인코딩)은
`Test/LoadTest/SuperSocketLite.LoadTest.Tests/ZeroAllocationTests.cs`가 검증한다.

**효과는 실제로 쟀다** — 같은 처리량에서 클라이언트 p99 지연 -17.3%, p99.9 -30.7%,
Gen0 GC 318회 → 6회(각 3회 실행 합계). 자세한 수치와 재현 방법은 [9장](#9-beforeafter-측정)에 있다.

## 결론 요약

**라이브러리 코어는 이미 정상 상태(steady state)에서 거의 무할당이다.**
수신은 `System.IO.Pipelines`가 ArrayPool 세그먼트를 재사용하고, 필터는 파이프에서 직접
파싱하며(캐리 버퍼 없음), 송신 큐는 struct 기반 채널이라 enqueue당 할당이 없다.
따라서 코어를 더 최적화하는 것보다 **앱 경계 — ReceiveFilter, 패킷 핸들러, 송신 호출부 —
의 사용 패턴을 바꾸는 것**이 남은 효과의 대부분을 차지한다.

| 순위 | 개선 | 제거되는 것 | 라이브러리 수정 |
|---|---|---|---|
| 1 | [수신: 재사용 RequestInfo + `ReadOnlySequence` body](#3-개선-1--수신-패킷당-할당-0-만들기) | 패킷당 `byte[]` 1개 + 복사 1회 + 객체 1개 | 불필요 |
| 2 | [수신: 스레드 넘길 때 ArrayPool 복사](#4-개선-2--패킷을-다른-스레드로-넘기는-구조라면-arraypool-복사) | 패킷당 `byte[]` 1개 (복사는 유지) | 불필요 |
| 3 | [송신: `new byte[]` 대신 stackalloc/풀 + `TrySendCopied`](#5-개선-3--송신-패킷당-new-byte-제거) | 송신당 `byte[]` 1개 + Gen0 pinning | 불필요 |
| 4 | [런타임: Server GC / DATAS 설정](#6-개선-4--런타임-gc-설정-코드-변경-없음) | GC 일시정지 시간·빈도 | 불필요 |
| 5 | [(조건부) Pipe에 고정(pinned) 메모리 풀 주입](#7-조건부--pipe에-pinned-슬랩-풀-주입) | 수신 버퍼 pinning 단편화 | 필요 |

1~4는 전부 실용적이라 이 저장소에 적용해 두었다. 5는 측정에서 문제가 보일 때만 한다.
검토했지만 게임 서버에 실용적이지 않아 **제외한 방법**은 [8장](#8-검토했지만-제외한-방법)에 있다.

---

## 1. 현재 이미 적용되어 있는 무할당 설계

앱에서 중복 작업을 하지 않도록, 코어가 이미 해결한 부분을 정리한다.

| 영역 | 설계 | 위치 |
|---|---|---|
| 수신 버퍼 | `Pipe`가 ArrayPool 기반 세그먼트를 재사용. `GetMemory()` → `SetBuffer(Memory<byte>)` → `ReceiveAsync()`로 소켓이 파이프 버퍼에 직접 쓴다 (수신 복사 0회) | `AsyncSocketSession.StartReceive()` |
| 수신 파싱 | 필터가 `ReadOnlySequence<byte>`를 파이프에서 직접 파싱. 미완성 요청은 파이프에 남겨두므로 캐리 버퍼·재조립 복사가 없다 | `FixedHeaderReceiveFilter.Filter()`, `AppSession.ProcessRequest()` |
| 송신 큐 | `Channel<SendItem>` — `SendItem`은 readonly struct라 enqueue당 힙 할당 0 | `Common/ChannelSendingQueue.cs` |
| 송신 배치 | 드레인 리스트(`_sendBatch`, `_pooledInFlight`)를 세션당 재사용 | `SocketSession.cs:101` |
| 복사 송신 | `TrySendCopied`가 ArrayPool에서 빌려 복사하고 **전송 완료 시 라이브러리가 자동 반납** | `SocketSession.TrySendCopied()` / `ReturnPooledSendBuffers()` |
| SAEA | 수신 proxy·송신 SAEA를 `SmartPool`로 재사용 | `AsyncSocketServer.cs` |
| 기타 | 시간 스탬프는 `Environment.TickCount64`(무할당), 세그먼트 합산은 for 루프(`SumSegments`), 송신 재시도 정책은 struct(`SendRetryPolicy`) | 각 파일 |

즉 **코어 경로에서 패킷당 힙 할당은 0**이다. 남는 할당은 전부 앱 코드가 만든다.

## 2. 남은 할당 지점 진단

빈도 기준으로 분류하면 우선순위가 명확해진다.

**패킷당 (가장 중요 — 초당 수만~수십만 회)**

1. 앱 ReceiveFilter의 `body.ToArray()` — 패킷당 `byte[]` 1개 + 복사 1회.
   현재 튜토리얼 전부가 이 패턴이다 (`EchoServer/ReceiveFilter.cs:57` 등).
2. 앱 ReceiveFilter의 `new EFBinaryRequestInfo(...)` — 패킷당 클래스 인스턴스 1개.
3. 앱 송신부의 `var packet = new byte[n]` — 응답당 `byte[]` 1개. 이 배열은 전송이 끝날
   때까지 pinning되는데, 갓 할당된 Gen0 배열의 pinning은 힙 단편화도 유발한다.

**접속당 (게임 서버에선 무시 가능)**

세션·필터·`Pipe`·`Channel`·GUID 문자열 등. 게임 서버 연결은 장수 명이므로 총량에서
비중이 낮다. 최적화 대상이 아니다.

**드문 경로 (무시)**

부분 전송 시 `TrimSegments`의 리스트 할당, 큐 가득 시 `SendAsync`의 상태 머신 등.
발생 빈도가 낮아 실익이 없다.

**금지 목록**

`Send(string)` / `Send(string, params object[])`는 호출마다 인코딩 배열(+포맷 문자열)을
할당한다. 게임 서버 핫패스에서는 쓰지 않는다 (관리용 텍스트 프로토콜 전용).

---

## 3. 개선 1 — 수신: 패킷당 할당 0 만들기

### 왜 안전한가 (핵심 근거)

디스패치 체인이 **세션당 단일 태스크에서 완전히 동기**로 돈다:

```
ProcessPipeAsync (세션당 1개 태스크)
  └─ ReadAsync → AppSession.ProcessRequest(sequence)
       └─ 루프: Filter() → ResolveRequestInfo() → ExecuteCommand() ← 핸들러 동기 실행
  └─ AdvanceTo(consumed)   ← 핸들러가 전부 리턴한 뒤에야 호출됨
```

- `NewRequestReceived` 핸들러가 리턴하기 전에는 `AdvanceTo`가 호출되지 않으므로,
  `ResolveRequestInfo`가 받은 `ReadOnlySequence<byte>`가 가리키는 파이프 메모리는
  **핸들러 실행 동안 유효**하다.
- 필터 인스턴스는 세션당 1개이고(`ReceiveFilterFactory.CreateFilter`), 같은 세션의 다음
  패킷 파싱은 현재 핸들러가 리턴한 뒤에 시작된다.

따라서 **필터가 보관한 RequestInfo 인스턴스 1개를 매 패킷 재사용**해도 되고, body를
복사 없이 `ReadOnlySequence<byte>` 그대로 핸들러에 넘겨도 된다. 라이브러리 수정 없이
앱 코드만으로 가능하다.

### 구현

> 실제 코드: `Tutorials/EchoServer/ReceiveFilter.cs`, `Test/LoadTest/SuperSocketLite.LoadTest.Server/`.
> 아래는 그 코드를 최소 형태로 옮긴 것이다.

```csharp
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// 패킷당 할당이 없는 요청 정보. 필터가 인스턴스 1개를 재사용하며,
/// Body는 수신 파이프의 메모리를 직접 가리킨다.
/// </summary>
public sealed class ZeroAllocRequestInfo : IRequestInfo
{
    public string Key => null!;

    public short TotalSize { get; private set; }
    public short PacketID { get; private set; }
    public sbyte Value1 { get; private set; }

    /// <summary>파이프 메모리를 가리키는 body. 핸들러가 리턴하면 무효가 된다.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(short totalSize, short packetID, sbyte value1, ReadOnlySequence<byte> body)
    {
        TotalSize = totalSize;
        PacketID = packetID;
        Value1 = value1;
        Body = body;
    }
}

public class ZeroAllocReceiveFilter : FixedHeaderReceiveFilter<ZeroAllocRequestInfo>
{
    public const int HeaderSize = 5;

    // 필터는 세션당 1개, 디스패치는 동기이므로 인스턴스 재사용이 안전하다.
    private readonly ZeroAllocRequestInfo _reusableRequest = new();

    public ZeroAllocReceiveFilter()
        : base(HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[HeaderSize];
        header.CopyTo(headerBuffer);
        return BinaryPrimitives.ReadInt16LittleEndian(headerBuffer) - HeaderSize;
    }

    protected override ZeroAllocRequestInfo ResolveRequestInfo(
        ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[HeaderSize];
        header.CopyTo(headerBuffer);

        _reusableRequest.Set(
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(2, 2)),
            (sbyte)headerBuffer[4],
            body);

        return _reusableRequest;
    }
}
```

핸들러에서는 `SequenceReader<byte>`로 직접 읽거나, sequence를 지원하는 직렬화기에
바로 넘긴다. MemoryPack은 `ReadOnlySequence<byte>` 오버로드가 있다:

```csharp
void OnPacketRequest(NetworkSession session, ZeroAllocRequestInfo request)
{
    switch (request.PacketID)
    {
        case PacketId.Move:
            // 복사·할당 없이 파이프 메모리에서 바로 역직렬화
            var move = MemoryPackSerializer.Deserialize<PKTMove>(request.Body);
            ...
            break;
    }
    // 리턴 후 request와 request.Body는 재사용/무효화된다.
}
```

### 지켜야 할 규칙 (반드시 문서화·코드 리뷰 항목으로)

1. **핸들러 리턴 후 `request`와 `request.Body`는 절대 사용 금지.** 필드에 저장하거나,
   람다에 캡처하거나, 다른 스레드 큐에 넣으면 안 된다. 리턴 후 파이프가 그 메모리를
   다음 수신에 재사용하고, RequestInfo 인스턴스도 다음 패킷 값으로 덮어써진다.
2. 보관이 필요한 데이터는 **핸들러 안에서** 앱 소유 구조체/객체로 변환(역직렬화)하거나
   복사해 둔다.
3. 패킷을 별도 스레드로 넘기는 아키텍처(아래 개선 2)와는 **양립하지 않는다** — 그 경우
   복사는 불가피하며 개선 2를 쓴다.

### 기대 효과와 실용성 판단

- 패킷당 할당 3건(`byte[]`, 객체, 복사) → **0건**. 수신 경로 전체가 소켓 → 파이프 →
  역직렬화까지 복사 없이 이어진다.
- 제약("핸들러 안에서 끝내라")은 에코·게이트·릴레이·실시간 전투처럼 **핸들러에서 즉시
  처리하는 구조**에서는 부담이 없다. 대부분의 모바일 게임 서버 로직이 여기 해당하면
  1순위로 적용할 것.

---

## 4. 개선 2 — 패킷을 다른 스레드로 넘기는 구조라면: ArrayPool 복사

`PvPGameServer`처럼 수신 스레드가 패킷을 로직 스레드 큐(`BufferBlock` 등)로 넘기는
구조는 body가 핸들러보다 오래 살아야 하므로 **복사 1회는 불가피**하다. 대신 할당은
없앨 수 있다: `new byte[]` 대신 `ArrayPool<byte>.Shared`에서 빌린다.

> 실제 코드: `Tutorials/PvPGameServer/ReceiveFilter.cs`(대여)와 `PacketProcessor.cs`(반납).
> 반납은 `Process()` 루프의 `finally` **한 곳**에서만 한다.

```csharp
public sealed class PooledPacketRequest : IRequestInfo
{
    public string Key => null!;

    public string SessionID = string.Empty;
    public byte[] Buffer = Array.Empty<byte>();  // ArrayPool 배열 — Length가 아니라 Count까지만 유효
    public int Count;

    public void SetBody(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Count = checked((int)(header.Length + body.Length));
        Buffer = ArrayPool<byte>.Shared.Rent(Count);

        header.CopyTo(Buffer);
        if (!body.IsEmpty)
            body.CopyTo(Buffer.AsSpan((int)header.Length));
    }

    /// <summary>로직 스레드가 패킷 처리를 마친 뒤 정확히 한 번 호출한다.</summary>
    public void ReturnBuffer()
    {
        var buffer = Buffer;
        Buffer = Array.Empty<byte>();
        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

로직 스레드의 소비 루프 끝에서 `ReturnBuffer()`를 호출한다 (예외 경로 포함 `finally`).

### 주의점

- **Rent는 요청보다 큰 배열을 줄 수 있다.** 길이는 반드시 `Count`로 따로 들고 다닌다
  (`Buffer.Length` 사용 금지).
- **이중 반납 금지.** 반납 지점을 소비 루프 한 곳으로 고정하면 규율이 단순해진다.
- 반납을 놓치면 누수가 아니라 "풀 미스 → 새 할당"으로 조용히 퇴화한다. 부하 테스트에서
  `alloc-rate`로 확인할 것.
- RequestInfo 객체 자체(수십 바이트)는 여전히 패킷당 1개 할당된다. 바이트 비중이 큰
  body 배열이 사라지는 것이 핵심이고, 객체까지 없애려면 `ObjectPool<T>`를 얹을 수
  있지만 **규율 비용 대비 이득이 작아 선택 사항**이다.

---

## 5. 개선 3 — 송신: 패킷당 `new byte[]` 제거

> 실제 코드: `Tutorials/EchoServer/MainServer.cs`의 `EchoPacket`/`WritePacket`,
> `Test/LoadTest/SuperSocketLite.LoadTest.Server/PacketHandlers.cs`.

### 단일 대상 응답: 스택/풀 버퍼에 직렬화 → `TrySendCopied`

```csharp
// 작은 패킷(수백 바이트 이하): stackalloc
Span<byte> packet = stackalloc byte[64];
var written = SerializeMovePacket(packet, ...);
session.TrySendCopied(packet[..written]);

// 크기가 가변이면: ArrayPool
var buffer = ArrayPool<byte>.Shared.Rent(maxSize);
try
{
    var written = Serialize(buffer, ...);
    session.TrySendCopied(buffer.AsSpan(0, written));
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);  // TrySendCopied는 즉시 복사하므로 바로 반납 가능
}
```

`TrySendCopied`는 내부에서 ArrayPool 버퍼로 복사하고 **전송 완료 시 라이브러리가 알아서
반납**하므로(`ReturnPooledSendBuffers`) 앱은 수명 관리 부담이 없다. 복사 1회가 남지만:

- 게임 패킷(수십~수백 바이트)에서 memcpy 비용은 할당+GC 비용보다 훨씬 싸다.
- 전송 중 pinning되는 배열이 갓 만든 Gen0 배열이 아니라 **워밍업된 풀 배열(대부분
  Gen2)**이 되므로 pinning으로 인한 힙 단편화도 함께 줄어든다.

### 브로드캐스트: 배열 1개를 zero-copy로 공유

같은 페이로드를 N명에게 보낼 때 N번 복사하는 것은 낭비다. 이때는 배열을 1개 만들어
`TrySend(byte[])`(zero-copy)로 공유한다:

```csharp
var packet = BuildBroadcastPacket(...);       // 할당 1회 (N과 무관)

foreach (var member in room.Sessions)
    member.TrySend(packet, 0, packet.Length); // 복사 0회 — 같은 배열 공유
// 규칙: 이 배열은 만들고 나서 절대 수정하지 않는다 (전송 완료 시점을 알 수 없으므로 불변으로 취급)
```

### 큐가 가득 찰 수 있는 대량 송신: `SendAsync`

`TrySend`가 false를 반환하는 상황(느린 클라이언트)에서 스핀 대기 대신
`SendAsync(ReadOnlyMemory<byte>, CancellationToken)`을 쓰면 배열 기반 메모리는
zero-copy로 들어가고, 큐 여유를 비동기로 기다린다. 타임아웃은 CTS로 건다.

### 정리: 송신 API 선택 기준

| 상황 | API | 복사 | 할당 |
|---|---|---|---|
| 단발 응답, 큐가 차면 **포기**하고 직접 처리 | `TrySendCopied(span)` → bool | 1회 (풀→풀) | 0 |
| 단발 응답, 기존 `Send`와 같은 동작을 원함 | `SendCopied(span)` | 1회 (풀→풀) | 0 |
| 브로드캐스트 (불변 배열 공유) | `TrySend(byte[])` | 0회 | 1회/N명 |
| 느린 클라이언트, 배압 필요 | `SendAsync(memory, ct)` | 0회 (배열 기반) | 0 (동기 완료 시) |
| 핫패스에서 `Send(string)` | — | 쓰지 않는다 | — |

`TrySendCopied`와 `SendCopied`는 복사·할당 특성이 같고 **큐가 가득 찼을 때만** 다르다.
`TrySendCopied`는 `false`를 돌려주고, `SendCopied`는 `SendTimeOut`까지 재시도한 뒤
`TimeoutException`을 던진다(즉 `Send`의 무할당 판이다). 기존 `Send` 코드를 옮길 때는
실패 처리 로직을 그대로 두려면 `SendCopied`를 쓴다 — 이 저장소의 예제들이 그렇게 했다.

---

## 6. 개선 4 — 런타임 GC 설정 (코드 변경 없음)

서버 실행 프로젝트 csproj에 (이 저장소의 서버 14개에는 이미 들어가 있다):

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

- **Server GC는 필수.** Workstation GC는 멀티코어 서버에서 처리량이 크게 떨어진다.
- .NET 10의 Server GC는 **DATAS**(동적 힙 조정)가 기본으로 켜져 있다. 메모리 사용량은
  줄지만, 유휴→버스트 전환에서 힙을 다시 키우는 비용이 지연 스파이크로 나타날 수 있다.
  동접이 일정한 상시 부하 게임 서버라면 꺼서 고정 힙으로 두고 비교 측정하는 것을 권장한다.

  **어느 쪽이 나은지는 서버마다 다르므로 csproj에 박아 두지 않았다.** 빌드를 고치지 말고
  환경 변수로 껐다 켜며 같은 부하로 비교하라 — 재빌드가 필요 없다:

  ```powershell
  $env:DOTNET_GCDynamicAdaptationMode = 0   # DATAS 끔 (기본값은 1)
  ```

  결론이 나면 그때 `<GarbageCollectionAdaptationMode>0</GarbageCollectionAdaptationMode>`로
  고정한다.
- 컨테이너 배포 시 메모리 상한이 있다면 `GCHeapHardLimitPercent`로 GC가 상한을 인지하게
  한다.

개선 1~3을 적용하면 Gen0 할당 자체가 급감하므로, GC 설정의 역할은 "남은 GC를 저렴하게"
정도다. 순서는 코드 개선이 먼저다.

---

## 7. (조건부) Pipe에 pinned 슬랩 풀 주입

수신 버퍼는 `SetBuffer(Memory<byte>)` → `ReceiveAsync` 동안 런타임이 pinning한다.
기본 `Pipe`는 ArrayPool 배열을 쓰므로 워밍업 후에는 대부분 Gen2 배열이 pinning되어
문제가 작지만, **접속/해제 churn이 심한 서버**에서는 pinning 단편화가 측정될 수 있다.

해결책은 Kestrel의 `PinnedBlockMemoryPool` 방식 — `GC.AllocateUninitializedArray<byte>
(size, pinned: true)`로 POH(Pinned Object Heap)에 슬랩을 만들고 이를 `MemoryPool<byte>`
로 감싸 `PipeOptions(pool: ...)`에 주입한다. `SocketSession.Initialize()`의 `Pipe` 생성부를
고치면 되므로 변경 범위는 작다.

**단, 이것은 측정이 먼저다.** `dotnet-counters`의 `poh-size` / Gen2 단편화 지표에서 문제가
보이지 않으면 하지 않는다. 기본 ArrayPool로 충분한 경우가 대부분이라 우선순위 최하위.

---

## 8. 검토했지만 제외한 방법

게임 서버 실사용 관점에서 비용 대비 이득이 없어 제외한다.

| 방법 | 제외 이유 |
|---|---|
| `TRequestInfo`를 struct로 바꾸는 API 개편 | `class` 제약을 푸는 전면 breaking change. 개선 1(인스턴스 재사용)로 동일한 효과를 무파괴로 얻는다 |
| 세션·필터·`Pipe` 오브젝트 풀링 | 접속당 1회 할당인데 게임 서버 연결은 장수 명 — 총량에서 무의미 |
| `TrimSegments`(부분 전송) 무할당화 | 커널 버퍼가 가득 찬 예외적 상황에서만 실행. 빈도가 낮아 실익 없음 |
| unsafe 포인터 파싱, 커스텀 어로케이터 | `SequenceReader`/`BinaryPrimitives`와 성능 차이가 미미한데 안전성·유지보수 비용이 큼 |
| 송신 경로 완전 무할당(수제 `IValueTaskSource` 등) | 이미 struct 채널 + 리스트 재사용으로 정상 경로 할당 0 — 남은 것이 없음 |
| `Send(string)`용 풀 인코딩 오버로드 추가 | 게임 서버는 바이너리 프로토콜 — 핫패스에서 문자열 API를 안 쓰는 것으로 충분 |

---

## 9. before/after 측정

개선 1·3은 LoadTest 서버에 **스위치**로 들어가 있다. 같은 빌드를 두 번 돌리면서
패킷당 버퍼 처리 방식만 바꿔 비교할 수 있다 — 코드를 되돌리거나 커밋을 옮길 필요가 없다.

```powershell
cd Test\LoadTest

# before: 개선 전 동작 (패킷마다 본문 배열·요청 인스턴스·응답 배열을 새로 만든다)
.\run-loadtest.ps1 -RunId a-legacy -Repeat 3 -Clients 500 -Duration 00:02:00 -AllocMode legacy -SkipReport

# after: 현재 동작 (패킷당 할당 0)
.\run-loadtest.ps1 -RunId a-pooled -Repeat 3 -Clients 500 -Duration 00:02:00 -AllocMode pooled -SkipReport

# 두 실행을 나란히 놓은 HTML 리포트
.\run-loadtest.ps1 -ReportOnly -Baseline a-legacy -Candidate a-pooled
```

꼬리 지연은 실행마다 흔들리므로 `-Repeat 3` 이상으로 돌린다.
서버 CSV에는 `gc_gen0_delta` / `gc_gen1_delta` / `gc_gen2_delta` / `gc_heap_bytes`가 이미
들어 있으므로 리포트만으로 GC 변화를 볼 수 있다.

프로세스 단위로 더 자세히 보려면 서버가 도는 동안:

```powershell
dotnet-counters monitor -p <pid> --counters System.Runtime[alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc,gc-heap-size]
```

핵심 지표는 `alloc-rate`(B/s)와 `gen-0-gc-count` 증가 속도다.
목표는 **정상 부하에서 Gen2 GC가 사실상 발생하지 않고, Gen0 GC 간격이 수 초 이상**인 상태다.

### 실측 결과 (2026-08-14, 각 3회 실행)

`-Repeat 3 -Clients 300 -SendRate 40 -Payload large(4KB) -Duration 00:00:45`,
모드당 요청 약 88만 건, 목표 12,000 req/s. 로컬 개발 PC(Windows 11) 기준이므로
**절대값이 아니라 차이의 방향과 크기**를 보라.

| 지표 | legacy | pooled | 변화 |
|---|---:|---:|---|
| 클라이언트 p99 지연 | 4.799 ms | 3.967 ms | **-17.3%** |
| 클라이언트 p99.9 지연 | 10.623 ms | 7.359 ms | **-30.7%** |
| 서버 메모리 증가 | 67.4 MB | 25.2 MB | **-42.1 MB** |
| Gen0 GC 횟수 (3회 합) | 318 | **6** | -98% |
| Gen1 GC 횟수 (3회 합) | 263 | **3** | -99% |
| Gen2 GC 횟수 (3회 합) | 6 | **0** | -100% |
| 서버 힙 평균 | 16.5 MB | 9.7 MB | -41% |
| 서버 핸들러 p99 | 287 µs | 256 µs | -10.8% |
| 처리량 | 11,999 /s | 12,001 /s | 동일 (목표 100% 달성) |
| 오류율 | 0% | 0% | — |
| 총 요청 | 880,999 | 883,585 | 동일 |

꼬리 지연이 크게 좋아진 것이 핵심이다. GC 횟수가 줄어 **일시정지가 사라진 자리**가
p99.9에서 가장 크게 드러난다. 처리량은 두 모드가 같으므로 "덜 일해서 빨라진" 것이 아니다.

풀 경로는 응답을 "내 버퍼에 직렬화 → `SendCopied`가 라이브러리 풀 버퍼로 복사"하므로 memcpy가
한 번 더 든다. 그런데 서버가 재는 핸들러 처리 시간도 내려갔다 — **게임 패킷 크기에서 추가
memcpy는 없앤 할당·GC 비용에 비해 무시할 수준**이라는 5장의 주장이 수치로 확인된 셈이다.

> 참고: 1회씩만 돌렸을 때는 처리량 -4.3%, 핸들러 p99 +42%처럼 반대 방향 수치가 나왔는데
> 3회 반복에서 모두 사라졌다. **1회 실행 수치로 판단하지 말라**는 예시로 남겨 둔다.

### 주의: 무엇이 비교되는가

- `--alloc-mode`는 **이진 TCP 경로에만** 적용된다. text-line·UDP 부가 리스너는 언제나 풀
  경로로 동작한다(기본값이 꺼져 있어 비교에 끼어들지 않는다).
- 서버 계측 자체도 요청마다 돌므로, 두 실행의 `-Metrics` 수준은 반드시 같게 둔다.
  계측 비용 자체를 재려면 `measure-metrics-overhead.ps1`을 쓴다.
- `legacy`가 재현하는 것은 **패킷 버퍼 3종**(요청 인스턴스·본문 배열·응답 배열)뿐이다.
  계측 레코더(`RequestMetricRecorder`)를 클래스에서 구조체로 바꾼 것은 스위치와 무관하게 항상
  적용되므로, `--metrics full`로 재면 진짜 개선 전은 요청당 4개, `legacy`는 3개를 할당한다.
  즉 위 표의 개선 폭은 실제보다 **조금 작게** 나온 값이다 — 방향은 보수적이라 결론은 그대로다.
- `run-matrix.ps1`과 `measure-metrics-overhead.ps1`은 `-AllocMode`를 모른다. 언제나 기본값
  `pooled`로 돈다.
- DATAS on/off 비교는 6장의 환경 변수로 따로 한다. 한 번에 한 변수만 바꾼다.

### 새 서버를 만들 때의 적용 순서

1. **개선 3(송신)** — 핸들러 구조와 무관하게 적용 가능하고 위험이 가장 낮다.
2. **개선 1 또는 2(수신)** — 핸들러에서 즉시 처리하는 구조면 1, 스레드 핸드오프 구조면 2.
   `body.ToArray()`를 쓰고 있다면 여기서 alloc-rate가 가장 크게 떨어진다.
3. **개선 4(GC 설정)** — DATAS on/off를 같은 부하로 비교해 p99 지연이 좋은 쪽을 택한다.

---

## 10. 저장소 적용 범위

### 개선 1(수신 무할당)을 적용한 곳 — 핸들러에서 바로 처리하는 서버

`EchoServer`, `EchoServerEx`, `EchoServer_GenericHost`, `BinaryPacketServer`,
`MultiPortServer`, `sendFailTestServer`, 그리고 LoadTest 서버(이진 TCP·text-line).

이 서버들은 `NewRequestReceived` 안에서 응답까지 끝내고 리턴하므로 요청 인스턴스 재사용과
본문 무복사가 안전하다. 요청 정보의 `Body`는 `byte[]`에서 `ReadOnlySequence<byte>`로 바뀌었다.

LoadTest의 **UDP 리스너**도 데이터그램 전체를 배열로 펴던 것과 페이로드의 문자열 왕복
(bytes → string → bytes)을 없앴다. 다만 라이브러리가 UDP 세션을 문자열 ID로 찾으므로
키·세션 ID 문자열과 요청 인스턴스는 데이터그램마다 남는다 — 프로토콜 규약이라 어쩔 수 없다.
수신 버퍼 수명은 TCP와 같다: `UdpReceivePacket.Dispose()`가 핸들러 리턴 뒤에 반납한다.

### 개선 2(ArrayPool)를 적용한 곳 — 패킷을 로직 스레드로 넘기는 서버

`PvPGameServer`. 수신 필터가 `ArrayPool`에서 빌리고, `PacketProcessor.Process()`의
`finally` 한 곳에서 반납한다. 풀 배열은 요청보다 클 수 있으므로 길이는 `DataSize`로 들고
다니고, 역직렬화에는 `DataSpan`을 넘긴다.

### 그대로 둔 곳과 이유

- `ChatServer`, `ChatServerEx`, `GameServer_MoDedicated`, `GameServer_MoDedicated2`,
  `GateServer` — 모두 `PvPGameServer`와 같은 스레드 핸드오프 구조라 적용할 개선은
  **개선 2 하나**이고, 그 방법은 `PvPGameServer`가 이미 보여 준다. 다섯 곳에 반납 규율을
  복제하면 배우는 것 없이 use-after-return 위험만 늘어난다. 필요하면
  `PvPGameServer`를 그대로 본떠 옮기면 된다.
  이 서버들의 **송신** 경로는 이미 문서 권장 형태다 — 브로드캐스트가 배열 하나를 만들어
  모든 세션에 공유한다(`Room.cs`).
- `SimpleUDPServer` — 수신 핸들러 자체가 등록되어 있지 않은 최소 예제라 손댈 핫패스가 없다.
- 클라이언트 예제와 LoadTest 클라이언트 — 부하를 만드는 쪽이라 서버 수치에 영향을 주지 않는다.
  측정 조건을 흔들지 않기 위해 그대로 두었다.

### 라이브러리(`SuperSocketLite/`)

**변경 없음.** 1장에 정리했듯 코어는 이미 정상 경로에서 패킷당 할당이 0이다.
이번 작업으로 공개 API가 바뀐 것도 없다.
