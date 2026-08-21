# Proving the server you built actually runs

**[🇰🇷 한국어 (Korean)](verify_kr.md)**

**Compiling and running are unusually different things in this library.**
Every trap in [cautions.md](cautions.md) compiles cleanly. So "the build succeeded" is not a place
to stop — you have to start the server and round-trip a packet through it.

## The smoke client

`Test/SmokeClient` is a console-only client. The repository's other test clients
(`Test/TestClient`, `Template/TestClient_MemoryPack`) are WinForms, so neither CI nor an agent can
run them.

```bash
# 1) Start the server (separate shell, or in the background)
dotnet run --project <your-server> -c Release -- --port 32452

# 2) Check the round trip. Exit code 0 on success, 1 on failure
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --expect-echo
```

The default protocol is `[2-byte total length LE][2-byte packet ID LE][body]`, matching the server
that `Template/dotnet-new` generates.

### Common invocations

```bash
# Echo check — the response body must equal the request body
dotnet run --project Test/SmokeClient -c Release -- \
    --port 32452 --packet-id 101 --expect-id 102 --text "hello" --expect-echo

# Concurrency — 50 connections x 20 packets x 512 bytes
dotnet run --project Test/SmokeClient -c Release -- \
    --port 32452 -n 50 -c 20 --size 512 --expect-echo

# Oversized packet — see how the server behaves past MaxRequestLength
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --size 65000 --timeout 3000

# One-way protocol (server sends no response)
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --no-wait-response -c 100
```

### When your protocol differs

| Option | Meaning |
|---|---|
| `--len-bytes <2\|4>` | Size of the length field. Default 2 |
| `--id-bytes <0\|2>` | Size of the packet-ID field. `0` for protocols without one |
| `--length-excludes-header` | The length field carries only the body length |
| `--big-endian` | Read and write length and ID as big-endian |

Full list: `dotnet run --project Test/SmokeClient -- --help`.

### Reading the output

```
smokeclient -> 127.0.0.1:32452  connections=50 count=20 body=512B
  connected : 50/50
  sent      : 1000
  received  : 1000
  elapsed   : 67ms  (14953 packets/s)
OK
```

- `connected` lower than requested → check `MaxConnectionNumber`
- `sent` climbing while `received` stays at 0 → your protocol options don't match the server, or
  the server's filter never completes a request
- `response body differs from the request (same length, different content)` → **this is a violation
  of [cautions.md](cautions.md) §1, §2 or §4.** Raise the load to reproduce it more reliably:
  try `-n 100 -c 50`

## The library's own tests

If you changed library code, run both suites.

```bash
dotnet run --project Test/SuperSocketLiteRegressionTests -c Release
dotnet run --project Test/LoadTest/SuperSocketLite.LoadTest.Tests -c Release
```

## Verifying from outside the repository

If you only reference the package and have no `Test/SmokeClient`, drop this into a scratch console
project — it does the same job.

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

## Before you report done

- [ ] The server started and printed `listening on ...`
- [ ] The smoke client exited with code 0
- [ ] `received` matches expectations with concurrent connections (`-n 50` or more)
- [ ] No exceptions or `unknown packet id` entries in the server log
- [ ] Zero build warnings

Do not skip the concurrency item. This library's characteristic bugs **never reproduce on a single
connection.**
