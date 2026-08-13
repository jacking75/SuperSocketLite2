# 코드 간결화 계획

> # ✅ 전 단계 완료 (2026-08-13). 이 문서는 **이력**이다 — 다시 실행하지 말 것.
>
> A~D 전 단계를 구현했다. 결과: 라이브러리 **11,203줄 → 6,603줄(-41%), 85개 파일 → 67개**,
> 버전 0.90.0 → 0.91.0. 커밋 `9f8788b`(A-2) ~ `e2e2521`(C-3).
>
> - 실행 순서는 7장 권장안과 다르다. **D-5를 C-1보다 먼저** 했다 — D-5가 필터 3종을
>   통째로 지워서 C-1에서 sequence로 옮길 필터가 6종 → 3종으로 줄기 때문이다.
> - 6장 D 표의 O/X 판단대로 진행했다. D-6(IActiveConnector), D-8(LastActiveTime)은 남겼다.
> - 아래 본문의 줄 수·줄 번호·예상 감소량은 **작업 전 기준**이라 현재 코드와 맞지 않는다.
> - 최종 결과 요약은 `.claude/tasks.md`의 TASK-22, public API 변경은 `README.md`의
>   "0.91 마이그레이션 가이드"를 본다.

작성일: 2026-08-13 (KST)
대상: `SuperSocketLite/` 라이브러리 소스 **85개 파일 / 11,197줄**
기준 커밋: `072aede`
빌드 기준선: `dotnet build -c Release` → **경고 0, 오류 0**
회귀 테스트 기준선: 36개 전부 통과 (`dotnet run --project Test/SuperSocketLiteRegressionTests -c Release`)

> 이 문서는 **다음 세션의 작업 지시서**다. 위에서부터 순서대로 진행하면 된다.
> 각 항목은 독립 커밋 단위로 쪼개 두었다.

---

## 0. 결론 먼저

| 단계 | 내용 | 예상 감소 | 동작 변화 | 판단 필요 |
|---|---|---|---|---|
| **A** | 기계적 정리 (주석·using·프로퍼티·죽은 코드) | **약 -1,800줄** | 없음 | 아니오 |
| **B** | 중복 코드 통합 | 약 -300줄 | 없음 | 아니오 |
| **C** | 구조 단순화 (수신 필터 이중 경로 제거가 핵심) | 약 -1,400줄 | public API 변경 | 일부 |
| **D** | 기능 축소 | 최대 -1,900줄 | 기능 제거 | **예** |

- **A+B+C만 해도 11,197줄 → 약 7,300줄 (-35%)** 이고, 그중 진짜 어려운 건 C-1 하나뿐이다.
- D는 "이 기능을 앞으로도 쓸 것인가"에 대한 답이 있어야 진행할 수 있다. → [6장](#6-단계-d--기능-축소-사용자-판단-필요)

---

## 1. 현재 상태 측정

```
전체                     11,197줄
├─ XML 문서 주석 (///)    3,002줄  (27%)
├─ 빈 줄                  1,892줄  (17%)
├─ 일반 주석 (//)           234줄  ( 2%)
└─ 실제 코드              6,069줄  (54%)
```

### 큰 파일 순

| 파일 | 줄수 | 문제 |
|---|---|---|
| `SocketBase/AppServerBase.cs` | 1,187 | 갓 클래스 — 설정·수명주기·메트릭·이벤트·세션팩토리·능동접속이 한 곳에 |
| `SocketBase/AppSession.cs` | 999 | 송신 재시도 루프 3벌 + 수신 필터 이중 경로 |
| `SocketSession.cs` | 896 | 상태 머신 + 송신 큐 + 파이프 + 로깅이 한 파일 |
| `SocketBase/AppServer.cs` | 428 | 타이머 3벌이 같은 패턴 3번 반복 |
| `AsyncSocketSession.cs` | 409 | |
| `Protocol/FixedHeaderReceiveFilter.cs` | 307 | `FixedHeaderSequenceReceiveFilter`(186줄)와 같은 일을 다른 방식으로 |

### XML 주석 실태

| 패턴 | 개수 |
|---|---|
| 단독 줄 `/// <summary>` + `/// </summary>` | 623 + 622 = **1,245줄** |
| `/// <param name="offset">The offset.</param>` 류 동어반복 | **194개** |
| 빈 `/// <returns></returns>` | **75개** |
| `<value>` 블록 (거의 전부 summary 재탕) | 65개 ≈ 195줄 |
| `Initializes a new instance of the ... class.` | 51개 |
| **정보가 있는 `<remarks>`** | **33개** ← 이것만 지키면 된다 |

즉 문서 주석 3,002줄 중 **정보가 있는 건 `<remarks>` 33블록과 일부 `<summary>` 본문뿐**이다.

### 네이밍

`.claude/conventions.md`는 private 필드를 `_camelCase`, static을 `s_`로 규정한다.
실제 코드에는 **`m_` 접두사가 678곳 / 29개 파일**에 남아 있고, 같은 파일 안에서
`m_State`와 `_receivePipe`가 섞여 있다(`SocketSession.cs:44` vs `:48`).

---

## 2. 원칙

### 간결하다 = 읽을 것이 적다

우선순위 순:

1. **없앤다** — 안 쓰는 코드, 중복 경로
2. **합친다** — 같은 일을 하는 것 두 개
3. **줄인다** — 보일러플레이트
4. **나눈다** — 1,000줄 클래스

### 건드리면 안 되는 것

아래는 짧아 보이지만 **의도적으로 그렇게 쓰인 코드**다. 주석이 이유를 설명하고 있으니
"간결화" 명목으로 지우지 말 것.

| 위치 | 이유 |
|---|---|
| `SocketSession.cs:449-546` `StartSend`/`OnSendingCompleted`의 이중 `Count` 검사 | 큐 카운터와 채널이 2단계로 갱신되는 레이스를 흡수한다 |
| `SocketSession.cs:488-499` `ReturnPooledSendBuffers` 호출 시점 | 소켓이 배열을 읽는 중에 풀 반납하면 버퍼가 두 번 대여된다 |
| `AppSession.cs:66` `volatile bool m_Connected` | ARM에서 무한 스핀 방지 |
| `AppServerBase.cs:290` 스레드풀 설정의 CAS | 서버 인스턴스 2개 동시 초기화 시 이중 설정 방지 |
| `SocketAsyncEventArgsProxy.cs:38-49` IOCP 인라인 처리 | 패킷당 스레드 홉 + 할당 3개를 아낀다 |
| `AsyncSocketSession.cs:154-188` `StartReceive`의 동기완료 드레인 루프 | 재귀 대신 루프 — 스택 오버플로 방지 |
| `TcpSocketServerBase.cs:61` `LingerOption(false, 0)` | abortive close가 아님. 주석대로 유지 |

---

## 3. 단계 A — 기계적 정리 (동작 변화 없음)

> 이 단계는 **컴파일 결과가 바뀌지 않아야 한다.** 커밋 전 `git diff --stat`으로 줄 수만 줄었는지 확인.

### A-1. XML 문서 주석 압축 — 약 -1,500줄 ⭐ 최대 효과

규칙 4개만 적용한다.

**(1) 한 문장 summary는 한 줄로**

```csharp
// before (3줄)
/// <summary>
/// Gets the port.
/// </summary>
public int Port { get; }

// after (1줄)
/// <summary>Gets the port.</summary>
public int Port { get; }
```
→ 623블록 중 대부분. **약 -1,050줄**

**(2) 동어반복 `<param>` / 빈 `<returns>` / 재탕 `<value>` 삭제**

```csharp
// 삭제 대상 (정보량 0)
/// <param name="offset">The offset.</param>
/// <param name="length">The length.</param>
/// <returns></returns>
/// <value>The size of the receive buffer.</value>
```
→ 194 + 75 + 195 = **약 -460줄**

**(3) `Initializes a new instance of the <see cref="X"/> class.` 는 삭제**

생성자 이름만 봐도 아는 내용이다. 인자에 설명이 필요하면 `<param>`만 남긴다. → 51곳

**(4) `<remarks>` 33개는 한 글자도 건드리지 않는다**

스레드 모델·버퍼 수명·플랫폼 차이를 설명하는, 이 저장소에서 가장 값진 문서다.

**대상 파일 (효과 순)**: `IServerConfig.cs`(180줄 중 130줄이 주석), `ServerConfig.cs`,
`IAppServer.cs`, `IAppSession.cs`, `ISocketSession.cs`, `ILog.cs`, `RequestInfo.cs`,
`AppServerBase.cs`, `AppSession.cs`

---

### A-2. `ImplicitUsings` 활성화 — 약 -110줄

`SuperSocketLite.csproj`:
```xml
<ImplicitUsings>enable</ImplicitUsings>
```
`using System;` / `System.Collections.Generic` / `System.IO` / `System.Linq` /
`System.Threading` / `System.Threading.Tasks` 선언 **62줄**이 바로 사라지고,
남은 `using`도 파일당 2~3줄로 줄어든다(현재 총 191줄).

같이 할 것:
- 사용하지 않는 `using` 제거 (`AppServerBase.cs`의 `System.Text`, `AppServer.cs`의 `System.Linq` 등)
- `AppServer.cs`의 `System.Threading.Timer` / `System.Threading.Tasks.Parallel` 완전 수식명 → `using`으로 정리 (`AppServer.cs:203,208,233,264,294,334,341`)

---

### A-3. 프로퍼티·필드 보일러플레이트 — 약 -150줄

```csharp
// before
private string m_Name = null!;
public string Name
{
    get { return m_Name; }
}

// after
public string Name { get; private set; } = null!;
```

대상: `get { return ...; }` 형태 **32곳**.
대표: `AppServerBase.cs`의 `State`/`Name`/`TotalHandledRequests`/`Listeners`,
`AppSession.cs`의 `Connected`/`LocalEndPoint`/`RemoteEndPoint`/`Logger`/`Config`,
`SocketSession.cs`의 `Client`, `FixedSizeReceiveFilter.cs`의 `Size`,
`CountSpliterReceiveFilter.cs`의 `LeftBufferSize`/`NextReceiveFilter`.

---

### A-4. 죽은 코드 제거 — 약 -60줄

| 대상 | 근거 |
|---|---|
| `Common/StringExtension.cs` (33줄) | 라이브러리·튜토리얼·테스트 어디서도 호출 없음. `int.TryParse`로 충분 |
| `FixedHeaderReceiveFilter.cs:233-236` | `if (toBeCopied)`와 `else`의 본문이 **완전히 동일**한 죽은 분기 |
| `AppServerBase.cs:1001-1002` | 주석 처리된 로그 코드 |
| `AppServerBase.cs:30` `NullAppSession` | `default(TAppSession)!` = 그냥 `null`. 이름이 오해를 부른다 → `null` 직접 사용 |
| `AppServerBase.cs:1067-1069` | `/// Resets the session's security protocol.` — 아래 메서드와 무관한 잘못 붙은 주석 |
| `SocketBase/Async.cs` | `Task.Factory.StartNew(...).ContinueWith(OnlyOnFaulted)` → `Task.Run` + `try/catch`로 절반 |

---

### A-5. 네이밍 통일 + `.editorconfig` — 줄 수 변화 없음, 가독성 ⭐

1. `m_Xxx` → `_xxx` **678곳** (IDE 일괄 리네임으로 처리 가능)
2. `s_Meter` 등 static은 그대로 (`s_` 규칙 준수 중)
3. `Common/TheadPoolEx.cs` → `ThreadPoolEx.cs` (**오타**)
4. `.editorconfig`를 저장소 루트에 추가해 규칙을 고정한다:

```ini
[*.cs]
dotnet_naming_rule.private_fields_underscore.severity = warning
csharp_new_line_before_open_brace = all
csharp_indent_size = 4
dotnet_diagnostic.IDE0090.severity = warning   # new() 단순화
dotnet_diagnostic.IDE0028.severity = warning   # 컬렉션 초기화 단순화
```
그리고 csproj에 `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`.
→ **앞으로 다시 지저분해지는 것을 빌드가 막아 준다.** A-5는 이 계획에서 유일하게
"재발 방지" 성격을 가진 항목이므로 건너뛰지 말 것.

---

## 4. 단계 B — 중복 코드 통합 (동작 변화 없음)

### B-1. 송신 재시도 루프 3벌 → 1벌 — 약 -70줄

`AppSession.cs:391-424`, `:457-490`, `:533-566` 세 곳이 **글자 하나까지 같은** 스핀+타임아웃 루프다.

```csharp
// 통합 후
void SendWithRetry<T>(T payload, Func<T, bool> trySend)
{
    if (!m_Connected) return;
    if (trySend(payload)) return;

    var sendTimeOut = Config.SendTimeOut;
    if (sendTimeOut < 0) throw new TimeoutException("The sending attempt timed out");

    var deadline = Environment.TickCount64 + sendTimeOut;
    var spinWait = new SpinWait();

    while (m_Connected)
    {
        spinWait.SpinOnce();
        if (trySend(payload)) return;
        if (sendTimeOut > 0 && Environment.TickCount64 >= deadline)
            throw new TimeoutException("The sending attempt timed out");
    }
}
```
> `SendCopied(ReadOnlySpan<byte>)`은 `ref struct`라 제네릭에 못 넣는다.
> → `ReadOnlySpan` 버전만 별도 유지하거나, 내부에서 풀 버퍼로 한 번 복사한 뒤
> `ArraySegment` 경로에 합류시킨다. **후자를 권장** (한 벌로 완전히 통일된다).

### B-2. `ExecuteCommand` 3벌 → 1벌 — 약 -20줄

`AppServerBase.cs:864`(protected virtual), `:907`(internal), `:917`(명시적 인터페이스 구현).
뒤의 두 개는 캐스팅만 하고 앞 것을 부른다. `internal` 하나만 남기고 인터페이스 구현이 그걸 부르게 한다.

### B-3. `IActiveConnector.ActiveConnect(EndPoint)` 3벌 → 기본 인터페이스 메서드 — 약 -30줄

`AppServerBase.cs:1172`, `AsyncSocketServer.cs:204`, `UdpSocketServer.cs:226`이
전부 `ActiveConnect(target, null)`을 부르는 1줄짜리 오버로드다.
→ `IActiveConnector` 인터페이스에 default 구현으로 올리면 3곳이 사라진다.

### B-4. 타이머 3벌 공통화 — 약 -60줄

`AppServer.cs`의 `StartSessionSnapshotTimer` / `StartClearSessionTimer` /
`StartCollectSendSessionTimer`(203~351)와, `Stop()`(389-425)의 **완전히 동일한 해제 블록 3벌**.

```csharp
// 시작
Timer StartPeriodicTimer(TimerCallback body, int intervalMillSec)
{
    var gate = new object();
    return new Timer(_ =>
    {
        if (!Monitor.TryEnter(gate)) return;
        try { body(null); } finally { Monitor.Exit(gate); }
    }, null, intervalMillSec, intervalMillSec);
}

// 해제
static void StopTimer(ref Timer? timer)
{
    timer?.Change(Timeout.Infinite, Timeout.Infinite);
    timer?.Dispose();
    timer = null;
}
```

### B-5. `ToInt32BufferSize` 3벌 → 1벌 — 약 -15줄

`FixedHeaderReceiveFilter.cs:303`, `FixedSizeReceiveFilter.cs:191`,
`FixedHeaderSequenceReceiveFilter.cs:153`에 같은 private static 메서드가 3번.
(단계 C-1을 하면 자동으로 해결된다.)

### B-6. SAEA 거부 경로 중복 — 약 -25줄

`AsyncSocketServer.cs:62-97`에 "풀 고갈 → 로그 → 소켓 닫기 → null 반환"이 3번 반복.
지역 함수 `RejectClient(client, reason)` 하나로 합친다.

### B-7. `UdpSocketServer.cs:187-200` — 약 -8줄

`if (appSession == null) { 생성; ProcessRequest(); } else { ProcessRequest(); }` →
생성 실패만 조기 반환하고 `ProcessRequest`는 한 번만 호출.

---

## 5. 단계 C — 구조 단순화 (핵심)

### C-1. 수신 필터 이중 경로 단일화 — 약 **-1,800줄** ⭐⭐ 이 문서에서 가장 큰 건

#### 문제

지금 모든 수신 필터는 **완전히 다른 알고리즘 두 벌**을 구현하고 있다.

| 경로 | 진입점 | 필요한 부속품 |
|---|---|---|
| 레거시 `byte[]` | `IReceiveFilter<T>.Filter(byte[], offset, length, toBeCopied, out rest)` | `IOffsetAdapter`, `ArraySegmentList`, `BinaryUtil.SearchMark`, `SearchMarkState`, `ReceiveFilterBase`, AppSession의 캐리 버퍼(`_filterBuffer`) |
| zero-copy | `ISequenceReceiveFilter<T>.Filter(ReadOnlySequence<byte>, out consumed, out examined)` | `SequenceReader<byte>` (BCL) |

`TerminatorReceiveFilter`가 대표적이다 — byte[] 경로는 **121줄**의 오프셋 산술
(`Buffer.BlockCopy(readBuffer, offset - m_ParsedLengthInBuffer, readBuffer, offset - m_OffsetDelta, ...)`)
인데, sequence 경로는 **18줄**이고 하는 일이 같다.

```csharp
// TerminatorReceiveFilter.cs:191-208 — 이게 전부다
var reader = new SequenceReader<byte>(buffer);
if (!reader.TryReadTo(out ReadOnlySequence<byte> body, m_SearchState.Mark, advancePastDelimiter: true))
{
    consumed = buffer.Start; examined = buffer.End;
    return NullRequestInfo;
}
consumed = examined = reader.Position;
var data = SequenceFilterHelper.AsArraySegment(body);
return ProcessMatchedRequest(data.Array!, data.Offset, data.Count);
```

게다가 `FixedHeaderSequenceReceiveFilter`는 **sequence로 구현한 뒤 byte[] 경로를
다시 흉내내려고 `m_LegacyBuffer` 46줄**을 얹었다. 방향이 완전히 뒤집혀 있다.

#### 방침

**`IReceiveFilter<T>`의 `Filter(byte[], ...)`를 없애고 `ReadOnlySequence` 하나로 통일한다.**

```csharp
public interface IReceiveFilter<TRequestInfo> where TRequestInfo : IRequestInfo
{
    TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);
    IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; }
    FilterState State { get; }
    void Reset();
}
// ISequenceReceiveFilter<T> 는 삭제 (이제 이게 유일한 인터페이스이므로)
```
> `LeftBufferSize`도 삭제 대상이다. MaxRequestLength 검사는 이미 sequence 경로에서
> `sequence.Slice(consumed).Length`로 직접 계산하고 있다(`AppSession.cs:891`).

#### 작업 순서

1. **UDP를 sequence로 옮긴다** — 유일한 byte[] 실사용처다.
   `UdpSocketServer.cs:128,195,199` → `new ReadOnlySequence<byte>(receivedData, offset, count)` 로
   감싸서 넘긴다. 데이터그램은 항상 단일 세그먼트라 `consumed`/`examined`는 무시하면 된다.
2. **필터 6종을 sequence 전용으로 축소** — 각 파일에서 byte[] 메서드를 지운다.
3. **`FixedHeaderReceiveFilter` ↔ `FixedHeaderSequenceReceiveFilter` 병합** — 이름은
   `FixedHeaderReceiveFilter` 하나로 남기고, `ResolveRequestInfo(ReadOnlySequence header, ReadOnlySequence body)`
   시그니처를 채택한다. `m_LegacyBuffer` 46줄이 통째로 사라진다.
   > **호환 주의**: 기존 게임 서버들이 `GetBodyLengthFromHeader(byte[], int, int)` /
   > `ResolveRequestInfo(ArraySegment<byte>, byte[], int, int)`를 오버라이드하고 있다
   > (`.claude/architecture.md`의 예제 그대로). **마이그레이션 예제를 README에 반드시 추가할 것.**
4. **AppSession 수신부 정리** — `FilterRequest`(652-707), `ProcessRequest(byte[])`(719-748),
   캐리 버퍼 할당/확장 블록(763-841), `_filterBuffer`/`CompleteReceivePipe`가 전부 사라지고
   `ProcessSequenceRequest`(844-909) 하나만 남는다.
5. **부속품 파일 삭제**

#### 삭제되는 파일

| 파일 | 줄수 |
|---|---|
| `Common/ArraySegmentList.cs` | 257 |
| `Common/BinaryUtil.cs` | 180 |
| `Common/SearchMarkState.cs` | 34 |
| `SocketBase/Protocol/ReceiveFilterBase.cs` | 120 |
| `SocketBase/Protocol/IOffsetAdapter.cs` | 12 |
| `SocketBase/Protocol/ISequenceReceiveFilter.cs` | 22 |
| **소계** | **625** |

#### 줄어드는 파일

| 파일 | 현재 | 예상 |
|---|---|---|
| `FixedHeaderReceiveFilter.cs` + `FixedHeaderSequenceReceiveFilter.cs` | 493 | ~120 |
| `TerminatorReceiveFilter.cs` | 296 | ~110 |
| `CountSpliterReceiveFilter.cs` | 267 | ~120 |
| `BeginEndMarkReceiveFilter.cs` | 230 | ~110 |
| `FixedSizeReceiveFilter.cs` | 195 | ~85 |
| `AppSession.cs` (수신부) | ~190 | 0 |
| **소계** | | **약 -1,130** |

**C-1 합계 약 -1,755줄.** 부수 효과로 `.claude/cautions.md`의
"`RawDataReceived`를 등록하면 zero-copy 경로가 꺼진다"는 주의 사항도 사라진다
(→ D-2와 함께 처리).

---

### C-2. `Setup` 오버로드 정리 — 약 -80줄, **버그 함정 제거** ⭐

#### 문제: 이름이 같은 `Setup`이 5개

```csharp
public bool Setup(int port)                                                    // :381
public bool Setup(string ip, int port, ISocketServerFactory? = null, ...)      // :458
public bool Setup(IServerConfig config, ISocketServerFactory? = null, ...)     // :406
public bool Setup(IRootConfig, IServerConfig, ISocketServerFactory? = null,...)// :422  ← 진짜 진입점
protected virtual bool Setup(IRootConfig rootConfig, IServerConfig config)     // :267  ← 파생 클래스용 훅, 기본 구현은 return true
```

**`Setup(rootConfig, config)`를 인자 2개로 호출하면 C# 오버로드 규칙상
`:267`의 훅(아무것도 안 하고 `true` 반환)이 선택된다.**
서버가 아무 준비 없이 "성공"을 반환한다.

실제로 저장소의 **모든 호출부 20여 곳이 예외 없이 `logFactory:` 명명 인자를 붙이고 있다.**
우연이 아니라 이 함정을 피하려고 그렇게 쓴 것이다.

```csharp
Setup(new RootConfig(), _config, logFactory: new ConsoleLogFactory());
//                               ^^^^^^^^^^^ 이게 없으면 조용히 no-op
```

#### 방침

1. `protected virtual bool Setup(IRootConfig, IServerConfig)` → **`OnSetup(...)`으로 개명**
   (`OnStarted`/`OnStopped`와 이름 규칙도 맞는다)
2. 편의 오버로드 `Setup(int port)` / `Setup(string ip, int port, ...)`는 **삭제** —
   저장소 어디서도 안 쓴다. `new ServerConfig { Ip = ..., Port = ... }`가 더 명확하다
3. `SetupBasic` / `SetupMedium` / `SetupAdvanced` / `SetupFinal` (272~374) 4단계 분해 정리
   - `SetupMedium`(317)은 필터 팩토리 대입 + 커넥션 필터 추가뿐
   - `SetupAdvanced`(333)는 `SetupListeners` 호출 한 줄
   → 한 개의 `Setup` 본문으로 펼치면 흐름이 훨씬 잘 보인다
4. **리플렉션 제거** (`AppServerBase.cs:302-306`)
   ```csharp
   // before — 같은 어셈블리인데 문자열로 찾고 있다
   var t = Type.GetType("SuperSocketLite.SocketEngine.SocketServerFactory, SuperSocketLite", true)!;
   socketServerFactory = (ISocketServerFactory)Activator.CreateInstance(t)!;

   // after
   socketServerFactory ??= new SocketServerFactory();
   ```

---

### C-3. `AppServerBase`(1,187줄) 분할 — 줄 수 변화 없음, 가독성 ⭐

부분 클래스(partial)로 쪼갠다. 파일 하나가 하나의 관심사만 담는다.

| 새 파일 | 옮길 내용 | 대략 |
|---|---|---|
| `AppServerBase.Setup.cs` | `Setup*` 전부, `SetupListeners`, `ParseIPAddress` | 250줄 |
| `AppServerBase.Metrics.cs` | `s_Meter` 이하 계측기 + `Record*` + `Total*` (130-223) | 120줄 |
| `AppServerBase.Events.cs` | `NewSessionConnected` / `SessionClosed` / `NewRequestReceived` / `RawDataReceived` | 200줄 |
| `AppServerBase.cs` | 수명주기(Start/Stop/StopAsync) + 세션 컨테이너 | 450줄 |

> 메트릭은 아예 `ServerMetrics` 클래스로 뽑아 `AppServerBase`가 필드 하나로 들고 있어도 된다.
> 그러면 `IAppServer`의 `RecordBytesReceived`/`RecordBytesSent`/... 5개도 인터페이스에서 뺄 수 있다.

---

### C-4. `SocketSession` 상태 머신 명료화 — 약 -30줄, 가독성 ⭐

#### (1) CloseReason을 상태 int에 인코딩하는 것을 그만둔다

```csharp
// 현재 (SocketSession.cs:685-695) — m_State 하나에 플래그와 이유를 겹쳐 넣는다
private const int m_CloseReasonMagic = 256;
int GetCloseReasonValue(CloseReason r)  => ((int)r + 1) * m_CloseReasonMagic;
CloseReason GetCloseReasonFromState()   => (CloseReason)(m_State / m_CloseReasonMagic - 1);
```
비트 배치가 주석(36-43)에만 있고 코드에는 없다. `Closed`(0x01000000) 비트가 서면
나눗셈 결과가 깨진다(현재는 호출 순서 덕에 드러나지 않을 뿐이다).

→ **`int m_CloseReasonCode` 필드를 따로 두고 `Interlocked.CompareExchange`로 한 번만 기록**한다.
`AddStateFlag(GetCloseReasonValue(reason))` 호출 3곳이 `TrySetCloseReason(reason)` 한 줄이 된다.

#### (2) 상태 조작 헬퍼 4개 정리

`AddStateFlag`(54) / `AddStateFlag(int,bool)`(59) / `TryAddStateFlag`(81) / `RemoveStateFlag`(101) —
CAS 루프가 4벌이다. `Interlocked.Or` / `Interlocked.And`(.NET 5+)로 대부분 한 줄이 된다.

```csharp
void RemoveStateFlag(int flag) => Interlocked.And(ref m_State, ~flag);
bool TryAddStateFlag(int flag) => (Interlocked.Or(ref m_State, flag) & flag) != flag;
```

#### (3) 중복 송신 API 정리

`SocketSession.cs:330-333`의 `TrySend(ReadOnlySpan<byte>)`는 `TrySendCopied`를 그대로 부르는
별칭이다. 어디서도 안 쓰므로 삭제.

---

### C-5. 쓰이지 않는 default 인터페이스 구현 제거 — 약 -50줄

`ISocketSession`의 구현체는 `SocketSession` **하나뿐**이고(→ `AsyncSocketSession`, `UdpSocketSession`),
그 하나가 아래를 전부 재정의한다. 즉 default 구현은 **절대 실행되지 않는다.**

| 위치 | 대상 |
|---|---|
| `ISocketSession.cs:122-125` | `TrySendCopied` default |
| `ISocketSession.cs:138-142` | `SendAsync` default |
| `ISocketSession.cs:149` | `IsSendIdle => true` default |
| `IAppServer.cs:110-120` | `RecordSessionRejected` / `RecordSendQueueFull` / `RecordSendError`의 `{ }` |

→ 전부 일반 인터페이스 멤버로 되돌린다. "기본 구현이 있다"는 오해가 사라진다.

---

## 6. 단계 D — 기능 축소 (사용자 판단 필요)

> **여기부터는 기능이 없어진다. 아래 표에 O/X를 표시해 주면 다음 세션에서 그대로 진행한다.**
>
> 참고: `TASK-20`(2026-08-13)에서 이 기능들은 **의도적으로 남긴 것**이다.
> 그때와 판단이 달라졌는지 확인이 필요하다.

| # | 기능 | 저장소 내 사용 | 제거 시 감소 | 진행? |
|---|---|---|---|---|
| D-1 | **CollectSend** (`CollectSendIntervalMillSec`) | **0곳** | 약 -180줄 | O |
| D-2 | **RawDataReceived** | **0곳** | 약 -60줄 | O |
| D-3 | **IConnectionFilter** | **0곳** | 약 -60줄 | O |
| D-4 | **ISocketServerFactory 주입** | **0곳** | 약 -80줄 | O |
| D-5 | **문자열 프로토콜 계열** | 튜토리얼 1개 | 약 -900줄 | O |
| D-6 | **IActiveConnector** (능동 접속) | 튜토리얼 2개 | 약 -140줄 | X |
| D-7 | `AppSession.Items` / `PrevCommand` / `CurrentCommand` / `LogCommand` | **0곳** | 약 -60줄 | O |
| D-8 | `AppSession.LastActiveTime` (DateTime 변환) | 테스트 1개 | 약 -25줄 | X |

### D-1. CollectSend

여러 Send를 모아 주기적으로 한 번에 보내는 기능. **한 기능이 6개 타입에 걸쳐 퍼져 있다.**

`ISocketSession.CollectSend/GetCollectSendData/CommitCollectSend` +
`IAppSession.GetCollectSendData/CommitCollectSend`(**셋 중 둘만 노출 — 이미 일관성이 깨져 있다**) +
`AppSession`(608-621) + `SocketSession`(247-260) + `Common/ReuseLockBaseBuffer.cs`(85줄) +
`AppServer.CollectSendSession` 타이머(203-259) + `ServerConfig.CollectSendIntervalMillSec`.

부가 이득: 켜면 `SyncSend=true`가 강제되는 숨은 부작용(`SocketSession.cs:158-162`)과,
`TODO.md`에 남아 있는 "`ReuseLockBaseBuffer.Commit`의 압축 조건이 의도와 반대로 보인다"는
미해결 항목이 함께 사라진다.

### D-2. RawDataReceived

`IRawDataProcessor<T>` + `AppServerBase`(810-845) + `AppSession.FilterRequest`의 훅.
**C-1을 하면 어차피 byte[] 경로가 없어지므로 같이 정리하는 것이 자연스럽다.**
필요하다면 `ReadOnlySequence<byte>`를 받는 형태로 되살릴 수 있다.

### D-5. 문자열 프로토콜 계열 — 가장 큰 결정

이 라이브러리의 실사용은 **바이너리 패킷 게임 서버**다(`.claude/architecture.md` 명시).
문자열 명령 프로토콜용으로만 존재하는 것:

`TerminatorReceiveFilter`(296) + `TerminatorReceiveFilterFactory`(62) +
`CountSpliterReceiveFilter`(267) + `CountSpliterReceiveFilterFactory`(76) +
`BeginEndMarkReceiveFilter`(230) + `CommandLineReceiveFilterFactory`(39) +
`BasicRequestInfoParser`(67) + `StringRequestInfo`(45) + `IRequestInfoParser` +
`AppServer`/`AppSession`의 비제네릭·문자열 변형(약 120)

= **약 1,200줄** (C-1 적용 후에는 약 900줄).

- 남기면: 진입 예제(`SwitchReceiveFilter` 튜토리얼)와 범용성 유지
- 없애면: `AppServer` 3계층(`AppServer` / `AppServer<T>` / `AppServer<T,TReq>`)이
  **`AppServer<T,TReq>` 하나로** 줄고, `AppSession`도 3계층 → 1계층이 된다.
  타입 계층이 절반이 되므로 **체감 간결도는 줄 수 이상**이다.

### D-6. IActiveConnector

서버가 다른 서버에 능동 접속하는 기능. `GateServer_GameServer`, `ChatServerEx`가 쓴다.
게이트↔게임 서버 구조를 계속 쓸 거면 남긴다.
남기더라도 B-3(default 인터페이스 메서드)으로 3벌 중복은 제거한다.

---

## 7. 권장 실행 순서

각 단계 끝에서 **반드시** 빌드 + 회귀 테스트를 돌리고 커밋한다.

```
1. A-2  ImplicitUsings + using 정리            (가장 안전, 워밍업)
2. A-5  .editorconfig 추가 + m_ → _ 일괄 리네임 (이후 작업이 규칙 위에서 진행되도록 먼저)
3. A-4  죽은 코드 제거
4. A-3  프로퍼티 보일러플레이트
5. A-1  XML 주석 압축                          (파일 단위로 쪼개 여러 커밋 권장)
   ── 여기까지 약 -1,800줄, 동작 변화 0 ──
6. B-1 ~ B-7  중복 통합                        (항목당 1커밋)
   ── 여기까지 약 -2,100줄 ──
7. C-5  쓰이지 않는 default 구현 제거
8. C-2  Setup 정리 (버그 함정 제거 — 우선순위 높음)
9. C-4  SocketSession 상태 머신
10. C-1 수신 필터 단일화                        ⭐ 가장 큰 작업, 단독 세션 권장
11. C-3 AppServerBase 분할                     (C-1/C-2 끝난 뒤에 해야 충돌이 적다)
   ── 여기까지 약 -3,900줄, 11,197 → 약 7,300 (-35%) ──
12. D   사용자 판단 후 진행
```

**C-1은 다른 작업과 섞지 말 것.** 필터 6종 + AppSession + UDP + 튜토리얼 2개를 동시에
건드리므로, 다른 변경이 섞이면 회귀 원인 추적이 어려워진다.

---

## 8. 검증

```powershell
# 1. 라이브러리 빌드 — 경고 0, 오류 0 유지
dotnet build C:\github_dev\SuperSocketLite2\SuperSocketLite\SuperSocketLite.csproj -c Release

# 2. 회귀 테스트 36개
dotnet run --project Test\SuperSocketLiteRegressionTests -c Release

# 3. 튜토리얼 전체 빌드 (public API 변경 시 필수)
dotnet build C:\github_dev\SuperSocketLite2\Tutorials\AllProjects.sln -c Release

# 4. 부하 테스트 (C-1 이후 반드시)
dotnet run --project Test\LoadTest\SuperSocketLite.LoadTest.Server -c Release
```

### 단계별 판정 기준

| 단계 | 통과 조건 |
|---|---|
| A | 빌드 경고 0 + 테스트 36/36 + **IL 동등** (`git diff`에 코드 변경이 없어야 함) |
| B | 빌드 경고 0 + 테스트 36/36 |
| C-1 | 테스트 36/36 + **필터 6종 각각에 대해 "여러 세그먼트로 쪼개진 요청" 테스트 존재** |
| C-2 | 튜토리얼 전체 빌드 통과 (Setup 시그니처가 바뀌므로) |
| D | 삭제한 기능을 쓰던 튜토리얼도 함께 정리 |

### C-1 전에 추가해야 할 테스트

현재 sequence 경로 테스트는 `Terminator`/`BeginEndMark`/`CountSpliter`/`FixedHeaderSequence`에만 있다.
byte[] 경로를 지우기 전에 **아래 2개를 먼저 추가**해서 동작 동등성을 고정한다.

1. `FixedSizeReceiveFilter` — 요청이 3개 세그먼트로 쪼개져 도착
2. `FixedHeaderReceiveFilter` — 헤더가 세그먼트 경계에 걸침 + 바디 길이 0 + 파이프라이닝(요청 3개 연속)

---

## 9. 효과 요약

| 구분 | 현재 | A+B+C 후 | A+B+C+D 후 |
|---|---|---|---|
| 전체 줄수 | 11,197 | **약 7,300** | 약 5,400 |
| XML 주석 | 3,002 | 약 1,300 | 약 1,000 |
| 실제 코드 | 6,069 | 약 4,600 | 약 3,400 |
| 소스 파일 | 85 | 약 79 | 약 65 |
| 수신 필터 인터페이스 | 2벌 | **1벌** | 1벌 |
| `AppServer` 타입 계층 | 3단 | 3단 | **1단** |
| `AppServerBase.cs` | 1,187줄 | 4개 파일 / 최대 450줄 | 동일 |

> 각 단계의 감소량은 서로 겹치는 부분이 있다(C-1이 지우는 파일의 주석은 A-1에도 계산됨).
> 위 합계는 중복을 제거한 추정치다.

---

## 10. 문서 갱신 (작업 완료 후)

간결화가 끝나면 아래 문서도 함께 고쳐야 한다. **코드만 고치고 문서를 두면 그게 더 헷갈린다.**

| 문서 | 갱신 내용 |
|---|---|
| `.claude/architecture.md` | "수신 필터 두 경로" 표 삭제, `ReceiveFilter` 구현 예제를 sequence 버전으로 교체 |
| `.claude/cautions.md` | "`RawDataReceived` 등록 시 zero-copy 꺼짐" 항목 삭제(C-1), CollectSend 항목(D-1) |
| `.claude/conventions.md` | `.editorconfig`로 강제됨을 명시 |
| `README.md` | 필터 마이그레이션 가이드 추가 (**필수** — 외부 게임 서버가 영향받는다) |
| `.claude/tasks.md` | 완료 이력 추가 |
| `TODO.md` | `ReuseLockBaseBuffer.Commit` 미해결 항목 처리(D-1로 소멸 또는 별도 수정) |
