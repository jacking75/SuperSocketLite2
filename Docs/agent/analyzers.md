# 애널라이저 규칙 (SSL001 ~ SSL007)

`SuperSocketLite2` NuGet 패키지에는 Roslyn 애널라이저가 함께 들어 있다.
**패키지를 참조하면 자동으로 켜진다.** 따로 설치하거나 설정할 것이 없다.

```bash
dotnet add package SuperSocketLite2   # 애널라이저도 같이 온다
```

문서에 백 번 적는 것보다 빌드 경고 하나가 낫다는 판단으로 만들었다.
여기 있는 규칙은 전부 `cautions.md`의 항목 중 **컴파일은 되지만 부하가 걸려야 터지는** 것들이다.

## 규칙

| ID | 무엇을 잡나 | 대응 |
|---|---|---|
| **SSL001** | `RequestInfo`나 `Body`를 필드/프로퍼티에 저장 | 핸들러 안에서 역직렬화하거나 `ArrayPool`로 복사 |
| **SSL002** | `RequestInfo`를 람다·지역 함수에 캡처 | 필요한 값만 꺼내서 캡처 |
| **SSL003** | `ArrayPool` 대여 버퍼를 `Send`/`TrySend`로 전송 | `SendCopied` / `TrySendCopied` |
| **SSL004** | `ReadOnlySequence`의 `First.Span` / `FirstSpan`으로 읽기 | `CopyTo(Span)` 또는 `SequenceReader` |
| **SSL005** | `RequestInfo`를 받는 `async` 메서드 | 핸들러는 동기로 끝내고, 값을 복사해 넘긴다 |
| **SSL006** | `Setup()` / `Start()` 반환값 무시 | `if (!server.Setup(...)) { ... }` |
| **SSL007** | `GetAllSessions()` / `GetSessions()` 결과를 null 검사 없이 사용 | 지역 변수에 받아 `null` 확인 |

전부 기본 심각도가 **Warning**이다. 자세한 배경은 [cautions.md](cautions.md).

## 잡히지 않는 것

오탐을 내지 않는 것을 우선했다. 아래는 **의도적으로** 통과시킨다.

- `_lastPacketId = request.PacketId;` — 값 타입 멤버는 복사되므로 저장해도 안전하다
- `if (header.IsSingleSegment) { ... header.First.Span ... }` — 확인하고 들어간 최적화
- `PacketWriter.Send(session, id, request.Body)` — 인자로 넘기는 것 자체는 문제가 아니다.
  받는 쪽이 핸들러가 리턴하기 전에 다 쓰면 된다

반대로 애널라이저가 잡지 못하는 위반도 있다. 예를 들어 요청 본문을 다른 객체의 메서드에
넘긴 뒤 그 객체가 나중에 읽는 경우는 지역 분석으로 알 수 없다.
**애널라이저는 안전망이지 검사기가 아니다.** [cautions.md](cautions.md)의 리뷰 체크리스트를 같이 쓴다.

## 규칙 하나를 끄고 싶다면

`.editorconfig`로 프로젝트 전체를 조정한다.

```ini
[*.cs]
# 심각도를 오류로 올린다 — 게임 서버라면 SSL001 · SSL003 은 오류로 두는 편이 낫다
dotnet_diagnostic.SSL001.severity = error
dotnet_diagnostic.SSL003.severity = error

# 특정 규칙을 끈다
dotnet_diagnostic.SSL004.severity = none
```

한 줄만 예외로 두려면 `#pragma`를 쓰고 **왜 안전한지 주석을 남긴다.**

```csharp
#pragma warning disable SSL004 // 이 필터는 헤더 2바이트만 읽고, 호출부가 IsSingleSegment를 보장한다
var length = BinaryPrimitives.ReadInt16LittleEndian(header.First.Span);
#pragma warning restore SSL004
```

## 소스와 테스트

- 구현: `Analyzers/SuperSocketLite.Analyzers/`
- 규칙 목록: `Analyzers/SuperSocketLite.Analyzers/AnalyzerReleases.Unshipped.md`
- 패키지에 싣는 부분: `SuperSocketLite/SuperSocketLite.csproj`의 `IncludeAnalyzerInPackage` 타깃

규칙을 추가하면 `Descriptors.cs`, 애널라이저 클래스, `AnalyzerReleases.Unshipped.md`,
그리고 이 문서를 같이 고친다.
