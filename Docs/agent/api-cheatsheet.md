# API 치트시트

라이브러리의 public 타입은 55개지만, 서버 하나를 만드는 데 실제로 쓰는 건 아래 12개다.
**나머지는 찾지 말고 이 목록에서 고른다.**

## using 한 벌

거의 모든 서버 파일이 이 조합으로 시작한다.

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

> `FixedHeaderReceiveFilter` / `FixedSizeReceiveFilter`만 `SocketEngine.Protocol`이고
> 나머지 프로토콜 타입은 `SocketBase.Protocol`이다. 자주 틀리는 지점이다.

## 반드시 쓰는 12개

| 타입 | 네임스페이스 | 역할 |
|---|---|---|
| `AppServer<TSession, TRequestInfo>` | `SocketBase` | 서버. 상속해서 내 서버를 만든다 |
| `AppSession<TSession, TRequestInfo>` | `SocketBase` | 세션. 상속해서 내 세션을 만든다 |
| `IRequestInfo` | `SocketBase.Protocol` | 파싱된 요청. 직접 구현한다 |
| `FixedHeaderReceiveFilter<TRequestInfo>` | `SocketEngine.Protocol` | 길이 프리픽스 프로토콜용 필터 베이스 |
| `FixedSizeReceiveFilter<TRequestInfo>` | `SocketEngine.Protocol` | 고정 길이 패킷용 필터 베이스 |
| `DefaultReceiveFilterFactory<TFilter, TRequestInfo>` | `SocketBase.Protocol` | 세션마다 필터를 `new`로 만들어 주는 기본 팩토리 |
| `ServerConfig` | `SocketBase.Config` | 포트·최대 연결 수 등 설정 |
| `RootConfig` | `SocketBase.Config` | `Setup`에 넘기는 루트 설정. 보통 `new RootConfig()` 그대로 |
| `SocketMode` | `SocketBase` | `Tcp` / `Udp` |
| `CloseReason` | `SocketBase` | `SessionClosed` 이벤트의 종료 사유 |
| `ILog` / `ConsoleLogFactory` | `SocketBase.Logging` | 로깅. 실서비스는 `MicrosoftLoggingLogFactory` |
| `ServerState` | `SocketBase` | `Initializing` / `Running` / `Stopping` 등 서버 상태 |

## `AppServer<TSession, TRequestInfo>`

```csharp
// 생성 — 필터 팩토리를 base 생성자에 넘긴다
public MyServer() : base(new DefaultReceiveFilterFactory<MyFilter, MyRequestInfo>()) { }

// 설정 — Start() 전에 반드시 한 번. false면 시작하면 안 된다
bool Setup(IRootConfig rootConfig, IServerConfig config,
           IReceiveFilterFactory<TRequestInfo>? receiveFilterFactory = null,
           ILogFactory? logFactory = null);
bool Setup(IServerConfig config, ...);   // RootConfig 생략형

// 생명주기
bool Start();
bool Start(CancellationToken cancellationToken);
void Stop();
Task StopAsync(TimeSpan drainTimeout);   // 큐에 남은 응답을 흘려보내고 닫는다. 권장
void Dispose();

// 이벤트 — 생성자에서 등록하는 게 관례
event SessionHandler<TSession> NewSessionConnected;
event RequestHandler<TSession, TRequestInfo> NewRequestReceived;
event SessionHandler<TSession, CloseReason> SessionClosed;

// 세션 조회
TSession? GetSessionByID(string sessionID);
IEnumerable<TSession>? GetAllSessions();                          // null일 수 있다
IEnumerable<TSession>? GetSessions(Func<TSession, bool> critera); // null일 수 있다
int SessionCount { get; }

// 상태
ServerState State { get; }
string Name { get; }
ILog Logger { get; }
IServerConfig Config { get; }
DateTime StartedTime { get; }     // UTC
ListenerInfo[]? Listeners { get; }
```

`GetAllSessions()`와 `GetSessions()`는 **`null`을 돌려줄 수 있다.** 서버가 아직 안 떴거나
내려가는 중이면 그렇다. 브로드캐스트 코드에서 그냥 `foreach` 돌리면 `NullReferenceException`이 난다.

## `AppSession<TSession, TRequestInfo>`

### 송신 — 어느 것을 쓰나

| 메서드 | 복사 여부 | 큐가 가득 찼을 때 | 언제 쓰나 |
|---|---|---|---|
| `SendCopied(ReadOnlySpan<byte>)` | **복사함** | `SendTimeOut`까지 스핀 후 `TimeoutException` | **기본값으로 이걸 쓴다.** 버퍼를 바로 재사용할 수 있다 |
| `TrySendCopied(ReadOnlySpan<byte>)` | 복사함 | `false` 리턴 | 던지지 않고 실패를 다루고 싶을 때 |
| `Send(byte[] data, int offset, int length)` | **안 함 (zero-copy)** | 스핀 후 `TimeoutException` | 전송 끝날 때까지 안 건드릴 버퍼를 이미 갖고 있을 때 |
| `TrySend(byte[], int, int)` | 안 함 | `false` 리턴 | 위와 같고 예외 대신 bool |
| `Send(ArraySegment<byte>)` / `TrySend(...)` | 안 함 | 스핀 / `false` | 위와 같음 |
| `Send(IList<ArraySegment<byte>>)` | **리스트만 복사**, 배열은 공유 | 스핀 / `false` | 여러 조각을 한 메시지로 보낼 때 |
| `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` | 배열 기반이면 안 함 | `await`로 대기 | 큐가 찼을 때 스핀 대신 기다리고 싶을 때 |
| `Send(string)` / `TrySend(string)` | — | 스핀 / `false` | 텍스트 프로토콜 |

- **`SendAsync`에는 `SendTimeOut`이 적용되지 않는다.** 직접 `CancellationToken`으로 상한을 건다.
- `SendCopied`는 데이터가 비면 아무것도 큐에 넣지 않고 성공으로 돌아간다.
  `Send(buffer, 0, 0)`은 길이 0 세그먼트를 실제로 전송한다(UDP에서 빈 데이터그램이 나간다).

### 나머지

```csharp
void Close();
void Close(CloseReason reason);
void SendEndWhenSendingTimeOut();   // TimeoutException 후 Close() 하기 전에 반드시 호출

string SessionID { get; }
bool Connected { get; }
IPEndPoint? RemoteEndPoint { get; }
IPEndPoint? LocalEndPoint { get; }
DateTime StartTime { get; }         // UTC
DateTime LastActiveTime { get; }    // UTC, 단조 tick에서 역산하므로 수 ms 오차
ILog Logger { get; }
LogSessionContext SessionLogContext { get; }   // 구조적 로깅용. 읽어도 할당 없음
IServerConfig Config { get; }
AppServerBase<TSession, TRequestInfo> AppServer { get; }

// 오버라이드 지점
protected virtual void OnSessionStarted();
protected virtual void OnSessionClosed(CloseReason reason);
protected virtual void HandleException(Exception e);
protected virtual void HandleUnknownRequest(TRequestInfo requestInfo);
```

## `IRequestInfo` / 필터

```csharp
public interface IRequestInfo
{
    string Key { get; }   // 바이너리 프로토콜이면 string.Empty로 두면 된다
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

`FixedHeaderReceiveFilter<T>`를 상속하면 `Filter`를 직접 짤 필요가 없다. 두 개만 구현한다.

```csharp
protected abstract int GetBodyLengthFromHeader(ReadOnlySequence<byte> header);
protected abstract TRequestInfo? ResolveRequestInfo(ReadOnlySequence<byte> header,
                                                    ReadOnlySequence<byte> body);
protected virtual bool ValidateBodyLength(int bodyLength);  // 기본 구현이 MaxRequestLength를 본다
protected int HeaderSize { get; }
```

`FixedSizeReceiveFilter<T>`는 하나만 구현한다.

```csharp
protected abstract TRequestInfo? ProcessMatchedRequest(ReadOnlySequence<byte> buffer);
public int Size { get; }
```

## `ServerConfig` 주요 값

기본값이 명시된 것만 적었다. 나머지는 `SuperSocketLite/SocketBase/Config/ServerConfig.cs` 참조.

| 설정 | 기본값 | 설명 |
|---|---|---|
| `Ip` | — | `"Any"` 또는 특정 IP. 보통 `"Any"` |
| `Port` | — | 리스닝 포트 |
| `Mode` | `Tcp` | `SocketMode.Tcp` / `SocketMode.Udp` |
| `Name` | — | 서버 이름. 로그에 찍힌다 |
| `MaxConnectionNumber` | 100 | **초과하면 즉시 끊고 `NewSessionConnected`를 부르지 않는다** |
| `MaxRequestLength` | 1024 | 요청 최대 길이. 게임 서버는 대개 올려야 한다 |
| `ReceiveBufferSize` | 4096 | |
| `SendBufferSize` | 2048 | |
| `SendTimeOut` | 5000 (ms) | 블로킹 `Send` 계열이 포기하고 던지기까지의 시간 |
| `SendingQueueSize` | 5 | 세션당 송신 큐 깊이 |
| `ClearIdleSession` | false | 유휴 세션 정리 켜기 |
| `IdleSessionTimeOut` | 300 (초) | |
| `ClearIdleSessionInterval` | 120 (초) | |
| `KeepAliveTime` | 600 (초) | |
| `KeepAliveInterval` | 60 (초) | |
| `KeepAliveRetryCount` | 5 | `ServerConfig`에만 있다. 직접 만든 `IServerConfig`는 기본값을 못 받는다 |
| `ListenBacklog` | 100 | |
| `NoDelay` | false | Nagle 끄기. 실시간 게임이면 대개 `true` |
| `ReceiveInlineOnIocpThread` | **true** | IOCP 스레드에서 파이프를 바로 전진시킨다. 패킷당 스레드 홉과 `Task` 할당 2개를 아낀다 |
| `PreAllocateSAEA` / `MinPoolSize` | true / 100 | 시작 시 SAEA를 전부 미리 할당 |
| `MaxReceivePipeBufferSize` | 65536 | 수신 파이프 백프레셔 임계값. `MaxRequestLength`에 맞춰 자동으로 올라간다 |
| `SyncSessionConnectedEvent` | **false** | `true`면 `NewSessionConnected`가 accept 경로에서 동기 호출되어 "접속 → 첫 요청" 순서가 보장된다. 대신 핸들러가 accept를 블로킹한다 |
| `AcceptLoopCount` | 1 | 동시 accept 루프 수. 재접속 폭주를 흡수할 때 올린다 (최대 64) |
| `UseZeroByteReceive` | false | 유휴 세션이 실제 수신 버퍼를 잡지 않게 한다. 대부분 조용한 서버에서 메모리 절약 |

## 로깅

```csharp
// 개발용
new ConsoleLogFactory()

// 실서비스 — Serilog / NLog / ZLogger / log4net 전부 이걸로 붙인다
using Microsoft.Extensions.Logging;
ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());
new MicrosoftLoggingLogFactory(loggerFactory)
```

`ILog`는 `Debug/Info/Warn/Error/Fatal`과 `IsDebugEnabled` 같은 가드 프로퍼티를 갖는다.
세션 정보를 남길 땐 메시지에 세션 ID를 문자열로 박지 말고 `session.SessionLogContext`를 넘긴다.

```csharp
session.Logger.Log(LogEventLevel.Info, session.SessionLogContext, "login ok");
```

## 관측

`Meter("SuperSocketLite")` 하나로 전부 나간다. OpenTelemetry 수집기를 붙이면 바로 보인다.

- 카운터: `total-requests`, `total-bytes-received`, `total-bytes-sent`, `sessions-rejected`,
  `send-queue-full`, `send-errors`, `active-connections`(UpDownCounter)
- 히스토그램: `request-duration` (핸들러 체류 시간)
- 게이지: `session-count`, 송신 큐 깊이, SAEA 풀 사용량

게이지는 `ObservableGauge`라 아무도 안 보면 계산 자체가 안 일어난다.
