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
cd SuperSocketLite
dotnet build -c Release
# 출력: 저장소 루트의 bin/net10.0/ (csproj의 OutputPath가 ..\bin)
```
  
  
## 상세 문서

- 아키텍처 및 코드 흐름 → `.claude/architecture.md`
- 코딩 컨벤션 → `.claude/conventions.md`
- 알려진 주의 사항 → `.claude/cautions.md`

위 셋 중 아키텍처와 주의 사항은 사람이 읽는 HTML판이 `Docs/`에 함께 있다
(`Docs/Architecture.html` · `Docs/Architecture_kr.html`, `Docs/Cautions.html` · `Docs/Cautions_kr.html`).
**원문은 `.claude/`의 Markdown 쪽이다.** 내용을 고치면 `Docs/`의 영/한 두 판도 같이 고친다.