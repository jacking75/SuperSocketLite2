# Analyzer rules (SSL001–SSL007)

**[🇰🇷 한국어 (Korean)](analyzers_kr.md)**

The `SuperSocketLite2` NuGet package ships Roslyn analyzers.
**They turn on the moment you reference the package** — nothing to install, nothing to configure.

```bash
dotnet add package SuperSocketLite2   # the analyzers come with it
```

They exist because one build warning beats a hundred lines of documentation. Every rule here
targets an item from [cautions.md](cautions.md) that **compiles fine and only breaks under load.**

## The rules

| ID | What it catches | What to do instead |
|---|---|---|
| **SSL001** | A `RequestInfo` or its `Body` stored in a field or property | Deserialize inside the handler, or copy through `ArrayPool` |
| **SSL002** | A `RequestInfo` captured in a lambda or local function | Capture only the values you need |
| **SSL003** | An `ArrayPool` rental passed to `Send` / `TrySend` | `SendCopied` / `TrySendCopied` |
| **SSL004** | Reading a `ReadOnlySequence` through `First.Span` / `FirstSpan` | `CopyTo(Span)` or `SequenceReader` |
| **SSL005** | An `async` method that takes a `RequestInfo` | Keep handlers synchronous; copy values out for async work |
| **SSL006** | An ignored `Setup()` / `Start()` return value | `if (!server.Setup(...)) { ... }` |
| **SSL007** | `GetAllSessions()` / `GetSessions()` used without a null check | Assign to a local and check for `null` |

All of them default to **Warning**. The reasoning behind each is in [cautions.md](cautions.md).

## What they deliberately don't flag

Avoiding false positives was the priority. These pass on purpose:

- `_lastPacketId = request.PacketId;` — value-typed members are copied, so storing them is safe
- `if (header.IsSingleSegment) { ... header.First.Span ... }` — a guarded fast path
- `PacketWriter.Send(session, id, request.Body)` — passing the body as an argument is fine, as long
  as the callee consumes it before the handler returns

The converse is also true: some violations are beyond local analysis. Handing the body to another
object's method, where that object reads it later, cannot be seen from the call site.
**The analyzers are a safety net, not a verifier.** Use them alongside the review checklist in
[cautions.md](cautions.md).

## Turning a rule off — or up

Adjust project-wide through `.editorconfig`.

```ini
[*.cs]
# Promote to errors — on a game server, SSL001 and SSL003 are worth failing the build for
dotnet_diagnostic.SSL001.severity = error
dotnet_diagnostic.SSL003.severity = error

# Disable a rule
dotnet_diagnostic.SSL004.severity = none
```

To except a single line, use `#pragma` **and leave a comment explaining why it is safe.**

```csharp
#pragma warning disable SSL004 // this filter reads 2 header bytes and the caller guarantees IsSingleSegment
var length = BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);
#pragma warning restore SSL004
```

## Source and maintenance

- Implementation: `Analyzers/SuperSocketLite.Analyzers/`
- Rule inventory: `Analyzers/SuperSocketLite.Analyzers/AnalyzerReleases.Unshipped.md`
- Packaging: the `IncludeAnalyzerInPackage` target in `SuperSocketLite/SuperSocketLite.csproj`

Adding a rule means touching `Descriptors.cs`, the analyzer class,
`AnalyzerReleases.Unshipped.md`, and both language versions of this document.
