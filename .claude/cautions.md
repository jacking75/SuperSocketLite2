# 알려진 주의 사항
  
## 스레드 안전성
`NewSessionConnected`와 `NewRequestReceived`는 서로 다른 스레드에서 **동시에** 호출될 수 있다.
클라이언트가 접속 직후 바로 패킷을 보내는 경우 레이스 컨디션에 주의한다.
  

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