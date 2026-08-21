# SuperSocketLite2 — 에이전트용 문서

**[🇬🇧 English](README.md)**

AI 코딩 에이전트가 **SuperSocketLite2로 서버를 만들 때** 읽는 문서다.
사람이 읽는 문서는 `Docs/*.html` 쪽이고, 이 디렉토리는 같은 내용을 에이전트가
토큰 낭비 없이 읽을 수 있는 마크다운으로 옮겨 둔 것이다.

> `Docs/Library_Architecture.html` 같은 단독 실행 HTML은 파일 하나가 650KB다.
> 에이전트가 그걸 열면 컨텍스트가 통째로 날아가므로 **절대 열지 말고 여기를 읽는다.**

## 어디부터 읽나

| 상황 | 문서 |
|---|---|
| 서버를 처음부터 만든다 | [recipes_kr.md](recipes_kr.md) § 1 최소 서버 |
| 타입 이름 / 네임스페이스 / 시그니처가 필요하다 | [api-cheatsheet_kr.md](api-cheatsheet_kr.md) |
| 패킷 프로토콜을 정의한다 | [recipes_kr.md](recipes_kr.md) § 2 ReceiveFilter |
| 코드를 쓰기 전에 지뢰를 확인한다 | **[cautions_kr.md](cautions_kr.md) — 필수** |
| 만든 서버가 실제로 도는지 확인한다 | [verify_kr.md](verify_kr.md) |
| 빌드에 SSL0xx 경고가 떴다 | [analyzers_kr.md](analyzers_kr.md) |

## 최소한 이것만은

에이전트가 이 라이브러리에서 가장 많이 틀리는 세 가지다. 코드를 쓰기 전에 확인한다.

1. **`NewRequestReceived` 핸들러가 리턴하면 `RequestInfo`와 `Body`는 무효다.**
   필드에 저장·람다 캡처·다른 스레드 큐에 넣기 전부 금지. 남기려면 핸들러 안에서 복사한다.
2. **`Send(byte[], ...)`는 zero-copy다.** 넘긴 배열을 전송 완료 전에 수정하면 깨진 데이터가 나간다.
   버퍼를 바로 재사용해야 하면 `SendCopied`를 쓴다.
3. **`header`/`body`는 `ReadOnlySequence<byte>`라 세그먼트 여러 개에 걸칠 수 있다.**
   `header.First.Span`으로 바로 읽지 말고 `CopyTo(Span)`를 쓴다.

셋 다 **컴파일은 되고 가벼운 부하에서는 잘 돈다.** 부하가 올라야 깨지므로 리뷰에서 잡아야 한다.
전체 목록은 [cautions_kr.md](cautions_kr.md)에 있다.

세 가지 모두 `SuperSocketLite2` 패키지에 들어 있는 애널라이저가 빌드 경고로 잡는다
(`SSL001`~`SSL007`, [analyzers_kr.md](analyzers_kr.md)). **경고를 끄지 말고 고친다.**

## 이 문서와 HTML 문서의 관계

| 이 문서 | 대응하는 사람용 문서 |
|---|---|
| [cautions_kr.md](cautions_kr.md) | `Docs/Cautions.html` / `Docs/Cautions_kr.html`, `Docs/Getting_Started*.html` 7장 |
| [api-cheatsheet_kr.md](api-cheatsheet_kr.md) | `Docs/Getting_Started*.html`, `README.md` |
| [recipes_kr.md](recipes_kr.md) | `Tutorials/`, `Template/` |

**코드가 바뀌어 주의 사항이 달라지면 여섯 곳을 같이 고친다** — `cautions.md`, `cautions_kr.md`, `Cautions.html`,
`Cautions_kr.html`, `Getting_Started*.html` 7장. 스킬(`.claude/skills/supersocketlite2/`)은
이 문서를 참조만 하므로 따로 고칠 필요 없다.
