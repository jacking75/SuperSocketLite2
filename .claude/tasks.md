# 개선 작업 목록

코드 변경 후 반드시 `dotnet build` 성공을 확인한다.
기존 public API 시그니처는 변경하지 않는다 (하위 호환 유지).

---

## 🔴 우선순위 높음

### TASK-01: System.IO.Pipelines 도입
- **파일**: `AsyncSocketSession.cs`, `AsyncStreamSocketSession.cs`
- **목표**: byte[] 기반 수신을 PipeReader로 교체, 메모리 복사 감소
- **작업**:
  - PipeWriter에 직접 소켓 데이터 수신
  - PipeReader → ReceiveFilter 데이터 전달
  - 기존 ReceiveFilter 인터페이스 호환 유지

### TASK-02: Span/Memory 송신 오버로드 추가
- **파일**: `SocketSession.cs`, `AsyncSocketSession.cs`
- **목표**: `TrySend(ReadOnlyMemory<byte>)`, `TrySend(ReadOnlySpan<byte>)` 추가
- **작업**: 기존 `ArraySegment<byte>` 오버로드는 유지

### TASK-03: CancellationToken 지원
- **파일**: `AppServerBase.cs`, `TcpAsyncSocketListener.cs`
- **목표**: `Start(CancellationToken)` 지원, Accept 루프에 취소 전달
- **작업**: 기존 `Start()` 시그니처는 default 파라미터로 유지

### TASK-04: SocketAsyncEventArgs 풀 개선
- **파일**: `AsyncSocketServer.cs`
- **목표**: `ArrayPool<byte>.Shared` 조합으로 동적 연결 수 대응
- **작업**: 기존 MaxConnectionNumber 사전 할당 방식은 옵션으로 유지

---

## 🟡 우선순위 중간

### TASK-05: IAsyncDisposable 구현
- **파일**: `AppServerBase.cs`
- **목표**: `await using` 패턴 지원, 모든 세션 비동기 종료 대기

### TASK-06: System.Diagnostics.Metrics 추가
- **파일**: `AppServerBase.cs`, `AsyncSocketServer.cs`
- **목표**: `Meter("SuperSocketLite")`로 런타임 메트릭 노출
- **측정 항목**: 활성 연결 수, 누적 요청 수, 송수신 바이트

### TASK-07: ReceiveFilter Span 오버로드
- **파일**: `SocketBase/Protocol/IReceiveFilter.cs`, `Protocol/FixedHeaderReceiveFilter.cs`
- **목표**: `ReadOnlySpan<byte>` 기반 처리로 전환 (default 구현으로 하위 호환 유지)

### TASK-08: IP Rate Limiting 연결 필터
- **파일**: `SocketBase/IConnectionFilter.cs` (새 파일: `IpRateLimitConnectionFilter.cs`)
- **목표**: 동일 IP 초당 연결 시도 횟수 제한 기본 구현 제공

---

## 🟢 우선순위 낮음

### TASK-09: Nullable Reference Types 활성화
- **파일**: `SuperSocketLite.csproj` 및 전체 소스
- **목표**: `<Nullable>enable</Nullable>` 설정 후 모든 경고 해소

### TASK-10: 단위 테스트 추가 (xUnit)
- **파일**: `Test/` 디렉토리
- **목표**: 핵심 컴포넌트 테스트
- **대상**: `FixedHeaderReceiveFilter`, `SocketSession` 상태 머신, `SmartPool<T>`, `BufferManager`

### TASK-11: 레거시 코드 정리
- **파일**: `AppServerBase.cs`, `AppSession.cs`
- **목표**:
  - `[Obsolete]` `OnStartup()` 제거, `OnStarted()`로 통일
  - 주석 처리된 `BeginInvoke`/`EndInvoke` 코드 제거  