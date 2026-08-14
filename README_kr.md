# SuperSocketLite2

**[🇺🇸 English README](README.md)**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/language-C%23-239120)](https://learn.microsoft.com/dotnet/csharp/)

**SuperSocketLite2**는 .NET용 고성능 비동기 TCP/UDP 소켓 서버 라이브러리다. 부실한 I/O를
용납하지 않는 워크로드, 즉 실시간 멀티플레이어 게임 서버를 위해 만들었다. `SocketAsyncEventArgs`
(IOCP)와 `System.IO.Pipelines` 위에 세션 기반·이벤트 구동 프레임워크를 얹어서, 연결 관리·버퍼
풀링·백프레셔 처리를 새로 만드는 대신 게임 프로토콜 자체에 집중할 수 있게 한다.

원조 [SuperSocketLite](https://github.com/jacking75/SuperSocketLite)를 밑바닥부터 다시 쓴
버전이다. 그 SuperSocketLite는 [SuperSocket](https://github.com/kerryjiang/SuperSocket) 1.16을
.NET용으로 이식하며 불필요한 기능을 덜어낸 것이었다. SuperSocketLite2는 그 API 중 여전히
타당한 부분은 남기고, 수신 경로·송신 큐·오브젝트 풀처럼 나머지는 `Pipelines`와 최신 .NET을
중심으로 한 설계로 전부 교체했다.

## 왜 SuperSocketLite2인가

- **Zero-copy 수신.** 세션마다 `System.IO.Pipelines.Pipe`를 하나씩 둔다. 수신 필터는
  `ReadOnlySequence<byte>`에서 곧바로 요청을 파싱한다 — 세션마다 두는 캐리 버퍼가 없고,
  요청이 한 번에 통째로 오면 추가 복사도 없으며, 요청이 미완성이면 나머지가 도착할 때까지
  파이프에 그대로 남는다.
- **할당을 피하도록 설계된 송신 경로.** 송신은 세션별 락 없는 bounded `Channel<T>`로 큐잉되고,
  배치로 drain되어, scatter-gather I/O(`SocketAsyncEventArgs.BufferList`)로 소켓에 전달된다 —
  큐에 쌓인 세그먼트 여러 개가 syscall 한 번으로 나간다. 수신·송신 양쪽의
  `SocketAsyncEventArgs` 객체는 연결마다 새로 만들지 않고 풀링해서 재사용한다.
- **복사 방식을 직접 고른다.** `Send`는 zero-copy다(전송이 끝날 때까지 버퍼는 호출자 소유).
  `SendCopied`는 풀 버퍼로 복사해서 호출자가 자기 버퍼를 즉시 재사용할 수 있게 한다.
  `SendAsync`는 큐가 가득 찼을 때 `Send`의 블로킹 재시도 루프 대신 `await`할 수 있는
  `ValueTask<bool>`을 준다. 자신의 핫패스에 맞는 것을 고르면 된다.
- **백프레셔와 우아한 종료는 뒷전이 아니다.** 수신 파이프의 일시정지/재개 임계값은 설정한
  `MaxRequestLength`에 맞춰 조정되므로, 느린 핸들러가 있어도 메모리가 무한정 늘어나지 않는다.
  `StopAsync(drainTimeout)`은 신규 접속을 막고, 이미 큐에 들어간 응답을 다 내보낸 뒤에 세션을
  닫는다.
- **바이너리 우선의 교체 가능한 프로토콜 계층.** `IReceiveFilter<T>`를 한 번만 구현하면 된다 —
  내장된 `FixedHeaderReceiveFilter<T>`와 `FixedSizeReceiveFilter<T>`가 흔한 케이스(길이
  프리픽스 패킷, 고정 크기 패킷)를 커버하고, 파이프라이닝된 요청(한 번의 읽기에 완결된 패킷
  여러 개가 함께 오는 경우)도 알아서 처리된다.
- **핫패스를 건드리지 않고도 관측 가능.** 요청·바이트 카운터, 활성 연결 게이지, 요청 처리
  시간 히스토그램이 `System.Diagnostics.Metrics`(`Meter("SuperSocketLite")`)를 통해 노출되어
  OpenTelemetry 호환 수집기라면 바로 붙일 수 있다. 게이지는 관측형(Observable)이라 아무도
  듣고 있지 않으면 비용이 전혀 들지 않는다.
- **원하는 로깅 라이브러리를 쓴다.** 라이브러리는 자체 `ILog` 추상화 하나에만 의존하고,
  내장 `MicrosoftLoggingLogFactory` 브리지를 함께 제공한다 — Serilog, NLog, ZLogger, log4net
  모두 각자의 `Microsoft.Extensions.Logging` 프로바이더를 통해 바로 동작한다.
- **TCP와 UDP를 같은 프레임워크로.** UDP 세션도 TCP와 동일한 `AppSession` 모델을 쓴다.
  원격 엔드포인트 기준으로 세션을 구분하거나, 데이터그램에 직접 담은 세션 ID로 구분할 수
  있다.
- **최신 .NET, nullable 주석 완비, 레거시 부담 없음.** .NET 10을 타겟으로 하고
  `System.IO.Pipelines`와 `System.Threading.Channels`를 전면에 쓴다. 아무도 쓰지 않던 API
  표면은 그대로 끌고 오지 않았다(무엇을 뺐고 대신 무엇을 하면 되는지는
  `.claude/architecture.md`의 "제거된 기능"에 있다).

## 빠른 시작

### 요구 사항

- .NET 10.0 SDK
- Windows 또는 Linux (비동기 소켓 엔진과 TCP keep-alive 옵션 모두 크로스 플랫폼이다)

### 라이브러리 가져오기

SuperSocketLite2(.NET 10 대상, `Pipelines` 기반 엔진 — 이 문서가 설명하는 그대로)는 별도의
NuGet 패키지 **`SuperSocketLite2`**로 배포된다 — NuGet.org의 기존 `SuperSocketLite` 패키지(여전히
재작성 이전의 .NET 9 라인)와는 다른, 독립된 패키지 ID다. 이 저장소의 변경은 기존 패키지에 영향을
주지 않는다.

```bash
dotnet add package SuperSocketLite2
```

이게 전부다 — 저장소를 로컬에 받을 필요가 없다. [`Tutorials/EchoServer_NuGet`](Tutorials/EchoServer_NuGet)이
NuGet 패키지만으로 완성한 실행 가능한 서버다([`Tutorials/EchoServer`](Tutorials/EchoServer)와 완전히
같고, `ProjectReference` 대신 `PackageReference`를 쓴 것뿐이다).

배포된 버전이 아니라 최신 소스로 빌드하고 싶다면(아직 릴리스 안 된 수정 사항을 쓰거나, 라이브러리
자체를 고칠 때) 프로젝트를 직접 참조한다.

```bash
git clone https://github.com/jacking75/SuperSocketLite2.git
```

```xml
<ItemGroup>
  <ProjectReference Include="..\SuperSocketLite2\SuperSocketLite\SuperSocketLite.csproj" />
</ItemGroup>
```

### 최소 에코 서버

프로토콜은 길이 프리픽스 패킷이다: 리틀엔디언 4바이트 본문 길이 다음에 본문이 온다.

```csharp
// EchoProtocol.cs
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

// 요청 정보. Body가 수신 파이프를 그대로 가리키고 필터가 같은 인스턴스를 돌려주므로,
// 패킷을 받는 데 드는 할당이 하나도 없다.
// 대신 둘 다 핸들러가 리턴하면 무효다. 패킷을 다른 스레드로 넘겨야 한다면
// Docs/GC_Copy_Minimization.md를 보라.
public sealed class MyRequestInfo : IRequestInfo
{
    public string Key => string.Empty;

    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(ReadOnlySequence<byte> body) => Body = body;
}

// 수신 필터: 4바이트 길이 프리픽스를 읽고 본문을 파싱한다.
public sealed class MyReceiveFilter : FixedHeaderReceiveFilter<MyRequestInfo>
{
    // 돌려 써도 되는 이유: 필터는 세션마다 하나이고, 다음 패킷은 이전 핸들러가
    // 리턴한 뒤에야 파싱된다.
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

// 받은 본문을 그대로 돌려보낸다. SendCopied는 라이브러리 자신의 풀 버퍼로 복사하므로,
// 이 핸들러 안에서만 유효한 메모리를 넘겨도 안전하다.
server.NewRequestReceived += (session, request) =>
{
    if (request.Body.IsSingleSegment)
        session.SendCopied(request.Body.FirstSpan);
    else
        session.SendCopied(request.Body.ToArray());   // 드묾: 패킷이 파이프 조각에 걸친 경우
};

if (!server.Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory()))
{
    Console.WriteLine("서버 설정에 실패했다.");
    return;
}

server.Start();
Console.WriteLine("2012 포트에서 대기 중. 아무 키나 누르면 종료...");
Console.ReadKey();
server.Stop();
```

이것으로 완결된, 바로 실행 가능한 TCP 서버다. [`Tutorials/EchoServer`](Tutorials/EchoServer)가
같은 것을 실행 가능한 프로젝트로 담고 있다(라이브러리를 `ProjectReference`로 참조).
[`EchoServer_NuGet`](Tutorials/EchoServer_NuGet)은 똑같은 서버를 `SuperSocketLite2` NuGet
패키지로 참조한 버전이다. [`EchoServerEx`](Tutorials/EchoServerEx)는 옵션 파싱과
NLog를, [`EchoServer_GenericHost`](Tutorials/EchoServer_GenericHost)는 `Generic Host` 서비스로
띄우는 방법을 보여 준다.

## 동작 방식

```
[TCP 클라이언트]
    ↓
TcpAsyncSocketListener        accept 루프 (SocketAsyncEventArgs / IOCP)
    ↓
AsyncSocketServer              SocketAsyncEventArgs 풀 관리, 세션 생성
    ↓
SocketSession                  상태 머신 (InReceiving / InSending / Closed),
    ↓                          세션마다 System.IO.Pipelines.Pipe 하나
AppSession<TSession, TReq>     사용자 세션 타입, Send/SendAsync/SendCopied
    ↓
IReceiveFilter<TRequestInfo>   ReadOnlySequence<byte>를 요청으로 파싱
    ↓
AppServerBase.NewRequestReceived   사용자 핸들러가 여기서 실행된다
```

IOCP 완료 스레드는 파이프 라이터를 전진시키고 다음 수신을 거는 일만 한다 — 애플리케이션
코드는 절대 여기서 돌지 않는다. 세션마다 전용 태스크가 파이프를 읽고 필터를 실행해
`NewRequestReceived`를 호출하므로, 한 연결의 느린 핸들러가 다른 연결의 I/O를 막을 수 없다.

송신도 같은 구조로 나뉜다: `TrySend`/`Send`는 세션별 `Channel`에 쌓이고, single-flight 송신
루프가 그 시점까지 쌓인 것을 한 배치로 묶어 소켓 쓰기 한 번으로 내보낸다. 부분 전송(드물지만
발생 가능)은 처음부터 다시 큐잉하지 않고 남은 바이트만으로 재시도한다.

오브젝트 풀 크기 산정, 수신 파이프의 백프레셔 임계값, 세션 상태 머신, 로깅 추상화까지
전체 내역은 [`.claude/architecture.md`](.claude/architecture.md)에 있다.

## 데이터 보내기

| 메서드 | 복사 방식 | 언제 쓰나 |
|---|---|---|
| `Send(byte[], offset, length)` / `TrySend` | Zero-copy — 라이브러리가 배열 참조를 그대로 들고 있는다 | 전송 전까지 다시 건드리지 않을 버퍼를 이미 갖고 있을 때 |
| `SendCopied(ReadOnlySpan<byte>)` / `TrySendCopied` | 풀 버퍼로 복사 | 호출 직후 자기 버퍼를 바로 재사용해야 할 때 (재사용하는 스크래치 버퍼 등) |
| `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` | 배열 기반 메모리면 zero-copy | `Send`의 블로킹 재시도 루프 대신, 큐가 가득 찼을 때 `await`하고 싶을 때 |
| `Send(IList<ArraySegment<byte>>)` | enqueue 시 **리스트 자체**는 복사되지만 내부 배열은 아니다 | 세그먼트 여러 개를 논리적으로 한 메시지로 보낼 때 |

`TrySend*`는 세션이 닫혔거나 큐가 가득 찼을 때 블로킹·예외 대신 `false`를 반환한다.
`Send`/`SendCopied`는 `ServerConfig.SendTimeOut`까지 스핀 대기한 뒤 `TimeoutException`을
던진다. zero-copy 오버로드의 정확한 버퍼 수명 규칙은
[`.claude/cautions.md`](.claude/cautions.md)를 참고한다.

## 설정

`ServerConfig`는 흔히 쓰는 항목(`Port`, `MaxConnectionNumber`, `ReceiveBufferSize`,
`SendTimeOut`, TCP keep-alive, 유휴 세션 정리 등)에 더해 알아두면 좋은 옵션 몇 가지를 둔다.

| 설정 | 기본값 | 용도 |
|---|---|---|
| `ReceiveInlineOnIocpThread` | `true` | 수신 파이프 전진을 스레드 풀로 넘기지 않고 IOCP 완료 스레드에서 바로 한다 — 수신 패킷마다 스레드 전환 1회와 `Task` 할당 2회를 아낀다. |
| `PreAllocateSAEA` / `MinPoolSize` | `true` / `100` | 최상의 accept 지연을 위해 기동 시 풀링될 `SocketAsyncEventArgs`를 전부 미리 만들거나, `MinPoolSize`에서 시작해 필요할 때 늘린다. |
| `MaxReceivePipeBufferSize` | `65536` | 수신 파이프의 백프레셔 임계값. `MaxRequestLength`를 담을 수 있도록 자동으로 올라가서, 큰 최대 요청 크기가 수신 루프를 교착시키지 않는다. |
| `SyncSessionConnectedEvent` | `false` | `NewSessionConnected`를 accept 중에 동기 호출해, 빠른 클라이언트의 첫 요청보다 반드시 먼저 실행되도록 구조적으로 보장한다. |
| `AcceptLoopCount` | `1` | 같은 리슨 소켓에서 accept 루프를 여러 개 동시에 돌린다 — 재접속 폭주를 흡수해야 하는 서버에 도움이 된다. |
| `UseZeroByteReceive` | `false` | 유휴 세션이 실제 수신 버퍼를 붙잡는 대신 zero-byte 수신으로 대기한다 — 대부분의 세션이 조용한 서버에서 유휴 연결의 메모리를 줄인다. |
| `KeepAliveRetryCount` | `5` | 연결을 죽은 것으로 판단하기까지 응답 없는 keep-alive 프로브를 몇 번 보낼지. `0` 이하면 OS 기본값을 그대로 쓴다. `ServerConfig`에만 있으므로 `IServerConfig`를 직접 구현하면 기본값이 쓰인다. |

## UDP 지원

UDP 세션도 TCP와 같은 `AppSession`/`IReceiveFilter` 파이프라인을 거친다. 두 가지 모드를
지원한다: 원격 엔드포인트 기준으로 세션 하나씩 두는 기본 모드, 그리고 요청 타입이
`UdpRequestInfo`를 상속하면 페이로드에 직접 담은 세션 ID로 구분하는 모드 — 후자는 클라이언트가
NAT 재바인딩을 겪어도 같은 논리적 세션을 유지할 수 있게 해 준다.
[`Tutorials/SimpleUDPServer`](Tutorials/SimpleUDPServer)를 참고한다.

## 관측성

전부 `Meter("SuperSocketLite")` 하나를 통해 노출된다.

- **카운터**: `total-requests`, `total-bytes-received`, `total-bytes-sent`, `sessions-rejected`,
  `send-queue-full`, `send-errors`, 그리고 `active-connections`(`UpDownCounter`)
- **히스토그램**: `request-duration` (요청 핸들러에서 소요된 시간)
- **게이지**: `session-count`, 그리고 내부 송신 큐 깊이와 `SocketAsyncEventArgs` 풀 사용량
  게이지(공개 C# 프로퍼티로는 노출되지 않지만, 이 Meter를 구독하는 어떤 계측 수집기에도 보인다)

게이지는 `ObservableGauge`라서 실제로 수집기가 듣고 있을 때만 계산이 일어난다 — 송신 큐 깊이도
누가 물어보는 순간에만 세션을 훑는다. 카운터는 다르다. 구독자가 있든 없든 일이 일어날 때마다
갱신되며, 그 비용은 이벤트당 `Add` 한 번이다.

## 예제

[`Tutorials/`](Tutorials) 디렉터리는 최소 에코 서버에서 시작해 더 완성된 패턴으로 확장된다.

| 프로젝트 | 보여주는 것 |
|---|---|
| [`EchoServer`](Tutorials/EchoServer) | 최소한의 엔드투엔드 구성 |
| [`EchoServer_NuGet`](Tutorials/EchoServer_NuGet) | 같은 서버를 프로젝트 참조 대신 `SuperSocketLite2` NuGet 패키지로 빌드 |
| [`EchoServerEx`](Tutorials/EchoServerEx) | 커맨드라인 옵션, NLog 연동 |
| [`EchoServer_GenericHost`](Tutorials/EchoServer_GenericHost) | `Generic Host` 서비스로 실행하기 |
| [`ChatServer`](Tutorials/ChatServer) / [`ChatServerEx`](Tutorials/ChatServerEx) | 여러 세션에 브로드캐스트하기 |
| [`BinaryPacketServer`](Tutorials/BinaryPacketServer) | 구조화된 바이너리 프로토콜 |
| [`MultiPortServer`](Tutorials/MultiPortServer) | 여러 포트를 동시에 리슨하기 |
| [`SimpleUDPServer`](Tutorials/SimpleUDPServer) | UDP 세션 |
| [`GateServer_GameServer`](Tutorials/GateServer_GameServer), [`PvPGameServer`](Tutorials/PvPGameServer), [`GameServer_MoDedicated`](Tutorials/GameServer_MoDedicated) | 실제 서비스에 가까운 게임 서버 형태 |

## 테스트 및 품질

라이브러리 자체가 상당한 안전망을 갖추고 있다. 수신/송신 파이프라인, 세션 상태 머신,
종료 경로의 경합, 로깅 어댑터를 다루는 **회귀 테스트 40건**
(`Test/SuperSocketLiteRegressionTests`)에 더해, 실제 TCP/UDP 트래픽을 실서버에 걸어
HTML 리포트를 만들고 기준 실행 대비 처리량·지연 회귀를 판정할 수 있는
**부하 테스트 툴킷**(자체 테스트 110건, `Test/LoadTest`)까지 함께 관리한다. 핵심 라이브러리를
바꿀 때마다 두 스위트를 모두 돌린다.

```bash
dotnet run --project Test/SuperSocketLiteRegressionTests -c Release
dotnet run --project Test/LoadTest/SuperSocketLite.LoadTest.Tests -c Release
```

## 문서

- [아키텍처 및 데이터 흐름](.claude/architecture.md)
- [코딩 컨벤션](.claude/conventions.md)
- [알려진 주의 사항](.claude/cautions.md) — 스레드 안전성, zero-copy 버퍼 수명, UDP 특이사항
- [GC·데이터 복사 최소화 가이드](Docs/GC_Copy_Minimization.md) — 수신 필터·패킷 핸들러·송신
  호출부에서 패킷당 할당을 0으로 만드는 방법
- [시작하기](Docs/Getting_Started_kr.html) — 빌드, 사용법, 위 주의 사항을 한 문서에서
- [다이어그램](Docs/index_kr.html) — 아키텍처, TCP 연결 흐름, 수신/송신 파이프라인 상세
- [VS Code에서 저장소 전체 분석 설정하기](Docs/VSCode_Repository_Analysis_kr.html)

## 기여

이슈와 풀 리퀘스트는 [GitHub](https://github.com/jacking75/SuperSocketLite2)에서 환영한다.

## 라이선스

[MIT](LICENSE)
