# 작업 로그

> 아래 기록이 참조하는 계획 문서(`TODO.md`, `SIMPLIFY.md`, `.claude/tasks.md`,
> `Docs/LoadTest_Improvement_Plan.html`, `PERFORMANCE_PLAN.md`)는 해당 작업이 모두 끝난 뒤
> 저장소에서 삭제했다. 내용은 git 이력에 남아 있고, 계속 쓸모 있는 부분(기각한 최적화 후보와
> 그 이유)은 `.claude/architecture.md`로 옮겼다.

## 2026-08-14 12:04 KST - 마이그레이션 가이드 삭제

- 사용자 판단으로 `Docs/Migration_0.90_to_0.91.md`를 지웠다. 0.91은 사실상 재작성이라 0.90에서 옮겨 올 사람이 없다는 것.
- 다만 §5의 **제거된 기능 표**는 성격이 다르다. "옛 SuperSocket에 있던 X는 어디 갔나"에 답하는 목록이고 저장소에서 유일한 기록이었다. `.claude/architecture.md`에 "제거된 기능" 절로 옮겼다.
- README 두 종이 "무엇을 왜 제거했는지는 마이그레이션 가이드 참고"라고 링크하고 있어 그 문장도 새 위치로 돌렸다. `Docs/index.html`의 카드도 정리.
- `Docs/`에 남은 것은 `GC_Copy_Minimization.md`, `VSCode_Repository_Analysis.html`, `index.html` 셋이다.

## 2026-08-14 11:47 KST - 문서 전수 점검: 낡은 문서 6종 삭제, 사실 오류 다수 수정

- 저장소의 문서 24종을 코드와 하나씩 대조했다. **가장 심각한 문제는 `Docs/`의 다이어그램 HTML 4종**이었다. 문자열 프로토콜 계열 제거와 `byte[]` 필터 경로 제거를 거치는 동안 갱신되지 않아, `ISequenceReceiveFilter`·`FixedHeaderSequenceReceiveFilter`·`CollectSend`·`RawDataReceived`·`LeftBufferSize`·`m_SendQueue`처럼 **지금 없는 것들을 주력으로 설명**하고 있었다. 수신 파이프라인 문서는 11개 절 중 7개가 무효였다.
- 삭제한 것: `SuperSocketLite_Architecture.html`, `SuperSocketLite_ReceivePipeline_ReceiveFilter.html`, `SuperSocketLite_SendPipeline_Detail.html`, `SuperSocketLite_TCP_Connection_Flow.html`, `LoadTest_Toolkit_Pipeline.html`(완료된 작업의 계획도), `PERFORMANCE_PLAN.md`(완료된 계획서).
- **버리기 전에 고유한 내용은 옮겼다.** 송신 경로 상세와 연결·수신 기동 순서(`pauseWriterThreshold` 계산 포함), UDP 경로를 `.claude/architecture.md`에 글로 새로 썼고, `PERFORMANCE_PLAN.md`의 "기각한 최적화 후보" 표도 같은 문서로 옮겼다. 세션/Pipe 풀링이나 `PipeScheduler.Inline`처럼 다시 제안되기 쉬운 항목이라 이유를 남겨야 했다. 계획서에만 있던 `KeepAliveRetryCount`는 README 두 종의 설정 표로 옮겼다.
- 다이어그램이 다시 필요하면 `CLAUDE.md` 규칙대로 `archify`로 새로 만든다. 손으로 고치면 SVG 안의 그림까지는 못 고치고, 생성기가 다시 만들 결과와도 어긋난다.
- **빌드 명령이 틀려 있었다.** `CLAUDE.md`와 `AGENTS.md` 둘 다 `cd SuperSocketLite/SuperSocketLite`인데 그런 경로가 없다(csproj는 한 단계 깊이). 출력도 `SuperSocketLite/bin/`이 아니라 저장소 루트 `bin/net10.0/`다. 실제로 빌드해서 확인하고 고쳤다.
- README 두 종: 없어진 `Tutorials/SwitchReceiveFilter` 링크 제거, `active-connections`는 `ObservableGauge`가 아니라 `UpDownCounter`(수집기가 없어도 갱신된다), `UdpRequestInfo`는 인터페이스가 아니라 클래스라 "구현"이 아니라 "상속", LoadTest 테스트 개수 94→110. **가장 큰 것은 첫 예제**가 방금 저장소가 버린 `body.ToArray()` 패턴을 가르치고 있던 것 — 인스턴스 재사용 + `ReadOnlySequence` 형태로 바꿨다. 두 파일은 완전 대응이므로 항상 같이 고쳐야 한다.
- `.claude/conventions.md`의 규칙 3개가 코드와 정반대였다. `t_` 접두사(실제 `[ThreadStatic]` 필드는 `s_SessionIdFilter` 하나뿐이고 `.editorconfig`에 `t_` 규칙이 없다), "`private` 생략"(실제로는 212번 명시), "정수는 `Int32`"(라이브러리에 `Int32` 계열 0건). 코드 쪽이 옳으므로 규칙을 실제에 맞췄고, `.editorconfig`가 강제하는 항목을 표에 표시했다. `Tutorials/coding_rule.md`의 같은 규칙도 고쳤다.
- `ILog`의 "필수 구현은 플래그 6개 + 메서드 8개"는 실제로 5개 + 7개다(`IsTraceEnabled`와 `Trace(string)`은 default가 있다). 문서와 `ILog.cs`의 XML 주석 양쪽을 고쳤다.
- **문서가 아니라 저장소 버그도 하나 나왔다.** `SuperSocketLite2.slnx`에 `SuperSocketLite.LoadTest.Report`가 빠져 있어 그 프로젝트만 IntelliSense·F12 대상이 아니었다. 추가하니 32→33개가 되어 `VSCode_Repository_Analysis.html`의 "33개" 서술도 저절로 맞게 됐다.
- 클라이언트가 `--scenario idle-heartbeat`를 도움말에 광고하는데 **분기가 없어 조용히 `echo`로 동작**하고 있었다. 광고를 지웠다(`IdleHeartbeatScenario` 클래스는 단위 테스트가 쓰므로 남겼다). 구현할지는 별도 판단.
- `Test/README.md`는 없어진 `BufferManager`를 없는 디렉토리 이름으로 설명하고 40건·110건짜리 테스트 스위트 둘이 통째로 빠져 있어 재작성했다. `Template/README.md`는 4개 항목 중 3개가 틀렸다(bat 파일명, 없는 `GameServer_02`, 서버와 테스트 클라이언트가 뒤바뀜).
- 검증: 라이브러리 회귀 **40건**, LoadTest **110건** 전부 통과. 빌드 경고 0. 문서 상대 링크 전수 검사에서 깨진 링크 0(`coding_rule.md`의 `[Flags](비트 필드)` 하나를 찾아 고쳤다).

## 2026-08-14 11:18 KST - LoadTest 서버를 강제 종료 대신 정상 종료시키도록 수정

- `run-loadtest.ps1`이 실행 끝에 서버를 `Stop-Process -Force`로 죽이고 있었다. 그러면 세션 정리도, **마지막 CSV 표본 기록**도 일어나지 않는다. 마지막 표본은 `ServerMetricsHostedLoop.Dispose()`에서만 쓰이므로, 클라이언트가 이미 다 빠져나갔는데도 서버 CSV의 끝 행이 "활성 세션 N개"로 남았다. 리포트는 정확히 그 끝 행으로 세션 누수를 판정하므로 **멀쩡한 실행이 불합격**으로 나왔다. 30초 실행인데 CSV가 14초에서 끊기기도 했다.
- 서버에 `--stop-file <path>`를 넣었다(`StopFileSignal.cs`). 그 경로에 파일이 생기면 정상 종료한다. 스크립트는 이 파일로 종료를 요청하고 30초 기다린 뒤, 그래도 안 끝날 때만 강제로 내리고 경고를 찍는다. 서버가 기동할 때와 끝날 때 파일을 지우므로 이전 실행이 남긴 파일로 새 서버가 뜨자마자 끝나는 일은 없다.
- **파일을 고른 이유**: 콘솔 없이 띄운 자식 프로세스에 Ctrl+C를 보내는 것은 Windows에서 까다롭고, 이름 있는 동기화 객체는 유닉스에서 지원되지 않는다.
- `-KillServerAt`의 장애 주입은 **그대로 강제 종료**다(`Stop-LoadTestServerForce`). 갑작스러운 서버 손실을 재현하는 것이 목적이기 때문이다. 재기동한 서버는 실행 끝에 정상 종료된다.
- 검증하다 **기존 버그 1건**을 찾아 함께 고쳤다. `Start-Process -PassThru`가 돌려준 Process 객체는 핸들을 미리 잡아 두지 않으면(`$null = $proc.Handle`) 종료 뒤 `ExitCode`가 빈 값이 된다. `$null -ne 0`이 참이라 `-KillServerAt` 실행은 성공해도 항상 "클라이언트가 코드 로 끝났다"로 죽고 있었다.
- 확인: 일반 실행은 세션 정리 PASS(활성 0), 표본이 전 구간 23.7초를 덮고 stop 파일도 남지 않는다. 장애 주입은 중간 강제 종료·재기동이 그대로 동작하고 끝에서 세션 정리 PASS. LoadTest 자체 테스트 **104 → 110건**(`StopFileSignalTests` 신규 6건).
- 장애 주입 실행에서 "목표 레이트 달성" 미달이 뜨는 것은 서버를 5초 내린 시나리오라 **정상**이다. 기본 임계값을 낮추면 일반 실행의 진짜 회귀를 놓치므로 그대로 뒀다.

## 2026-08-14 10:57 KST - GC·데이터 복사 최소화: 가이드 작성 후 예제·부하 테스트에 적용

- 핫패스를 다시 훑은 결론은 **라이브러리 코어에는 이미 정상 경로 패킷당 할당이 0**이라는 것이었다. 남은 할당은 전부 앱 경계(ReceiveFilter, 패킷 핸들러, 송신 호출부)에서 나온다. 그래서 `SuperSocketLite/`는 **한 줄도 고치지 않았고** 공개 API도 그대로다. 근거·방법·측정 절차는 `Docs/GC_Copy_Minimization.md`에 정리했다.
- **개선 1(수신 무할당)**: 핸들러에서 바로 응답하는 서버 6개와 LoadTest 서버. 요청 정보의 `Body`가 `byte[]` → `ReadOnlySequence<byte>`로 바뀌어 수신 파이프를 그대로 가리키고, 요청 인스턴스도 필터가 세션마다 하나만 두고 돌려 쓴다. 안전한 근거는 라이브러리가 핸들러를 **동기로** 부르고 그 뒤에 `AdvanceTo`를 부른다는 것. 대신 **핸들러가 리턴하면 요청과 본문이 무효**라는 계약이 생겼고, 이걸 `.claude/cautions.md`에 적었다.
- **개선 2(ArrayPool)**: 패킷을 로직 스레드로 넘기는 `PvPGameServer`. 필터가 빌리고 `PacketProcessor.Process`의 `finally` **한 곳**에서 반납한다. 풀 배열은 요청보다 클 수 있으므로 길이는 `DataSize`, 역직렬화는 `DataSpan`을 쓴다.
- **개선 3(송신 무할당)**: `List<byte>` + `BitConverter.GetBytes` + `ToArray()`로 응답당 배열을 4~5개 만들던 것을 stackalloc/ArrayPool 버퍼 + `SendCopied`로 바꿨다. `Send`와 실패 동작이 같도록 `TrySendCopied`가 아니라 `SendCopied`를 골랐다.
- **개선 4**: 서버 실행 프로젝트 14개에 Server GC. DATAS는 서버마다 답이 달라 csproj에 박지 않고 `DOTNET_GCDynamicAdaptationMode`로 비교하도록 문서에 적었다.
- **핸드오프 서버 5개(ChatServer, ChatServerEx, MoDedicated, MoDedicated2, GateServer)는 일부러 그대로 뒀다.** `PvPGameServer`와 같은 구조라 적용할 개선이 하나뿐이고, 반납 규율을 다섯 곳에 복제하면 배우는 것 없이 use-after-return 위험만 는다. 이 서버들의 송신 경로는 이미 권장 형태(브로드캐스트가 배열 하나를 공유)였다.
- **측정(각 3회, 300클라 × 40req/s × 4KB × 45초, 모드당 약 88만 요청)**: 클라이언트 p99 4.799 → 3.967ms(-17.3%), p99.9 10.623 → 7.359ms(-30.7%), 서버 메모리 증가 67.4 → 25.2MB, Gen0/Gen1/Gen2 318/263/6 → **6/3/0**. 처리량과 오류율은 동일.
- **1회 실행 수치를 믿으면 안 된다는 사례를 얻었다.** 각 1회만 돌렸을 때는 처리량 -4.3%, 핸들러 p99 +42%처럼 반대 방향 수치가 나왔는데 3회 반복에서 전부 사라졌다(핸들러 p99는 오히려 -10.8%). 재현용으로 `--alloc-mode pooled|legacy` 스위치를 넣어 같은 빌드로 개선 전 동작을 재현할 수 있게 했다.
- 테스트: LoadTest 자체 **94 → 104건**(`ZeroAllocationTests` 신규 — 인스턴스 재사용, 본문 무복사, 조각난 시퀀스 파싱, 버퍼 경계 검사), 라이브러리 회귀 **40건**. 계측 레코더도 요청당 할당이 없도록 구조체로 바꿨다.

## 2026-08-14 01:20 KST - 성능 개선안 구현: 4건 채택, 1건 철회, 버그 1건 발견·수정

- `PERFORMANCE_PLAN.md`의 P1~P5를 전부 구현한 뒤 실측으로 판정했다. 회귀 테스트 **31 → 40건 전부 통과**, LoadTest **94/94**, 라이브러리 빌드 경고 0.
- **가장 큰 성과는 계획에 없던 버그 1건이다.** 세션이 닫혀도 **송신 SAEA가 풀로 반납되지 않아**, 서버가 기동 후 누적 `MaxConnectionNumber`개의 접속만 처리하고 그 뒤로는 전부 거부하고 있었다. 원인은 `AsyncSocketSession.OnClosed`가 종료 핸들러를 부르기 **전에** `_socketEventArgSend`를 null로 지우는데, 핸들러가 반납할 인스턴스를 바로 그 필드(`SendSAEA`)에서 읽고 있었던 것. 떼어낸 인스턴스를 `_detachedSendSAEA`에 보관해 노출하도록 고쳤다. 수정 한 줄을 되돌리면 전용 테스트 2건이 실패하고 복원하면 통과하는 것까지 확인했다.
- **P1(카운터 스트라이핑)은 철회했다.** 동작은 정확했지만 처리량을 **일관되게 1.2% 떨어뜨렸다**. HEAD와 교차 측정 3회씩을 두 번 돌려, 전체 변경본은 범위가 겹치지 않게 느리고(13,347 → 13,185 pps) P1만 뺀 변경본은 범위가 완전히 중첩(13,262 → 13,248)하는 것으로 원인을 P1로 특정했다. 이 부하는 초당 4만 회 갱신을 16코어가 나눠 하는 수준이라 캐시라인 경합이 거의 없고, 그러면 `GetCurrentProcessorId` 비용만 남는다. 계획서의 채택 게이트대로 클래스와 단위 테스트를 제거했다.
- 채택한 나머지: **P2** 송신 배치 종료 시 `Interlocked.And` 반환값으로 닫힘을 판정해 평상시 `lock(SyncRoot)`를 건너뛴다. **P3** `ServerConfig.AcceptLoopCount`(기본 1, 1~64 클램프)로 리스너 accept 루프 다중화, `OnStopped` 1회 보장은 카운트다운. **P4** `ServerConfig.UseZeroByteReceive`(기본 false)로 유휴 세션이 수신 버퍼를 점유하지 않게 하는 프로브 수신 — 프로브 판정을 별도 플래그가 아니라 **게시한 버퍼 길이**로 해서 스레드 간 공유 상태를 없앴다. **P5** LingerOption 정적 캐시, 부분 송신 로그 가드.
- **최종본은 정상 부하에서 기존 코드와 측정상 구분되지 않는다.** 즉 처리량을 올리지는 못했고 대신 잃은 것도 없다. 측정 한계도 기록해 둔다 — 클라이언트와 서버가 같은 16코어를 공유해 상한이 약 13,200 pps다. P3·P4는 각각 재접속 폭주·대량 유휴 세션 시나리오로 따로 재야 의미가 있고 이번엔 "기본값에서 회귀 없음"까지만 봤다.
- 함정 하나: P2 스트레스 테스트를 처음에 `MaxConnectionNumber`만큼만 성공하도록 잘못 짰다. 클라이언트가 소켓 종료를 관측하는 시점이 서버의 세션 정리보다 빨라서, 곧바로 재접속하면 종료가 아니라 접속 한도를 시험하게 된다. 라운드마다 세션이 사라질 때까지 기다리도록 고쳤고, 이 실패가 위 버그를 찾아낸 실마리였다.

## 2026-08-13 23:16 KST - 부하 테스트 툴킷 남은 항목 전부 구현 (C5·B4·C6·B2)

- 계획서에 보류로 남아 있던 네 항목을 모두 넣었다. LoadTest 테스트 **94/94**(신규 14건), 회귀 테스트 **31/31**, 빌드 경고 0개.
- **C5(서버 계측 확장)**: 송신 큐 깊이와 SAEA 풀 잔량을 `Meter("SuperSocketLite")`의 ObservableGauge 6개로 내보낸다. **공개 타입·속성은 하나도 늘지 않았다** — 라이브러리 변경은 전부 `internal`이고, `ServerProbe`가 `MeterListener`로 받아 CSV 6컬럼을 쓴다. 공개 API를 넓힐지가 오래 걸린 판단이었는데, 이미 `session-count`가 쓰던 계기 통로를 그대로 쓰는 것으로 정리했다.
- 큐 깊이는 **송신마다 카운터를 증감하지 않고 관측 시점에 세션을 훑어** 구한다. 표본 주기가 1초이므로 핫패스에 비용을 얹을 이유가 없다. 같은 이유로 프로브는 이 Meter의 **게이지 6개만** 구독한다 — 요청마다 값을 더하는 카운터까지 구독하면 측정 대상의 송수신 경로에 콜백 비용이 붙는다.
- 값이 없을 때는 **`0`이 아니라 `-1`**로 적는다. 0으로 적으면 "큐가 비었다"로 읽혀 재지 않은 것을 잘 돌았다고 말하게 된다. 분석 뷰 `analysis_server_backpressure`와 리포트가 음수를 집계에서 뺀다.
- **B4(서버 장애 주입)**: `run-loadtest.ps1 -KillServerAt`이 부하 중 서버를 죽였다 살린다. 클라이언트는 `--reconnect-on-drop`으로 다시 붙고 `outage_total`·`reconnect_total`·`max_outage_ms`가 요약에 남는다. **회복은 접속이 아니라 응답으로 잰다** — 서버가 리슨을 다시 열어도 아직 처리하지 못하는 구간이 있어서, 접속 성공을 회복으로 세면 그 구간이 통째로 빠진다.
- **C6(계측 오버헤드 검증)**: `--metrics full|no-gauges|off`와 `measure-metrics-overhead.ps1`. 이 과정에서 **판정 로직의 결함**을 찾아 고쳤다 — 계측을 끈 실행은 서버 표본이 없는데 기존 로직이 `서버 예외 0건`을 통과로, 처리량을 0으로 급락한 것으로 읽었다. 이제 서버 쪽 판정은 **보류**로 남고 처리량은 클라이언트 기준으로 견준다.
- **B2(선언적 시나리오)**: `--scenario-file <json>`으로 `prologue`·`operations`(weight)·`thinkTime`을 기술한다. 잘못된 정의 7가지는 부하를 걸기 전에 거부한다. `thinkTime`은 닫힌 루프에서만 유효하므로 열린 루프와 함께 주면 조용히 무시하지 않고 경고를 찍는다.
- 자체 테스트 하나가 **현재 디렉토리를 저장소 루트로 가정**하고 있어 `Test/LoadTest`에서 실행하면 실패했다. `RepoPaths.cs`로 루트를 찾도록 고쳤다.
- 작업이 끝났으므로 계획 문서 4종을 삭제했다. 사용 방법은 `Test/LoadTest/BUILD_AND_USAGE.md`와 `README.md`에 옮겨져 있다.

## 2026-08-13 18:57 KST - 부하 테스트 툴킷 단계 4(자동화) 구현

- 계획서 단계 4의 D1·D2·D3·E1·E2·E3를 구현했다. LoadTest 테스트 **80/80**(신규 9건), 회귀 테스트 **31/31**, 빌드 경고 0개.
- **D1**: 신규 프로젝트 `SuperSocketLite.LoadTest.Report`. CSV를 읽어 파일 하나로 열리는 HTML을 만든다. 판정·지표·시계열을 담고 **차트는 인라인 SVG**라 외부 자원을 참조하지 않는다(테스트로 고정).
- **D2+D3**: `--baseline`/`--run`이 접두사로 실행을 묶고 **지표별 중앙값**으로 비교한다. 임계값은 `thresholds.json`으로 분리했고 위반 시 `--fail-on-regression`으로 종료 코드 1을 반환한다. **예외와 세션 누수는 중앙값 대신 최악값**을 남긴다 — 세 번 중 한 번 난 사고가 중앙값에 묻히면 안 된다.
- **판정이 실제로 회귀를 잡는지 확인했다.** `repeat01~03`(닫힌 루프)을 기준으로 `open01`(열린 루프)을 비교하니 p99 +393%, p99.9 +222%로 **불합격 + 종료 코드 1**. 동시에 "페이싱 불일치"를 보류로 표시해 **그 차이가 성능 저하가 아니라 측정 방식 차이임을 함께 알렸다.** 같은 페이싱끼리 비교하면 9항목 전부 통과, 종료 코드 0.
- **E1+E2+E3**: `run-loadtest.ps1`이 서버 기동→**리슨 확인**→클라이언트→서버 정리→리포트를 한 번에 한다. 고정 시간 대기 대신 포트가 열릴 때까지 기다린다 — 단계 3에서 서버가 이미 끝난 뒤 클라이언트를 돌려 정상 동작을 결함으로 오인한 일이 있었다. `run-matrix.ps1`은 정상 2종 + 이상 4종을 훑는다. **여섯 조합 실행에서 서버 예외 0건, 세션 누수 0건**을 확인했다.
- PowerShell 5.1이 BOM 없는 UTF-8을 ANSI로 읽어 한글이 깨지므로 두 스크립트에 BOM을 넣었다.
- 남은 항목: C5(서버 계측 확장 — 라이브러리 공개 API 판단 필요), B4(서버 장애 주입), C6(계측 오버헤드 검증), B2(선언적 시나리오 정의). 계획서 12장에 착수 조건과 함께 정리했다.

## 2026-08-13 18:29 KST - 부하 테스트 툴킷 단계 3(커버리지) 구현

- 계획서 단계 3에서 B1·B3·A5를 구현했다. LoadTest 테스트 **71/71**(신규 5건), 회귀 테스트 **31/31**, 빌드 경고 0개.
- **B1**: 서버가 세 리스너를 함께 띄운다(`--text-port`, `--udp-port`). **`--transport udp`와 `--protocol text-line`은 옵션만 있고 받아 줄 서버가 없어 실행 자체가 불가능했는데** 이제 돈다. 라이브러리에 구분자 기반 필터가 없어(문자열 프로토콜 계열이 제거됨) text-line 필터를 `IReceiveFilter`로 직접 구현했다. 세 리스너는 계측기를 공유한다 — GC·메모리·CPU는 프로세스 단위라 따로 재는 게 의미 없고 세션·요청 수는 합산이 읽기 쉽다.
- **B3**: 순간 폭주(`--scenario burst`), 비정상 종료(`--abort-percent`, RST 전송), 대용량 페이로드(`--payload huge`/`mixed-huge`)를 추가했다. 폭주는 기본 레이트 위에 주기마다 뭉치를 얹는 방식이고, 열린 루프 덕에 그 뭉치가 응답 대기에 막히지 않고 실제로 몰려 나간다.
- **A5**: 실행 시작 시 스레드풀 최소 워커를 미리 확보한다(워커 증가 속도가 초당 한두 개라 수천 클라이언트 램프업을 늦춘다). 연결 실패가 임시 포트 고갈 등 부하 발생기 쪽 한계인지 구분해 `local_resource_exhaustion`으로 센다.
- **프로토콜 한계를 발견했다.** `huge`를 60KB로 잡았더니 요청이 하나도 안 나갔다. 패킷 헤더의 `totalSize`가 `Int16`이라 **본문 최대가 32,762바이트**다. 그 한계에 붙여 다시 잡았다.
- **C5(송신 큐·풀 계측)는 보류했다.** `ChannelSendingQueue.Count`는 있으나 세션 인터페이스로 노출되지 않고 `SmartPool`은 크기 조회 API 자체가 없다. 얻으려면 라이브러리 공개 API를 넓혀야 하는데 이는 툴킷 개선 범위를 넘으므로 판단이 필요하다.
- UDP 수동 확인 때 응답이 안 와 코드를 의심했으나, 원인은 **서버 실행 시간이 이미 끝난 뒤 클라이언트를 돌린 것**이었다. 변수를 하나씩 되돌려 확인했고 코드는 정상이었다. 같은 실수를 막으려고 두 프로토콜 모두 통합 테스트로 고정했다.

## 2026-08-13 17:08 KST - 부하 테스트 툴킷 단계 2(부하 정확도) 구현

- 계획서 단계 2 항목 3건(A1~A3)을 구현했다. 부하 생성을 **닫힌 루프에서 열린 루프로** 전환했다. LoadTest 테스트 **66/66**(신규 6건 포함, 3회 연속), 회귀 테스트 **31/31**, 빌드 경고 0개.
- **A1**: 송신 시각을 실행 시작 기준 절대 일정으로 고정했다. 한 번 늦어도 다음 송신은 원래 예정 시각을 따르므로 오차가 누적되지 않는다. `--pacing open|closed` 추가(기본 `open`).
- **A2+A3**: 송신/수신을 독립 루프로 나누고 본문 앞 8바이트 상관 ID로 요청-응답을 짝짓는다(응답 순서 무관). `--max-in-flight`로 깊이 제한. `ClientActor`를 partial로 나눠 `ClientActor.OpenLoop.cs`에 구현했다.
- 실측 효과가 명확하다. 목표 500 req/s에서 **달성률 96.8% → 99.9%**(계획서 완료 기준 ±1% 달성). 응답을 20ms 늦추자 닫힌 루프는 **84.8%로 떨어졌지만 열린 루프는 100.0%를 유지**했다. "서버가 느려지면 부하도 같이 줄어든다"는 문제가 실증되고 동시에 해소됐다.
- 정상 조건에서 열린 루프의 RTT p99가 5.6ms로 닫힌 루프 1.8ms보다 3배 높다. **측정이 나빠진 게 아니라 부하가 제대로 걸린 것**이다. 목표 레이트로 보내면 서버에 요청이 쌓이고(최대 25건 관측) 그 대기가 지연에 반영된다. 닫힌 루프는 한 번에 하나만 보내 이 대기를 만들지 않아 지연을 낮게 보고하고 있었다.
- **부수 소득 — 세션 수 계측 버그를 찾아 고쳤다.** 재접속 폭주 테스트가 실패해 재현하니 `total_connected=50, total_closed=50`인데 `active_sessions=2`였다. `active_sessions`를 증감식 카운터로 세면서 "0이면 감소 건너뛰기" 방어를 둔 탓이다. 접속·종료 이벤트는 다른 스레드에서 오므로 순서가 뒤바뀌면 감소가 버려져 값이 영구히 어긋난다. 두 누적 카운터의 차이로 구하도록 바꿨다. **없는 누수를 있다고 판정하던 버그**이며, 열린 루프가 재접속을 빠르게 만들어 드러났다.
- 페이싱이 다른 실행은 지연 비교가 불가능하므로 `client_summary.csv`와 분석 뷰에 `pacing`을 기록한다. 클라이언트 포화 판별용으로 `send_schedule_delay_*`, `send_skipped_in_flight`, `max_in_flight_observed`를 추가했다. 문서 3종과 계획서 HTML(4장 신설)을 갱신했다.

## 2026-08-13 16:14 KST - 부하 테스트 툴킷 단계 1(측정 신뢰성) 구현

- 계획서의 단계 1 항목 6건(C1~C4, D4, A4)을 구현했다. LoadTest 자체 테스트 **60/60**, 라이브러리 회귀 테스트 **31/31**, 전체 솔루션 클린 빌드 **경고 0개**.
- **C1**: `LatencyHistogram`을 `List<long>`+단일 락에서 HdrHistogram식 고정 버킷(서브버킷 256, 24구간)으로 교체했다. 스레드별 슬롯으로 락 경합을 없애고 실행 전체 누적과 1초 창을 동시에 유지한다. 정확도를 실측 검증했다 — 전량 기록 실행에서 CSV 원본을 직접 정렬한 값 대비 **오차 0.4% 이내이고 전부 원본 이상**(버킷 상한을 반환하므로 성능을 좋게 보고하지 않는다).
- **C2**: `client_summary.csv`에 실행 전체 p50/p90/p95/p99/p99.9/max, 성공률, 목표 달성률을 기록한다. **샘플링 0.01 실행에서 CSV는 108행인데 분위수는 응답 10,916건 전량 기준**으로 계산됨을 확인했다. "부하가 클수록 지연 통계가 부정확해지던" 구조가 해소됐다.
- **C3+D4**: CSV에 `phase`(rampup/steady/rampdown/idle) 컬럼을 넣고 분석 뷰가 정상 구간만 집계하게 했다. 효과가 크다 — 같은 데이터가 정상 구간 483 RPS인데 전체 평균으로는 206 RPS로 보였다(**2.35배 차이**). 서버 phase 판정이 클라이언트 `--ramp-up 5초`와 정확히 일치함도 확인했다.
- **C4+A4**: 종료 시 `Flush()`가 샘플을 쓰던 중복(14ms 간격 3행)과 정상 종료가 오류로 집계되던 문제(send_fail 2, socket_error 2)를 없앴다. 판정 뷰가 수정 전 실행을 불합격, 수정 후를 합격으로 정확히 구분한다.
- 재현성 3회 반복: 처리량과 달성률은 **0.35% 변동**으로 안정적이나 **p99는 44%, p99.9는 124% 변동**한다. 측정 결함이 아니라 로컬 환경 변동이며, 꼬리 지연으로 회귀를 판정하려면 여러 회 실행의 중앙값이 필요하다. 이 점을 계획서 3.5절과 단계 4 설계 지침에 남겼다.
- CSV 컬럼 추가에도 하위 호환이 유지됨을 구/신 스키마 혼재 상태로 검증했다. 문서 3종(README, BUILD_AND_USAGE, analysis/README)과 계획서 HTML을 갱신했다.

## 2026-08-13 15:29 KST - 부하 테스트 툴킷 구축 계획 수립 (Docs/LoadTest_Improvement_Plan.html)

- 다음 세션 구현용 계획서를 HTML로 작성했다. 착수 전 `Test/LoadTest`를 빌드하고 100 클라이언트 30초 프로브 실행(`run_id=probe01`)을 돌려 **추정이 아닌 실측**으로 현재 수준을 진단했다. 기능은 정상이다(연결 실패·타임아웃·서버 예외 0, 세션 누수 없음).
- 측정 정확도에서 문제 4건을 실측으로 확인했다. ① 목표 초당 500요청 대비 정상 구간 484.4요청만 발생(타이머 해상도 + **닫힌 루프 구조** — 서버가 느려지면 부하도 함께 줄어 지연이 과소평가된다). ② 히스토그램을 1초마다 리셋해 **실행 전체 p99가 어디에도 남지 않는다**(1초 창 p99가 529~6,321µs로 12배 요동). ③ 서버 샘플 76행 중 45행이 무부하 구간인데 CSV에 구간 표시가 없다. ④ 종료 시 중복 샘플 3행, 정상 종료가 오류 2건으로 집계.
- 코드 리뷰로 추가 확인: `LatencyHistogram`이 `List<long>`+단일 락이라 고부하에서 측정기 자체가 병목이고, `--transport udp|text`는 대응 서버가 없어 실행 불가다.
- 계획은 A(클라이언트)·B(시나리오)·C(측정)·D(분석)·E(자동화) 22개 항목을 4단계로 나눴다. **단계 1을 "측정 신뢰성"에 배정**했다 — 부하를 정교하게 걸어도 결과를 정확히 읽지 못하면 의미가 없고, `TODO.md`가 개선 항목 10곳 이상에서 이 툴킷을 검증 수단으로 참조하기 때문이다.
- 산출물: `Docs/LoadTest_Improvement_Plan.html`(계획서), `Docs/LoadTest_Toolkit_Pipeline.html`(archify 목표 구조 다이어그램, showcase 검증 9/9 및 4개 뷰포트 통과), `Docs/index.html`에 링크 추가.

## 2026-08-13 14:48 KST - SIMPLIFY.md 구현 (A~D 전 단계 완료)

- 라이브러리 **11,203줄 → 6,603줄(-41%), 85개 파일 → 67개**. 단계마다 빌드·회귀 테스트·튜토리얼 빌드를 확인하고 12개 커밋으로 나눴다. 버전 0.90.0 → 0.91.0.
- 가장 큰 건인 **C-1(수신 필터 이중 경로 단일화)** 을 계획보다 뒤로 미루고 **D-5(문자열 프로토콜 제거)를 먼저** 했다. D-5가 필터 3종을 통째로 지워서 C-1에서 sequence로 옮길 필터가 6종 → 3종으로 줄었기 때문이다.
- C-1 결과 `IReceiveFilter`가 `ReadOnlySequence` 전용이 되고 세션 캐리 버퍼·오프셋 산술이 사라졌다. `ArraySegmentList`/`BinaryUtil`/`SearchMarkState`/`ReceiveFilterBase`/`IOffsetAdapter`/`ISequenceReceiveFilter` 6개 파일이 통째로 삭제됐다. **public API가 바뀌므로 `README.md`에 0.91 마이그레이션 가이드를 넣었다.**
- 실제 버그 2건이 정리됐다. `Setup(rootConfig, config)`가 조용히 no-op이 되던 오버로드 함정(`OnSetup`으로 개명), CloseReason을 `_state`에 곱셈 인코딩해 `Closed` 비트가 서면 엉뚱한 값이 나오던 문제(별도 필드로 분리).
- 검증: 회귀 테스트 31/31, LoadTest 통합 56/56, 실부하(TCP 50클라이언트 20초) 34,608 송신 / 타임아웃 0 / RTT p99 약 0.98ms.

## 2026-08-13 13:11 KST - 라이브러리 코드 간결화 계획 수립 (SIMPLIFY.md)

- 라이브러리 85개 파일 11,197줄을 전수 조사해 간결화 방안을 `SIMPLIFY.md`로 정리했다. 다음 세션의 작업 지시서다.
- 최대 건은 **수신 필터의 이중 경로**(레거시 `byte[]` vs zero-copy `ReadOnlySequence`)다. 필터 6종이 같은 일을 두 알고리즘으로 구현 중이고, sequence 하나로 통일하면 `ArraySegmentList`/`BinaryUtil`/`ReceiveFilterBase` 등 6개 파일이 통째로 사라져 약 1,800줄이 준다.
- 조사 중 **`Setup` 오버로드 함정**을 발견했다. `Setup(rootConfig, config)`를 인자 2개로 부르면 아무것도 안 하고 `true`를 반환하는 `protected virtual` 훅이 선택된다. 저장소의 모든 호출부가 `logFactory:` 명명 인자를 붙인 이유가 이것이다. `OnSetup`으로 개명을 제안했다.
- XML 주석 3,002줄 중 정보가 있는 건 `<remarks>` 33블록뿐이고 동어반복 `<param>` 194개·빈 `<returns>` 75개 등이 나머지다. 기계적 압축만으로 약 1,500줄이 준다.
- A(기계적)+B(중복 통합)+C(구조) 단계까지 하면 11,197 → 약 7,300줄(-35%). D(기능 축소)는 CollectSend·RawDataReceived·문자열 프로토콜 계열 등 8건에 대한 사용자 판단이 필요해 표로 남겨 두었다.

## 2026-08-13 12:38:19 KST - 다이어그램 스킬을 저장소에 포함(팀 공유)

- 팀원 모두가 같은 스킬을 쓰도록 `archify`(v2.14)와 `diagram-design`(v2.3)을 `.claude/skills/` 에 벤더링했다. 저장소를 받으면 별도 설치 없이 바로 쓸 수 있다.
- `diagram-design`은 사용자 개인 설치본(다른 프로젝트용 "플래너의 잉크" 팔레트가 적용된 v2.2)이 아니라 **업스트림 최신 v2.3을 기본 스킨 그대로** 넣었다. 개인 설치본은 그대로 두었다.
- `.gitignore`의 `[Bb]in/`(.NET 빌드 산출물용)이 `archify/bin/`(CLI 진입점)까지 제외하고 있어 스킬 경로만 예외 처리했다. 이걸 놓쳤으면 스킬이 동작하지 않는 상태로 커밋될 뻔했다.
- `CLAUDE.md`의 설치 안내를 "위치 + 갱신 방법"으로 바꿨다.

## 2026-08-13 12:23:55 KST - 다이어그램 스킬 설치 및 문서 작성 규칙 추가

- `archify`(tt-a1i, MIT v2.14)를 `~/.claude/skills/archify`에 설치했다. 저장소 루트가 아니라 `archify/` 하위 폴더가 스킬 본체다. `archify doctor` 전 항목 통과(Node.js v22).
- `diagram-design`(cathrynlavery, MIT)은 이미 설치돼 있어 그대로 뒀다. 업스트림은 v2.3이지만 설치본 v2.2에 사용자가 커스터마이즈한 스타일 가이드("플래너의 잉크" 팔레트)가 있어 덮어쓰지 않았다.
- `CLAUDE.md`에 문서 작성 규칙(어떤 다이어그램에 어떤 스킬을 쓰는지)과 두 스킬의 설치 방법을 적었다. 두 저장소 모두 스킬이 하위 폴더에 있어 그 폴더만 복사해야 하며, 적어둔 명령이 그대로 동작하는지 실제로 클론해 확인했다.
- `CLAUDE.md`의 디렉토리 설명에 남아 있던 `BufferManager`(이미 제거된 클래스)를 현재 구성으로 고쳤다.


## 2026-08-13 12:14:29 KST - 빌드 산출물 이름 충돌 해소 및 실행 스크립트 갱신

- 같은 폴더에 같은 이름으로 출력해 서로 덮어쓰던 프로젝트 4개에 고유한 `AssemblyName`을 지정했다: `GameServer_MoDedicated` / `GameServer_MoDedicated2`(둘 다 `GameServer`였음), `EchoClient` / `PvPGameServer_Client`(둘 다 `csharp_test_client`였음).
- 실행 스크립트 12개가 전부 `net9.0\`(GateServer는 `net5.0\`)을 가리켜 동작하지 않던 것을 `net10.0\`으로 고쳤다. 실행 스크립트가 없던 `GameServer_MoDedicated2`용을 새로 추가했다.
- 결과: 출력 충돌 0건, `MSB3061`(파일 삭제 거부) 경고 소멸. 32개 프로젝트 빌드 오류 0개, CS 경고 0개, 회귀 테스트 36개 통과.
- 남은 `MSB3026`은 9개 서버가 같은 `00_server_bins` 폴더로 동일한 NuGet 의존성을 동시에 복사할 때 간헐적으로 뜨는 재시도 경고다. MSBuild가 재시도해 성공하며, 폴더를 공유하는 현재 구성(모든 `run_*.bat`이 그 폴더에 있음)에 따른 것이라 그대로 두었다.


## 2026-08-13 11:53:51 KST - 빌드 점검, net10.0 통일, MessagePack→MemoryPack 전환

- 디스크의 32개 프로젝트가 모두 솔루션에 등록된 것을 확인하고, obj 전체 삭제 후 프로젝트별로 각각 클린 빌드해 전부 성공(CS 경고 0개)을 확인했다.
- 클라이언트 5개가 아직 `net8.0-windows*`였던 것을 `net10.0-windows*`로 올렸다. net10.0 프레임워크에 이미 포함돼 NU1510이 뜨던 `Microsoft.CSharp`/`System.ValueTuple`/`System.Threading.Tasks.Extensions` 참조도 제거했다.
- `Microsoft.Windows.Compatibility`(8.0.0/8.0.6/9.0.3 혼재)와 `Microsoft.Extensions.Hosting`/`Logging`(9.0.3)을 10.0.8로 통일했다. 이로써 `System.Data.SqlClient 4.8.5`(높음 심각도) 경고가 사라졌다.
- MessagePack(3.1.6, 보안 권고 12건)을 쓰던 7개 프로젝트를 전부 MemoryPack 1.21.4로 전환했다. 타입 86개에 `partial`+`[MemoryPackable]`을 적용하고 `[Key]` 특성 110개를 제거했다(MemoryPack은 선언 순서로 직렬화). 직렬화 대상 멤버 수가 110개로 정확히 일치함을 확인했고, 통신 짝의 패킷 레이아웃 일치와 런타임 라운드트립도 검증했다.
- 어느 프로젝트도 참조하지 않던 `00_superSocketLite_libs` 프리빌드 DLL 디렉터리를 `Template`/`Tutorials` 양쪽에서 삭제하고, 이를 안내하던 `Tutorials/README.md`를 실제 구성(프로젝트 참조)에 맞게 고쳤다.
- 결과: 32개 프로젝트 전부 빌드 경고 0개(기존 NuGet 보안 권고 168건 소멸), 회귀 테스트 36개 통과.


## 2026-08-13 11:01:45 KST - 로깅 인터페이스 정비

- 외부 로그 라이브러리(NLog/Serilog/ZLogger/log4net/MEL) 연동성을 점검하고 발견한 문제를 전부 처리했다.
- `MicrosoftLoggingLogFactory` 브리지를 내장해 어댑터 없이 MEL 프로바이더를 쓰는 모든 라이브러리를 커버했고, MEL과 겹치던 `ILoggerProvider`를 `ILogProvider`로 개명했다.
- 할당 없는 `LogSessionContext`(readonly struct) + `LogEventLevel` 기반 구조적 로깅을 추가하고, 세션 정보를 개행으로 이어붙이던 9곳을 제거해 모든 로그를 단일 행으로 만들었다.
- 죽은 `IsSharedConfig` 제거, 전 레벨 Exception 오버로드/`Trace` 추가(전부 default 구현이라 하위 호환 유지), 튜토리얼·템플릿 어댑터 13벌 정리.
- 전체 솔루션 33개 프로젝트 빌드 CS 경고 0개·오류 0개, 회귀 테스트 36개(신규 6개) 전부 통과.


## 2026-08-13 09:48:19 KST - 미사용 코드·기능 제거

- 라이브러리 전수 조사 후 참조가 전혀 없는 코드를 제거했다: `SendingQueue`(ChannelSendingQueue로 대체됨), HTTP 필터 3종, `IReceiveFilter`의 Span 오버로드, `AssemblyUtil`, `Platform`, `ISystemEndPoint`, `IWorkItem`, `HotUpdateAttribute`, 커맨드 어셈블리 설정 등 소스 파일 12개.
- `SmartPool`의 인터페이스 4종을 단일 클래스로, `ArraySegmentList`를 byte 전용으로 축약하고 XML 설정 잔재인 죽은 config 속성 8개를 제거했다.
- 결과: 13,578줄 → 10,777줄(-20.6%). 전체 솔루션 33개 프로젝트 빌드 오류 0개, 회귀 테스트 30개 전부 통과.
- 제거된 이름과 대체 수단 대응표는 `.claude/tasks.md`의 TASK-20에 정리했다.

## 2026-08-11 17:08:09 KST - VS Code 전체 분석 문서화

- README에 `SuperSocketLite2.slnx` 기본 솔루션 설정과 F12 부분 실패 원인을 정리했다.
- Docs 매뉴얼에 설정·재로드·로그 검증·문제 해결 절차를 설명하는 전용 문서를 추가했다.
- 문서 인덱스에 새 매뉴얼 링크를 연결하고 설정 JSON과 상대 링크를 검증했다.

## 2026-08-11 17:00:30 KST - VS Code 저장소 전체 분석 설정

- VS Code C# 확장의 기본 솔루션을 루트 `SuperSocketLite2.slnx`로 지정했다.
- `.slnx`에 저장소의 C# 프로젝트 33개가 모두 등록된 것을 `dotnet sln list`로 확인했다.
- 실행 중인 Roslyn이 임시 `Canonical.csproj` 대신 통합 솔루션을 로드하도록 창 재로드가 필요하다.

## 2026-08-11 15:08:45 KST - VS 2026 통합 솔루션 원격 반영

- 새 통합 솔루션과 작업 로그를 커밋 대상으로 정리했다.
- 전체 프로젝트 33개 등록 및 빌드 검증 결과를 기록했다.
- `main` 브랜치의 변경 사항을 `origin/main`에 반영하도록 준비했다.

## 2026-08-11 14:59:45 KST - VS 2026 통합 솔루션 생성

- 저장소 전체에서 C# 프로젝트 33개를 검색했다.
- 루트에 XML 기반 VS 2026 솔루션 `SuperSocketLite2.slnx`를 생성했다.
- 모든 프로젝트를 디렉터리 기반 솔루션 폴더에 등록하고 누락 여부를 확인했다.
- NuGet 복원 후 단일 MSBuild 노드로 전체 빌드해 오류 0개를 확인했다.
