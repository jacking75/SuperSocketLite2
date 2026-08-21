# SuperSocketLite2

## 프로젝트 개요
C# 비동기 I/O 소켓 서버 라이브러리.
.NET 10.0 타겟, TCP/UDP 지원, 주 사용처는 모바일 게임 서버.
  
  
## 디렉토리 구조

```
SuperSocketLite/          ← 핵심 라이브러리 소스
├── SocketBase/           ← AppServer/AppSession 베이스, 인터페이스
├── Protocol/             ← ReceiveFilter 구현체들
├── Common/               ← SmartPool, ChannelSendingQueue 등 유틸리티
├── AsyncSocketServer.cs  ← TCP 비동기 서버 (핵심)
├── SocketSession.cs      ← 소켓 세션 상태 머신 (핵심)
└── UdpSocketServer.cs    ← UDP 서버
Analyzers/                ← Roslyn 애널라이저 (SSL001~SSL007). 라이브러리 패키지에 실려 나간다
Docs/agent/               ← 에이전트용 마크다운 문서 (HTML 문서의 기계가 읽는 판)
Tutorials/                ← 예제 프로젝트 (EchoServer, ChatServer 등)
Template/                 ← 게임 서버 템플릿
└── dotnet-new/           ← `dotnet new sslite2-server` 템플릿 패키지
Test/                     ← 테스트 프로젝트
└── SmokeClient/          ← 헤드리스 스모크 클라이언트 (서버 왕복 검증)
```
  

## 빌드

```bash
cd SuperSocketLite
dotnet build -c Release
# 출력: 저장소 루트의 bin/net10.0/ (csproj의 OutputPath가 ..\bin)
```
  
  
## 라이브러리를 *사용하는* 코드를 쓸 때

이 저장소를 고치는 게 아니라 **SuperSocketLite2로 서버를 만드는** 작업이라면
`supersocketlite2` 스킬을 쓴다(`.claude/skills/supersocketlite2/`, 저장소에 포함되어 있다).

깊이 있는 내용은 `Docs/agent/`에 마크다운으로 있다. **필요한 것만 골라 읽는다.**

| 필요한 것 | 문서 |
|---|---|
| 어디부터 볼지 | `Docs/agent/README.md` |
| 타입·네임스페이스·시그니처·`ServerConfig` 기본값 | `Docs/agent/api-cheatsheet.md` |
| 컴파일은 되지만 부하가 걸려야 터지는 함정 8가지 | `Docs/agent/cautions.md` |
| 복사해 쓰는 코드 11종 | `Docs/agent/recipes.md` |
| 만든 서버가 실제로 도는지 확인 | `Docs/agent/verify.md` |
| `SSL0xx` 빌드 경고의 의미 | `Docs/agent/analyzers.md` |

> **`Docs/*.html`은 열지 않는다.** `Library_Architecture.html` 같은 단독 실행 HTML은
> 파일 하나가 650KB라 컨텍스트를 통째로 날린다. 같은 내용이 `Docs/agent/*.md`에 있다.

## 상세 문서

- 아키텍처 및 코드 흐름 → `Docs/Architecture.html` (한글판 `Docs/Architecture_kr.html`)
- 알려진 주의 사항 → `Docs/Cautions.html` (한글판 `Docs/Cautions_kr.html`)
- 코딩 컨벤션 → `.claude/conventions.md`

아키텍처와 주의 사항은 아키텍처 문서이므로 `.claude/`가 아니라 `Docs/`에 둔다.
영/한 두 판이 본문이고 그 뒤에 따로 원문 Markdown이 있지 않으므로, 내용을 고칠 때는 **두 판을 같이** 고친다.
주의 사항 8가지는 `Docs/Getting_Started.html`(영/한) 7장에도 그대로 실려 있으니 함께 확인한다.
같은 8가지가 `Docs/agent/cautions.md`에도 있으므로 **고칠 때는 네 곳을 같이 고친다.**

## 에이전트 지원 자산을 고칠 때

| 고치는 것 | 같이 확인할 곳 |
|---|---|
| `Docs/agent/cautions.md` | `Docs/Cautions.html`, `Docs/Cautions_kr.html`, `Docs/Getting_Started*.html` 7장 |
| 애널라이저 규칙 추가·변경 | `Descriptors.cs`, 애널라이저 클래스, `AnalyzerReleases.Unshipped.md`, `Docs/agent/analyzers.md` |
| `Docs/agent/*.md`, `.claude/skills/supersocketlite2/**` | 손댈 것 없다. 템플릿 패키지가 pack 때 원본을 그대로 집어넣는다 |
| 라이브러리 패키지 버전 | `Template/dotnet-new/SuperSocketLite2.Templates.csproj`, 템플릿 프로젝트의 `PackageReference` |


## 문서 작성 규칙

문서를 만들 때는 아래 스킬을 사용한다.

| 스킬 | 용도 |
|---|---|
| `archify` | 아키텍처 · 워크플로 · 시퀀스 · 데이터 흐름 · 라이프사이클/상태 다이어그램. 스키마 검증을 거친 단독 실행 HTML을 만든다. Mermaid 입력 변환도 된다. |
| `diagram-design` | 그 외 일반 다이어그램(플로차트, ER, 타임라인, 간트, 차트, 벤 등). 기존 draw.io / Mermaid 파일 가져오기도 된다. |

- 구조를 그리는 다이어그램은 `archify`를 먼저 쓴다. 스키마 검증이 있어 잘못된 그림이 나올 여지가 적다.
- 산출물은 단독 실행 HTML이므로 `Docs/` 에 둔다 (예: `Docs/VSCode_Repository_Analysis.html`).
- 글만 있는 문서(작업 로그, 규칙, 태스크)는 스킬 없이 Markdown으로 쓴다.

### 스킬 위치

두 스킬은 `.claude/skills/` 에 저장소째 포함되어 있다. **따로 설치할 필요가 없고**,
저장소를 받으면 팀원 모두가 같은 버전을 쓰게 된다.

```
.claude/skills/
├── archify/          (tt-a1i, MIT, v2.14)  — Node.js 18 이상 필요
└── diagram-design/   (cathrynlavery, MIT, v2.3)
```

`archify`는 Node.js로 동작한다. 환경 점검은 아래로 한다.

```bash
node .claude/skills/archify/bin/archify.mjs doctor
```

### 스킬 갱신

업스트림에서 받은 그대로 두었으므로 폴더를 통째로 바꾸면 된다.
**두 저장소 모두 스킬이 루트가 아니라 하위 폴더에 있으므로 그 폴더만 복사한다.**

```bash
# archify — 저장소의 archify/ 하위 폴더가 스킬 본체
git clone --depth 1 https://github.com/tt-a1i/archify.git /tmp/archify-repo
rm -rf .claude/skills/archify && cp -r /tmp/archify-repo/archify .claude/skills/archify

# diagram-design — 저장소의 skills/diagram-design/ 하위 폴더가 스킬 본체
git clone --depth 1 https://github.com/cathrynlavery/diagram-design.git /tmp/dd-repo
rm -rf .claude/skills/diagram-design && cp -r /tmp/dd-repo/skills/diagram-design .claude/skills/diagram-design
```

> `diagram-design`은 `references/style-guide.md`의 색상·폰트를 프로젝트에 맞게 바꿔 쓰는 구조다.
> 지금은 업스트림 기본 스킨이며, 이 프로젝트 색을 정했다면 그 파일을 고쳐 커밋한다.
> 갱신 시 덮어쓰면 사라지므로 먼저 백업한다.