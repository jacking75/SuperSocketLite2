# 코딩 컨벤션

전체 규칙 원문: `Tutorials/coding_rule.md`

> **아래 규칙 중 상당수는 저장소 루트 `.editorconfig`로 빌드가 강제한다.**
> `SuperSocketLite.csproj`에 `EnforceCodeStyleInBuild=true`가 켜져 있어
> 네이밍 위반(`_camelCase` / `s_` / const PascalCase)과 사용하지 않는 `using`,
> `new()` 미단순화, 컬렉션 초기화 미단순화가 **빌드 경고**로 잡힌다.
> 라이브러리 빌드는 경고 0을 유지하므로 규칙을 어기면 바로 드러난다.

---

## 필수 네이밍 규칙

빌드가 강제하는 것은 **⚙** 로 표시했다. 나머지는 합의일 뿐이라 어겨도 빌드는 통과한다.

| 대상 | 규칙 | 예시 |
|---|---|---|
| 클래스/구조체 | PascalCase | `AsyncSocketServer` |
| Private/internal 인스턴스 필드 ⚙ | `_camelCase` | `private int _count;` |
| Private/internal static 필드 ⚙ | `s_` 접두사 | `static int s_total;` |
| Private/internal const ⚙ | PascalCase | `const int DefaultSize = 5;` |
| 지역 변수/매개변수 | camelCase | `int bufferSize` |
| 정수 타입 | C# 키워드를 쓴다 | `int`, `short`, `long` |
| 인터페이스 | `I-` 접두사 | `IReceiveFilter` |
| 비동기 메서드 | `-Async` 접미사 | `SendAsync` |
| 추상 클래스 | `-Base` 접미사 | `AppServerBase` |
| 논리값 변수 | `is-`, `has-`, `can-` | `bool isConnected` |
| 컬렉션 변수 | 복수형 | `List<Session> sessions` |

`[ThreadStatic]` 필드에 따로 두는 접두사는 없다. static 규칙을 그대로 따른다
(`UdpSocketServer`의 `s_SessionIdFilter`).
  

## 필수 스타일 규칙

- **중괄호**: Allman 스타일 — 항상 새 줄에서 시작, 한 줄 조건문도 중괄호 필수
- **인덴트**: 스페이스 4칸
- **`private` 키워드**: 생략하지 않고 적는다. 기본값이라 없어도 되지만 라이브러리는 전부 명시한다
- **매직 넘버**: 사용 금지 → 상수/열거형으로 대체
- **`nameof(...)`**: 하드코딩 문자열 대신 사용
  
  
## 권고 규칙

- 메서드 길이: 최대 100줄, 이상적으로 30줄 이내
- 예외를 흐름 제어에 사용 금지 (`TryParse` 등 패턴 활용)
- `Dispose()` 후 null 설정
- 로컬 변수는 사용 지점 근처에서 선언