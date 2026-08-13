# 작업 로그

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
