# Migration Guide: 0.90 → 0.91 (Receive Filters · Setup)

0.91 removed the `byte[]`-based receive filter path and unified everything on
`ReadOnlySequence<byte>`. **If you have a custom `ReceiveFilter`, its signature has to change.**

## 1. `FixedHeaderReceiveFilter<T>`

If you were using `FixedHeaderSequenceReceiveFilter<T>`, only the **name** changes.

```csharp
// before
public class ReceiveFilter : FixedHeaderSequenceReceiveFilter<MyRequestInfo>
// after
public class ReceiveFilter : FixedHeaderReceiveFilter<MyRequestInfo>
```

If you were using the old `byte[]`-based `FixedHeaderReceiveFilter<T>`, move two methods over.

```csharp
// before
protected override int GetBodyLengthFromHeader(byte[] header, int offset, int length)
{
    return BitConverter.ToInt16(header, offset) - HeaderSize;
}

protected override MyRequestInfo ResolveRequestInfo(
    ArraySegment<byte> header, byte[] buffer, int offset, int length)
{
    return new MyRequestInfo(
        BitConverter.ToInt16(header.Array, 0),
        buffer.CloneRange(offset, length));
}

// after
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buf = stackalloc byte[HeaderSize];
    header.CopyTo(buf);                                   // safe even across segment boundaries
    return BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(0, 2)) - HeaderSize;
}

protected override MyRequestInfo ResolveRequestInfo(
    ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
{
    Span<byte> buf = stackalloc byte[HeaderSize];
    header.CopyTo(buf);

    return new MyRequestInfo(
        BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(0, 2)),
        body.ToArray());
}
```

- The `offset` / `length` / `toBeCopied` arguments are gone entirely. The library slices the
  request boundary for you.
- `header` / `body` may span multiple pipe segments. Don't read `header.First.Span` directly —
  use `CopyTo(Span)` or `ToArray()`.
- When there is no body, `body` is an empty sequence (never null).
- If you need the raw header+body bytes concatenated (e.g. for MemoryPack), copy both sequences
  into one array yourself.

## 2. `FixedSizeReceiveFilter<T>`

```csharp
// before
protected override MyRequestInfo ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied)
// after
protected override MyRequestInfo ProcessMatchedRequest(ReadOnlySequence<byte> buffer)
```

## 3. Implementing `IReceiveFilter<T>` directly

```csharp
// before
TRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest);
int LeftBufferSize { get; }

// after
TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);
```

| Result | consumed | examined |
|---|---|---|
| One complete request | end of the request | same as `consumed` |
| Not enough data yet | `buffer.Start` | `buffer.End` |

`LeftBufferSize` is gone. `MaxRequestLength` is now checked by the library itself, against the
unconsumed length.

## 4. The `Setup` signature

Two arguments that nothing used (`socketServerFactory`, `connectionFilters`) were dropped. If you
were already calling `Setup` with named arguments like `logFactory:`, there is nothing to change.

```csharp
Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());   // still works
```

If a derived class overrode `protected override bool Setup(IRootConfig, IServerConfig)`, the hook
was renamed to **`OnSetup`**.

## 5. Removed features

| Removed | Replacement |
|---|---|
| `CollectSend` / `GetCollectSendData` / `CommitCollectSend`, `CollectSendIntervalMillSec` | None. Batch on the application side and call `SendCopied` once if you need this. |
| `RawDataReceived` / `IRawDataProcessor<T>` | None. Handle it in a receive filter. |
| `IConnectionFilter` | None. Validate in `OnNewSessionConnected` and call `Close` if rejected. |
| Injecting `ISocketServerFactory` | None. The library picks an implementation based on `SocketMode`. |
| `AppSession.Items` / `PrevCommand` / `CurrentCommand`, `LogCommand` | None. Add fields to your own session subclass. |
| The string-command protocol stack (`StringRequestInfo`, `TerminatorReceiveFilter`, `CountSpliterReceiveFilter`, `BeginEndMarkReceiveFilter`, the non-generic `AppServer`/`AppSession`) | None. Use `AppServer<TSession, TRequestInfo>` with a binary filter. |
