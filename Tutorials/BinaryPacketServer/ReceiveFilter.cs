using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace BinaryPacketServer;

public class EFBinaryRequestInfo : BinaryRequestInfo
{
    public int PacketID { get; private set; }
    public short Value1 { get; private set; }
    public short Value2 { get; private set; }

    public EFBinaryRequestInfo(int packetID, short value1, short value2, byte[] body)
        : base(null, body)
    {
        PacketID = packetID;
        Value1 = value1;
        Value2 = value2;
    }
}

public class ReceiveFilter : FixedHeaderSequenceReceiveFilter<EFBinaryRequestInfo>
{
    private const int FrameHeaderSize = 12;

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

        return new EFBinaryRequestInfo(
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.Slice(0, 4)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(4, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(6, 2)),
            body.ToArray());
    }
}
