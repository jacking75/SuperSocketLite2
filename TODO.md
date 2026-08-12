# TODO — 성능·안정성 개선 작업 목록

작성일: 2026-08-12.

> **상태: TODO-01 ~ TODO-19 전부 완료 (2026-08-12).** 아래 본문은 각 항목의 문제 분석과 구현
> 방침을 남겨 둔 이력 문서다. 완료 항목의 제목에 ✅ 표시가 있고, 사용자에게 영향이 가는 동작
> 변경은 해당 절에 별도로 적어 두었다.
>
> 회귀 테스트는 10개 → 31개로 늘었다: `dotnet run --project Test/SuperSocketLiteRegressionTests -c Release`
>
> 남은 것 (필요해지면 신규 TODO로):
> - `HttpReceiveFilterBase`의 `ISequenceReceiveFilter` 구현 (사용 빈도가 낮아 보류)
> - LoadTest 기반 before/after 성능 수치 측정 (RPS, p99, Gen0)
> - `ReuseLockBaseBuffer.Commit`의 압축 조건(`MinumBufferSize < 남은 공간`)이 의도와 반대로
>   보인다 — 여유가 적을 때가 아니라 많을 때 압축한다. 동작상 손상은 없어 그대로 두었다.

## 공통 규칙

- 코드 변경 후 반드시 빌드 확인: `cd SuperSocketLite && dotnet build -c Release`
- 회귀 테스트 실행: `dotnet run --project Test/SuperSocketLiteRegressionTests -c Release`
- 기존 public API 시그니처는 변경하지 않는다. 새 기능은 오버로드/신규 멤버/설정 옵션으로 추가한다.
- 작업 순서는 P0 → P1 → P2 → P3 권장. P0의 3건은 서로 독립이므로 개별 커밋으로 진행한다.

## 현재 상태 요약 (2026-08-12 코드 기준)

이미 완료되어 있어 다시 할 필요 없는 것들:

- ✅ System.IO.Pipelines 수신 경로 (TASK-01), BufferManager 제거
- ✅ SAEA 풀 동적/사전 할당 (`PreAllocateSAEA`, `MinPoolSize`) — 구 TASK-04
- ✅ `Start(CancellationToken)` — 구 TASK-03의 서버 수명주기 부분
- ✅ Metrics 기본 (`Meter("SuperSocketLite")`: total-requests, bytes, active-connections) — 구 TASK-06 부분
- ✅ `IReceiveFilter`에 `ReadOnlySpan<byte>` default 오버로드 — 구 TASK-07 부분
- ✅ `ISequenceReceiveFilter` + `FixedSizeReceiveFilter`/`FixedHeaderSequenceReceiveFilter`의 zero-copy 경로
- ✅ `ChannelSendingQueue` (구 SendingQueue 대체), Accept 루프의 `AcceptAsync(ct)` 전환

---

# P0 — 버그 수정 (안정성, 최우선)

## TODO-01: TCP KeepAlive가 어떤 플랫폼에서도 적용되지 않는 문제 수정 — ✅ 완료 (2026-08-12)

**문제**

- `SuperSocketLite/SuperSocketLite.csproj`에 `WINDOWS` 컴파일 심볼이 정의되어 있지 않다.
- 따라서 `Platform.cs:17`의 `#if WINDOWS` 블록이 컴파일에서 빠지고, static 생성자에서 예외가 발생하지 않아 `SupportSocketIOControlByCodeEnum`은 **모든 플랫폼에서 true**가 된다.
- `TcpSocketServerBase.cs:52-61`에서 `SupportSocketIOControlByCodeEnum == true`이면 else 분기로 가는데, 그 안의 `client.IOControl(...)`도 `#if WINDOWS`라서 컴파일에서 제거됨 → **KeepAlive 설정 코드가 아무것도 실행되지 않는다.**
- 결과: 클라이언트가 비정상 종료(케이블 뽑힘, 모바일 망 단절)해도 OS 레벨 감지가 없고, `ClearIdleSession` 타이머에만 의존한다. 모바일 게임 서버에서 유령 세션이 IdleSessionTimeOut(기본 300초)까지 생존.
- 부수 버그: `UdpSocketListener.cs:38-47`도 같은 플래그를 보고 `IOControl(SIO_UDP_CONNRESET)`을 호출하는데, Linux에서는 raw IOControl이 `PlatformNotSupportedException`을 던져 catch로 넘어가 **UDP 서버 Start()가 Linux에서 통째로 실패**한다.

**구현 방법**

1. `TcpSocketServerBase.CreateSession()`의 KeepAlive 블록 전체(생성자의 `m_KeepAliveOptionValues` 바이트 배열 생성 포함)를 .NET Core 3.0+ 크로스 플랫폼 소켓 옵션으로 교체:
   ```csharp
   client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
   client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, config.KeepAliveTime);       // 초 단위
   client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, config.KeepAliveInterval); // 초 단위
   client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
   ```
   - RetryCount는 `ServerConfig.KeepAliveRetryCount`(기본 5) 프로퍼티를 신설해서 받도록 한다 (`IServerConfig`에는 추가하지 말 것 — 인터페이스 변경 회피. `ServerConfig`에만 추가하고 `TcpSocketServerBase`에서 `config as ServerConfig`로 읽거나 기본값 사용).
   - 개별 `SetSocketOption`이 실패해도 세션 생성은 계속되도록 try/catch로 감싸고 경고 로그만 남긴다.
2. `Platform.SupportSocketIOControlByCodeEnum` 사용처를 정리:
   - `TcpSocketServerBase`: 사용 제거.
   - `UdpSocketListener.cs:38`: `if (Platform.SupportSocketIOControlByCodeEnum)` → `if (OperatingSystem.IsWindows())`로 교체하고, IOControl 호출을 개별 try/catch로 감싸 실패해도 리스너 시작은 계속되게 한다 (SIO_UDP_CONNRESET는 Windows 전용 개선일 뿐 필수가 아님).
   - `Platform` 클래스 자체는 public이므로 삭제하지 말고 [Obsolete] 처리만 검토.

**검증**

- Windows에서 서버 기동 → 클라이언트 접속 후 `netsh interface tcp` 또는 Wireshark로 keep-alive 프로브 확인. 간단하게는 회귀 테스트에 "접속된 Socket의 `GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)` 값 확인" 테스트 추가.
- (가능하면) WSL/Linux에서 UDP 서버(`Tutorials` 또는 LoadTest UDP 시나리오) Start 성공 확인.

**부수 수정 (UDP 테스트 작성 중 발견)**

`UdpSocketListener.Stop()`이 소켓 Close 직후 `receiveSAE.SetBuffer(null, 0, 0)`를 호출하는데, 중단된 ReceiveFrom 완료 통지가 아직 도착하지 않았으면 `InvalidOperationException`이 Stop() 밖으로 던져진다. 게다가 기존 코드는 `SetBuffer`보다 **먼저** `ArrayPool.Return`을 호출해서, 커널이 아직 쓰고 있을 수 있는 버퍼를 풀에 돌려주는 위험이 있었다. → 순서를 뒤집고(detach 성공 시에만 반환) try/catch로 감쌌다.

---

## TODO-02: `SendSync`의 client == null 경로에서 InSending 플래그 누수 → 세션 좀비화 — ✅ 완료 (2026-08-12)

**문제**

`AsyncSocketSession.cs:165-205`의 `SendSync()`는 전송 도중 `Client`가 null이면 (다른 스레드가 `InternalClose`로 소켓을 정리한 경우) 그냥 `return` 한다 (175행, 190행 부근 두 곳). 이때 `OnSendingCompleted()`도 `OnSendError()`도 호출되지 않으므로:

- `SocketState.InSending` 플래그가 영원히 남는다 (`SocketSession.StartSend`에서 세팅한 것).
- 닫는 쪽 스레드는 `InternalClose → ValidateNotInSendingReceiving()`이 false라 `OnClosed`를 못 올린다.
- 결과: `Closed` 이벤트 미발생 → `AsyncSocketServer.SessionClosed` 미호출 → **SAEA 2개(수신 proxy + 송신)가 풀로 반환되지 않고, 세션이 세션 컨테이너에서 제거되지 않는다.** SyncSend 모드(특히 CollectSend는 SyncSend 강제)에서 접속 해제 폭풍이 오면 풀 고갈로 이어질 수 있다.

**구현 방법**

1. `SendSync()`의 두 `if (client == null) return;`을 `OnSendError(queue, CloseReason.SocketError); return;`으로 교체한다. `OnSendError → OnSendEnd(closeReason, forceClose:true)`가 InSending 해제와 `ValidateClosed`를 처리해 준다.
2. 같은 파일의 `SendAsync()`는 이미 null 경로에서 `OnSendError`를 호출하므로 수정 불필요 — 대칭성만 확인.

**검증**

회귀 테스트 추가 (Test/SuperSocketLiteRegressionTests):
- SyncSend 세션에서 전송 루프 진입 후 소켓을 강제로 닫고, 일정 시간 내 `Closed` 콜백이 호출되는지 확인.
- 단위 수준으로는 SocketSession의 상태 플래그를 리플렉션으로 읽어 "SendSync 에러 경로 후 InSending == 0" 검증.

---

## TODO-03: 동기 완료(synchronous completion) 재귀로 인한 스택 오버플로 위험 제거 — ✅ 완료 (2026-08-12)

**문제**

- TCP 수신: `AsyncSocketSession.StartReceive()` → `Client.ReceiveAsync(e)`가 동기 완료(`willRaiseEvent == false`)하면 `ProcessReceive(e)`를 직접 호출하고, `ProcessReceive`는 flush가 동기 완료하면 다시 `StartReceive()`를 호출한다 (`AsyncSocketSession.cs:142-163`, `276-304`). 수신 버퍼에 데이터가 계속 차 있으면 **StartReceive ↔ ProcessReceive 상호 재귀**가 무한정 깊어질 수 있다.
- UDP 수신: `UdpSocketListener.eventArgs_Completed()`가 마지막에 `ReceiveFromAsync`를 걸고 동기 완료 시 자기 자신을 재귀 호출한다 (`UdpSocketListener.cs:60-63`, `108-115`). 패킷 폭주 시 동일한 위험.

**구현 방법 (TCP)**

`StartReceive`를 루프로 바꾼다. 동기 완료 처리 결과를 bool로 돌려받는 내부 메서드로 분리:

```csharp
private void StartReceive()
{
    var e = SocketAsyncProxy.SocketEventArgs;
    try
    {
        while (true)
        {
            var memory = _pipeWriter!.GetMemory(Config.ReceiveBufferSize);
            e.SetBuffer(memory);

            if (!OnReceiveStarted())
                return;

            var client = Client;
            if (client == null) { OnReceiveTerminated(CloseReason.SocketError); return; }

            if (client.ReceiveAsync(e))
                return;                       // 비동기 완료 → Completed 이벤트에서 ProcessReceive 호출

            if (!ProcessReceiveCore(e))       // 동기 완료 → 인라인 처리
                return;                       // 종료/에러 또는 flush가 비동기로 넘어감
        }
    }
    catch (Exception exc)
    {
        LogError(exc);
        OnReceiveTerminated(CloseReason.SocketError);
    }
}
```

- `ProcessReceiveCore(e)`: 기존 `ProcessReceive` 본문에서 "다음 수신 게시" 부분을 제거한 것. 반환값 의미 — true: 계속 수신 루프 돌 것, false: 수신 종료(에러/취소) 또는 `FlushAsync`가 비동기로 넘어가서 continuation(`FlushPipeAndStartReceiveAsync`)이 `StartReceive()`를 다시 호출할 예정.
- 기존 public `ProcessReceive(SocketAsyncEventArgs)`(IOCP 완료 이벤트에서 호출됨)는 `if (ProcessReceiveCore(e)) StartReceive();`로 재작성. 이벤트 경로는 매번 새 스택이므로 재귀 없음.

**구현 방법 (UDP)**

`eventArgs_Completed`의 마지막 부분을 루프로 교체:

```csharp
while (true)
{
    var listenSocket = m_ListenSocket;
    if (listenSocket == null || listenSocket.ReceiveFromAsync(e))
        break;
    if (!ProcessReceivedFrom(e))   // 패킷 처리 부분을 분리한 메서드, 에러 시 false
        break;
}
```

**검증**

- LoadTest의 EchoBinary 시나리오를 localhost(동기 완료가 가장 잘 발생하는 환경)에서 고부하로 실행하여 StackOverflow 없이 완주 확인.
- 기존 회귀 테스트 전체 통과.

---

## TODO-19: sequence 수신 경로의 `MaxRequestLength` 오판정 — ✅ 완료 (2026-08-12)

**문제**

`AppSession.ProcessSequenceRequest`(`AppSession.cs:719-729`)가 `sequence.Length >= maxRequestLength`로 검사한다. 여기서 `sequence`는 **PipeReader가 넘겨준 미소비 버퍼 전체**이지 현재 파싱 중인 부분 요청이 아니다.

클라이언트가 요청을 파이프라이닝해서 서버의 소비 속도보다 빠르게 보내면 Pipe에 데이터가 쌓이다가 pauseWriterThreshold(기본 64KB)에 도달하고, 개별 요청은 전부 정상 크기인데도 `Close(CloseReason.ProtocolError)`로 접속이 끊긴다.

재현: `Test/SuperSocketLiteRegressionTests`의 "Loopback echo survives a synchronous-completion burst" 테스트에서 `MaxRequestLength = 4096`, 244바이트 패킷 4000개를 연속 전송하면 서버 로그에 `Max request length: 4096, current processed length: 65582`가 찍히고 RST. (HEAD 기준으로도 동일하게 재현 — TODO-03 변경과 무관한 기존 버그)

현재 회귀 테스트는 이 제한에 걸리지 않도록 `MaxRequestLength`를 1MB로 올려 두었다. 수정 후에는 4096으로 되돌려 검증할 것.

**구현 방향**

- 검사 대상을 "현재 미완성 요청의 누적 길이"로 바꾼다. sequence 필터가 불완전 반환 시 `consumed == buffer.Start`이므로, `AdvanceTo` 이후 남는 길이(= 다음 호출의 `sequence.Length` 중 아직 파싱 못한 부분)를 필터의 `LeftBufferSize`로 관리하고 그 값으로 판정.
- 또는 필터 루프를 돌린 뒤 남은(미소비) 길이로 판정하도록 검사 위치를 파싱 **후**로 옮긴다.
- TODO-11(나머지 필터의 sequence 구현), TODO-13(pauseWriterThreshold 노출)과 함께 설계하는 것이 좋다.

---

# P1 — 핫패스 성능

## TODO-04: 수신 완료마다 발생하는 Task 2개 + 클로저 할당 제거 (최대 성능 항목) — ✅ 완료 (2026-08-12)

`ServerConfig.ReceiveInlineOnIocpThread`(기본 true) 옵션까지 구현. `IAsyncSocketSession`에 같은 이름의 프로퍼티를 추가해 프록시의 static 핸들러가 세션별 설정을 읽는다. else 분기의 throw는 로그로 대체.

**문제**

`SocketAsyncEventArgsProxy.cs:32-47`: IOCP 수신 완료 이벤트가 올 때마다

```csharp
socketSession.AsyncRun(() => socketSession.ProcessReceive(e));
```

를 호출한다. `Async.AsyncRun`(`SocketBase/Async.cs:56-73`)은 `Task.Factory.StartNew` + `ContinueWith`이므로 **패킷 수신 1회당 클로저 1개 + Task 2개 할당 + 스레드 풀 홉 1회**가 발생한다. 초당 수만 패킷이면 GC 압력과 레이턴시에 직접 영향.

과거에는 `ProcessReceive`가 앱 로직(`ProcessRequest`)까지 실행했기 때문에 IOCP 스레드 보호를 위해 홉이 필요했지만, Pipelines 도입 후 `ProcessReceive`는 `PipeWriter.Advance + FlushAsync + 다음 수신 게시`만 수행하고 앱 로직은 `ProcessPipeAsync` 태스크에서 돈다. **인라인 실행이 안전해졌다.**

**구현 방법**

1. `SocketEventArgs_Completed`에서 직접 호출로 교체:
   ```csharp
   static void SocketEventArgs_Completed(object? sender, SocketAsyncEventArgs e)
   {
       var socketSession = e.UserToken as IAsyncSocketSession;
       if (socketSession == null)
           return;

       if (e.LastOperation == SocketAsyncOperation.Receive)
       {
           try
           {
               socketSession.ProcessReceive(e);
           }
           catch (Exception exc)
           {
               socketSession.Logger?.Error("ProcessReceive failed", exc);
           }
       }
       // else 분기의 throw는 로그로 대체 (IOCP 콜백 스레드에서 throw 금지)
   }
   ```
2. TODO-03의 루프 구조와 함께 적용해야 한다 (인라인화하면 동기 완료 체인이 더 잘 이어지므로 재귀 제거가 선행 조건).
3. IOCP 스레드에서 실행되는 코드가 블로킹하지 않는지 확인: `ProcessReceive` 경로에서 블로킹 요소는 없음 (`FlushAsync`는 비동기, `RecordBytesReceived`는 Interlocked). `OnReceiveTerminated → ValidateClosed`는 `lock`을 잡지만 짧다.
4. (선택) 만약의 회귀 대비로 `ServerConfig.ReceiveInlineOnIocpThread`(기본 true) 옵션을 두고 false면 기존 AsyncRun 경로 유지.

**검증**

- LoadTest EchoBinary before/after 비교: RPS, p99 레이턴시, `dotnet-counters`의 Gen0 수집 횟수.
- 회귀 테스트 전체 통과.

---

## TODO-05: `ChannelSendingQueue` 락 제거 + Drain 리스트 재사용 — ✅ 완료 (2026-08-12)

**동작 변경 2건 (사용자 영향)**

1. `SendingQueueSize`가 이제 **세그먼트 수가 아니라 대기 중인 "전송 요청" 수**를 센다. 다중 세그먼트 `Send(IList<ArraySegment<byte>>)`는 슬롯 1개만 차지한다 (원자적 enqueue를 락 없이 보장하기 위함).
2. `SendItem`은 호출자의 `IList`를 **복사해서** 보관한다. TODO 원안(리스트 참조 보관)대로 하면 enqueue 직후 호출자가 리스트를 재사용할 때 데이터가 깨지므로, 세그먼트 배열 1개를 복사하는 쪽을 택했다 (다중 세그먼트 송신은 핫패스가 아님). byte[] 자체는 여전히 공유 — 기존 zero-copy 주의사항 그대로.

**문제** (`Common/ChannelSendingQueue.cs`)

1. 모든 `TryEnqueue`/`DrainAvailable`/`Complete`가 단일 `lock (m_SyncRoot)`을 통과한다. Channel의 lock-free 장점이 사라지고, 다중 스레드에서 같은 세션에 Send하면 락 경합이 발생한다.
2. `DrainAvailable()`이 호출될 때마다 `new List<ArraySegment<byte>>()`를 할당한다. 송신 사이클마다 리스트 1개 + 내부 배열 할당.

**구현 방법**

1. **락 제거**: Bounded Channel의 `TryWrite`는 자체적으로 (용량 초과 / Complete 이후) false를 반환하므로 원자성이 보장된다.
   ```csharp
   public bool TryEnqueue(ArraySegment<byte> item)
   {
       if (!m_Channel.Writer.TryWrite(item))
           return false;
       Interlocked.Increment(ref m_Count);
       return true;
   }
   ```
   - `m_Count`는 advisory 값이다. `SocketSession.OnSendingCompleted`의 이중 확인 로직(`Count == 0 → OnSendEnd → Count > 0 → StartSend(true)`)이 카운트/채널 간 순간적 불일치를 이미 흡수하므로 정확한 동기화가 필요 없다. 이 불변식을 주석으로 명시할 것.
   - `Complete()`는 `m_Channel.Writer.TryComplete()`만 호출.
2. **IList 오버로드의 all-or-nothing 보장**: 락 없이 부분 기입을 막으려면 큐 항목 타입을 바꾼다.
   ```csharp
   private readonly Channel<object> m_Channel;   // ArraySegment<byte>(박싱 회피 위해 아래 구조체) 또는
   internal readonly struct SendItem
   {
       public readonly ArraySegment<byte> Segment;
       public readonly IList<ArraySegment<byte>>? Segments;  // 다중 세그먼트는 한 슬롯
   }
   ```
   `Channel<SendItem>`으로 선언하면 박싱 없음. 다중 세그먼트 Send는 SendItem 하나로 enqueue → 슬롯 1개만 차지하므로 원자적. Drain 시 평탄화한다.
3. **Drain 리스트 재사용**: 시그니처를 `void DrainAvailable(List<ArraySegment<byte>> into)`로 바꾸고, `SocketSession`이 세션당 리스트 1개를 보유·재사용한다.
   - 안전 근거: `InSending` 플래그로 송신은 세션당 single-flight이다. 이전 drain 결과 리스트는 `OnSendingCompleted`/`OnSendError` 시점에 SAEA에서 분리(`ClearPrevSendState`)된 뒤에야 다음 `StartSend → Drain`이 일어난다. 이 불변식을 `SocketSession.StartSend`에 주석으로 남길 것.
   - 주의: `AsyncSocketSession.OnSendingCompleted`의 부분 전송 경로(`TrimSegments`)는 새 리스트를 만들어 재전송하므로 재사용 리스트와 충돌하지 않지만, TODO-08 적용 시 풀 반환 타이밍과 얽히므로 함께 설계할 것.
4. `ChannelSendingQueue`는 internal이므로 시그니처 변경 자유. 호출부는 `SocketSession.cs`뿐.

**검증**

- 회귀 테스트의 "Channel send queue drains batches in FIFO order" 테스트를 새 시그니처로 갱신 + 다중 스레드 enqueue 스트레스 테스트 추가 (N 스레드 × M 항목 enqueue 후 drain 총합 검증).
- LoadTest에서 송신 처리량 비교.

---

## TODO-06: 송신 완료 경로의 LINQ 제거 — ✅ 완료 (2026-08-12)

**문제**

`AsyncSocketSession.cs:108` — 비동기 송신 완료마다 `queue.Sum(q => q.Count)` (델리게이트 + enumerator 할당). 113행도 동일한 Sum을 한 번 더 호출.

**구현 방법**

```csharp
var count = 0;
for (var i = 0; i < queue.Count; i++)
    count += queue[i].Count;
```

부분 전송 로그 문자열(113행)은 부분 전송이 실제 발생했을 때만 만들어지므로 그대로 두되, 두 번째 `queue.Sum`도 같은 방식으로 교체.

또한 이 지점에서 `AppSession?.AppServer.RecordBytesSent(e.BytesTransferred)` 호출이 누락되어 있다 (수신은 `RecordBytesReceived`를 호출하는데 송신 메트릭은 아무 데서도 안 불림). `ProcessCompleted` 성공 시 `RecordBytesSent(e.BytesTransferred)`를 추가하고, `SendSync`에도 전송 성공 바이트를 기록한다.

**검증**: 빌드 + 회귀 테스트. 메트릭은 TestServer 실행 후 `dotnet-counters monitor --counters SuperSocketLite`로 total-bytes-sent 증가 확인.

---

## TODO-07: `DateTime.Now` → `DateTime.UtcNow` / `Environment.TickCount64` — ✅ 완료 (2026-08-12)

**동작 변경 (사용자 영향)**

- `AppSession.StartTime`, `AppSession.LastActiveTime`, `AppServerBase.StartedTime`이 **로컬 시간에서 UTC로** 바뀌었다. 이 값을 화면/로그에 그대로 찍는 앱 코드는 `.ToLocalTime()`을 붙여야 한다.
- `LastActiveTime`은 내부 tick 스탬프(`Environment.TickCount64`)에서 역산하므로 수 ms 오차가 있다. 유휴 판정(`ClearIdleSession`)은 프로퍼티를 거치지 않고 `LastActiveTimeTicks`를 직접 비교하므로 벽시계 조정(DST/NTP)의 영향을 받지 않는다.
- 핫패스(Send 성공, `ExecuteCommand`)는 `internal AppSession.MarkActive()`만 호출한다. 라이브러리 내 `DateTime.Now` 사용처는 0건.

**문제**

`DateTime.Now`는 타임존 변환 때문에 `UtcNow`보다 수 배 느린데, 다음 핫패스에서 호출된다:

- `AppSession.cs:337, 404` — **모든 Send 성공마다** (`LastActiveTime = DateTime.Now`)
- `AppServerBase.cs:784` — 모든 요청 처리마다 (`ExecuteCommand`)
- `AppSession.cs:371, 383, 437, 449` — 송신 재시도 타임아웃 계산 (spin 루프 내에서 반복 호출)
- `AppServer.cs:299` — ClearIdleSession 타이머

**구현 방법**

1. 타임아웃/유휴 판정 등 **내부 비교 용도**는 전부 `Environment.TickCount64`(밀리초, 단조 증가)로 교체한다:
   - `AppSession.InternalSend`의 spin 타임아웃: `var deadline = Environment.TickCount64 + sendTimeOut;` → 루프에서 `Environment.TickCount64 >= deadline`.
   - 세션 내부에 `internal long LastActiveTimeTicks` (TickCount64) 필드를 추가하고 Send/ExecuteCommand에서는 이것만 갱신. `ClearIdleSession`은 이 필드로 판정.
2. public `LastActiveTime`/`StartTime` DateTime 프로퍼티는 하위 호환을 위해 유지하되, `LastActiveTime`의 getter가 Tick 기반 값에서 근사 계산(`DateTime.UtcNow - (nowTicks - lastActiveTicks)`)해 반환하도록 하거나, 간단히 세터 유지 + 내부 판정만 Tick 기반으로 이원화한다. **권장: 이원화** (public 프로퍼티는 기존 의미 유지하되 갱신 빈도를 낮춤 — 예: ExecuteCommand에서만 갱신, Send 핫패스에서는 Tick 필드만 갱신).
3. 남는 `DateTime.Now`는 모두 `DateTime.UtcNow`로 교체 (StartedTime, 로그 메시지 용도). 로그 출력 포맷이 로컬 시간에서 UTC로 바뀌는 것은 허용 (변경 사실을 커밋 메시지에 명시).

**검증**: 회귀 테스트 + ClearIdleSession 동작 테스트(짧은 IdleSessionTimeOut으로 유휴 세션이 닫히는지).

---

# P2 — 기능 추가

## TODO-08: 풀 기반 송신 (copy-on-send) — 송신 버퍼 수명 문제 해결 + 할당 제거 — ✅ 완료 (2026-08-12)

**추가 API**: `AppSession.TrySendCopied(ReadOnlySpan<byte>)` / `AppSession.SendCopied(ReadOnlySpan<byte>)`,
`ISocketSession.TrySendCopied` (default 구현으로 추가 → 인터페이스 하위 호환 유지).
기존 `TrySend(ReadOnlySpan<byte>)`와 `TrySend(ReadOnlyMemory<byte>)`의 non-array 폴백이 내부적으로
풀 경로를 타므로 `ToArray()` 할당이 사라졌다.

**풀 반환 시점**: 배치 종료 지점(`OnSendingCompleted(sentItems)` / `OnSendError`)에서만 반환한다.
세션이 송신 도중 죽으면 그 배치의 배열은 풀에 돌아가지 않고 GC에 맡긴다 — 커널이 아직 읽고 있을 수
있는 배열을 풀에 돌려주는 것보다 안전하기 때문. 부분 전송 재전송(`TrimSegments`)은 같은 배열의 다른
구간을 가리킬 뿐이므로 배치 종료 시점 반환이 정확히 안전하다.

**문제**

- 현재 `Send(byte[] ...)`는 호출자의 배열을 큐에 **참조로** 보관한다. 전송 완료 전에 호출자가 버퍼를 재사용하면 잘못된 데이터가 전송된다 (게임 서버에서 자주 밟는 함정, 문서화도 안 되어 있음).
- `TrySend(ReadOnlySpan<byte>)`(`SocketSession.cs:282-285`)는 `ToArray()`로 GC 할당을 한다.

**구현 방법**

1. `SocketSession`에 내부 풀 송신 경로 추가:
   ```csharp
   public bool TrySendCopied(ReadOnlySpan<byte> data)
   {
       if (IsClosed) return false;
       var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
       data.CopyTo(buffer);
       if (!m_SendQueue.TryEnqueue(new SendItem(new ArraySegment<byte>(buffer, 0, data.Length), pooled: true)))
       {
           ArrayPool<byte>.Shared.Return(buffer);
           return false;
       }
       StartSend(true);
       return true;
   }
   ```
   TODO-05의 `SendItem` 구조체에 `bool Pooled` 플래그를 추가한다.
2. **반환 시점**: drain된 배치가 완전히 끝나는 지점 — `SocketSession.OnSendingCompleted(sentItems)` 진입 시와 `OnSendError` — 에서 해당 배치의 pooled 배열을 `ArrayPool.Return`한다.
   - 구현: `SocketSession`이 drain 시 `List<byte[]> m_PooledInFlight`(세션당 재사용)에 pooled 배열을 모아 두고, 배치 종료 시 일괄 반환 후 Clear.
   - 부분 전송 재전송(`TrimSegments`) 중에는 배열이 아직 참조되므로 **배치 종료 시점 반환**이 정확히 안전하다 (TrimSegments는 같은 배열의 다른 구간을 가리킬 뿐).
3. 기존 `TrySend(ReadOnlySpan<byte>)`와 `TrySend(ReadOnlyMemory<byte>)`의 non-array 폴백을 내부적으로 `TrySendCopied`로 라우팅 (동작 동일, 할당만 풀로 대체 — public 시그니처 불변).
4. `AppSession`에 대응 API 노출: `public virtual bool TrySendCopied(ReadOnlySpan<byte> data)` (+ `Send`형 blocking 변형은 InternalSend 패턴 재사용).
5. 문서화: 기존 `Send(byte[])`는 zero-copy이며 전송 완료까지 버퍼를 수정하면 안 된다는 주의를 XML doc과 `.claude/cautions.md`에 명시.

**검증**

- 회귀 테스트: TrySendCopied 후 원본 버퍼를 즉시 오염시키고 수신 데이터가 원본과 일치하는지 (에코 왕복).
- ArrayPool 반환 검증: 반복 송신 후 메모리 증가 없는지 LoadTest로 확인.

---

## TODO-09: `ValueTask` 기반 비동기 송신 API + 백프레셔 — ✅ 완료 (2026-08-12)

**문제**

송신 큐가 가득 찼을 때 현재 선택지는 ① `TrySend` false 반환(호출자가 알아서), ② `Send`의 **SpinWait 블로킹**(`AppSession.cs:373-387` — 스레드를 태우면서 대기)뿐이다. async/await 기반 게임 서버 코드에서 자연스럽게 쓸 수 있는 비동기 대기가 없다.

**구현 방법**

1. `ChannelSendingQueue`에 비동기 enqueue 추가 (Channel이라서 공짜):
   ```csharp
   public async ValueTask<bool> EnqueueAsync(SendItem item, CancellationToken ct)
   {
       while (await m_Channel.Writer.WaitToWriteAsync(ct).ConfigureAwait(false))
       {
           if (m_Channel.Writer.TryWrite(item))
           {
               Interlocked.Increment(ref m_Count);
               return true;
           }
       }
       return false;   // 채널 Complete됨 (세션 종료)
   }
   ```
2. `SocketSession`에:
   ```csharp
   public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
   {
       if (IsClosed) return false;
       if (TrySend(data)) return true;              // 빠른 경로 (동기 성공)
       // 느린 경로: 큐 공간을 비동기 대기
       var item = CreateSendItem(data);             // array-backed면 그대로, 아니면 pooled copy
       if (!await m_SendQueue.EnqueueAsync(item, ct).ConfigureAwait(false)) return false;
       StartSend(true);
       return true;
   }
   ```
3. `AppSession`에 `public virtual ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)` 노출. `Connected` 체크 포함.
4. 타임아웃은 호출자가 `CancellationTokenSource(TimeSpan)`으로 제어 — `SendTimeOut` 설정과 무관하게 명시적. XML doc에 명시.
5. `ISocketSession` 인터페이스에는 추가하지 않는다(인터페이스 변경 회피). `AppSession → SocketSession` 구체 타입 경유가 안 되면 internal 캐스팅 헬퍼 사용 (`SocketSession`은 internal 클래스이므로 `AppSession`에서 `SocketSession as ISocketSession` 대신 새 internal 인터페이스 `IAsyncSendSupport`를 SocketEngine에 추가하는 방법도 가능 — 구현 시 접근성 확인 필요. `ISocketSession`은 public interface이므로 **default implementation**으로 추가하는 것이 하위 호환 유지에 가장 깔끔).

**검증**

- 회귀 테스트: 큐 크기 1로 설정 → 첫 Send로 채운 뒤 SendAsync가 대기하다가 drain 후 완료되는지; 세션 Close 시 대기 중 SendAsync가 false로 풀리는지; CancellationToken 취소 시 OperationCanceledException.

---

## TODO-10: Graceful Shutdown — `StopAsync(TimeSpan drainTimeout)` — ✅ 완료 (2026-08-12)

**문제**

`AppServer.Stop()`(`SocketBase/AppServer.cs:399-435`)은 타이머 정리 후 모든 세션을 `CloseReason.ServerShutdown`으로 즉시 끊는다. 송신 큐에 남은 데이터(예: "서버 점검" 공지 패킷)가 전송되지 못하고 유실될 수 있다.

**구현 방법**

1. `AppServerBase`에 추가:
   ```csharp
   public async Task StopAsync(TimeSpan drainTimeout)
   {
       // 1) 리스너만 먼저 중단 — 새 접속 차단 (SocketServerBase에 StopListeners() 분리 필요)
       // 2) 모든 세션의 송신 큐가 빌 때까지 폴링 대기 (50ms 간격, drainTimeout 상한)
       //    조건: session.SocketSession의 send queue Count == 0 && !InSending
       // 3) 기존 Stop() 호출 (세션 강제 종료 포함)
   }
   ```
2. 세부 구현:
   - `SocketServerBase`에 `internal void StopListeners()`를 분리 (`Stop()`은 이를 재사용).
   - 송신 drain 판정을 위해 `ISocketSession`에 `bool IsSendIdle { get; }` default 프로퍼티 추가 (`m_SendQueue.Count == 0 && !CheckState(InSending)`).
   - drain 대기 중에도 수신은 계속 처리된다(정상 — 마지막 요청에 대한 응답 송신을 허용). 수신도 차단하고 싶으면 후속 옵션으로.
   - 상태 전이: `ServerState.Stopping`을 drain 시작 시점에 설정해 재진입 방지. 기존 `Stop()`과의 동시 호출은 기존 CAS(`m_StateCode`)로 방어되는지 확인하고, `StopAsync`도 같은 CAS를 통과하도록 한다.
3. `Start(CancellationToken)`의 등록 콜백은 기존대로 `Stop()` 호출 유지 (급정지 시맨틱).

**검증**

- 회귀 테스트: 큐에 대용량 데이터 enqueue 직후 StopAsync 호출 → 클라이언트가 전체 데이터를 수신하는지; drainTimeout 초과 시 강제 종료되는지.

---

## TODO-11: 나머지 ReceiveFilter의 `ISequenceReceiveFilter` 구현 — ✅ 완료 (2026-08-12, Http 제외)

**문제**

현재 zero-copy sequence 경로를 타는 필터는 `FixedSizeReceiveFilter`(+ 이를 상속하는 `FixedHeaderReceiveFilter`)와 `FixedHeaderSequenceReceiveFilter`뿐이다. `TerminatorReceiveFilter`(채팅/텍스트 프로토콜), `CountSpliterReceiveFilter`, `BeginEndMarkReceiveFilter`, `HttpReceiveFilterBase`는 `AppSession.ProcessRequest`의 **carry-buffer 복사 경로**로 떨어져 수신 바이트 전체가 매번 `_filterBuffer`로 복사된다 (`AppSession.cs:631-717`).

**구현 방법**

1. 우선순위: `TerminatorReceiveFilter` → `BeginEndMarkReceiveFilter` → `CountSpliterReceiveFilter` (Http는 사용 빈도 낮으면 보류).
2. 각 필터에 `ISequenceReceiveFilter<TRequestInfo>.Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)`를 구현한다. 구현 패턴은 `FixedHeaderSequenceReceiveFilter.cs:44-84` 참고:
   - `SequenceReader<byte>` 또는 `buffer.PositionOf((byte)b)`로 종결자/마크 탐색.
   - 완전한 요청 발견 시: `consumed = 요청 끝`, `examined = consumed`, RequestInfo 반환.
   - 불완전: `consumed = buffer.Start`, `examined = buffer.End` (PipeReader가 데이터를 유지하고 추가 수신을 기다림) — 이 경우 `LeftBufferSize`를 `buffer.Length`로 갱신해 MaxRequestLength 검사가 동작하게 한다.
   - 다중 바이트 종결자("\r\n")가 세그먼트 경계에 걸리는 경우 처리 주의 — `SequenceReader.TryReadTo(out ReadOnlySequence<byte> result, ReadOnlySpan<byte> delimiter)`가 이를 처리해 준다.
3. 기존 byte[] `Filter`는 그대로 유지 (UDP 경로와 하위 호환용).
4. `AppSession.ProcessRequest(ReadOnlySequence)`의 분기 조건(`AppSession.cs:633`)은 수정 불필요 — 필터가 인터페이스를 구현하는 순간 자동으로 sequence 경로를 탄다. 단, **`RawDataReceived` 핸들러가 등록되면 sequence 경로가 비활성화**된다는 사실을 XML doc에 명시.

**검증**

- 회귀 테스트: 종결자가 세그먼트 경계에 걸리도록 다중 세그먼트 `ReadOnlySequence`를 만들어 파싱 확인 (기존 `FixedHeaderSequenceReceiveFilterParsesMultiSegmentRequest` 테스트 패턴 재사용).
- EchoServer 튜토리얼(커맨드라인 프로토콜)로 실동작 확인.

---

## TODO-12: 메트릭 확장 (구 TASK-06 마무리) — ✅ 완료 (2026-08-12)

**현재**: total-requests, total-bytes-received/sent(수신만 실동작, TODO-06 참고), active-connections.

**추가할 것** (`AppServerBase.cs`의 `s_Meter`에):

| 계측기 | 타입 | 기록 지점 |
|---|---|---|
| `sessions-rejected` | Counter<long> | `AsyncSocketServer.ProcessNewClient`의 풀 고갈 분기 (2곳) |
| `send-queue-full` | Counter<long> | `SocketSession.TrySend`가 enqueue 실패로 false 반환할 때 |
| `request-duration` | Histogram<double> (ms) | `ExecuteCommand`에서 `Stopwatch.GetTimestamp()` 전후 측정 |
| `session-count` | ObservableGauge<int> | `SessionCount` 콜백 등록 (AppServer<T> 레벨) |
| `send-errors` | Counter<long> | `OnSendError` |

**구현 방법**

- `AsyncSocketServer`/`SocketSession`(SocketEngine)에서 기록해야 하는 항목은 `IAppServer`를 통해 접근할 수 없으므로, `AppServerBase`에 `public void RecordSessionRejected()`, `public void RecordSendQueueFull()`, `public void RecordSendError()` 메서드를 추가하고 (기존 `RecordBytesReceived` 패턴과 동일) `AppSession.AppServer` 경유로 호출한다. `IAppServer` 인터페이스에는 default 구현 없는 멤버 추가 금지 — 필요하면 `IAppServer`에 default no-op으로 추가.
- `request-duration`은 태그 없이 시작 (서버 이름 태그만). 커맨드 키별 태그는 카디널리티 폭발 위험이 있어 옵션(`Config.DetailedMetrics`)으로.
- `Stopwatch.GetTimestamp()` + `Stopwatch.GetElapsedTime(start)` 사용 (할당 없음).

**검증**: TestServer 기동 → `dotnet-counters monitor --counters SuperSocketLite` → 각 시나리오(정상 요청, 큐 가득, 최대 접속 초과)에서 카운터 증가 확인. LoadTest ServerProbe가 새 카운터를 수집하도록 `ServerMetricsOptions` 갱신.

---

## TODO-13: 수신 Pipe 백프레셔 임계값 설정 노출 — ✅ 완료 (2026-08-12)

**문제**

`SocketSession.Initialize()`(`SocketSession.cs:155-160`)의 `PipeOptions`가 `pauseWriterThreshold`를 지정하지 않아 기본값(64KB)이 쓰인다. 느린 소비자(무거운 커맨드 처리) 상황에서 세션당 최대 64KB가 버퍼링된 후 수신이 멈추는데, 이 값을 게임 특성에 맞게 조정할 수 없다.

**구현 방법**

1. `ServerConfig`에 `public int MaxReceivePipeBufferSize { get; set; } = 65536;` 추가 (0 이하 = Pipe 기본값).
2. `Initialize()`에서:
   ```csharp
   var pipeOptions = new PipeOptions(
       minimumSegmentSize: Config.ReceiveBufferSize,
       pauseWriterThreshold: maxBuf,
       resumeWriterThreshold: maxBuf / 2,
       useSynchronizationContext: false);
   ```
   `IServerConfig` 인터페이스에는 추가하지 않고 `Config as ServerConfig`로 읽는다 (TODO-01과 동일한 호환 전략).
3. 제약 검증: `pauseWriterThreshold >= minimumSegmentSize`가 아니면 Pipe가 던지므로 `Math.Max(maxBuf, Config.ReceiveBufferSize * 2)`로 보정.

**검증**: 회귀 테스트에서 소비를 멈춘 세션에 임계값 이상 데이터를 보내고 `FlushAsync`가 pending되는지(수신 중단) 확인.

---

# P3 — UDP·정리·테스트

## TODO-14: UDP 송신 SAEA 재사용 — ✅ 완료 (2026-08-12)

**문제** (`UdpSocketSession.cs:47-113`)

송신할 때마다, 그리고 **큐의 세그먼트마다** `new SocketAsyncEventArgs()` + `Completed` 구독 + `Dispose`를 반복한다. UDP 게임 서버(고빈도 소규모 패킷)에서 할당 폭탄.

**구현 방법**

- 세션당 송신 SAEA 1개를 보유·재사용한다 (`InSending` single-flight 불변식 덕에 동시 사용 없음). `Completed` 구독은 최초 1회. `UdpSendState.Position` 진행 로직은 유지하되 SAEA를 새로 만들지 않고 `SetBuffer`만 갱신.
- 세션 종료(`OnClosed` override)에서 Dispose.
- 대안(더 현대적): `m_ServerSocket.SendToAsync(ReadOnlyMemory<byte>, SocketFlags, EndPoint, ct)` ValueTask API로 전환하고 SAEA 자체를 제거. 다만 송신 완료 → `OnSendingCompleted(queue)` 연결을 async 루프로 재구성해야 하므로 공수는 비슷. **권장: SAEA 재사용(변경 범위 최소)**.

**검증**: LoadTest UdpEcho 시나리오 before/after GC 카운트 비교.

## TODO-15: UDP 수신 경로 개선 — ✅ 완료 (2026-08-12)

**동작 변경**: sessionID 파싱용 필터가 **수신 스레드당 1개 재사용**된다(`Reset()` 후 재사용).
`UdpRequestInfo` 필터는 데이터그램 간 상태를 갖지 않아야 하고 `CreateFilter`에 넘어온 remote
endpoint를 캡처해서는 안 된다 — 이 규약을 `cautions.md`에 명시했다.
outstanding ReceiveFrom은 `min(ProcessorCount, 8)`개로 늘렸고, 데이터그램 처리는
`Task.Run` 대신 인라인 호출로 바꿔 패킷당 Task+클로저 할당을 없앴다.

**문제**

1. `UdpSocketServer.ProcessPackageWithSessionID`(`UdpSocketServer.cs:109`)가 **데이터그램마다** `m_ReceiveFilterFactory.CreateFilter(...)`로 필터를 새로 만든다.
2. `SocketListenerBase.OnNewClientAcceptedAsync`(`SocketListenerBase.cs:58-66`)가 데이터그램마다 `Task.Run` + 클로저.
3. `UdpSocketListener`는 outstanding ReceiveFrom이 1개라 수신 병렬성이 없다.

**구현 방법**

1. 필터 캐시: sessionID 파싱용 필터는 상태가 패킷 단위로 초기화된다는 전제 하에 `[ThreadStatic]` 캐시 또는 서버당 1개 + lock-free 재사용. 필터 구현이 stateful일 수 있으므로 **`Reset()` 호출 후 재사용** 규약으로. (UdpRequestInfo 필터는 관례상 stateless — 문서에 규약 명시.)
2. `OnNewClientAcceptedAsync` → UDP 경로에서는 동기 `OnNewClientAccepted` 호출로 교체하고, 무거운 처리(세션 생성)는 유지. TODO-03의 루프 전환과 함께 하면 수신 루프가 처리 완료까지 다음 수신을 못 거는 문제가 생기므로, **처리를 인라인화하는 대신 outstanding SAEA를 N개(예: `Environment.ProcessorCount`)로 늘려** 병렬성을 확보한다 (3번과 동시 해결).
3. N개 SAEA 각각 독립 수신 루프 (TODO-03의 UDP 루프 패턴 적용). `ArrayPool` rent/return은 현행 유지.

**검증**: LoadTest UdpEcho 처리량 비교. 순서 민감 로직이 없는지 확인 (UDP는 원래 무순서).

## TODO-16: 세션 이벤트 순서 보장 옵션 — ✅ 완료 (2026-08-12)

**문제**

`NewSessionConnected`가 `Task.Run`으로 발화(`AppServerBase.cs:941`)되므로, 접속 직후 첫 패킷의 `NewRequestReceived`(수신 파이프 태스크에서 동기 발화)가 **connected 핸들러보다 먼저** 실행될 수 있다 (cautions.md에 이미 경고된 레이스). 앱 코드가 매번 방어해야 한다.

**구현 방법**

- `ServerConfig.SyncSessionConnectedEvent { get; set; } = false` (기본값: 기존 동작 유지).
- true면 `OnNewSessionConnected`에서 핸들러를 동기 호출. 호출 지점인 `IAppServer.RegisterSession`은 `AsyncSocketServer.ProcessNewClient → RegisterSession`에서 실행되고, 수신 시작(`socketSession.Start()`)은 그 **뒤에** `AsyncRun`으로 스케줄되므로, 동기 호출 시 "connected 완료 → 수신 시작" 순서가 구조적으로 보장된다.
- `SessionClosed`는 현행 유지 (순서 민감도 낮음).
- XML doc과 cautions.md 갱신: 이 옵션이 켜져 있으면 핸들러가 accept 경로를 블로킹하므로 핸들러를 가볍게 유지하라는 주의 포함.

**검증**: 회귀 테스트 — 접속 즉시 패킷을 보내는 클라이언트로 이벤트 호출 순서 기록·검증 (옵션 on/off 각각).

## TODO-17: 회귀 테스트 확충 — ✅ 완료 (2026-08-12, 10개 → 31개)

현재 10개 테스트(`Test/SuperSocketLiteRegressionTests/Program.cs`)에 추가할 것:

1. **상태머신**: InSending 중 Close → 송신 완료 후 Closed 발화; 송신 에러 시 InSending 해제 (TODO-02 검증); 동시 Close 호출 멱등성.
2. **ChannelSendingQueue 동시성**: N 스레드 enqueue + drain 스트레스, Complete 후 enqueue 거부 (TODO-05 검증).
3. **KeepAlive 옵션 적용** (TODO-01 검증).
4. **실소켓 루프백 에코**: 서버 기동 → TCP 접속 → 고정 헤더 패킷 왕복 → 정상 종료까지의 스모크 테스트 (현재 회귀 테스트에는 실소켓 테스트가 없음). 포트는 0으로 바인드 후 실제 포트 조회.
5. TODO-08/09/10 각 항목의 검증 시나리오.

장기적으로는 xUnit 프로젝트로 전환을 검토하되, 당장은 기존 커스텀 러너 형식 유지 (일관성).

## TODO-18: 소소한 정리 — ✅ 완료 (2026-08-12)

1. **`lock (this)` 제거**: `SocketSession.ValidateClosed`(`SocketSession.cs:541`)가 `lock (this)`를 사용 — 이미 존재하는 `SyncRoot` 필드로 교체 (외부 코드가 세션 객체를 lock하면 데드락 가능한 안티패턴).
2. **CollectSend 스냅샷 ArrayPool화**: `AppServer.CollectSendSession`(`AppServer.cs:253-255`)의 `new byte[]` 스냅샷을 `ArrayPool<byte>.Shared` rent/return으로. TODO-08의 pooled send(`TrySendCopied`)가 생기면 그걸 직접 호출하는 것이 최선 (복사 1회 제거).
3. **Linger 주석 수정**: `TcpSocketServerBase.cs:64`의 `LingerOption(false, 0)` 주석 "즉시 제거한다"는 부정확 — enable:false는 기본 graceful close다. RST 종료를 원하면 `(true, 0)`. 의도를 확인하고 주석 또는 코드를 정정.
4. **`ReuseLockBaseBuffer.Commit` 단순화**: `ReuseLockBaseBuffer.cs:84-94`의 temp 배열 경유 이중 BlockCopy는 불필요 — `Buffer.BlockCopy`는 overlap-safe이므로 직접 한 번에 복사. 파일 상단 "TODO 유닛테스트 필요" 해소 (경계 조건 테스트 추가).
5. **`.claude/tasks.md` 갱신**: TASK-04 완료 표시(코드에 구현됨), TASK-06/07 부분 완료 표시, 이 TODO.md를 참조하도록 정리.
6. **`AsyncSocketSession.SendAsync`의 Buffer/BufferList 전환 방어**: `SendAsync`에서 이전 상태가 확실히 청소되었다는 전제를 Debug.Assert로 명시 (`sae.Buffer == null && sae.BufferList == null`). SAEA는 Buffer와 BufferList를 동시에 설정하면 던진다.

---

# 구현 순서 제안 (다음 세션)

| 순서 | 항목 | 예상 규모 | 의존성 |
|---|---|---|---|
| 1 | TODO-01 (KeepAlive) | 소 | 없음 |
| 2 | TODO-02 (SendSync 플래그 누수) | 극소 | 없음 |
| 3 | TODO-03 (재귀 → 루프) | 중 | 없음 |
| 4 | TODO-04 (수신 인라인화) | 소 | TODO-03 선행 필수 |
| 5 | TODO-06 (LINQ 제거 + bytes-sent 메트릭) | 극소 | 없음 |
| 6 | TODO-05 (송신 큐 개선) | 중 | 없음 (TODO-08과 SendItem 설계 공유) |
| 7 | TODO-07 (DateTime) | 소 | 없음 |
| 8 | TODO-08 (pooled send) | 중 | TODO-05 |
| 9 | TODO-09 (SendAsync) | 중 | TODO-05, TODO-08 |
| 10 | TODO-12 (메트릭 확장) | 소 | 없음 |
| 11 | TODO-10 (graceful shutdown) | 중 | 없음 |
| 12 | TODO-11 (sequence 필터) | 중 | 없음 |
| 13 | TODO-13 ~ TODO-18 | 각 소~중 | 표기된 대로 |

각 항목 완료 시마다: 빌드 → 회귀 테스트 → (핫패스 변경이면) LoadTest 비교 → 커밋.
