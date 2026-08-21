; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SSL001 | SuperSocketLite.Lifetime | Warning | RequestInfo 를 필드/프로퍼티에 저장한다
SSL002 | SuperSocketLite.Lifetime | Warning | RequestInfo 를 람다에 캡처한다
SSL003 | SuperSocketLite.Lifetime | Warning | ArrayPool 대여 버퍼를 zero-copy Send 로 보낸다
SSL004 | SuperSocketLite.Usage | Warning | ReadOnlySequence 를 단일 세그먼트로 가정한다
SSL005 | SuperSocketLite.Lifetime | Warning | 요청 핸들러가 async 다
SSL006 | SuperSocketLite.Usage | Warning | Setup / Start 반환값을 확인하지 않는다
SSL007 | SuperSocketLite.Usage | Warning | GetAllSessions / GetSessions 결과의 null 을 확인하지 않는다
