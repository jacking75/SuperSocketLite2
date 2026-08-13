# 개선 작업 목록

코드 변경 후 반드시 `dotnet build` 성공을 확인한다.
개발 중이므로 public API는 자유롭게 바꿔도 된다 (하위 호환 제약 없음).

> **이 문서는 이력용이다.** 현행 작업 목록은 저장소 루트의 `TODO.md`를 본다.
> TODO.md의 TODO-01 ~ TODO-19는 2026-08-12에 모두 완료되었다.

---

## 완료된 태스크

### TASK-22: 코드 간결화 (`SIMPLIFY.md` A~D 전 단계) — ✅ 완료 (2026-08-13)

라이브러리 **11,203줄 → 6,603줄 (-41%), 85개 파일 → 67개**. 버전 0.90.0 → 0.91.0.
단계마다 빌드 경고 0 + 회귀 테스트 + 튜토리얼 빌드를 확인하고 개별 커밋했다.

| 단계 | 내용 | 줄수 |
|---|---|---|
| A-2 | `ImplicitUsings` 활성화, using 98줄 제거 | 11,203 → 11,068 |
| A-5 | `m_Xxx` → `_xxx` 735곳, `.editorconfig` + `EnforceCodeStyleInBuild` 도입 | 11,068 |
| A-4 | 죽은 코드 제거(`StringExtension`, 죽은 분기, `NullAppSession`, `Async.AsyncRun` 재작성) | 11,026 |
| A-3 | `get { return ...; }` 32곳 → 식 본문 프로퍼티 | 10,916 |
| A-1 | XML 주석 압축(3,002줄 → 1,209줄). `<remarks>` 33개는 보존 | 9,145 |
| B | 중복 통합 7건(송신 재시도 루프, 타이머, SAEA 거부 경로 등) | 9,118 |
| C-5·C-2 | 실행되지 않는 default 구현 제거, `Setup` 정리(`OnSetup` 개명으로 no-op 함정 제거) | 9,060 |
| C-4 | `SocketSession` 상태 머신: CloseReason 인코딩 분리, CAS 루프 4벌 → 1벌 | 9,060 |
| D-5 | 문자열 프로토콜 계열 제거. `AppServer`/`AppSession` 3단 계층 → 1단 | 8,072 |
| D-1,2,3,4,7 | CollectSend / RawDataReceived / IConnectionFilter / 팩토리 주입 / Items 제거 | 7,720 |
| C-1 | **수신 필터 이중 경로 단일화** — `IReceiveFilter`가 sequence 전용 | 6,564 |
| C-3 | `AppServerBase`를 partial 4개로 분할 | 6,603 |

- **남긴 기능**: `IActiveConnector`(D-6), `AppSession.LastActiveTime`(D-8), UDP, `SendEndWhenSendingTimeOut`.
- **public API가 바뀐다.** 외부 게임 서버 영향은 `README.md`의 "0.91 마이그레이션 가이드" 참고.
- 검증: 회귀 테스트 31/31, LoadTest 통합 56/56,
  실부하(TCP 50클라이언트 20초) 34,608 송신 / 34,603 수신 / 타임아웃 0 / p99 약 0.98ms.
- 회귀 테스트 수가 36 → 31로 준 것은 삭제한 기능 전용 테스트 5개(문자열 필터 3, CollectSend 버퍼 2)를
  함께 지웠기 때문이다. C-1 사전 테스트 2개는 새로 추가했다.

### TASK-21: 로깅 인터페이스 정비 — ✅ 완료 (2026-08-13)
외부 로그 라이브러리 연동성 점검 후 발견한 문제를 전부 처리했다.
- **MEL 브리지 추가**: `MicrosoftLoggingLogFactory` / `MicrosoftLoggingLog`.
  `Microsoft.Extensions.Logging.Abstractions` 의존이 생겼다(구현체는 안 딸려옴).
- **이름 충돌 제거**: `ILoggerProvider` → `ILogProvider`. 새 열거형은 `LogLevel`이 아니라
  `LogEventLevel`로 명명.
- **전 레벨 Exception 오버로드 + `Trace` 레벨** 추가. 전부 default 구현이라 기존 어댑터가 안 깨진다.
- **구조적 로깅**: `LogSessionContext`(readonly struct, 할당·박싱 없음) +
  `ILog.Log(LogEventLevel, in LogSessionContext, string, Exception?)`.
  `params object[]`는 의도적으로 쓰지 않았다.
- 세션 정보를 `Environment.NewLine`으로 이어붙이던 9곳 제거 → 모든 로그가 단일 행.
- `LogFactoryBase.IsSharedConfig`(항상 false인 죽은 속성) 제거, 선택적 헬퍼임을 문서화.
- 어댑터 13벌 정리: NLog 10벌은 예외를 `Log.Error(ex, msg)`로 제대로 전달 + 구조적 속성 지원,
  ZLogger 3벌은 삭제하고 내장 브리지로 대체.
- `Template/GameServer_01_GenericHost`가 오래된 `net9.0` DLL을 참조하던 것을 프로젝트 참조로 교체.
- 회귀 테스트 6개 추가(총 36개).


### TASK-20: 미사용 코드·기능 제거 — ✅ 완료 (2026-08-13)
- 라이브러리 13,578줄 → 10,777줄 (-2,801줄, -20.6%), 소스 파일 12개 삭제.
- 남긴 것: UDP 지원, `RawDataReceived`, `CollectSend`, `IConnectionFilter`, `ILog` 전체 레벨.
- 없어진 이름을 코드나 예전 문서에서 만났을 때 참고할 대응표:

| 제거 대상 | 대체 |
|---|---|
| `IReceiveFilter<T>.Filter(ReadOnlySpan<byte>, bool, out int)` | `ISequenceReceiveFilter<T>.Filter(ReadOnlySequence<byte>, ...)` |
| `ISocketSession.OrigReceiveOffset` | 없음 (Pipelines 경로에서 항상 0이었다) |
| `AppServerBase.OnStartup()` | `OnStarted()` |
| `AppServer<T,TReq>.GetAppSessionByID()` | `GetSessionByID()` |
| `AppServerBase.GetFilePath()` | `Path.Combine(AppContext.BaseDirectory, ...)` |
| `HttpReceiveFilterBase` / `HttpRequestInfoBase` / `MimeHeaderHelper` | 없음 (HTTP 미지원) |
| `SendingQueue` / `SendingQueueSourceCreator` | 내부 `ChannelSendingQueue` |
| `IPoolInfo` / `ISmartPool<T>` / `ISmartPoolSource` / `ISmartPoolSourceCreator<T>` / `SmartPoolSource` | `SmartPool<T>` 생성자 (`minPoolSize, maxPoolSize, Func<T>`) |
| `ISocketServer.SendingQueuePool` | 없음 (항상 null이었다) |
| `IWorkItem` / `IWorkItemBase` / `ISystemEndPoint` / `ISocketServerAccessor` | 멤버는 `IAppServer`로 흡수 |
| `IReceiveFilterFactory` (비제네릭 마커) | `IReceiveFilterFactory<TRequestInfo>` |
| `IConnectionFilter.Initialize()` | 없음 (호출된 적 없다) |
| `AssemblyUtil` | `PropertyCopier.CopyPropertiesTo` |
| `Platform` | `OperatingSystem.IsWindows()` |
| `ArraySegmentList<T>` (제네릭) / `IList<byte>` 구현 / `Decode` / `DecodeMask` | byte 전용 `ArraySegmentList` |
| `BinaryUtil`의 `IndexOf` / `StartsWith` / `EndsWith` / mark 오버로드 | `SearchMark(state)` / `CloneRange` |
| `StringExtension`의 `ToInt32` 외 전부 | `int.TryParse` 등 BCL |
| `IServerConfig`: `ReceiveFilterFactory` / `Disabled` / `ConnectionFilter` / `LogFactory` / `DefaultCulture` | 없음 (XML 설정 잔재) |
| `IRootConfig`: `LogFactory` / `OptionElements` / `DefaultCulture` | 없음 |
| `HotUpdateAttribute` / `ICommandAssemblyConfig` / `CommandAssemblyConfig` | 없음 |

### TASK-04: SocketAsyncEventArgs 풀 개선 — ✅ 완료
- **파일**: `AsyncSocketServer.cs`
- `PreAllocateSAEA`(기본 true) / `MinPoolSize` 설정으로 사전 할당과 동적 증설을 선택한다.
- 수신 proxy 풀과 송신 SAEA 풀 2개를 `SmartPool`로 관리한다.

### TASK-06: System.Diagnostics.Metrics 추가 — ✅ 완료
- **파일**: `AppServerBase.cs`, `AsyncSocketServer.cs`, `SocketSession.cs`
- `Meter("SuperSocketLite")` 계측기: `total-requests`, `total-bytes-received`,
  `total-bytes-sent`, `active-connections`, `session-count`(ObservableGauge),
  `sessions-rejected`, `send-queue-full`, `send-errors`, `request-duration`(Histogram, ms).
- 확인: `dotnet-counters monitor --counters SuperSocketLite`

### TASK-07: ReceiveFilter Span 오버로드 — ✅ 완료 → **TASK-22로 대체됨**
- 당시에는 `IReceiveFilter`(byte[])와 `ISequenceReceiveFilter`(zero-copy) 두 경로를 병행했다.
- TASK-22(C-1)에서 `ISequenceReceiveFilter`를 없애고 `IReceiveFilter` 자체를
  `ReadOnlySequence` 전용으로 만들어 경로가 하나로 합쳐졌다.

### TASK-01 Pipelines 전환 / TASK-02 Span·Memory 송신 / TASK-03 CancellationToken / TASK-09 Nullable — ✅ 완료
- 수신은 `System.IO.Pipelines` 기반이며 `BufferManager`는 제거되었다.
- `Start(CancellationToken)`, Accept 루프의 `AcceptAsync(ct)`, 비동기 송신
  `SendAsync(ReadOnlyMemory<byte>, CancellationToken)`까지 구현되었다.
