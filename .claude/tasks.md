# 개선 작업 목록

코드 변경 후 반드시 `dotnet build` 성공을 확인한다.
개발 중이므로 public API는 자유롭게 바꿔도 된다 (하위 호환 제약 없음).

> **이 문서는 이력용이다.** 현행 작업 목록은 저장소 루트의 `TODO.md`를 본다.
> TODO.md의 TODO-01 ~ TODO-19는 2026-08-12에 모두 완료되었다.

---

## 완료된 태스크

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

### TASK-07: ReceiveFilter Span 오버로드 — ✅ 완료
- **파일**: `SocketBase/Protocol/IReceiveFilter.cs`
- `ReadOnlySpan<byte>` 오버로드가 default 구현으로 존재한다.
- 실질적인 상위 호환은 `ISequenceReceiveFilter<T>`(zero-copy `ReadOnlySequence` 경로)이며,
  `FixedSizeReceiveFilter`, `FixedHeaderReceiveFilter`, `FixedHeaderSequenceReceiveFilter`,
  `TerminatorReceiveFilter`, `BeginEndMarkReceiveFilter`, `CountSpliterReceiveFilter`가 구현한다.
  (`HttpReceiveFilterBase`는 미구현 — 필요해지면 TODO 신설)

### TASK-01 Pipelines 전환 / TASK-02 Span·Memory 송신 / TASK-03 CancellationToken / TASK-09 Nullable — ✅ 완료
- 수신은 `System.IO.Pipelines` 기반이며 `BufferManager`는 제거되었다.
- `Start(CancellationToken)`, Accept 루프의 `AcceptAsync(ct)`, 비동기 송신
  `SendAsync(ReadOnlyMemory<byte>, CancellationToken)`까지 구현되었다.
