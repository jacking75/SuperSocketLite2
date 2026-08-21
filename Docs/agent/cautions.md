# 주의 사항 — 컴파일은 되지만 부하가 걸려야 터지는 것들

**코드를 쓰기 전에 읽고, 리뷰할 때 다시 읽는다.**
전부 컴파일에 통과하고 가벼운 테스트에서도 잘 돈다. 동시 접속이 붙어야 깨지므로,
"돌아가니까 맞다"는 판단이 여기서는 통하지 않는다.

사람이 읽는 판은 `Docs/Cautions.html` / `Docs/Cautions_kr.html`이고, 같은 8가지가
`Docs/Getting_Started*.html` 7장에도 실려 있다. **내용을 고치면 네 곳을 같이 고친다.**

---

## 1. 스레드 안전성

`NewSessionConnected`와 `NewRequestReceived`는 **같은 세션이어도 서로 다른 스레드에서 동시에**
호출될 수 있다. 클라이언트가 접속하자마자 패킷을 보내면 `NewSessionConnected` 처리 도중에
`NewRequestReceived`가 들어온다.

```csharp
// 위험 — 접속 핸들러가 세팅을 끝냈다고 가정한다
private void OnConnected(MySession session)
{
    session.Player = new Player();          // 아직 대입 전인데
}

private void OnRequest(MySession session, MyRequestInfo req)
{
    session.Player.Handle(req);             // 여기서 NullReferenceException
}
```

해결은 둘 중 하나다.

```csharp
// 방법 A — 순서를 구조적으로 보장한다 (권장)
var config = new ServerConfig
{
    // NewSessionConnected가 accept 경로에서 동기 호출된다.
    // 핸들러가 accept를 블로킹하므로 반드시 가볍게 유지한다.
    SyncSessionConnectedEvent = true,
};

// 방법 B — 요청 핸들러가 스스로 방어한다
private void OnRequest(MySession session, MyRequestInfo req)
{
    var player = session.Player;
    if (player is null)
    {
        return;                             // 또는 세션을 닫는다
    }

    player.Handle(req);
}
```

---

## 2. 송신 버퍼 수명 (zero-copy)

`Send(byte[], int, int)`, `Send(ArraySegment<byte>)`, `Send(IList<ArraySegment<byte>>)`,
`SendAsync(ReadOnlyMemory<byte>)`(배열 기반일 때)는 **호출자의 배열을 참조로 큐에 넣는다.**
전송이 끝나기 전에 그 배열을 건드리면 잘못된 데이터가 나간다.

```csharp
// 위험 — 아직 전송 중인 버퍼를 덮어쓴다
session.Send(buffer, 0, len);
buffer[0] = 0;

// 안전 — 라이브러리가 풀 버퍼로 복사해 간다
session.SendCopied(buffer.AsSpan(0, len));
buffer[0] = 0;                              // OK
```

- `Send(IList<...>)`의 **리스트 자체**는 enqueue 시 복사되므로 호출 직후 재사용해도 된다.
  공유되는 건 리스트 안의 배열이다.
- 빈 데이터 처리가 다르다. `SendCopied` / `TrySendCopied`는 데이터가 비면 아무것도 큐에 넣지
  않고 성공으로 돌아간다. `Send(buffer, 0, 0)`은 길이 0 세그먼트를 실제로 전송한다 —
  UDP라면 빈 데이터그램이 나간다. 빈 패킷에 의미가 있는 프로토콜을 `Send`에서 `SendCopied`로
  옮길 때만 확인하면 된다.

**판단 기준:** 확신이 없으면 `SendCopied`를 쓴다. 스택이나 `ArrayPool`에서 빌린 버퍼를
`Send`로 넘기는 코드는 거의 항상 버그다.

---

## 3. 수신 필터

필터는 파이프에서 `ReadOnlySequence<byte>`를 직접 받는다.

**요청이 아직 완성되지 않았으면 `consumed`를 전진시키지 말고 그대로 둔다.** 데이터는 파이프에
남고 다음 수신 때 이어서 온다. 필터가 자체 캐리 버퍼를 두면 오히려 복사가 늘어난다.

```csharp
// 위험 — 세그먼트가 하나라고 가정한다
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    return BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);   // 헤더가 쪼개지면 깨진다
}

// 안전 — 스택 버퍼로 모아서 읽는다
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buffer = stackalloc byte[HeaderSize];
    header.CopyTo(buffer);
    return BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2));
}
```

`header`도 `body`도 세그먼트 여러 개에 걸칠 수 있다. `.First.Span`으로 바로 읽지 말고
`CopyTo(Span)`이나 `ToArray()`를 쓴다. 헤더는 대개 작으므로 `stackalloc`이 정답이다.

UDP + `UdpRequestInfo` 조합에서 sessionID 파싱용 필터는 **수신 스레드당 1개가 재사용된다**
(`Reset()` 후 재사용). 이 필터는 데이터그램 간 상태를 가지면 안 되고,
`CreateFilter`에 넘어온 remote endpoint를 캡처해서도 안 된다.

---

## 4. RequestInfo와 본문의 수명 — 가장 많이 틀리는 것

라이브러리는 `NewRequestReceived` 핸들러를 **동기로** 부르고, 핸들러가 전부 리턴한 뒤에야
파이프를 전진시킨다(`AppServerBase.ExecuteCommand` → `ProcessPipeAsync`의 `AdvanceTo`).
UDP도 같다 — `UdpReceivePacket.Dispose()`가 핸들러 리턴 후에 수신 버퍼를 풀에 돌려준다.

이 보장 덕분에 앱은 **패킷당 할당을 0으로** 만들 수 있다. 필터가 요청 인스턴스 하나를 돌려 쓰고
본문을 `ReadOnlySequence<byte>`로 그대로 넘기면 된다. `Tutorials/EchoServer`가 그 형태다.

대신 계약이 생긴다.

> **핸들러가 리턴하면 그 `RequestInfo`와 본문은 더 이상 유효하지 않다.**
> 필드에 저장하거나, 람다에 캡처하거나, 다른 스레드 큐에 넣으면 안 된다.

```csharp
// 위험 — 전부 같은 실수다
private void OnRequest(MySession session, MyRequestInfo req)
{
    _lastRequest = req;                        // 필드 저장 — 다음 패킷이 같은 인스턴스를 덮어쓴다
    _queue.Enqueue(req);                       // 다른 스레드로 전달 — 그 스레드가 볼 때는 이미 무효
    Task.Run(() => Handle(req.Body));          // 람다 캡처 — 위와 같다
    _ = HandleAsync(session, req);             // await 하지 않는 async — 리턴 후에 본문을 읽는다
}

// 안전 A — 핸들러 안에서 역직렬화해서 값만 남긴다
private void OnRequest(MySession session, MyRequestInfo req)
{
    var login = MemoryPackSerializer.Deserialize<LoginReq>(req.Body);   // 값 타입/새 객체
    _queue.Enqueue(login);                                              // 이건 넘겨도 된다
}

// 안전 B — 로직 스레드로 바이트를 넘겨야 하면 풀에서 빌려 복사한다
private void OnRequest(MySession session, MyRequestInfo req)
{
    var length = checked((int)req.Body.Length);
    var rented = ArrayPool<byte>.Shared.Rent(length);
    req.Body.CopyTo(rented);

    // 처리 후 한 곳에서 ArrayPool<byte>.Shared.Return(rented) 한다.
    _queue.Enqueue(new PacketWork(session.SessionID, rented, length));
}
```

패킷을 로직 스레드로 넘기는 구조라면 zero-copy 방식을 쓸 수 없다. 안전 B가 그 형태이고,
`Tutorials/PvPGameServer`가 실제 예다. 자세한 건 `Docs/GC_Copy_Minimization.md`.

**어겼을 때 컴파일은 되고 가벼운 부하에서는 대개 동작한다.** 파이프 버퍼가 재사용될 만큼
부하가 올라야 데이터가 깨지므로 찾기 매우 어렵다.

---

## 5. 시간 값은 UTC

`AppSession.StartTime` / `LastActiveTime`, `AppServerBase.StartedTime`은 전부 UTC다.

```csharp
// 위험 — 로컬 시간으로 착각하고 비교한다
if (DateTime.Now - session.StartTime > TimeSpan.FromMinutes(5))   // 시차만큼 어긋난다

// 안전
if (DateTime.UtcNow - session.StartTime > TimeSpan.FromMinutes(5))

// 화면·로그에 로컬로 찍을 때만 변환한다
logger.Info($"connected at {session.StartTime.ToLocalTime()}");
```

`LastActiveTime`은 단조 tick에서 역산하므로 수 ms 오차가 있다. 정밀한 비교에는 쓰지 않는다.

---

## 6. 타임아웃 처리 순서

`TimeoutException` 발생 후 세션을 종료할 때는 **반드시 이 순서**를 지킨다.

```csharp
try
{
    session.SendCopied(packet);
}
catch (TimeoutException)
{
    session.SendEndWhenSendingTimeOut();   // 먼저 이것
    session.Close();                       // 그 다음 이것
}
```

`SendEndWhenSendingTimeOut()` 없이 `Close()`만 부르면 소켓이 정리되지 않는다.

---

## 7. 최대 연결 수 초과

`MaxConnectionNumber`를 넘으면 라이브러리가 **즉시 연결을 끊는다.**
이때 **`NewSessionConnected`는 호출되지 않는다.**

접속 수를 세션 이벤트로 카운트하는 코드는 거절된 연결을 못 본다.
거절 건수는 메트릭 `sessions-rejected`로 확인한다.

---

## 8. UDP 모드

- UDP 세션은 내부 `_client`(`Socket`)가 **null일 수 있다.** 소켓 인스턴스를 공유하는 구조라서 그렇다.
- `Close()` 내부에서 UDP/TCP 경로가 분기된다. UDP 관련 코드를 고칠 때 주의한다.
- 3번에 적은 대로, sessionID 파싱 필터는 수신 스레드당 1개가 재사용된다.

---

## 리뷰 체크리스트

이 라이브러리를 쓰는 코드를 리뷰할 때 이 순서로 본다.

- [ ] `RequestInfo`나 `Body`가 핸들러 밖으로 나가는 경로가 있는가 (필드/캡처/큐/`async` 미대기)
- [ ] `Send`에 스택 버퍼나 `ArrayPool` 대여 버퍼를 넘기지 않았는가 → `SendCopied`여야 한다
- [ ] 필터에서 `.First.Span`으로 헤더를 읽지 않았는가 → `CopyTo(Span)`이어야 한다
- [ ] 접속 핸들러가 세팅한 값을 요청 핸들러가 무방비로 읽지 않는가
- [ ] `GetAllSessions()` / `GetSessions()` 결과의 `null` 검사를 했는가
- [ ] `TimeoutException` 처리에 `SendEndWhenSendingTimeOut()`이 `Close()`보다 앞에 있는가
- [ ] 시간 비교에 `DateTime.Now`가 아니라 `DateTime.UtcNow`를 썼는가
