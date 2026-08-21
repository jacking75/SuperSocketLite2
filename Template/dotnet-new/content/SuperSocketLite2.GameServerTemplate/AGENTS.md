# SuperSocketLite2.GameServerTemplate — 에이전트 가이드

이 프로젝트는 **SuperSocketLite2**(C# 비동기 TCP/UDP 소켓 서버 라이브러리) 기반 게임 서버다.
`dotnet new sslite2-server` 로 만들어졌다.

## 이 라이브러리에서 반드시 지키는 3가지

**코드를 한 줄이라도 쓰기 전에 읽는다.** 셋 다 컴파일에 통과하고 가벼운 테스트에서도 잘 돈다.
동시 접속이 붙어야 깨지므로 "돌려 보니 되더라"로는 절대 잡히지 않는다.

### (1) 핸들러가 리턴하면 `PacketRequestInfo`와 `Body`는 무효다

라이브러리는 `NewRequestReceived` 를 동기로 부르고, 리턴한 뒤에야 수신 파이프를 전진시킨다.
필터가 요청 인스턴스 하나를 세션 내내 돌려 쓰기 때문에 패킷당 할당이 0이 되는 대신 계약이 붙는다.

```csharp
// 금지 — 전부 같은 실수다
_lastRequest = request;                    // 필드 저장
_queue.Enqueue(request);                   // 다른 스레드로 전달
Task.Run(() => Handle(request.Body));      // 람다 캡처
_ = HandleAsync(session, request);         // await 하지 않는 async

// 허용 — 핸들러 안에서 값으로 바꾼다
var req = MemoryPackSerializer.Deserialize<ReqLogin>(request.Body);
_queue.Enqueue(req);
```

바이트 그대로 다른 스레드로 넘겨야 하면 `ArrayPool` 에서 빌려 복사하고, 처리 후 한 곳에서 반납한다.

### (2) `Send` 는 zero-copy, `SendCopied` 는 복사

`Send(byte[], ...)` 계열은 호출자의 배열을 참조로 큐에 넣는다. 전송이 끝나기 전에 그 배열을
건드리면 깨진 데이터가 나간다.

```csharp
session.Send(buffer, 0, len);  buffer[0] = 0;              // 금지
session.SendCopied(buffer.AsSpan(0, len));  buffer[0] = 0; // OK
```

`stackalloc` 버퍼나 `ArrayPool` 대여 버퍼를 `Send` 로 넘기는 코드는 거의 항상 버그다.
이 프로젝트의 `PacketWriter` 가 올바른 형태다.

### (3) `ReadOnlySequence<byte>` 는 세그먼트에 걸칠 수 있다

```csharp
BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);   // 금지

Span<byte> buffer = stackalloc byte[HeaderSize];             // OK
header.CopyTo(buffer);
BinaryPrimitives.ReadInt16LittleEndian(buffer);
```

### 셋 다 빌드가 잡아 준다

`SuperSocketLite2` 패키지에는 Roslyn 애널라이저가 함께 들어 있다. 위 세 가지는
`SSL001`~`SSL005` 경고로 빌드에서 바로 드러난다. **경고를 억제하지 말고 고친다.**
규칙 전체는 `Docs/agent/analyzers.md`.

## 파일 구조

| 파일 | 역할 |
|---|---|
| `Protocol.cs` | `PacketId` 열거형, `PacketRequestInfo`, `PacketReceiveFilter` |
| `MainServer.cs` | `NetworkSession`, `MainServer`. 이벤트 등록과 핸들러 디스패치 |
| `PacketHandlers.cs` | 패킷별 처리 로직 |
| `PacketWriter.cs` | 응답 패킷 생성·전송 (할당 없는 형태) |
| `Program.cs` | 설정, 시작, 우아한 종료 |

## 패킷을 하나 추가하는 순서

1. `Protocol.cs` 의 `PacketId` 에 `ReqXxx` / `ResXxx` 를 추가한다
2. `PacketHandlers.cs` 에 `HandleXxx(NetworkSession, PacketRequestInfo)` 를 만든다
3. `MainServer.RegisterHandlers()` 에 `_handlers[(short)PacketId.ReqXxx] = PacketHandlers.HandleXxx;` 한 줄
4. 응답은 `PacketWriter.Send(session, (short)PacketId.ResXxx, body)` 로 보낸다

**핸들러는 동기로 끝내야 한다.** `async void` 나 `await` 없는 비동기 호출을 넣으면
메서드가 먼저 리턴하고 그 뒤에 본문을 읽게 되어 데이터가 깨진다.

## 빌드와 검증

```bash
dotnet build -c Release          # 경고 0개를 유지한다
dotnet run -c Release -- --port 32452
```

**컴파일이 되는 것과 서버가 도는 것은 이 라이브러리에서 특히 다르다.**
구현을 마쳤다고 보고하기 전에 서버를 띄우고 패킷 왕복을 실제로 확인한다.

## 더 읽을 것

`Docs/agent/` 에 에이전트용 문서가 함께 들어 있다. **필요한 것만 골라 읽는다.**

| 필요한 것 | 문서 |
|---|---|
| 타입 · 네임스페이스 · 시그니처 · `ServerConfig` 기본값 | `Docs/agent/api-cheatsheet.md` |
| 주의 사항 8가지 + 리뷰 체크리스트 | `Docs/agent/cautions.md` |
| 복사해 쓰는 코드 11종 | `Docs/agent/recipes.md` |
| 서버가 실제로 도는지 확인하는 방법 | `Docs/agent/verify.md` |
| `SSL0xx` 빌드 경고의 의미 | `Docs/agent/analyzers.md` |

원본 저장소 — <https://github.com/jacking75/SuperSocketLite2>

## 끝내기 전 자체 점검

- [ ] `PacketRequestInfo` 나 `Body` 가 핸들러 밖으로 나가는 경로가 없다
- [ ] `stackalloc` · `ArrayPool` 버퍼를 `Send` 가 아니라 `SendCopied` 로 보냈다
- [ ] 필터에서 `.First.Span` 대신 `CopyTo(Span)` 을 썼다
- [ ] `GetAllSessions()` 결과의 `null` 을 검사했다
- [ ] `MaxRequestLength` 가 실제 최대 패킷보다 크다
- [ ] 빌드 경고 0개
- [ ] 서버를 띄우고 패킷 왕복을 실제로 확인했다
