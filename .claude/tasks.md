# 개선 작업 목록

코드 변경 후 반드시 `dotnet build` 성공을 확인한다.
기존 public API 시그니처는 변경하지 않는다 (하위 호환 유지).

> **이 문서는 이력용이다.** 현행 작업 목록은 저장소 루트의 `TODO.md`를 본다.
> TODO.md의 TODO-01 ~ TODO-19는 2026-08-12에 모두 완료되었다.

---

## 완료된 태스크

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
