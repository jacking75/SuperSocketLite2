using Microsoft.CodeAnalysis;

namespace SuperSocketLite.Analyzers;

/// <summary>
/// 진단 목록. 전부 "컴파일은 되지만 부하가 걸려야 터지는" 실수를 겨냥한다.
/// 자세한 설명은 저장소의 <c>Docs/agent/cautions.md</c> 에 있다.
/// </summary>
internal static class Descriptors
{
    private const string LifetimeCategory = "SuperSocketLite.Lifetime";
    private const string UsageCategory = "SuperSocketLite.Usage";

    private const string HelpUri = "https://github.com/jacking75/SuperSocketLite2/blob/main/Docs/agent/cautions.md";

    /// <summary>SSL001 — RequestInfo 를 필드/프로퍼티에 저장한다.</summary>
    public static readonly DiagnosticDescriptor RequestInfoStored = new(
        id: "SSL001",
        title: "RequestInfo 가 핸들러 밖으로 저장되고 있습니다",
        messageFormat: "'{0}' 은(는) 요청 핸들러가 리턴하면 무효가 됩니다. 필드나 프로퍼티에 저장하지 말고 핸들러 안에서 역직렬화하거나 복사하세요.",
        category: LifetimeCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "라이브러리는 요청 핸들러를 동기로 부르고, 리턴한 뒤에야 수신 파이프를 전진시킵니다. " +
            "필터가 요청 인스턴스 하나를 세션 내내 돌려 쓰기 때문에 핸들러가 리턴한 뒤의 RequestInfo 와 " +
            "그 본문은 다음 패킷의 내용으로 덮여 있을 수 있습니다. 컴파일도 되고 가벼운 부하에서는 " +
            "대개 동작하므로 찾기 매우 어렵습니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL002 — RequestInfo 를 람다에서 캡처한다.</summary>
    public static readonly DiagnosticDescriptor RequestInfoCaptured = new(
        id: "SSL002",
        title: "RequestInfo 가 람다에 캡처되고 있습니다",
        messageFormat: "'{0}' 이(가) 람다에 캡처되었습니다. 람다가 실행될 때는 이미 무효일 수 있으니 핸들러 안에서 필요한 값을 꺼내 캡처하세요.",
        category: LifetimeCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "요청 인스턴스는 세션마다 재사용됩니다. 람다가 나중에 실행되면 그 사이에 도착한 다른 패킷의 " +
            "내용을 읽게 됩니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL003 — ArrayPool 대여 버퍼를 zero-copy Send 로 보낸다.</summary>
    public static readonly DiagnosticDescriptor PooledBufferSentWithoutCopy = new(
        id: "SSL003",
        title: "풀에서 빌린 버퍼를 zero-copy Send 로 보내고 있습니다",
        messageFormat: "'{0}' 은(는) ArrayPool 에서 빌린 버퍼입니다. '{1}' 은(는) 배열을 참조로만 큐에 넣으므로 반납 후에 전송될 수 있습니다. SendCopied / TrySendCopied 를 쓰세요.",
        category: LifetimeCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Send / TrySend 는 호출자의 배열을 참조로 큐에 넣습니다. ArrayPool 로 반납한 버퍼는 다른 " +
            "코드가 곧바로 다시 빌려 갈 수 있으므로, 전송이 끝나기 전에 내용이 바뀝니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL004 — ReadOnlySequence 를 단일 세그먼트로 가정한다.</summary>
    public static readonly DiagnosticDescriptor SequenceAssumedSingleSegment = new(
        id: "SSL004",
        title: "ReadOnlySequence 를 단일 세그먼트로 가정하고 있습니다",
        messageFormat: "'{0}' 은(는) 첫 세그먼트만 봅니다. 수신 파이프의 시퀀스는 세그먼트 여러 개에 걸칠 수 있으니 CopyTo(Span) 이나 SequenceReader 를 쓰거나, IsSingleSegment 를 먼저 확인하세요.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "헤더나 본문이 파이프 세그먼트 경계에 걸치면 First.Span 은 요청의 앞부분만 담고 있습니다. " +
            "패킷이 작을 때는 거의 항상 한 세그먼트에 들어오므로 부하가 올라야 드러납니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL005 — 요청 핸들러가 async 다.</summary>
    public static readonly DiagnosticDescriptor AsyncRequestHandler = new(
        id: "SSL005",
        title: "요청 핸들러가 async 입니다",
        messageFormat: "'{0}' 은(는) RequestInfo 를 받는 async 메서드입니다. 첫 await 에서 리턴하므로 그 뒤에 읽는 요청 본문은 이미 무효입니다. 핸들러는 동기로 끝내고, 비동기 작업이 필요하면 값을 복사해 넘기세요.",
        category: LifetimeCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "라이브러리는 핸들러가 리턴해야 수신 파이프를 전진시킵니다. async 핸들러는 첫 await 에서 " +
            "리턴해 버리므로 파이프가 곧바로 전진하고, 이어지는 코드는 덮어써진 버퍼를 읽습니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL006 — Setup / Start 반환값을 무시한다.</summary>
    public static readonly DiagnosticDescriptor SetupResultIgnored = new(
        id: "SSL006",
        title: "Setup / Start 의 반환값을 확인하지 않았습니다",
        messageFormat: "'{0}' 은(는) 실패를 예외가 아니라 false 로 알립니다. 반환값을 확인하고 실패면 서버를 시작하지 마세요.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Setup 이 실패한 채로 Start 를 부르면 서버가 뜬 것처럼 보이지만 아무것도 받지 못합니다.",
        helpLinkUri: HelpUri);

    /// <summary>SSL007 — GetAllSessions / GetSessions 결과의 null 검사를 빠뜨렸다.</summary>
    public static readonly DiagnosticDescriptor SessionEnumerationNotNullChecked = new(
        id: "SSL007",
        title: "세션 열거 결과의 null 을 확인하지 않았습니다",
        messageFormat: "'{0}' 은(는) 서버가 아직 시작되지 않았거나 내려가는 중이면 null 을 돌려줍니다. 먼저 지역 변수에 받아 null 을 확인하세요.",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "브로드캐스트 코드에서 가장 흔한 NullReferenceException 원인입니다.",
        helpLinkUri: HelpUri);
}
