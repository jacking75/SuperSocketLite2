# SuperSocketLite2 — docs for coding agents

**[🇰🇷 한국어 (Korean)](README_kr.md)**

These are the docs an AI coding agent reads when **building a server with SuperSocketLite2**.
The human-facing documents live in `Docs/*.html`; this directory carries the same material as
Markdown so an agent can read it without burning its context window.

> **Do not open `Docs/*.html`.** The standalone HTML documents such as
> `Library_Architecture.html` are 650KB each — a single one will fill an entire context window.
> Everything you need is here.

## Where to start

| You want to | Read |
|---|---|
| Build a server from scratch | [recipes.md](recipes.md) § 1, Minimal TCP server |
| Look up a type name, namespace, or signature | [api-cheatsheet.md](api-cheatsheet.md) |
| Define a packet protocol | [recipes.md](recipes.md) § 2, Receive filters |
| Know what will bite you before you write code | **[cautions.md](cautions.md) — required** |
| Prove the server you built actually runs | [verify.md](verify.md) |
| Understand an `SSL0xx` build warning | [analyzers.md](analyzers.md) |

## The short version

Three mistakes account for most of the trouble agents get into with this library.
Check them before you write code.

1. **Once your handler returns, the `RequestInfo` and its `Body` are dead.**
   Do not store them in a field, capture them in a lambda, or hand them to another thread.
   If you need the data to outlive the handler, deserialize or copy it inside the handler.
2. **`Send(byte[], ...)` is zero-copy.** Modify that array before the send completes and corrupt
   bytes go out on the wire. If you need your buffer back immediately, use `SendCopied`.
3. **`header` and `body` are `ReadOnlySequence<byte>` and may span several segments.**
   Never read through `header.First.Span`; use `CopyTo(Span)`.

All three **compile cleanly and work fine under light load.** They only break once the receive
pipe's buffers wrap around, which is why they have to be caught in review rather than in testing.
The full list is in [cautions.md](cautions.md).

All three are also caught at build time by the analyzers bundled in the `SuperSocketLite2` package
(`SSL001`–`SSL007`, see [analyzers.md](analyzers.md)). **Fix the warnings; don't suppress them.**

## How these files relate to the HTML docs

| This file | Human-facing counterpart |
|---|---|
| [cautions.md](cautions.md) | `Docs/Cautions.html` / `Docs/Cautions_kr.html`, and ch. 7 of `Docs/Getting_Started*.html` |
| [api-cheatsheet.md](api-cheatsheet.md) | `Docs/Getting_Started*.html`, `README.md` |
| [recipes.md](recipes.md) | `Tutorials/`, `Template/` |

**When the code changes and a caveat changes with it, six files move together** — `cautions.md`,
`cautions_kr.md`, `Cautions.html`, `Cautions_kr.html`, and ch. 7 of both `Getting_Started*.html`.
The skill at `.claude/skills/supersocketlite2/` only links here, so it needs no separate edit.
