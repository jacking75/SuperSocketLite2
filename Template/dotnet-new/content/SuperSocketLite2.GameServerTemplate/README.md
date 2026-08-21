# SuperSocketLite2.GameServerTemplate

[SuperSocketLite2](https://github.com/jacking75/SuperSocketLite2) 기반 TCP 게임 서버.

## 실행

```bash
dotnet build -c Release
dotnet run -c Release -- --port 32452
```

## 프로토콜

```
[2바이트 전체 길이 (LE)][2바이트 패킷 ID (LE)][본문]
```

전체 길이는 헤더 4바이트를 포함한 값이다. 패킷 ID는 `Protocol.cs` 의 `PacketId` 열거형에 있다.
기본으로 `ReqEcho(101)` 하나가 들어 있고, 받은 본문을 `ResEcho(102)` 로 그대로 돌려준다.

## 파일

| 파일 | 역할 |
|---|---|
| `Protocol.cs` | `PacketId`, `PacketRequestInfo`, `PacketReceiveFilter` |
| `MainServer.cs` | `NetworkSession`, `MainServer`, 핸들러 디스패치, 브로드캐스트 |
| `PacketHandlers.cs` | 패킷별 처리 로직 |
| `PacketWriter.cs` | 응답 패킷 생성·전송 |
| `Program.cs` | 설정, 시작, 우아한 종료 |

## 패킷 추가하기

1. `Protocol.cs` 의 `PacketId` 에 `ReqXxx` / `ResXxx` 추가
2. `PacketHandlers.cs` 에 `HandleXxx` 작성
3. `MainServer.RegisterHandlers()` 에 한 줄 등록

## AI 코딩 에이전트로 작업한다면

`AGENTS.md` 와 `.claude/skills/supersocketlite2/` 가 함께 들어 있다.
이 라이브러리에는 **컴파일은 되지만 부하가 걸려야 터지는** 함정이 몇 개 있어서,
에이전트에게 그 규칙을 먼저 읽히지 않으면 높은 확률로 틀린 코드가 나온다.

자세한 문서는 `Docs/agent/` 에 있다.

## 다음에 볼 것

- 실서비스 로거(Serilog / NLog / ZLogger) 연결 → `Docs/agent/recipes_kr.md` § 7
- MemoryPack 직렬화 → `Docs/agent/recipes_kr.md` § 8
- 네트워크 스레드와 로직 스레드 분리 → `Docs/agent/recipes_kr.md` § 9
- `ServerConfig` 튜닝 → `Docs/agent/api-cheatsheet_kr.md`
