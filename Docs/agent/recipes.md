# Recipes

**[🇰🇷 한국어 (Korean)](recipes_kr.md)**

Written to be copied and adapted. All of it respects the rules in [cautions.md](cautions.md).

| # | Recipe |
|---|---|
| 1 | [Minimal TCP server](#1-minimal-tcp-server) |
| 2 | [Receive filter — length prefix](#2-receive-filter--length-prefix-the-common-case) |
| 3 | [Receive filter — fixed size](#3-receive-filter--fixed-size) |
| 4 | [Receive filter — hand-written](#4-receive-filter--hand-written) |
| 5 | [Packet ID → handler dispatch](#5-packet-id--handler-dispatch) |
| 6 | [Broadcasting](#6-broadcasting) |
| 7 | [Generic Host + a production logger](#7-generic-host--a-production-logger) |
| 8 | [MemoryPack serialization](#8-memorypack-serialization) |
| 9 | [Handing packets to a logic thread](#9-handing-packets-to-a-logic-thread) |
| 10 | [UDP server](#10-udp-server) |
| 11 | [Graceful shutdown](#11-graceful-shutdown) |

---

## 1. Minimal TCP server

Three files. The protocol is `[2-byte total length][2-byte packet ID][body]`.

**`.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Run servers with the server GC -->
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SuperSocketLite2" Version="0.22.1" />
  </ItemGroup>
</Project>
```

**`Protocol.cs`**

```csharp
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace MyGameServer;

public sealed class PacketRequestInfo : IRequestInfo
{
    public const int HeaderSize = 4;   // [2] totalSize + [2] packetId

    public string Key => string.Empty;

    public short TotalSize { get; private set; }
    public short PacketId { get; private set; }

    /// <summary>The body, header excluded. Invalid once the handler returns.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(short totalSize, short packetId, ReadOnlySequence<byte> body)
    {
        TotalSize = totalSize;
        PacketId = packetId;
        Body = body;
    }
}

public sealed class PacketReceiveFilter : FixedHeaderReceiveFilter<PacketRequestInfo>
{
    // One filter per session, and the next packet is only parsed after the previous handler has
    // returned — so reusing a single instance is safe, and receiving costs zero allocations.
    private readonly PacketRequestInfo _reusable = new();

    public PacketReceiveFilter() : base(PacketRequestInfo.HeaderSize) { }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);                                   // never .First.Span — it can be split
        return BinaryPrimitives.ReadInt16LittleEndian(buffer) - PacketRequestInfo.HeaderSize;
    }

    protected override PacketRequestInfo ResolveRequestInfo(
        ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);

        _reusable.Set(
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2)),
            body);

        return _reusable;
    }
}
```

**`MainServer.cs`**

```csharp
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;

namespace MyGameServer;

public sealed class NetworkSession : AppSession<NetworkSession, PacketRequestInfo> { }

public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>
{
    public MainServer()
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    private void OnConnected(NetworkSession session)
    {
        Logger.Info($"connected: {session.SessionID}");
    }

    private void OnClosed(NetworkSession session, CloseReason reason)
    {
        Logger.Info($"closed: {session.SessionID}, {reason}");
    }

    private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
    {
        // Echo. Body is invalid once this method returns.
        PacketWriter.Send(session, request.PacketId, request.Body);
    }
}
```

**`Program.cs`**

```csharp
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;

namespace MyGameServer;

internal static class Program
{
    private static async Task Main()
    {
        var config = new ServerConfig
        {
            Ip = "Any",
            Port = 32452,
            Mode = SocketMode.Tcp,
            Name = "MyGameServer",
            MaxConnectionNumber = 2000,
            MaxRequestLength = 8192,     // the 1024 default is usually too small for game packets
            NoDelay = true,              // for real-time traffic
            SyncSessionConnectedEvent = true,   // guarantees "connected before first request"
        };

        var server = new MainServer();

        if (!server.Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory()))
        {
            Console.Error.WriteLine("server setup failed");
            return;
        }

        if (!server.Start())
        {
            Console.Error.WriteLine("server start failed");
            return;
        }

        Console.WriteLine($"listening on {config.Port}. press Ctrl+C to stop.");

        var stopping = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.TrySetResult(); };
        await stopping.Task;

        await server.StopAsync(TimeSpan.FromSeconds(5));   // let queued responses flush
    }
}
```

**A response writer** that doesn't allocate an array per response.

```csharp
using System.Buffers;
using System.Buffers.Binary;

namespace MyGameServer;

internal static class PacketWriter
{
    private const int StackBufferSize = 512;

    public static void Send(NetworkSession session, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            Write(packet, packetId, body);
            session.SendCopied(packet.Slice(0, totalSize));   // stack buffer → must be SendCopied
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            Write(rented, packetId, body);
            session.SendCopied(rented.AsSpan(0, totalSize));  // rented buffer → must be SendCopied
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void Write(Span<byte> destination, short packetId, ReadOnlySequence<byte> body)
    {
        var totalSize = PacketRequestInfo.HeaderSize + checked((int)body.Length);

        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), packetId);
        body.CopyTo(destination.Slice(PacketRequestInfo.HeaderSize));
    }
}
```

> **Never pass a `stackalloc` or `ArrayPool` buffer to `Send`.** It's zero-copy, so the buffer can
> be reclaimed before the data goes out. These cases are always `SendCopied`.

---

## 2. Receive filter — length prefix (the common case)

Recipe 1's `PacketReceiveFilter` is the answer. Here are the variations.

**When the length field holds only the body length** (no header size to subtract)

```csharp
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buffer = stackalloc byte[HeaderSize];
    header.CopyTo(buffer);
    return BinaryPrimitives.ReadInt32LittleEndian(buffer);   // already the body length
}
```

**For a big-endian protocol**

```csharp
return BinaryPrimitives.ReadInt32BigEndian(buffer);
```

**To impose your own length cap** — the default implementation honours `MaxRequestLength`.
Override to tighten it.

```csharp
protected override bool ValidateBodyLength(int bodyLength)
{
    if (bodyLength < 0 || bodyLength > 4096)
    {
        return false;    // sets FilterState.Error and the session is closed
    }

    return base.ValidateBodyLength(bodyLength);
}
```

---

## 3. Receive filter — fixed size

When every packet is the same size, there is exactly one method to implement.

```csharp
using System.Buffers;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

public sealed class TelemetryFilter : FixedSizeReceiveFilter<TelemetryRequestInfo>
{
    private readonly TelemetryRequestInfo _reusable = new();

    public TelemetryFilter() : base(32) { }   // every packet is 32 bytes

    protected override TelemetryRequestInfo ProcessMatchedRequest(ReadOnlySequence<byte> buffer)
    {
        Span<byte> packet = stackalloc byte[32];
        buffer.CopyTo(packet);                 // may span segments
        _reusable.Parse(packet);
        return _reusable;
    }
}
```

---

## 4. Receive filter — hand-written

Only for protocols the two base classes can't express, such as delimiter-based framing (`\r\n`).

```csharp
using System.Buffers;
using SuperSocketLite.SocketBase.Protocol;

public sealed class LineFilter : IReceiveFilter<LineRequestInfo>
{
    private readonly LineRequestInfo _reusable = new();

    public IReceiveFilter<LineRequestInfo>? NextReceiveFilter => null;
    public FilterState State { get; private set; }

    public LineRequestInfo? Filter(ReadOnlySequence<byte> buffer,
                                   out SequencePosition consumed, out SequencePosition examined)
    {
        // Not a full line yet? Leave consumed where it is. The data stays in the pipe and the rest
        // arrives with the next read — never keep a carry buffer of your own.
        consumed = buffer.Start;
        examined = buffer.End;

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n'))
        {
            return null;
        }

        consumed = reader.Position;
        examined = consumed;

        _reusable.Set(line);
        return _reusable;
    }

    public void Reset() => State = FilterState.Normal;
}
```

The contract for `Filter`:

- A complete request is available → return it and move `consumed` to the end of that request
- Not yet → return `null`, leave `consumed` at `buffer.Start` and set `examined` to `buffer.End`
- The protocol is broken → set `State = FilterState.Error` and the session is closed

---

## 5. Packet ID → handler dispatch

Once you have more than a few packets, replace the `switch` with a map.

```csharp
public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>
{
    private readonly Dictionary<short, Action<NetworkSession, PacketRequestInfo>> _handlers = new();

    public MainServer()
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        NewRequestReceived += OnRequestReceived;
        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        _handlers[(short)PacketId.ReqLogin] = HandleLogin;
        _handlers[(short)PacketId.ReqChat] = HandleChat;
    }

    private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
    {
        if (!_handlers.TryGetValue(request.PacketId, out var handler))
        {
            Logger.Warn($"unknown packet id: {request.PacketId}, session: {session.SessionID}");
            return;
        }

        try
        {
            handler(session, request);
        }
        catch (Exception ex)
        {
            // Stop a handler exception from taking down the receive loop.
            Logger.Error($"handler failed. id: {request.PacketId}", ex);
            session.Close(CloseReason.ApplicationError);
        }
    }
}
```

**Handlers must complete synchronously.** An `async void` handler — or an `async` call you don't
await — returns before the work is done, and the code that follows reads `request.Body` after it
has been invalidated. If you need asynchronous work, copy the values out first, as in recipe 9.

---

## 6. Broadcasting

```csharp
private void Broadcast(ReadOnlySpan<byte> packet, string? exceptSessionId = null)
{
    var sessions = GetAllSessions();

    if (sessions is null)      // null before the server is up, and while it is shutting down
    {
        return;
    }

    foreach (var session in sessions)
    {
        if (!session.Connected || session.SessionID == exceptSessionId)
        {
            continue;
        }

        // One blocked session must not stall the rest of the broadcast.
        session.TrySendCopied(packet);
    }
}
```

- Broadcast with **`TrySendCopied`**, not `Send`. Otherwise a single slow client stalls the whole
  loop for `SendTimeOut`.
- For per-room broadcasts, have the room hold its own session list rather than walking
  `GetAllSessions()` every time. The full scan becomes a bottleneck as session counts grow.

---

## 7. Generic Host + a production logger

`MicrosoftLoggingLogFactory` bridges Serilog, NLog, ZLogger and log4net.
**You do not need to write a per-library adapter class.**

```csharp
var host = new HostBuilder()
    .ConfigureAppConfiguration((ctx, config) =>
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true))
    .ConfigureLogging((ctx, logging) =>
    {
        logging.ClearProviders();
        logging.AddZLoggerConsole();      // or AddSerilog(), AddNLog(), ...
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<ServerOption>(ctx.Configuration.GetSection("ServerOption"));
        services.AddHostedService<MainServer>();

        // Hand the host's ILoggerFactory to SuperSocketLite as an ILogFactory.
        services.AddSingleton<SuperSocketLite.SocketBase.Logging.ILogFactory>(
            sp => new SuperSocketLite.SocketBase.Logging.MicrosoftLoggingLogFactory(
                sp.GetRequiredService<ILoggerFactory>()));
    })
    .Build();

await host.RunAsync();
```

The server also implements `IHostedService`.

```csharp
public sealed class MainServer : AppServer<NetworkSession, PacketRequestInfo>, IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ServerOption _option;
    private readonly SuperSocketLite.SocketBase.Logging.ILogFactory _logFactory;

    public MainServer(IHostApplicationLifetime lifetime,
                      IOptions<ServerOption> option,
                      SuperSocketLite.SocketBase.Logging.ILogFactory logFactory)
        : base(new DefaultReceiveFilterFactory<PacketReceiveFilter, PacketRequestInfo>())
    {
        _lifetime = lifetime;
        _option = option.Value;
        _logFactory = logFactory;

        NewSessionConnected += OnConnected;
        SessionClosed += OnClosed;
        NewRequestReceived += OnRequestReceived;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = new ServerConfig { /* filled from _option */ };

        if (!Setup(new RootConfig(), config, logFactory: _logFactory) || !Start())
        {
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => base.StopAsync(TimeSpan.FromSeconds(5));
}
```

---

## 8. MemoryPack serialization

This is "Safe (A)" from [cautions.md](cautions.md) §4 — deserialize inside the handler and keep
only the value.

```csharp
using MemoryPack;

[MemoryPackable]
public partial class ReqLogin
{
    public string UserId { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}

private void HandleLogin(NetworkSession session, PacketRequestInfo request)
{
    // Body is a ReadOnlySequence<byte>, which MemoryPack takes directly — no intermediate array.
    var req = MemoryPackSerializer.Deserialize<ReqLogin>(request.Body);

    if (req is null)
    {
        session.Close(CloseReason.ProtocolError);
        return;
    }

    // req is a freshly created object. It is safe to pass outside the handler.
    _loginQueue.Enqueue(new LoginWork(session.SessionID, req));
}
```

Responses need a header in front, so write into a pooled buffer.

```csharp
private static void SendPacket<T>(NetworkSession session, short packetId, T payload)
{
    var writer = new ArrayBufferWriter<byte>();   // not IDisposable — no `using`
    MemoryPackSerializer.Serialize(writer, payload);

    var bodyLength = writer.WrittenCount;
    var totalSize = PacketRequestInfo.HeaderSize + bodyLength;
    var rented = ArrayPool<byte>.Shared.Rent(totalSize);

    try
    {
        BinaryPrimitives.WriteInt16LittleEndian(rented.AsSpan(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(rented.AsSpan(2, 2), packetId);
        writer.WrittenSpan.CopyTo(rented.AsSpan(PacketRequestInfo.HeaderSize));

        session.SendCopied(rented.AsSpan(0, totalSize));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(rented);
    }
}
```

A complete working example lives in `Template/GameServer_MemoryPack`.

---

## 9. Handing packets to a logic thread

Game servers usually separate the network thread from the logic thread.
When you do, **you must copy the bytes.** Neither the `RequestInfo` nor its `Body` survives the
handler.

```csharp
public readonly record struct PacketWork(string SessionId, short PacketId, byte[] Buffer, int Length);

private void OnRequestReceived(NetworkSession session, PacketRequestInfo request)
{
    var length = checked((int)request.Body.Length);
    var rented = ArrayPool<byte>.Shared.Rent(length);

    request.Body.CopyTo(rented);   // copy here — Body is invalid once this returns

    if (!_logicQueue.Writer.TryWrite(new PacketWork(session.SessionID, request.PacketId, rented, length)))
    {
        ArrayPool<byte>.Shared.Return(rented);   // return it immediately if the queue is full
        Logger.Warn($"logic queue full. dropped packet {request.PacketId}");
    }
}

// Logic thread
private async Task LogicLoopAsync(CancellationToken token)
{
    await foreach (var work in _logicQueue.Reader.ReadAllAsync(token))
    {
        try
        {
            Process(work);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(work.Buffer);   // return in exactly one place
        }
    }
}
```

Pair every `ArrayPool` rental with exactly one return. Forgetting the return on the
queue-rejected path is the classic mistake. `Tutorials/PvPGameServer` is a real example.

---

## 10. UDP server

Change `Mode`; everything else matches TCP.

```csharp
var config = new ServerConfig
{
    Ip = "Any",
    Port = 555,
    Mode = SocketMode.Udp,          // the only difference
    MaxConnectionNumber = 1000,
    Name = "UdpServer",
};
```

There are two ways to map sessions.

1. **Default** — one session per remote endpoint. Nothing extra to do.
2. **Embedded session ID** — if your request type derives from `UdpRequestInfo`, the session is
   located by a session ID carried in the payload, so a client keeps its logical session across a
   NAT rebind.

```csharp
public sealed class MyUdpRequestInfo : UdpRequestInfo
{
    public MyUdpRequestInfo(string key, string sessionId) : base(key, sessionId) { }
}
```

With option 2, the filter that parses the session ID is **shared per receive thread.**
It must hold no state between datagrams and must not capture the remote endpoint from
`CreateFilter`. The full example is `Tutorials/SimpleUDPServer`.

---

## 11. Graceful shutdown

```csharp
// Works, but drops whatever is still queued
server.Stop();

// Preferred — stop accepting, flush what's already queued, then close
await server.StopAsync(TimeSpan.FromSeconds(5));
```

For a game server, send a shutdown notice, wait briefly, then call `StopAsync`.

```csharp
Broadcast(shutdownNotice);
await Task.Delay(TimeSpan.FromSeconds(1));
await server.StopAsync(TimeSpan.FromSeconds(5));
```
