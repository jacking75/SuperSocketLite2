# SmokeClient

SuperSocketLite2 서버에 실제로 붙어 패킷을 왕복시키는 **콘솔 전용** 클라이언트.
성공하면 종료 코드 0, 실패하면 1을 돌려주므로 CI와 AI 코딩 에이전트가 그대로 쓸 수 있다.

저장소의 다른 테스트 클라이언트(`Test/TestClient`, `Template/TestClient_MemoryPack`)는
WinForms라 헤드리스로 돌릴 수 없다. 그 빈자리를 메우는 것이 이 프로젝트다.

```bash
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --expect-echo
dotnet run --project Test/SmokeClient -c Release -- --port 32452 -n 50 -c 20 --size 512 --expect-echo
dotnet run --project Test/SmokeClient -- --help
```

기본 프로토콜은 `[2바이트 전체 길이 LE][2바이트 패킷 ID LE][본문]`이고,
`--len-bytes` · `--id-bytes` · `--length-excludes-header` · `--big-endian`으로 바꿀 수 있다.

사용법과 결과 해석은 [`Docs/agent/verify_kr.md`](../../Docs/agent/verify_kr.md)에 정리해 두었다.
