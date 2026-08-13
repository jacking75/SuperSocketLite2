# SuperSocketLite2
  
SuperSocketLite의 업그레드 버전이다.  
.NET 플랫폼을 지원한다.
SuperSocketLite2은 게임 서버 개발에 사용하는 것을 주 용도로 예상하고 있지만, 일반적인 Socket 서버 개발에도 사용할 수 있다.      
  
SuperSocketLite2는 고성능, 안정성, 사용 편이를 목표로 한다.

## 0.91 마이그레이션 가이드 (수신 필터 · Setup)

0.91에서 수신 필터의 `byte[]` 경로를 없애고 `ReadOnlySequence<byte>` 하나로 통일했다.
**직접 만든 `ReceiveFilter`가 있으면 시그니처를 바꿔야 한다.**

### 1. `FixedHeaderReceiveFilter<T>`

`FixedHeaderSequenceReceiveFilter<T>`를 쓰고 있었다면 **이름만** 바꾸면 된다.

```csharp
// before
public class ReceiveFilter : FixedHeaderSequenceReceiveFilter<MyRequestInfo>
// after
public class ReceiveFilter : FixedHeaderReceiveFilter<MyRequestInfo>
```

옛 `byte[]` 기반 `FixedHeaderReceiveFilter<T>`를 쓰고 있었다면 메서드 2개를 옮긴다.

```csharp
// before
protected override int GetBodyLengthFromHeader(byte[] header, int offset, int length)
{
    return BitConverter.ToInt16(header, offset) - HeaderSize;
}

protected override MyRequestInfo ResolveRequestInfo(
    ArraySegment<byte> header, byte[] buffer, int offset, int length)
{
    return new MyRequestInfo(
        BitConverter.ToInt16(header.Array, 0),
        buffer.CloneRange(offset, length));
}

// after
protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
{
    Span<byte> buf = stackalloc byte[HeaderSize];
    header.CopyTo(buf);                                   // 세그먼트 경계에 걸려도 안전
    return BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(0, 2)) - HeaderSize;
}

protected override MyRequestInfo ResolveRequestInfo(
    ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
{
    Span<byte> buf = stackalloc byte[HeaderSize];
    header.CopyTo(buf);

    return new MyRequestInfo(
        BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(0, 2)),
        body.ToArray());
}
```

- `offset` / `length` / `toBeCopied` 인자는 전부 사라졌다. 요청 경계는 라이브러리가 잘라 준다.
- `header` / `body`는 세그먼트 여러 개에 걸쳐 있을 수 있다. `header.First.Span`으로 바로 읽지 말고
  `CopyTo(Span)` 또는 `ToArray()`를 쓴다.
- 바디가 없으면 `body`는 빈 시퀀스다(널이 아니다).
- 헤더와 바디가 붙은 원본 바이트열이 필요하면(MemoryPack 등) 두 시퀀스를 한 배열에 이어 붙인다.

### 2. `FixedSizeReceiveFilter<T>`

```csharp
// before
protected override MyRequestInfo ProcessMatchedRequest(byte[] buffer, int offset, int length, bool toBeCopied)
// after
protected override MyRequestInfo ProcessMatchedRequest(ReadOnlySequence<byte> buffer)
```

### 3. `IReceiveFilter<T>`를 직접 구현한 경우

```csharp
// before
TRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest);
int LeftBufferSize { get; }

// after
TRequestInfo? Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined);
```

| 반환 | consumed | examined |
|---|---|---|
| 요청 1개 완성 | 요청 끝 위치 | consumed와 동일 |
| 데이터 부족 | `buffer.Start` | `buffer.End` |

`LeftBufferSize`는 없어졌다. `MaxRequestLength` 판정은 라이브러리가 미소비 길이로 직접 한다.

### 4. `Setup` 인자

`Setup`에서 쓰지 않던 인자 2개(`socketServerFactory`, `connectionFilters`)가 빠졌다.
`logFactory:` 처럼 **명명 인자**로 호출하고 있었다면 고칠 것이 없다.

```csharp
Setup(new RootConfig(), config, logFactory: new ConsoleLogFactory());   // 그대로 동작
```

파생 클래스에서 `protected override bool Setup(IRootConfig, IServerConfig)` 훅을 재정의하고
있었다면 이름이 **`OnSetup`**으로 바뀌었다.

### 5. 없어진 기능

| 제거 | 대체 |
|---|---|
| `CollectSend` / `GetCollectSendData` / `CommitCollectSend`, `CollectSendIntervalMillSec` | 없음. 필요하면 앱에서 모아 `SendCopied` 한 번 호출 |
| `RawDataReceived` / `IRawDataProcessor<T>` | 없음. 필터에서 처리 |
| `IConnectionFilter` | 없음. `OnNewSessionConnected`에서 검사 후 `Close` |
| `ISocketServerFactory` 주입 | 없음. `SocketMode`에 따라 라이브러리가 고른다 |
| `AppSession.Items` / `PrevCommand` / `CurrentCommand`, `LogCommand` | 없음. 파생 세션 클래스에 직접 필드를 둔다 |
| 문자열 명령 프로토콜(`StringRequestInfo`, `TerminatorReceiveFilter`, `CountSpliterReceiveFilter`, `BeginEndMarkReceiveFilter`, 비제네릭 `AppServer`/`AppSession`) | 없음. `AppServer<TSession, TRequestInfo>`와 바이너리 필터를 쓴다 |

## VS Code에서 저장소 전체 분석

저장소 루트의 `SuperSocketLite2.slnx`에는 라이브러리, 템플릿, 테스트, 튜토리얼을 포함한
C# 프로젝트 33개가 등록되어 있다. VS Code에서 저장소 루트를 열었을 때 C# 확장이 이 통합
솔루션을 자동으로 선택하지 않으면, 열어 본 파일만 임시 `Canonical.csproj`로 분석한다.
이 상태에서는 같은 파일이나 .NET 기본 라이브러리의 정의는 F12로 이동되지만, 아직 로드되지
않은 다른 소스 파일의 정의는 이동되지 않을 수 있다.

저장소 전체를 분석하려면 `.vscode/settings.json`에 다음 작업 영역 설정을 둔다.

```json
{
  "dotnet.defaultSolution": "SuperSocketLite2.slnx"
}
```

설정 후 명령 팔레트에서 `개발자: 창 다시 로드`를 실행한다. `출력` 패널의 `C#` 로그에서
임시 `Canonical.csproj`가 아니라 `SuperSocketLite2.slnx`와 그 프로젝트들이 로드되는지
확인한다. `.vscode/`는 이 저장소의 `.gitignore` 대상이므로 이 설정은 각 개발 환경에서
로컬로 생성해야 한다.

자세한 원인과 확인 절차는 [VS Code 저장소 전체 분석 매뉴얼](Docs/VSCode_Repository_Analysis.html)에 있다.
