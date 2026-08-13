# 알려진 주의 사항
  
## 스레드 안전성
`NewSessionConnected`와 `NewRequestReceived`는 서로 다른 스레드에서 **동시에** 호출될 수 있다.
클라이언트가 접속 직후 바로 패킷을 보내는 경우 레이스 컨디션에 주의한다.

`ServerConfig.SyncSessionConnectedEvent = true`로 두면 `NewSessionConnected`가 accept 경로에서
동기 호출되어 "connected → 첫 요청" 순서가 구조적으로 보장된다. 대신 핸들러가 accept를 블로킹하므로
반드시 가볍게 유지해야 한다. (기본값 false = 기존 동작)
  

## 송신 버퍼 수명 (zero-copy)
`Send(byte[], int, int)` / `Send(ArraySegment<byte>)` / `Send(IList<ArraySegment<byte>>)` /
`SendAsync(ReadOnlyMemory<byte>)`(배열 기반일 때)는 **호출자의 배열을 참조로 큐에 넣는다.**
전송이 완료되기 전에 그 배열을 수정하면 잘못된 데이터가 나간다.

```csharp
// 위험: buffer를 재사용하는 코드
session.Send(buffer, 0, len);
buffer[0] = 0;          // 아직 전송 중일 수 있다

// 안전: 세션이 풀 버퍼로 복사해 간다
session.SendCopied(buffer.AsSpan(0, len));
buffer[0] = 0;          // OK
```

`Send(IList<...>)`의 **리스트 자체**는 enqueue 시 복사되므로 호출 직후 재사용해도 된다
(배열만 공유된다).
  

## 수신 필터
필터는 파이프에서 `ReadOnlySequence<byte>`를 직접 받는다. 요청이 아직 완성되지 않았으면
`consumed`를 전진시키지 말고 그대로 두면 된다 — 데이터는 파이프에 남고 다음 수신 때 이어서 온다.
필터가 자체 캐리 버퍼를 두면 오히려 복사가 늘어난다.

`header` / `body`는 세그먼트 여러 개에 걸쳐 있을 수 있다. `header.First.Span`으로 바로 읽지 말고
`CopyTo(Span)`나 `ToArray()`를 쓴다.

UDP + `UdpRequestInfo` 조합에서 sessionID 파싱용 필터는 **수신 스레드당 1개가 재사용**된다
(`Reset()` 후 재사용). 이 필터는 데이터그램 간 상태를 갖지 않아야 하고
`CreateFilter`에 넘어온 remote endpoint를 캡처해서는 안 된다.
  

## 시간 값은 UTC
`AppSession.StartTime` / `LastActiveTime`, `AppServerBase.StartedTime`은 **UTC**다.
화면·로그에 로컬 시간으로 찍으려면 `.ToLocalTime()`을 붙인다.
`LastActiveTime`은 단조 tick에서 역산하므로 수 ms 오차가 있다.
  

## 타임아웃 처리 순서
`TimeoutException` 발생 후 세션을 종료할 때 반드시 아래 순서를 지킨다.

```csharp
// 올바른 순서
session.SendEndWhenSendingTimeOut();
session.Close();

// SendEndWhenSendingTimeOut() 없이 Close()만 호출하면 소켓이 정리되지 않는다.
```
  

## 최대 연결 수 초과
`MaxConnectionNumber` 초과 시 SuperSocketLite가 즉시 연결을 끊는다.
이 경우 `NewSessionConnected`가 **호출되지 않는다**.
  

## UDP 모드
UDP 세션은 `_client(Socket)`가 null일 수 있다 (소켓 인스턴스 공유 구조).
`Close()` 내부에서 UDP/TCP 경로가 분기되므로 UDP 관련 코드 수정 시 주의한다.
