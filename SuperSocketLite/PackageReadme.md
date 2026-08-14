# SuperSocketLite2

Async TCP/UDP socket server library for **.NET 10 (Windows/Linux)**, built for the kind of workload
that doesn't forgive sloppy I/O: real-time multiplayer game servers. A session-based, event-driven
framework on top of `SocketAsyncEventArgs` (IOCP) and `System.IO.Pipelines`, so you can focus on your
game protocol instead of reinventing connection management, buffer pooling, and backpressure handling.

This is a separate, from-scratch NuGet package — a different package ID from the older
[`SuperSocketLite`](https://www.nuget.org/packages/SuperSocketLite) package (still the pre-rewrite,
.NET 9 line). Installing this one does not affect existing `SuperSocketLite` users.

## Install

```bash
dotnet add package SuperSocketLite2
```

## A minimal echo server

```csharp
public sealed class MyRequestInfo : IRequestInfo
{
    public string Key => string.Empty;
    public ReadOnlySequence<byte> Body { get; private set; }
    public void Set(ReadOnlySequence<byte> body) => Body = body;
}

public sealed class MyReceiveFilter : FixedHeaderReceiveFilter<MyRequestInfo>
{
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
var server = new MyServer();
server.NewRequestReceived += (session, request) =>
{
    if (request.Body.IsSingleSegment)
        session.SendCopied(request.Body.FirstSpan);
    else
        session.SendCopied(request.Body.ToArray());
};

server.Setup(new RootConfig(), new ServerConfig { Ip = "Any", Port = 2012, MaxConnectionNumber = 1000 },
    logFactory: new ConsoleLogFactory());
server.Start();
```

## Documentation

- [README (full guide, quick start, configuration, observability)](https://github.com/jacking75/SuperSocketLite2/blob/main/README.md)
- [Getting Started — build, usage, must-knows](https://github.com/jacking75/SuperSocketLite2/blob/main/Docs/Getting_Started.html)
- [Architecture guide and diagrams](https://github.com/jacking75/SuperSocketLite2/blob/main/Docs/index.html)
- [Tutorials / example servers](https://github.com/jacking75/SuperSocketLite2/tree/main/Tutorials)

## License

MIT
