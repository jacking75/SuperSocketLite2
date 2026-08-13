using System;

using System.Buffers;
using System.Buffers.Binary;

using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace GameServer_MemoryPack;

/// <summary>
/// 이진 요청 정보 클래스
/// 패킷의 헤더와 보디에 해당하는 부분을 나타냅니다.
/// </summary>
public class PacketRequestInfo : BinaryRequestInfo
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

    /// <summary>
    /// MemoryPackBinaryRequestInfo 클래스의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="packetData">패킷 데이터</param>
    public PacketRequestInfo(byte[] packetData)
        : base(null, packetData)
    {
        Data = packetData;
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

        var totalSize = BinaryPrimitives.ReadUInt16LittleEndian(
            headerBuffer.Slice(PacketRequestInfo.PacketHeaderMemorypackStartPos, 2));

        return totalSize - PacketRequestInfo.HeaderSize;
    }

    /// <summary>
    /// 요청 정보를 해결합니다.
    /// MemoryPack 역직렬화는 헤더와 바디가 붙어 있는 원본 바이트열을 그대로 필요로 하므로
    /// 둘을 하나의 배열로 합쳐서 넘긴다.
    /// </summary>
    /// <param name="header">헤더</param>
    /// <param name="body">바디. 바디가 없으면 비어 있다</param>
    /// <returns>해결된 요청 정보</returns>
    protected override PacketRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        var packetData = new byte[PacketRequestInfo.HeaderSize + (int)body.Length];

        header.CopyTo(packetData);
        body.CopyTo(packetData.AsSpan(PacketRequestInfo.HeaderSize));

        return new PacketRequestInfo(packetData);
    }
}
