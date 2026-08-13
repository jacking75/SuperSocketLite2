using System;
using System.Buffers;
using System.Buffers.Binary;

using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace GameServer_01;

/// <summary>
/// 이진 요청 정보 클래스
/// 패킷의 헤더와 보디에 해당하는 부분을 나타냅니다.
/// </summary>
public class PacketRequestInfo : BinaryRequestInfo
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
    public PacketRequestInfo(Int16 totalSize, Int16 packetID, SByte value1, byte[] body)
        : base(null, body)
    {
        this.TotalSize = totalSize;
        this.PacketID = packetID;
        this.Value1 = value1;
    }
}

/// <summary>
/// 수신 필터 클래스
/// </summary>
public class PacketReceiveFilter : FixedHeaderReceiveFilter<PacketRequestInfo>
{
    /// <summary>
    /// ReceiveFilter 클래스의 새 인스턴스를 초기화합니다.
    /// </summary>
    public PacketReceiveFilter() : base(PacketRequestInfo.HeaderSize)
    {
    }

    /// <summary>
    /// 헤더에서 바디 길이를 가져옵니다.
    /// </summary>
    /// <param name="header">헤더. 세그먼트 여러 개에 걸쳐 있을 수 있다</param>
    /// <returns>바디 길이</returns>
    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(headerBuffer);

        var packetTotalSize = BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2));
        return packetTotalSize - PacketRequestInfo.HeaderSize;
    }

    /// <summary>
    /// 요청 정보를 해결합니다.
    /// </summary>
    /// <param name="header">헤더</param>
    /// <param name="body">바디. 바디가 없으면 비어 있다</param>
    /// <returns>해결된 요청 정보</returns>
    protected override PacketRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> headerBuffer = stackalloc byte[PacketRequestInfo.HeaderSize];
        header.CopyTo(headerBuffer);

        return new PacketRequestInfo(BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(0, 2)),
                                     BinaryPrimitives.ReadInt16LittleEndian(headerBuffer.Slice(2, 2)),
                                     (SByte)headerBuffer[4],
                                     body.ToArray());
    }
}
