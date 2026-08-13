using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace PvPGameServer;

/// <summary>
/// 메모리 팩으로 직렬화된 이진 요청 정보를 나타내는 클래스입니다.
/// </summary>
public class MemoryPackBinaryRequestInfo : BinaryRequestInfo
{
    /// <summary>
    /// 세션 ID를 나타냅니다.
    /// </summary>
    public string SessionID;

    /// <summary>
    /// 패킷의 헤더와 바디 전체를 나타내는 바이트 배열입니다.
    /// </summary>
    public byte[] Data;

    /// <summary>
    /// 패킷 헤더의 메모리 팩 시작 위치입니다.
    /// </summary>
    public const int PacketHeaderMemorypackStartPos = 1;

    /// <summary>
    /// 패킷 헤더의 크기입니다. 5는 실제 헤더의 크기이다
    /// </summary>
    public const int HeaderSize = 5 + PacketHeaderMemorypackStartPos;

    public MemoryPackBinaryRequestInfo(byte[] packetData)
        : base(null, packetData)
    {
        Data = packetData;
    }
}

/// <summary>
/// MemoryPackBinaryRequestInfo를 사용하는 sequence 기반 고정 헤더 수신 필터입니다.
/// </summary>
public class ReceiveFilter : FixedHeaderReceiveFilter<MemoryPackBinaryRequestInfo>
{
    public ReceiveFilter()
        : base(MemoryPackBinaryRequestInfo.HeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[MemoryPackBinaryRequestInfo.HeaderSize];
        header.CopyTo(headerBuffer);

        var totalSize = BinaryPrimitives.ReadUInt16LittleEndian(
            headerBuffer.Slice(MemoryPackBinaryRequestInfo.PacketHeaderMemorypackStartPos, 2));

        return totalSize - MemoryPackBinaryRequestInfo.HeaderSize;
    }

    protected override MemoryPackBinaryRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        var packetSize = checked((int)(header.Length + body.Length));
        var packetData = new byte[packetSize];

        header.CopyTo(packetData);

        if (!body.IsEmpty)
        {
            body.CopyTo(packetData.AsSpan((int)header.Length));
        }

        return new MemoryPackBinaryRequestInfo(packetData);
    }
}
