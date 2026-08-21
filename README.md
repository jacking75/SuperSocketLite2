# SuperSocketLite2

**[🇰🇷 한국어 문서 (Korean README)](README_kr.md)**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/)

**SuperSocketLite2** is a high-performance, async TCP/UDP socket server library for .NET, built
for the kind of workload that doesn't forgive sloppy I/O: real-time multiplayer game servers.
It gives you a session-based, event-driven framework on top of `SocketAsyncEventArgs` (IOCP) and
`System.IO.Pipelines`, so you can focus on your game protocol instead of reinventing connection
management, buffer pooling, and backpressure handling.

It is a ground-up rewrite of the original [SuperSocketLite](https://github.com/jacking75/SuperSocketLite),
which was itself a trimmed-down .NET port of [SuperSocket](https://github.com/kerryjiang/SuperSocket) 1.16.
SuperSocketLite2 keeps the parts of that API that made sense and replaces the rest — the receive
path, the send queue, the object pools — with a design built around `Pipelines` and modern .NET.

## Why SuperSocketLite2

- **Zero-copy receive.** Each session owns one `System.IO.Pipelines.Pipe`. Your receive filter
  parses requests straight out of a `ReadOnlySequence<byte>` — there is no per-session carry
  buffer, no extra copy for a request that arrives whole, and an incomplete request simply stays
  in the pipe until the rest of it shows up.
- **A send path built to avoid allocating.** Sends are queued through a lock-free, bounded
  `Channel<T>` per session, drained in batches, and handed to the socket with scatter-gather I/O
  (`SocketAsyncEventArgs.BufferList`) so several queued segments go out in a single syscall.
  `SocketAsyncEventArgs` objects for both receiving and sending are pooled and reused, not
  allocated per connection.
- **You choose the copy semantics.** `Send` is zero-copy (you own the buffer until it's actually
  sent); `SendCopied` copies into a pooled buffer so you can reuse your own buffer immediately;
  `SendAsync` gives you a `ValueTask<bool>` that waits when the send queue is full instead of
  spinning. Pick whichever fits your hot path.
- **Backpressure and graceful shutdown, not an afterthought.** The receive pipe's pause/resume
  thresholds are sized around your configured `MaxRequestLength`, so a slow handler can't grow
  memory without bound. `StopAsync(drainTimeout)` stops accepting new connections and lets
  already-queued responses flush before it closes anything.
- **A pluggable, binary-first protocol layer.** Implement `IReceiveFilter<T>` once and you're done
  — built-in `FixedHeaderReceiveFilter<T>` and `FixedSizeReceiveFilter<T>` cover the common cases
  (length-prefixed and fixed-size packets), and pipelined requests (several complete packets
  arriving in one read) are handled for you.
- **Observable without instrumenting your hot path.** Request/byte counters, active-connection
  gauges, and a request-duration histogram are published through `System.Diagnostics.Metrics`
  (`Meter("SuperSocketLite")`), ready for any OpenTelemetry-compatible collector. The gauges are
  observable, so they cost nothing when nobody is listening.
- **Bring your own logger.** The library depends only on its own tiny `ILog` abstraction, plus a
  built-in `MicrosoftLoggingLogFactory` bridge — so Serilog, NLog, ZLogger, and log4net all work
  out of the box through their `Microsoft.Extensions.Logging` providers.
- **TCP and UDP from the same framework.** UDP sessions get the same `AppSession` model as TCP,
  either keyed by remote endpoint or by a session ID you embed in the datagram yourself.
- **Modern .NET, nullable-annotated, no legacy baggage.** Targets .NET 10, uses
  `System.IO.Pipelines` and `System.Threading.Channels` throughout, and doesn't carry forward
  API surface nobody used ([`Docs/Architecture.html`](Docs/Architecture.html) lists what was
  dropped and what to do instead).

## Quick Start

### Requirements

- .NET 10.0 SDK
- Windows or Linux (the async socket engine and TCP keep-alive options are cross-platform)

### Get the library

SuperSocketLite2 (targeting .NET 10, the `Pipelines`-based engine described in this
document) ships as its own NuGet package, **`SuperSocketLite2`** — a separate package ID from the
older, pre-rewrite `SuperSocketLite` package on NuGet.org (still the .NET 9 line, unaffected by
this repository).

```bash
dotnet add package SuperSocketLite2
```

Or scaffold a complete, runnable server — protocol, handler dispatch, graceful shutdown, and the
AI-agent guidance described in [Working with an AI coding agent](#working-with-an-ai-coding-agent):

```bash
dotnet new install SuperSocketLite2.Templates
dotnet new sslite2-server -n MyGameServer
```

That's it — no local checkout needed. [`Tutorials/EchoServer_NuGet`](Tutorials/EchoServer_NuGet) is a
complete, runnable server built entirely against the NuGet package (identical to
[`Tutorials/EchoServer`](Tutorials/EchoServer), just with a `PackageReference` instead of a
`ProjectReference`).

If you want to build against the latest source instead of a released version — to pick up unreleased
fixes, or to modify the library itself — reference the project directly:

```bash
git clone https://github.com/jacking75/SuperSocketLite2.git
```

```xml
<ItemGroup>
  <ProjectReference Include="..\SuperSocketLite2\SuperSocketLite\SuperSocketLite.csproj" />
</ItemGroup>
```

### A minimal echo server

A protocol is a length-prefixed packet: a 4-byte little-endian body length, followed by the body.

```csharp
// EchoProtocol.cs
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

// The request info. Body points straight at the receive pipe and the filter hands back the
// same instance every time, so receiving a packet allocates nothing at all.
// The catch: both are only valid until your handler returns. See
// Docs/GC_Copy_Minimization.md if you need to pass a packet to another thread.
public sealed class MyRequestInfo : IRequestInfo
{
    public string Key => string.Empty;

    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(ReadOnlySequence<byte> body) => Body = body;
}

// The receive filter: parse a 4-byte length prefix, then the body.
public sealed class MyReceiveFilter : FixedHeaderReceiveFilter<MyRequestInfo>
{
    // Safe to reuse: there is one filter per session, and the next packet is only parsed
    // after your handler for the previous one has returned.
    private readonly MyRequestInfo _reusable = new();

    public MyReceiveFilter() : base(4) { }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buffer = stackalloc byte[4];
        header.CopyTo(buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    protected override MyRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        _reusable.Set(body);
        return _reusable;
    }
}

public sealed class MySession : AppSession<MySession, MyRequestInfo> { }

public sealed class MyServer : AppServer<MySession, MyRequestInfo>
{
    public MyServer() : base(new DefaultReceiveFilterFactory<MyReceiveFilter, MyRequestInfo>()) { }
}
```

```csharp
// Program.cs
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;

var config = new ServerConfig
{
    Ip = "Any",
    Port = 2012,
    MaxConnectionNumber = 1000,
    Mode = SocketMode.Tcp,
    Name = "EchoServer"
};

var server = new MyServer();

// Echo the body back. SendCopied copies into the library's own pooled send buffer, so it is
// safe to hand it memory that is only valid for the duration of this handler.
server.NewRequestReceived += (session, request) =>
{
    if (request.Body.IsSingleSegment)
        session.SendCopied(request.Body.FirstSpan);
    else
        session.SendCopied(request.Body.ToArray());   // rare: the packet spans pipe segments
};

if (!server.Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory()))
{
    Console.WriteLine("Failed to set up the server.");
    return;
}

server.Start();
Console.WriteLine("Listening on 2012. Press any key to stop...");
Console.ReadKey();
server.Stop();
```

That's a complete, runnable TCP server. [`Tutorials/EchoServer`](Tutorials/EchoServer) is the same
thing as a project you can run (referencing the library via `ProjectReference`);
[`EchoServer_NuGet`](Tutorials/EchoServer_NuGet) is the identical server referencing the
`SuperSocketLite2` NuGet package instead. [`EchoServerEx`](Tutorials/EchoServerEx) adds options
parsing and NLog, and [`EchoServer_GenericHost`](Tutorials/EchoServer_GenericHost) runs it as a
`Generic Host` service.

## How It Works

```
[TCP client]
    ↓
TcpAsyncSocketListener        accept loop(s) (SocketAsyncEventArgs / IOCP)
    ↓
AsyncSocketServer              pools SocketAsyncEventArgs, creates sessions
    ↓
SocketSession                  state machine (InReceiving / InSending / Closed),
    ↓                          one System.IO.Pipelines.Pipe per session
AppSession<TSession, TReq>     your session type, Send/SendAsync/SendCopied
    ↓
IReceiveFilter<TRequestInfo>   parses a ReadOnlySequence<byte> into a request
    ↓
AppServerBase.NewRequestReceived   your handler runs here
```

The IOCP completion thread only advances the pipe writer and posts the next receive — it never
runs your application code. A dedicated task per session reads the pipe, runs your filter, and
dispatches `NewRequestReceived`, so a slow handler on one connection can't stall another
connection's I/O.

Sending mirrors that split: `TrySend`/`Send` enqueue onto a per-session `Channel`, and a
single-flight send loop drains everything currently queued into one batch per socket write. A
partial send (rare, but possible) is retried with the remaining bytes, not requeued from scratch.

For the full breakdown — object pool sizing, the receive pipe's backpressure thresholds, the
session state machine, and the logging abstraction — see
[`Docs/Architecture.html`](Docs/Architecture.html).

## Sending Data

| Method | Copy semantics | When to use |
|---|---|---|
| `Send(byte[], offset, length)` / `TrySend` | Zero-copy — the library keeps a reference to your array | You own a buffer you won't touch again until it's sent |
| `SendCopied(ReadOnlySpan<byte>)` / `TrySendCopied` | Copies into a pooled buffer | You need your buffer back immediately (e.g. a reused scratch buffer) |
| `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` | Zero-copy for array-backed memory | You want to `await` when the send queue is full, instead of the blocking `Send` retry loop |
| `Send(IList<ArraySegment<byte>>)` | The **list** is copied on enqueue; the underlying arrays are not | Sending several segments as one logical message |

`TrySend*` returns `false` instead of blocking or throwing when the session is closed or its
queue is full; `Send`/`SendCopied` spin-wait up to `ServerConfig.SendTimeOut` and then throw
`TimeoutException`. See [`Docs/Cautions.html`](Docs/Cautions.html) for the exact
buffer-lifetime rules around the zero-copy overloads.

## Configuration

`ServerConfig` covers the usual suspects (`Port`, `MaxConnectionNumber`, `ReceiveBufferSize`,
`SendTimeOut`, TCP keep-alive, idle session cleanup, ...) plus a few knobs worth knowing about:

| Setting | Default | What it's for |
|---|---|---|
| `ReceiveInlineOnIocpThread` | `true` | Advances the receive pipe directly on the IOCP completion thread instead of dispatching to the thread pool — saves a thread hop and two `Task` allocations per received packet. |
| `PreAllocateSAEA` / `MinPoolSize` | `true` / `100` | Pre-allocate every pooled `SocketAsyncEventArgs` at startup for the best accept-time latency, or grow the pool on demand from `MinPoolSize`. |
| `MaxReceivePipeBufferSize` | `65536` | The receive pipe's backpressure threshold; automatically raised to fit `MaxRequestLength` so a large max request can't deadlock the receive loop. |
| `SyncSessionConnectedEvent` | `false` | Raises `NewSessionConnected` synchronously during accept, so it's structurally guaranteed to run before a fast client's first request. |
| `AcceptLoopCount` | `1` | Runs several concurrent accept loops on the same listening socket — helps a server that has to absorb a reconnect storm. |
| `UseZeroByteReceive` | `false` | An idle session waits on a zero-byte receive instead of holding a real receive buffer — cuts idle-connection memory on servers where most sessions are quiet. |
| `KeepAliveRetryCount` | `5` | How many unacknowledged keep-alive probes go out before the connection is treated as dead. `0` or less leaves the OS default alone. Lives on `ServerConfig` only, so a hand-written `IServerConfig` gets the default. |

## UDP Support

UDP sessions go through the same `AppSession`/`IReceiveFilter` pipeline as TCP. Two modes are
supported: one session per remote endpoint (the default), or — when your request type inherits from
`UdpRequestInfo` — a session ID you embed in the payload yourself, so a client can keep the same
logical session across a NAT rebind. See [`Tutorials/SimpleUDPServer`](Tutorials/SimpleUDPServer).

## Observability

Everything is published through a single `Meter("SuperSocketLite")`:

- **Counters**: `total-requests`, `total-bytes-received`, `total-bytes-sent`, `sessions-rejected`,
  `send-queue-full`, `send-errors`, plus `active-connections` (an `UpDownCounter`)
- **Histogram**: `request-duration` (time spent in your request handler)
- **Gauges**: `session-count`, plus internal send-queue-depth and `SocketAsyncEventArgs`
  pool-usage gauges (not exposed as public C# properties, but visible to any metrics collector
  subscribed to the meter)

The gauges are `ObservableGauge`s, so nothing is computed unless a collector is actually
listening — a send-queue-depth reading walks the sessions only at the moment someone asks for it.
The counters are different: they are updated as the work happens whether or not anyone is
subscribed, which costs a single `Add` per event.

## Examples

The [`Tutorials/`](Tutorials) directory builds up from a bare echo server to more complete
patterns:

| Project | What it shows |
|---|---|
| [`EchoServer`](Tutorials/EchoServer) | The minimal end-to-end setup |
| [`EchoServer_NuGet`](Tutorials/EchoServer_NuGet) | The same server, built against the `SuperSocketLite2` NuGet package instead of a project reference |
| [`EchoServerEx`](Tutorials/EchoServerEx) | Command-line options, NLog integration |
| [`EchoServer_GenericHost`](Tutorials/EchoServer_GenericHost) | Running as a `Generic Host` service |
| [`ChatServer`](Tutorials/ChatServer) / [`ChatServerEx`](Tutorials/ChatServerEx) | Broadcasting to multiple sessions |
| [`BinaryPacketServer`](Tutorials/BinaryPacketServer) | A structured binary protocol |
| [`MultiPortServer`](Tutorials/MultiPortServer) | Listening on several ports at once |
| [`SimpleUDPServer`](Tutorials/SimpleUDPServer) | UDP sessions |
| [`GateServer_GameServer`](Tutorials/GateServer_GameServer), [`PvPGameServer`](Tutorials/PvPGameServer), [`GameServer_MoDedicated`](Tutorials/GameServer_MoDedicated) | Closer-to-production game server shapes |

## Working with an AI coding agent

This library has a handful of rules that **compile fine and work under light load, but corrupt data
once real traffic arrives** — the receive pipe reuses its buffers, so a `RequestInfo` that escapes
its handler, or a pooled buffer handed to the zero-copy `Send`, only breaks when the pool actually
wraps around. An agent that hasn't been told about them will write that code confidently, and a
passing smoke test will not catch it.

So the guidance ships with the library rather than living in a wiki:

| What you get | Where it comes from |
|---|---|
| **Build-time enforcement** — 7 analyzer rules (`SSL001`–`SSL007`) for exactly these mistakes | Bundled in the `SuperSocketLite2` package; on as soon as you reference it |
| **Agent-readable docs** — API cheat sheet, the caveats as do/don't code pairs, 11 recipes, verification steps | [`Docs/agent/`](Docs/agent) |
| **A Claude Code skill** | [`.claude/skills/supersocketlite2/`](.claude/skills/supersocketlite2) — checked in, nothing to install |
| **`AGENTS.md` + the skill + the docs inside *your* project** | `dotnet new sslite2-server` puts them in the generated project |
| **A headless way for the agent to prove the server runs** | [`Test/SmokeClient`](Test/SmokeClient) |

The last one matters more than it looks. "It builds" is not evidence here, and the repository's other
test clients are WinForms — an agent can't run them. `SmokeClient` is a console app that connects,
round-trips packets over as many concurrent connections as you ask for, and exits non-zero on
mismatch:

```bash
dotnet run --project Test/SmokeClient -c Release -- --port 32452 -n 50 -c 20 --size 512 --expect-echo
```

The analyzer rules, briefly — see [`Docs/agent/analyzers.md`](Docs/agent/analyzers.md) for the full
list and how to tune severities:

| Rule | Catches |
|---|---|
| `SSL001` / `SSL002` | A `RequestInfo` or its body stored in a field, or captured in a lambda |
| `SSL003` | An `ArrayPool` buffer passed to the zero-copy `Send`/`TrySend` |
| `SSL004` | `ReadOnlySequence.First.Span` — reads only the first segment |
| `SSL005` | An `async` request handler (it returns at the first `await`, and the pipe moves on) |
| `SSL006` | `Setup()` / `Start()` return values ignored — they report failure with `false`, not an exception |
| `SSL007` | `GetAllSessions()` / `GetSessions()` used without a null check |

### Prompts that work

Four things separate a prompt that gets correct code from one that doesn't:

1. **Send it to the caveats first.** Everything else the agent can infer from the code; the
   lifetime contracts it cannot.
2. **Spell the wire format out in bytes.** "A 4-byte length prefix" is ambiguous — say whether it
   is little- or big-endian, and whether the length includes the header.
3. **Ask for proof, not a build.** Require a run under concurrent connections. A single-connection
   test passes even when the code is wrong.
4. **Forbid suppressing `SSL0xx` warnings.** Left unsaid, an agent that hits one may reach for
   `#pragma warning disable`.

Copy and adapt:

**Starting a server**

> Build a TCP game server with SuperSocketLite2. Read `Docs/agent/cautions.md` and
> `Docs/agent/recipes.md` § 1 first.
> Wire format: `[2-byte total length, little-endian][2-byte packet ID, little-endian][body]`, where
> the total length includes the 4-byte header. Max packet 8KB, max 3000 connections, port 32452.
> Start with one echo packet. Then run the server and verify with
> `Test/SmokeClient` over at least 50 concurrent connections before telling me it's done.

**Adding a packet to an existing server**

> Add a `ReqEnterRoom` / `ResEnterRoom` packet pair, following the existing packets in
> `Protocol.cs` and `PacketHandlers.cs`. Room state lives in a dictionary on the server.
> Before you write the handler, re-read `Docs/agent/cautions.md` § 4 — the request body must not
> escape the handler. Verify with `Test/SmokeClient` and show me the output.

**A protocol the built-in filters don't cover**

> My protocol is `[1-byte type][3-byte body length, big-endian][body]`, and type `0x00` is a
> heartbeat with no body. Write the `IRequestInfo` and the receive filter.
> See `Docs/agent/recipes.md` § 2 and § 4. Reuse a single request instance per session so
> receiving allocates nothing, and remember the header can span pipe segments.

**Reviewing code you already have**

> Review `GameServer/PacketHandlers.cs` against the checklist at the end of
> `Docs/agent/cautions.md`. I care most about anything that only breaks under load — a request or
> its body escaping a handler, a pooled buffer sent zero-copy, a sequence read as if it were one
> segment. Report what you find before changing anything.

**Chasing corruption that only shows up under load**

> Under load, clients occasionally receive a response belonging to another request. It never
> reproduces with one connection. Read `Docs/agent/cautions.md` § 1, § 2 and § 4, then find which
> one this code violates. Reproduce it first with
> `Test/SmokeClient -n 100 -c 50 --size 1024 --expect-echo`, then fix it and show the run passing.

**Moving packets to a logic thread**

> Right now the packet handlers do their work inline. Move them onto a single logic thread fed by
> a `Channel`. Follow `Docs/agent/recipes.md` § 9: copy the body out with `ArrayPool` inside the
> handler and return the buffer in exactly one place, including the path where the queue is full.
> Keep per-packet allocations at zero and don't suppress any `SSL0xx` warning — fix the cause.

These assume you have this repository checked out, for `Test/SmokeClient`. If you only reference
the package, say "verify it following `Docs/agent/verify.md`" instead — that document carries a
self-contained client you can drop into a scratch project, and `dotnet new sslite2-server` puts a
copy of it in your project.

None of this is agent-specific, incidentally. A human reading
[`Docs/agent/cautions.md`](Docs/agent/cautions.md) before their first packet handler will save
themselves the same afternoon.

## Testing & Quality

The library ships with a substantial safety net of its own: a **40-case regression suite**
covering the receive/send pipelines, the session state machine, close-path races, and logging
adapters (`Test/SuperSocketLiteRegressionTests`), plus a **load-testing toolkit** with its own
110-test self-suite (`Test/LoadTest`) that drives real TCP/UDP traffic against a live server,
produces an HTML report, and can gate a change on throughput/latency regressions against a
baseline run. Both suites are run before any change to the core library lands.

```bash
dotnet run --project Test/SuperSocketLiteRegressionTests -c Release
dotnet run --project Test/LoadTest/SuperSocketLite.LoadTest.Tests -c Release
```

For your own server, [`Test/SmokeClient`](Test/SmokeClient) is a console client that connects to a
running server and round-trips packets, exiting non-zero on any mismatch — usable from CI and from
an AI coding agent, unlike the WinForms test clients.

```bash
dotnet run --project Test/SmokeClient -c Release -- --port 32452 --expect-echo
```

## Documentation

- **[Agent-readable docs](Docs/agent)** — the same material as the HTML documents below,
  in plain Markdown: [API cheat sheet](Docs/agent/api-cheatsheet.md),
  [caveats](Docs/agent/cautions.md), [recipes](Docs/agent/recipes.md),
  [verification](Docs/agent/verify.md), [analyzer rules](Docs/agent/analyzers.md).
  Start here if you (or your agent) want answers without opening a 650KB standalone HTML page
- [Architecture & data flow](Docs/Architecture.html) — layers, the receive/send/UDP paths, logging,
  removed features, and the optimizations that were rejected
- [Coding conventions](.claude/conventions.md) *(Korean)*
- [Known caveats](Docs/Cautions.html) — thread-safety notes, zero-copy buffer lifetime, UDP quirks
- [Minimising GC and copies](Docs/GC_Copy_Minimization.md) *(Korean)* — how to get to zero
  per-packet allocations in your receive filter, packet handlers, and send calls
- [Getting Started](Docs/Getting_Started.html) — build, usage, and the same caveats as one page
- [Diagrams](Docs/index.html) — architecture, TCP connection flow, receive/send pipeline detail
- [Setting up VS Code for whole-repository analysis](Docs/VSCode_Repository_Analysis.html)

## Contributing

Issues and pull requests are welcome on [GitHub](https://github.com/jacking75/SuperSocketLite2).

## License

[MIT](LICENSE)
