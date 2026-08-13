using System;
using System.Buffers;
using System.Buffers.Binary;

using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;


namespace EchoServer;

public class EFBinaryRequestInfo : BinaryRequestInfo
{
    /// <summary>
    /// 전체 크기
    /// </summary>
    public Int16 TotalSize { get; private set; }

    /// <summary>
    /// 패킷 ID
    /// </summary>
    public Int16 PacketID { get; private set; }

    /// <summary>
    /// 예약(더미)값 
    /// </summary>
    public SByte Value1 { get; private set; }

    /// <summary>
    /// 헤더 크기
    /// </summary>
    public const int HeaderSize = 5;

    /// <summary>
    /// EFBinaryRequestInfo 클래스의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="totalSize">전체 크기</param>
    /// <param name="packetID">패킷 ID</param>
    /// <param name="value1">값 1</param>
    /// <param name="body">바디</param>
    public EFBinaryRequestInfo(Int16 totalSize, Int16 packetID, SByte value1, byte[] body)
        : base(null, body)
    {
        this.TotalSize = totalSize;
        this.PacketID = packetID;
        this.Value1 = value1;
    }
}

public class ReceiveFilter : FixedHeaderReceiveFilter<EFBinaryRequestInfo>
{
    public ReceiveFilter() : base(EFBinaryRequestInfo.HeaderSize)
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

        Console.WriteLine($"[ReceiveFilter.ResolveRequestInfo] body length:{body.Length}");

        return new EFBinaryRequestInfo(BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2)),
                                       BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(2, 2)),
                                       (SByte)headerBuffer[4],
                                       body.ToArray());
    }
}
