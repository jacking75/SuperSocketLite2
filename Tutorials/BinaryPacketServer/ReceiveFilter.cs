using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace BinaryPacketServer;

/// <summary>
/// 이진 요청 정보 클래스입니다.
/// </summary>
/// <remarks>
/// 패킷을 받는 데 드는 할당이 없습니다. <see cref="Body"/>는 수신 파이프의 메모리를 그대로
/// 가리키고, 인스턴스도 <see cref="ReceiveFilter"/>가 세션마다 하나만 두고 돌려 씁니다.
///
/// 그래서 <c>NewRequestReceived</c> 핸들러가 리턴하면 인스턴스도 본문도 유효하지 않습니다.
/// 값을 남기려면 핸들러 안에서 복사하거나 역직렬화해 두어야 합니다. 패킷을 다른 스레드로
/// 넘기는 서버라면 이 방식을 쓸 수 없습니다(<c>Docs/GC_Copy_Minimization.md</c>의 개선 1·2).
/// </remarks>
public class EFBinaryRequestInfo : IRequestInfo
{
    public int PacketID { get; private set; }
    public short Value1 { get; private set; }
    public short Value2 { get; private set; }

    public string Key => string.Empty;

    /// <summary>헤더를 뺀 본문입니다. 핸들러가 리턴하면 무효가 됩니다.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(int packetID, short value1, short value2, ReadOnlySequence<byte> body)
    {
        PacketID = packetID;
        Value1 = value1;
        Value2 = value2;
        Body = body;
    }
}

public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    private const int FrameHeaderSize = 12;

    // 필터는 세션마다 하나이고, 그 세션의 요청 처리는 파이프 태스크에서 동기로 끝난다.
    // 다음 패킷을 파싱할 때는 이전 핸들러가 이미 리턴한 뒤이므로 인스턴스를 돌려 써도 된다.
    private readonly EFBinaryRequestInfo _reusable = new();

    public ReceiveFilter()
        : base(FrameHeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[FrameHeaderSize];
        header.CopyTo(headerBuffer);
        return BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.Slice(8, 4));
    }

    protected override EFBinaryRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[FrameHeaderSize];
        header.CopyTo(headerBuffer);

        _reusable.Set(
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.Slice(0, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(6, 2)),
            body);

        return _reusable;
    }
}
