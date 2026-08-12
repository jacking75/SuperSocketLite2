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
  

## 수신 필터 zero-copy 경로
`RawDataReceived` 핸들러를 등록하면 sequence(zero-copy) 경로가 **비활성화**되고 모든 수신 데이터가
세션 캐리 버퍼로 복사된다. 성능이 중요하면 `RawDataReceived`를 쓰지 않는다.

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
  

## CollectSend 사용 시
`Config.CollectSendIntervalMillSec > 0`이면 `SyncSend = true`가 강제 설정된다.
사용 순서: `CollectSend()` → `GetCollectSendData()` → `CommitCollectSend()`
  

## UDP 모드
UDP 세션은 `m_Client(Socket)`가 null일 수 있다 (소켓 인스턴스 공유 구조).
`Close()` 내부에서 UDP/TCP 경로가 분기되므로 UDP 관련 코드 수정 시 주의한다.
  

## public API 변경 금지
기존 `public` 메서드 시그니처는 변경하지 않는다.
새 기능은 오버로드 또는 옵셔널 파라미터로 추가한다.