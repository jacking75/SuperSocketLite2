# API cheat sheet

**[🇰🇷 한국어 (Korean)](api-cheatsheet_kr.md)**

The library exposes 55 public types. Building a server actually uses the 12 below.
**Pick from this list rather than going looking.**

## The usings

Nearly every server file starts with some subset of these.

```csharp
using SuperSocketLite.SocketBase;            // AppServer, AppSession, SocketMode, CloseReason,
                                             // ServerState, SessionHandler, RequestHandler
using SuperSocketLite.SocketBase.Config;     // ServerConfig, RootConfig, IServerConfig
using SuperSocketLite.SocketBase.Logging;    // ILog, ILogFactory, ConsoleLogFactory,
                                             // MicrosoftLoggingLogFactory
using SuperSocketLite.SocketBase.Protocol;   // IRequestInfo, IReceiveFilter<T>,
                                             // IReceiveFilterFactory<T>, DefaultReceiveFilterFactory<,>,
                                             // FilterState, UdpRequestInfo
using SuperSocketLite.SocketEngine.Protocol; // FixedHeaderReceiveFilter<T>, FixedSizeReceiveFilter<T>
```

> `FixedHeaderReceiveFilter` and `FixedSizeReceiveFilter` are the only protocol types in
> `SocketEngine.Protocol`; everything else is in `SocketBase.Protocol`. This trips people up
> regularly.

## The 12 you will use

| Type | Namespace | What it's for |
|---|---|---|
| `AppServer<TSession, TRequestInfo>` | `SocketBase` | The server. Derive from it |
| `AppSession<TSession, TRequestInfo>` | `SocketBase` | The session. Derive from it |
| `IRequestInfo` | `SocketBase.Protocol` | One parsed request. Implement it |
| `FixedHeaderReceiveFilter<TRequestInfo>` | `SocketEngine.Protocol` | Base filter for length-prefixed protocols |
| `FixedSizeReceiveFilter<TRequestInfo>` | `SocketEngine.Protocol` | Base filter for fixed-size packets |
| `DefaultReceiveFilterFactory<TFilter, TRequestInfo>` | `SocketBase.Protocol` | Creates one filter per session with `new` |
| `ServerConfig` | `SocketBase.Config` | Port, connection limit, and the rest |
| `RootConfig` | `SocketBase.Config` | Passed to `Setup`. Usually just `new RootConfig()` |
| `SocketMode` | `SocketBase` | `Tcp` / `Udp` |
| `CloseReason` | `SocketBase` | Why a session closed, on `SessionClosed` |
| `ILog` / `ConsoleLogFactory` | `SocketBase.Logging` | Logging. In production, `MicrosoftLoggingLogFactory` |
| `ServerState` | `SocketBase` | `Initializing` / `Running` / `Stopping` … |

## `AppServer<TSession, TRequestInfo>`

```csharp
// Construction — hand the filter factory to the base constructor
public MyServer() : base(new DefaultReceiveFilterFactory<MyFilter, MyRequestInfo>()) { }

// Setup — exactly once, before Start(). Don't start if it returns false
bool Setup(IRootConfig rootConfig, IServerConfig config,
           IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null,
           ILogFactory? logFactory = null);
bool Setup(IServerConfig config, ...);   // without RootConfig

// Lifecycle
bool Start();
bool Start(CancellationToken cancellationToken);
void Stop();
Task StopAsync(TimeSpan drainTimeout);   // flushes queued responses before closing. Prefer this
void Dispose();

// Events — conventionally wired up in the constructor
event SessionHandler<TSession> NewSessionConnected;
event RequestHandler<TSession, TRequestInfo> NewRequestReceived;
event SessionHandler<TSession, CloseReason> SessionClosed;

// Session lookup
TSession? GetSessionByID(string sessionID);
IEnumerable<TSession>? GetAllSessions();                          // can be null
IEnumerable<TSession>? GetSessions(Func<TSession, bool> critera); // can be null
int SessionCount { get; }

// State
ServerState State { get; }
string Name { get; }
ILog Logger { get; }
IServerConfig Config { get; }
DateTime StartedTime { get; }     // UTC
ListenerInfo[]? Listeners { get; }
```

`GetAllSessions()` and `GetSessions()` **can return `null`** — before the server is up, or while it
is shutting down. Broadcast code that `foreach`es the call directly will throw
`NullReferenceException`.

## `AppSession<TSession, TRequestInfo>`

### Sending — which method

| Method | Copy semantics | When the queue is full | Use it when |
|---|---|---|---|
| `SendCopied(ReadOnlySpan<byte>)` | **Copies** | Spins up to `SendTimeOut`, then throws `TimeoutException` | **Your default.** You get your buffer back immediately |
| `TrySendCopied(ReadOnlySpan<byte>)` | Copies | Returns `false` | You want to handle failure without an exception |
| `Send(byte[] data, int offset, int length)` | **No copy (zero-copy)** | Spins, then `TimeoutException` | You own a buffer you won't touch until it's sent |
| `TrySend(byte[], int, int)` | No copy | Returns `false` | As above, with a bool instead of an exception |
| `Send(ArraySegment<byte>)` / `TrySend(...)` | No copy | Spins / `false` | As above |
| `Send(IList<ArraySegment<byte>>)` | **List** is copied, arrays are not | Spins / `false` | Several segments as one logical message |
| `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` | No copy for array-backed memory | `await`s | You'd rather wait than spin |
| `Send(string)` / `TrySend(string)` | — | Spins / `false` | Text protocols |

- **`SendTimeOut` does not apply to `SendAsync`.** Bound the wait yourself with a
  `CancellationToken`.
- `SendCopied` queues nothing and reports success for empty data. `Send(buffer, 0, 0)` genuinely
  transmits a zero-length segment (an empty datagram, over UDP).

### Everything else

```csharp
void Close();
void Close(CloseReason reason);
void SendEndWhenSendingTimeOut();   // must be called before Close() after a TimeoutException

string SessionID { get; }
bool Connected { get; }
IPEndPoint? RemoteEndPoint { get; }
IPEndPoint? LocalEndPoint { get; }
DateTime StartTime { get; }         // UTC
DateTime LastActiveTime { get; }    // UTC, derived from a monotonic tick — a few ms of error
ILog Logger { get; }
LogSessionContext SessionLogContext { get; }   // for structured logging; reading it allocates nothing
IServerConfig Config { get; }
AppServerBase<TSession, TRequestInfo> AppServer { get; }

// Override points
protected virtual void OnSessionStarted();
protected virtual void OnSessionClosed(CloseReason reason);
protected virtual void HandleException(Exception e);
protected virtual void HandleUnknownRequest(TRequestInfo requestInfo);
```

## `IRequestInfo` and filters

```csharp
public interface IRequestInfo
{
    string Key { get; }   // for a binary protocol, string.Empty is fine
}

public interface IReceiveFilter<TRequestInfo> where TRequestInfo : IRequestInfo
{
    TRequestInfo? Filter(ReadOnlySequence<byte> buffer,
                         out SequencePosition consumed, out SequencePosition examined);
    IReceiveFilter<TRequestInfo>? NextReceiveFilter { get; }
    void Reset();
    FilterState State { get; }   // Normal | Error
}
```

Derive from `FixedHeaderReceiveFilter<T>` and you never write `Filter` yourself — just these two:

```csharp
protected abstract int GetBodyLengthFromHeader(ReadOnlySequence<byte> header);
protected abstract TRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header,
                                                    ReadOnlySequence<byte> body);
protected virtual bool ValidateBodyLength(int bodyLength);  // default implementation honours MaxRequestLength
protected int HeaderSize { get; }
```

`FixedSizeReceiveFilter<T>` needs just one:

```csharp
protected abstract TRequestInfo? ProcessMatchedRequest(ReadOnlySequence<byte> buffer);
public int Size { get; }
```

## `ServerConfig` — the settings worth knowing

Only settings with a meaningful default are listed. For the rest see
`SuperSocketLite/SocketBase/Config/ServerConfig.cs`.

| Setting | Default | What it does |
|---|---|---|
| `Ip` | — | `"Any"` or a specific address. Usually `"Any"` |
| `Port` | — | Listening port |
| `Mode` | `Tcp` | `SocketMode.Tcp` / `SocketMode.Udp` |
| `Name` | — | Server name; shows up in logs |
| `MaxConnectionNumber` | 100 | **Over the limit, connections are dropped without raising `NewSessionConnected`** |
| `MaxRequestLength` | 1024 | Maximum request size. Game servers usually need this raised |
| `ReceiveBufferSize` | 4096 | |
| `SendBufferSize` | 2048 | |
| `SendTimeOut` | 5000 (ms) | How long a blocking `Send` spins before throwing |
| `SendingQueueSize` | 5 | Send queue depth per session |
| `ClearIdleSession` | false | Enable idle-session cleanup |
| `IdleSessionTimeOut` | 300 (s) | |
| `ClearIdleSessionInterval` | 120 (s) | |
| `KeepAliveTime` | 600 (s) | |
| `KeepAliveInterval` | 60 (s) | |
| `KeepAliveRetryCount` | 5 | Lives on `ServerConfig` only — a hand-written `IServerConfig` won't get the default |
| `ListenBacklog` | 100 | |
| `NoDelay` | false | Disable Nagle. Usually `true` for real-time games |
| `ReceiveInlineOnIocpThread` | **true** | Advances the pipe on the IOCP thread — saves a thread hop and two `Task` allocations per packet |
| `PreAllocateSAEA` / `MinPoolSize` | true / 100 | Pre-allocate every pooled `SocketAsyncEventArgs` at startup |
| `MaxReceivePipeBufferSize` | 65536 | Receive-pipe backpressure threshold; raised automatically to fit `MaxRequestLength` |
| `SyncSessionConnectedEvent` | **false** | `true` raises `NewSessionConnected` synchronously on accept, guaranteeing "connected before first request". The handler then blocks accept |
| `AcceptLoopCount` | 1 | Concurrent accept loops. Raise it to absorb a reconnect storm (max 64) |
| `UseZeroByteReceive` | false | Idle sessions wait on a zero-byte receive instead of holding a buffer — saves memory when most sessions are quiet |

## Logging

```csharp
// Development
new ConsoleLogFactory()

// Production — Serilog, NLog, ZLogger and log4net all attach through this one bridge
using Microsoft.Extensions.Logging;
ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());
new MicrosoftLoggingLogFactory(loggerFactory)
```

`ILog` gives you `Debug`/`Info`/`Warn`/`Error`/`Fatal` plus guard properties like
`IsDebugEnabled`. To attach session identity, don't bake the session ID into the message string —
pass `session.SessionLogContext`.

```csharp
session.Logger.Log(LogEventLevel.Info, session.SessionLogContext, "login ok");
```

## Observability

Everything is published through a single `Meter("SuperSocketLite")`. Attach any
OpenTelemetry-compatible collector and it shows up.

- Counters: `total-requests`, `total-bytes-received`, `total-bytes-sent`, `sessions-rejected`,
  `send-queue-full`, `send-errors`, `active-connections` (an `UpDownCounter`)
- Histogram: `request-duration` (time spent inside your handler)
- Gauges: `session-count`, send-queue depth, `SocketAsyncEventArgs` pool usage

The gauges are `ObservableGauge`s, so nothing is computed unless a collector is listening.
