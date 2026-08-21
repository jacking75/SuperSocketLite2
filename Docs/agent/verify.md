# 만든 서버가 실제로 도는지 확인하기

**컴파일이 되는 것과 서버가 도는 것은 이 라이브러리에서 특히 다르다.**
`Docs/agent/cautions.md`의 함정은 전부 컴파일에 통과한다. 그래서 "빌드 성공"으로
작업을 끝내면 안 되고, 반드시 서버를 띄우고 패킷을 왕복시켜 봐야 한다.

## 스모크 클라이언트

`Test/SmokeClient`는 콘솔 전용 클라이언트다. 저장소의 다른 테스트 클라이언트
(`Test/TestClient`, `Template/TestClient_MemoryPack`)는 WinForms라 CI나 에이전트가 돌릴 수 없다.

```bash
# 1) 서버를 띄운다 (별도 셸 또는 백그라운드)
dotnet run --project <내서버> -c Release -- --port 32452

# 2) 왕복을 확인한다. 성공하면 종료 코드 0, 실패하면 1
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --expect-echo
```

기본 프로토콜은 `[2바이트 전체 길이 LE][2바이트 패킷 ID LE][본문]`으로,
`Template/dotnet-new`가 만들어 주는 서버와 같다.

### 자주 쓰는 형태

```bash
# 에코 확인 — 응답 본문이 요청과 같아야 한다
dotnet run --project Test/SmokeClient -c Release -- \
    --port 32452 --packet-id 101 --expect-id 102 --text "hello" --expect-echo

# 동시성 — 50연결 × 20패킷 × 512바이트
dotnet run --project Test/SmokeClient -c Release -- \
    --port 32452 -n 50 -c 20 --size 512 --expect-echo

# 큰 패킷 — MaxRequestLength를 넘겼을 때 서버가 어떻게 구는지 본다
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --size 65000 --timeout 3000

# 단방향 프로토콜 (응답이 없는 서버)
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --no-wait-response -c 100
```

### 프로토콜이 다를 때

| 옵션 | 설명 |
|---|---|
| `--len-bytes <2\|4>` | 길이 필드 크기. 기본 2 |
| `--id-bytes <0\|2>` | 패킷 ID 필드 크기. `0`이면 ID 없는 프로토콜 |
| `--length-excludes-header` | 길이 필드가 본문 길이만 담는 프로토콜 |
| `--big-endian` | 길이·ID를 빅엔디안으로 |

전체 목록은 `dotnet run --project Test/SmokeClient -- --help`.

### 결과 읽기

```
smokeclient -> 127.0.0.1:32452  connections=50 count=20 body=512B
  connected : 50/50
  sent      : 1000
  received  : 1000
  elapsed   : 67ms  (14953 packets/s)
OK
```

- `connected`가 요청한 수보다 적다 → `MaxConnectionNumber`를 확인한다
- `sent`는 늘어나는데 `received`가 0이다 → 프로토콜 옵션이 서버와 다르거나,
  서버 필터가 요청을 완성하지 못하고 있다
- `응답 본문이 요청과 다릅니다 (길이는 같고 내용이 다름)` → **`cautions.md` 1·2·4번 위반이다.**
  부하를 올리면 재현율이 올라간다. `-n 100 -c 50`으로 다시 돌려 본다

## 라이브러리 자체 테스트

라이브러리 코드를 고쳤다면 이 둘을 돌린다.

```bash
dotnet run --project Test/SuperSocketLiteRegressionTests -c Release
dotnet run --project Test/LoadTest/SuperSocketLite.LoadTest.Tests -c Release
```

## 저장소 밖에서 검증하기

패키지만 참조하는 프로젝트라 `Test/SmokeClient`가 없다면, 아래를 임시 콘솔 프로젝트에
붙여 넣으면 같은 일을 한다.

```csharp
using System.Buffers.Binary;
using System.Net.Sockets;

const string Host = "127.0.0.1";
const int Port = 32452;
const short PacketId = 101;

var body = "hello"u8.ToArray();
var packet = new byte[4 + body.Length];
BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(0, 2), (short)packet.Length);
BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(2, 2), PacketId);
body.CopyTo(packet.AsSpan(4));

using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
await socket.ConnectAsync(Host, Port);
await using var stream = new NetworkStream(socket);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await stream.WriteAsync(packet, cts.Token);

var header = new byte[4];
await stream.ReadExactlyAsync(header, cts.Token);

var totalSize = BinaryPrimitives.ReadInt16LittleEndian(header);
var responseId = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(2, 2));
var responseBody = new byte[totalSize - 4];
await stream.ReadExactlyAsync(responseBody, cts.Token);

var echoed = responseBody.AsSpan().SequenceEqual(body);
Console.WriteLine($"id={responseId} body={responseBody.Length}B echoed={echoed}");
return echoed ? 0 : 1;
```

## 보고하기 전 확인

- [ ] 서버가 뜨고 `listening on ...`이 찍혔다
- [ ] 스모크 클라이언트가 종료 코드 0으로 끝났다
- [ ] 동시 연결(`-n 50` 이상)에서도 `received`가 기대치와 같다
- [ ] 서버 로그에 예외나 `unknown packet id`가 없다
- [ ] 빌드 경고 0개

동시 연결 항목을 건너뛰지 않는다. 이 라이브러리의 대표적인 버그는 **연결 1개로는
절대 재현되지 않는다.**
