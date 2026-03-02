# SuperSocketLite2 — Claude Code 가이드

## 프로젝트 개요
C# 비동기 I/O 소켓 서버 라이브러리.
.NET 10.0 타겟, TCP/UDP 지원, 주 사용처는 모바일 게임 서버.
  
  
## 디렉토리 구조

```
SuperSocketLite/          ← 핵심 라이브러리 소스
├── SocketBase/           ← AppServer/AppSession 베이스, 인터페이스
├── Protocol/             ← ReceiveFilter 구현체들
├── Common/               ← BufferManager, SmartPool 등 유틸리티
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