# Caveats — the things that compile fine and break under load

**[🇰🇷 한국어 (Korean)](cautions_kr.md)**

**Read this before you write code, and again when you review it.**
Every item below compiles, and most of them work in a light test. They only fail once real
concurrency arrives — so "it ran, therefore it's correct" is not a valid conclusion here.

The human-facing versions are `Docs/Cautions.html` and `Docs/Cautions_kr.html`, and the same eight
items appear in ch. 7 of `Docs/Getting_Started*.html`. **Edit them together.**

---

## 1. Thread safety

`NewSessionConnected` and `NewRequestReceived` can fire **concurrently on different threads, even
for the same session.** If a client sends a packet immediately after connecting, the request handler
can run while the connect handler is still going.

```csharp
// Dangerous — assumes the connect handler has finished its setup
private void OnConnected(MySession session)
{
    session.Player = new Player();          // not assigned yet...
}

private void OnRequest(MySession session, MyRequestInfo req)
{
    session.Player.Handle(req);             // ...NullReferenceException here
}
```

There are two ways out.

```csharp
// Option A — make the ordering structural (recommended)
var config = new ServerConfig
{
    // Raises NewSessionConnected synchronously on the accept path. Your handler blocks the
    // accept loop while it runs, so keep it light.
    SyncSessionConnectedEvent = true,
};

// Option B — let the request handler defend itself
private void OnRequest(MySession session, MyRequestInfo req)
{
    var player = session.Player;
    if (player is null)
    {
        return;                             // or close the session
    }

    player.Handle(req);
}
```

---

## 2. Send buffer lifetime (zero-copy)

`Send(byte[], int, int)`, `Send(ArraySegment<byte>)`, `Send(IList<ArraySegment<byte>>)` and
`SendAsync(ReadOnlyMemory<byte>)` (when the memory is array-backed) queue **a reference to your
array.** Touch it before the send completes and the wrong bytes go out.

```csharp
// Dangerous — overwrites a buffer that may still be in flight
session.Send(buffer, 0, len);
buffer[0] = 0;

// Safe — the library copies into its own pooled buffer
session.SendCopied(buffer.AsSpan(0, len));
buffer[0] = 0;                              // fine
```

- For `Send(IList<...>)` the **list itself** is copied on enqueue, so you can reuse the list right
  away. The arrays inside it are what stay shared.
- Empty payloads behave differently. `SendCopied` / `TrySendCopied` queue nothing and report
  success when the data is empty. `Send(buffer, 0, 0)` actually queues a zero-length segment and
  transmits it — over UDP that means an empty datagram goes out. This only matters if empty packets
  carry meaning in your protocol and you are migrating a call from `Send` to `SendCopied`.

**Rule of thumb:** when in doubt, use `SendCopied`. Code that hands a `stackalloc` buffer or an
`ArrayPool` rental to `Send` is almost always a bug.

---

## 3. Receive filters

A filter reads a `ReadOnlySequence<byte>` straight out of the pipe.

**If the request isn't complete yet, leave `consumed` where it is.** The data stays in the pipe and
the rest arrives with the next read. A filter that keeps its own carry buffer only adds copying.

```csharp
// Dangerous — assumes one segment
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    return BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);   // breaks on a split header
}

// Safe — gather into a stack buffer first
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buffer = stackalloc byte[HeaderSize];
    header.CopyTo(buffer);
    return BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2));
}
```

Both `header` and `body` can straddle segments. Don't reach through `.First.Span`; use
`CopyTo(Span)` or `ToArray()`. Headers are small, so `stackalloc` is usually the right answer.

With UDP plus `UdpRequestInfo`, the filter that parses the session ID is **shared per receive
thread** and reused after `Reset()`. It must hold no state between datagrams, and it must not
capture the remote endpoint passed to `CreateFilter`.

---

## 4. RequestInfo and body lifetime — the one people get wrong

The library calls `NewRequestReceived` **synchronously** and only advances the receive pipe after
your handler has returned (`AppServerBase.ExecuteCommand` → the `AdvanceTo` in `ProcessPipeAsync`).
UDP works the same way: `UdpReceivePacket.Dispose()` returns the receive buffer to the pool after
the handler returns.

That guarantee is what lets an application reach **zero allocations per packet**: the filter reuses
one request instance and hands the body straight through as a `ReadOnlySequence<byte>`.
`Tutorials/EchoServer` is built that way.

It comes with a contract.

> **Once the handler returns, that `RequestInfo` and its body are no longer valid.**
> Don't store them in a field, capture them in a lambda, or push them onto another thread's queue.

```csharp
// Dangerous — these are all the same mistake
private void OnRequest(MySession session, MyRequestInfo req)
{
    _lastRequest = req;                        // stored in a field — the next packet overwrites it
    _queue.Enqueue(req);                       // handed to another thread — stale by the time it looks
    Task.Run(() => Handle(req.Body));          // captured in a lambda — same problem
    _ = HandleAsync(session, req);             // un-awaited async — reads the body after the return
}

// Safe (A) — deserialize inside the handler and keep only the value
private void OnRequest(MySession session, MyRequestInfo req)
{
    var login = MemoryPackSerializer.Deserialize<LoginReq>(req.Body);   // a fresh object
    _queue.Enqueue(login);                                             // this one is fine to pass on
}

// Safe (B) — if the logic thread needs the raw bytes, rent and copy
private void OnRequest(MySession session, MyRequestInfo req)
{
    var length = checked((int)req.Body.Length);
    var rented = ArrayPool<byte>.Shared.Rent(length);
    req.Body.CopyTo(rented);

    // Return it in exactly one place after processing: ArrayPool<byte>.Shared.Return(rented).
    _queue.Enqueue(new PacketWork(session.SessionID, rented, length));
}
```

If your architecture hands packets to a logic thread, the zero-copy approach is not available to
you. Option B is the shape you want, and `Tutorials/PvPGameServer` is a working example. See
`Docs/GC_Copy_Minimization.md` for the details.

**Break this and the code compiles and usually works under light load.** The data only corrupts
once traffic is heavy enough for the pipe buffers to be recycled, which makes it very hard to find.

---

## 5. Time values are UTC

`AppSession.StartTime`, `AppSession.LastActiveTime` and `AppServerBase.StartedTime` are all UTC.

```csharp
// Dangerous — compares against local time
if (DateTime.Now - session.StartTime > TimeSpan.FromMinutes(5))   // off by your UTC offset

// Safe
if (DateTime.UtcNow - session.StartTime > TimeSpan.FromMinutes(5))

// Convert only when displaying
logger.Info($"connected at {session.StartTime.ToLocalTime()}");
```

`LastActiveTime` is derived from a monotonic tick stamp, so it carries a few milliseconds of error.
Don't use it for precise comparisons.

---

## 6. Timeout handling order

When a `TimeoutException` leads you to close a session, **this order is mandatory.**

```csharp
try
{
    session.SendCopied(packet);
}
catch (TimeoutException)
{
    session.SendEndWhenSendingTimeOut();   // this first
    session.Close();                       // then this
}
```

Calling `Close()` without `SendEndWhenSendingTimeOut()` leaves the socket uncleaned.

---

## 7. Exceeding the connection limit

When `MaxConnectionNumber` is exceeded the library **drops the connection immediately**, and
**`NewSessionConnected` is not raised.**

Code that counts connections through session events will never see the rejected ones.
Use the `sessions-rejected` metric for that.

---

## 8. UDP mode

- A UDP session's internal `_client` (`Socket`) **can be null** — the socket instance is shared.
- `Close()` branches internally between the UDP and TCP paths. Be careful when changing UDP code.
- As noted in §3, the session-ID filter is shared per receive thread.

---

## Review checklist

Work through this when reviewing code that uses the library.

- [ ] Is there any path where a `RequestInfo` or `Body` leaves the handler (field, capture, queue, un-awaited `async`)?
- [ ] Is a `stackalloc` or `ArrayPool` buffer being passed to `Send`? It should be `SendCopied`.
- [ ] Does a filter read a header through `.First.Span`? It should be `CopyTo(Span)`.
- [ ] Does the request handler read connect-handler state without guarding it?
- [ ] Are `GetAllSessions()` / `GetSessions()` results null-checked?
- [ ] In `TimeoutException` handling, does `SendEndWhenSendingTimeOut()` come before `Close()`?
- [ ] Do time comparisons use `DateTime.UtcNow` rather than `DateTime.Now`?

Most of these are also enforced at build time — see [analyzers.md](analyzers.md).
