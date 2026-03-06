# 개선 작업 목록

코드 변경 후 반드시 `dotnet build` 성공을 확인한다.
기존 public API 시그니처는 변경하지 않는다 (하위 호환 유지).

---

## 🔴 우선순위 높음

### TASK-04: SocketAsyncEventArgs 풀 개선
- **파일**: `AsyncSocketServer.cs`
- **목표**: `ArrayPool<byte>.Shared` 조합으로 동적 연결 수 대응
- **작업**: 기존 MaxConnectionNumber 사전 할당 방식은 옵션으로 유지

---

## 🟡 우선순위 중간

### TASK-06: System.Diagnostics.Metrics 추가
- **파일**: `AppServerBase.cs`, `AsyncSocketServer.cs`
- **목표**: `Meter("SuperSocketLite")`로 런타임 메트릭 노출
- **측정 항목**: 활성 연결 수, 누적 요청 수, 송수신 바이트

### TASK-07: ReceiveFilter Span 오버로드
- **파일**: `SocketBase/Protocol/IReceiveFilter.cs`, `Protocol/FixedHeaderReceiveFilter.cs`
- **목표**: `ReadOnlySpan<byte>` 기반 처리로 전환 (default 구현으로 하위 호환 유지)