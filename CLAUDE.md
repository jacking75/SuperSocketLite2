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
Tutorials/                ← 예제 프로젝트 (EchoServer, ChatServer 등)
Test/                     ← 테스트 프로젝트
```
  

## 빌드

```bash
cd SuperSocketLite/SuperSocketLite
dotnet build -c Release
# 출력: SuperSocketLite/bin/
```
  
  
## 상세 문서

- 아키텍처 및 코드 흐름 → `.claude/architecture.md`
- 코딩 컨벤션 → `.claude/conventions.md`
- 알려진 주의 사항 → `.claude/cautions.md`


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