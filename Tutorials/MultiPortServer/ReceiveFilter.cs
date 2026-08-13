using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace MultiPortServer;

public class EFBinaryRequestInfo : BinaryRequestInfo
{
    public short TotalSize { get; private set; }
    public short PacketID { get; private set; }
    public sbyte Value1 { get; private set; }

    public const int HeaderSize = 5;

    public EFBinaryRequestInfo(short totalSize, short packetID, sbyte value1, byte[] body)
        : base(null, body)
    {
        TotalSize = totalSize;
        PacketID = packetID;
        Value1 = value1;
    }
}

public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    public ReceiveFilter()
        : base(EFBinaryRequestInfo.HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[EFBinaryRequestInfo.HeaderSize];
        header.CopyTo(headerBuffer);

        var packetTotalSize = BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2));
        return packetTotalSize - EFBinaryRequestInfo.HeaderSize;
    }

    protected override EFBinaryRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[EFBinaryRequestInfo.HeaderSize];
        header.CopyTo(headerBuffer);

        return new EFBinaryRequestInfo(
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(2, 2)),
            (sbyte)headerBuffer[4],
            body.ToArray());
    }
}
