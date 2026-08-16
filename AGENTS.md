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

- 아키텍처 및 코드 흐름 → `Docs/Architecture.html` (한글판 `Docs/Architecture_kr.html`)
- 알려진 주의 사항 → `Docs/Cautions.html` (한글판 `Docs/Cautions_kr.html`)
- 코딩 컨벤션 → `.claude/conventions.md`

아키텍처와 주의 사항은 아키텍처 문서이므로 `.claude/`가 아니라 `Docs/`에 둔다.
영/한 두 판이 본문이고 그 뒤에 따로 원문 Markdown이 있지 않으므로, 내용을 고칠 때는 **두 판을 같이** 고친다.
주의 사항 8가지는 `Docs/Getting_Started.html`(영/한) 7장에도 그대로 실려 있으니 함께 확인한다.