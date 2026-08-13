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
- 개선 작업 목록 → `.claude/tasks.md`
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

### 스킬 설치

두 스킬 모두 MIT 라이선스이며 사용자 스킬 디렉터리(`~/.claude/skills/`)에 설치한다.
저장소에는 포함하지 않으므로 각 개발 환경에서 아래처럼 설치한다.
**두 저장소 모두 스킬이 루트가 아니라 하위 폴더에 있으므로 그 폴더만 복사한다.**

```bash
# archify  (https://github.com/tt-a1i/archify)  — 스킬은 archify/ 하위 폴더
git clone --depth 1 https://github.com/tt-a1i/archify.git /tmp/archify-repo
cp -r /tmp/archify-repo/archify ~/.claude/skills/archify
node ~/.claude/skills/archify/bin/archify.mjs doctor   # 설치 확인. Node.js 18 이상 필요

# diagram-design  (https://github.com/cathrynlavery/diagram-design) — 스킬은 skills/diagram-design/ 하위 폴더
git clone --depth 1 https://github.com/cathrynlavery/diagram-design.git /tmp/dd-repo
cp -r /tmp/dd-repo/skills/diagram-design ~/.claude/skills/diagram-design
```

`diagram-design`은 `references/style-guide.md`의 색상·폰트를 프로젝트에 맞게 바꿔 쓰는 구조다.
업스트림을 새로 받아 덮어쓰면 그 설정이 사라지므로, 갱신할 때는 `style-guide.md`를 먼저 백업한다.