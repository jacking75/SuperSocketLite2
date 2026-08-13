using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace GameServer;

public class EFBinaryRequestInfo : BinaryRequestInfo
{
    public ushort Size { get; private set; }
    public ushort PacketID { get; private set; }
    public sbyte Type { get; private set; }

    public EFBinaryRequestInfo(ushort size, ushort packetID, sbyte type, byte[] body)
        : base(null, body)
    {
        Size = size;
        PacketID = packetID;
        Type = type;
    }
}

public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    public ReceiveFilter()
        : base(CSBaseLib.PacketDef.HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[CSBaseLib.PacketDef.HeaderSize];
        header.CopyTo(headerBuffer);

        var packetSize = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(0, 2));
        return packetSize - CSBaseLib.PacketDef.HeaderSize;
    }

    protected override EFBinaryRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[CSBaseLib.PacketDef.HeaderSize];
        header.CopyTo(headerBuffer);

        return new EFBinaryRequestInfo(
            BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(0, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(2, 2)),
            (sbyte)headerBuffer[4],
            body.ToArray());
    }
}
