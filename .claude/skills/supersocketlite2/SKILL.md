---
name: supersocketlite2
description: SuperSocketLite2(C# 비동기 TCP/UDP 소켓 서버 라이브러리)로 서버를 만들거나 고칠 때 사용하는 스킬. AppServer/AppSession 정의, ReceiveFilter로 패킷 프로토콜 파싱, Send/SendCopied 선택, 패킷 핸들러 디스패치, 브로드캐스트, Generic Host 연동, MemoryPack 직렬화, UDP 세션, 로직 스레드 분리를 다룬다. zero-copy 버퍼 수명과 RequestInfo 수명처럼 컴파일은 되지만 부하가 걸려야 터지는 실수를 막는다. 게임 서버·실시간 서버·소켓 서버를 C#으로 만들 때, 또는 SuperSocketLite/SuperSocketLite2 코드를 읽거나 리뷰할 때 적용.
license: MIT
metadata:
  version: "1.0"
  library: SuperSocketLite2
---

# SuperSocketLite2

C# 비동기 TCP/UDP 소켓 서버 라이브러리. .NET 10 타겟, `System.IO.Pipelines` 기반 수신,
`Channel` 기반 송신, 기본이 zero-copy. 주 사용처는 모바일 게임 서버.

## 0. 이 라이브러리에서 반드시 지키는 3가지

**코드를 한 줄이라도 쓰기 전에 읽는다.** 셋 다 컴파일에 통과하고 가벼운 테스트에서도 잘 돈다.
동시 접속이 붙어야 깨지므로, "돌려 보니 되더라"로는 절대 잡히지 않는다.

### (1) 핸들러가 리턴하면 `RequestInfo`와 `Body`는 무효다

라이브러리는 `NewRequestReceived`를 **동기로** 부르고, 리턴한 뒤에야 수신 파이프를 전진시킨다.
그래서 필터는 요청 인스턴스 하나를 세션 내내 돌려 쓴다 — 패킷당 할당이 0이 되는 대신 계약이 붙는다.

```csharp
// 금지 — 전부 같은 실수다
_lastRequest = request;                        // 필드 저장
_queue.Enqueue(request);                       // 다른 스레드로 전달
Task.Run(() => Handle(request.Body));          // 람다 캡처
_ = HandleAsync(session, request);             // await 하지 않는 async

// 허용 — 핸들러 안에서 값으로 바꾼다
var req = MemoryPackSerializer.Deserialize<ReqLogin>(request.Body);
_queue.Enqueue(req);
```

바이트 그대로 다른 스레드에 넘겨야 하면 `ArrayPool`에서 빌려 복사한다(§4 레시피 9).

### (2) `Send`는 zero-copy, `SendCopied`는 복사

`Send(byte[], ...)` 계열은 호출자의 배열을 **참조로** 큐에 넣는다. 전송 완료 전에 그 배열을
건드리면 깨진 데이터가 나간다.

```csharp
session.Send(buffer, 0, len);  buffer[0] = 0;              // 금지
session.SendCopied(buffer.AsSpan(0, len));  buffer[0] = 0; // OK
```

**`stackalloc` 버퍼나 `ArrayPool` 대여 버퍼를 `Send`로 넘기는 코드는 거의 항상 버그다.**
확신이 없으면 `SendCopied`를 쓴다.

### (3) `ReadOnlySequence<byte>`는 세그먼트에 걸칠 수 있다

```csharp
BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);   // 금지 — 헤더가 쪼개지면 깨진다

Span<byte> buffer = stackalloc byte[HeaderSize];             // OK
header.CopyTo(buffer);
BinaryPrimitives.ReadInt16LittleEndian(buffer);
```

> 셋 다 `SuperSocketLite2` 패키지의 애널라이저가 빌드 경고로 잡는다
> (`SSL001`·`SSL002`·`SSL005` / `SSL003` / `SSL004`). **경고를 억제하지 말고 고친다.**
> 규칙 전체는 `Docs/agent/analyzers.md`.

## 1. 어디를 읽나

깊이 있는 내용은 아래 문서에 있다. **필요한 것만 골라 읽는다.**

| 필요한 것 | 문서 |
|---|---|
| 타입 이름 · 네임스페이스 · 시그니처 · `ServerConfig` 기본값 | `Docs/agent/api-cheatsheet.md` |
| 주의 사항 전체 8가지 + 리뷰 체크리스트 | `Docs/agent/cautions.md` |
| 복사해 쓰는 코드 11종 | `Docs/agent/recipes.md` |
| 만든 서버가 실제로 도는지 확인 | `Docs/agent/verify.md` |
| 빌드에 `SSL0xx` 경고가 떴다 | `Docs/agent/analyzers.md` |

저장소 밖(패키지만 참조하는 프로젝트)이라 위 경로가 없으면 여기서 본다 —
<https://github.com/jacking75/SuperSocketLite2/tree/main/Docs/agent>

**`Docs/*.html`은 열지 않는다.** `Library_Architecture.html` 같은 단독 실행 HTML은
파일 하나가 650KB라 컨텍스트를 통째로 날린다. 같은 내용이 `Docs/agent/*.md`에 있다.

## 2. 서버 하나를 만드는 순서

1. **패키지 참조** — `dotnet add package SuperSocketLite2`
   (저장소 안에서 작업 중이면 `SuperSocketLite/SuperSocketLite.csproj`를 `ProjectReference`)
2. **`IRequestInfo` 구현** — 파싱된 패킷 하나를 표현한다. `Body`는
   `ReadOnlySequence<byte>`로 들고, 인스턴스는 필터가 돌려 쓴다
3. **`ReceiveFilter`** — 길이 프리픽스면 `FixedHeaderReceiveFilter<T>`,
   고정 길이면 `FixedSizeReceiveFilter<T>`를 상속한다. 그 둘로 안 되는 프로토콜일 때만
   `IReceiveFilter<T>`를 직접 구현한다
4. **`AppSession<TSession, TRequestInfo>` 상속** — 세션당 상태를 여기 둔다
5. **`AppServer<TSession, TRequestInfo>` 상속** — 생성자에서
   `DefaultReceiveFilterFactory<TFilter, TRequestInfo>`를 `base`에 넘기고 이벤트 3개를 건다
6. **`ServerConfig` → `Setup()` → `Start()`** — `Setup`이 `false`면 `Start`하지 않는다
7. **`StopAsync(drainTimeout)`로 종료** — `Stop()`은 큐에 남은 응답을 버린다

전체 코드는 `Docs/agent/recipes.md` § 1에 있다. 그대로 복사해서 이름만 바꾸면 된다.

## 3. 자주 틀리는 것들

| 증상 | 원인 |
|---|---|
| `FixedHeaderReceiveFilter`를 못 찾는다 | `SuperSocketLite.SocketEngine.Protocol`이다. 나머지 프로토콜 타입은 `SocketBase.Protocol` |
| 접속 직후 첫 패킷에서 `NullReferenceException` | `NewSessionConnected`와 `NewRequestReceived`가 동시에 돈다. `SyncSessionConnectedEvent = true` |
| 브로드캐스트에서 `NullReferenceException` | `GetAllSessions()`는 `null`을 돌려줄 수 있다 |
| 부하가 올라가면 패킷 내용이 섞인다 | §0 (1) 또는 (2) 위반 |
| 큰 패킷이 안 들어온다 | `MaxRequestLength` 기본값이 1024다 |
| 접속 수가 안 맞는다 | `MaxConnectionNumber` 초과분은 `NewSessionConnected` 없이 끊긴다 |
| 느린 클라이언트 하나가 전체를 세운다 | 브로드캐스트에 `Send` 대신 `TrySendCopied`를 쓴다 |
| 타임아웃 후 소켓이 안 닫힌다 | `SendEndWhenSendingTimeOut()`을 `Close()`보다 먼저 부른다 |

## 4. 코딩 컨벤션

저장소 안에서 작업한다면 `.editorconfig`가 빌드로 강제한다(`EnforceCodeStyleInBuild=true`).

- private/internal 인스턴스 필드는 `_camelCase`, static은 `s_`, const는 PascalCase
- 중괄호는 Allman, 한 줄 조건문에도 중괄호를 붙인다
- `private`를 생략하지 않고 적는다
- 매직 넘버 금지, 하드코딩 문자열 대신 `nameof(...)`

전체는 `.claude/conventions.md`.

## 5. 끝내기 전 자체 점검

작업을 마쳤다고 보고하기 전에 이 목록을 직접 확인한다.

- [ ] `RequestInfo`나 `Body`가 핸들러 밖으로 나가는 경로가 없다 (필드/캡처/큐/`async` 미대기)
- [ ] `stackalloc` · `ArrayPool` 버퍼를 `Send`가 아니라 `SendCopied`로 보냈다
- [ ] 필터에서 `.First.Span` 대신 `CopyTo(Span)`을 썼다
- [ ] `GetAllSessions()` / `GetSessions()` 결과의 `null`을 검사했다
- [ ] `Setup()` 반환값을 확인하고 `false`면 `Start()`하지 않는다
- [ ] `MaxRequestLength`가 실제 최대 패킷보다 크다
- [ ] 빌드 경고 0개
- [ ] 서버를 띄우고 패킷 왕복을 실제로 확인했다 (`Docs/agent/verify.md`)

마지막 항목을 건너뛰고 "구현 완료"라고 보고하지 않는다. 컴파일이 되는 것과
서버가 도는 것은 이 라이브러리에서 특히 다르다.
