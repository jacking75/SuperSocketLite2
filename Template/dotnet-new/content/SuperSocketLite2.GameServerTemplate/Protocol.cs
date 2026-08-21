using System.Buffers;
using System.Buffers.Binary;

using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace SuperSocketLite2.GameServerTemplate;

/// <summary>패킷 ID. 프로토콜을 늘릴 때 여기에 추가한다.</summary>
public enum PacketId : short
{
    None = 0,

    ReqEcho = 101,
    ResEcho = 102,
}

/// <summary>
/// 파싱된 패킷 하나. 프로토콜은 <c>[2바이트 전체 길이][2바이트 패킷 ID][본문]</c> 이다.
/// </summary>
/// <remarks>
/// 이 인스턴스도 <see cref="Body"/>도 <c>NewRequestReceived</c> 핸들러가 리턴하면 무효가 된다.
/// 필터가 세션마다 인스턴스 하나를 돌려 쓰고 본문은 수신 파이프의 메모리를 그대로 가리키기
/// 때문이다. 덕분에 패킷을 받는 데 드는 할당이 0이지만, 값을 남기려면 핸들러 안에서
/// 역직렬화하거나 복사해야 한다. 자세한 내용은 <c>Docs/agent/cautions.md</c> 4번.
/// </remarks>
public sealed class PacketRequestInfo : IRequestInfo
{
    /// <summary>헤더 크기: 전체 길이(2) + 패킷 ID(2).</summary>
    public const int HeaderSize = 4;

    /// <summary>바이너리 프로토콜이라 쓰지 않는다.</summary>
    public string Key => string.Empty;

    /// <summary>헤더를 포함한 패킷 전체 길이.</summary>
    public short TotalSize { get; private set; }

    /// <summary>패킷 ID. <see cref="PacketId"/>로 캐스팅해서 쓴다.</summary>
    public short PacketId { get; private set; }

    /// <summary>헤더를 뺀 본문. 핸들러가 리턴하면 무효가 된다.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(short totalSize, short packetId, ReadOnlySequence<byte> body)
    {
        TotalSize = totalSize;
        PacketId = packetId;
        Body = body;
    }
}

/// <summary>수신 파이프의 <see cref="ReadOnlySequence{T}"/>를 직접 파싱하는 필터.</summary>
public sealed class PacketReceiveFilter : FixedHeaderReceiveFilter<PacketRequestInfo>
{
    // 필터는 세션마다 하나이고, 다음 패킷 파싱은 이전 핸들러가 리턴한 뒤에 일어난다.
    // 그래서 요청 인스턴스 하나를 돌려 써도 안전하다 = 패킷당 할당 0.
    private readonly PacketRequestInfo _reusable = new();

    public PacketReceiveFilter()
        : base(PacketRequestInfo.HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        // header는 세그먼트 여러 개에 걸칠 수 있다. .First.Span 으로 바로 읽으면 안 된다.
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);

        return BinaryPrimitives.ReadInt16LittleEndian(buffer) - PacketRequestInfo.HeaderSize;
    }

    protected override PacketRequestInfo ResolveRequestInfo(
        ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(buffer);

        _reusable.Set(
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2)),
            body);

        return _reusable;
    }
}
