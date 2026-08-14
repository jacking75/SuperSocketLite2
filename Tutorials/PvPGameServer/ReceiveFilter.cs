using System;
using System.Buffers;
using System.Buffers.Binary;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace PvPGameServer;

/// <summary>
/// 메모리 팩으로 직렬화된 이진 요청 정보를 나타내는 클래스입니다.
/// </summary>
/// <remarks>
/// 이 서버는 받은 패킷을 로직 스레드(<see cref="PacketProcessor"/>)로 넘기므로, 패킷은 수신
/// 핸들러가 리턴한 뒤에도 살아 있어야 합니다. 그래서 본문을 복사하는 것은 피할 수 없습니다.
/// 대신 배열을 <see cref="ArrayPool{T}"/>에서 빌려 패킷당 할당을 없앴습니다
/// (<c>Docs/GC_Copy_Minimization.md</c>의 개선 2).
///
/// 규칙: 로직 스레드가 처리를 마치면 <see cref="ReturnBuffer"/>를 정확히 한 번 불러야 합니다.
/// 반납 지점은 <c>PacketProcessor.Process</c>의 finally 한 곳뿐입니다.
/// </remarks>
public class MemoryPackBinaryRequestInfo : IRequestInfo
{
    /// <summary>
    /// 세션 ID를 나타냅니다.
    /// </summary>
    public string SessionID = string.Empty;

    /// <summary>
    /// 패킷 헤더의 메모리 팩 시작 위치입니다.
    /// </summary>
    public const int PacketHeaderMemorypackStartPos = 1;

    /// <summary>
    /// 패킷 헤더의 크기입니다. 5는 실제 헤더의 크기이다
    /// </summary>
    public const int HeaderSize = 5 + PacketHeaderMemorypackStartPos;

    public string Key => string.Empty;

    /// <summary>
    /// 패킷의 헤더와 바디 전체가 담긴 바이트 배열입니다.
    /// </summary>
    /// <remarks>
    /// 풀에서 빌린 배열은 요청한 크기보다 클 수 있습니다. 길이로는 반드시 <see cref="DataSize"/>를
    /// 쓰고, 읽을 때는 <see cref="DataSpan"/>을 씁니다. <c>Data.Length</c>는 패킷 크기가 아닙니다.
    /// </remarks>
    public byte[] Data { get; private set; } = [];

    /// <summary><see cref="Data"/>에서 실제 패킷이 들어 있는 바이트 수입니다.</summary>
    public int DataSize { get; private set; }

    /// <summary>패킷 전체를 가리키는 뷰입니다. 역직렬화에는 이것을 씁니다.</summary>
    public ReadOnlySpan<byte> DataSpan => Data.AsSpan(0, DataSize);

    // 풀에서 빌린 배열일 때만 값이 있습니다. 서버가 직접 만든 내부 패킷은 반납 대상이 아닙니다.
    private byte[] _pooled = [];

    /// <summary>서버 내부에서 만든 패킷을 담습니다. 풀 배열이 아니므로 반납하지 않습니다.</summary>
    public void SetOwnedData(byte[] data)
    {
        Data = data;
        DataSize = data.Length;
        _pooled = [];
    }

    /// <summary>
    /// 수신한 패킷을 풀에서 빌린 배열에 담습니다.
    /// 처리가 끝나면 <see cref="ReturnBuffer"/>가 반드시 불려야 합니다.
    /// </summary>
    public void SetPooledData(byte[] pooled, int size)
    {
        Data = pooled;
        DataSize = size;
        _pooled = pooled;
    }

    /// <summary>
    /// 빌린 버퍼를 풀에 돌려줍니다. 처리를 마친 뒤 정확히 한 번 부릅니다.
    /// 반납한 배열은 곧바로 잊으므로 두 번 불려도 같은 배열을 두 번 돌려주지 않습니다.
    /// </summary>
    public void ReturnBuffer()
    {
        var pooled = _pooled;

        if (pooled.Length == 0)
            return;

        _pooled = [];
        Data = [];
        DataSize = 0;
        ArrayPool<byte>.Shared.Return(pooled);
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

        // 패킷이 로직 스레드로 넘어가므로 복사는 피할 수 없다. 다만 배열은 풀에서 빌린다.
        var packetData = ArrayPool<byte>.Shared.Rent(packetSize);

        header.CopyTo(packetData);

        if (!body.IsEmpty)
        {
            body.CopyTo(packetData.AsSpan((int)header.Length));
        }

        var requestInfo = new MemoryPackBinaryRequestInfo();
        requestInfo.SetPooledData(packetData, packetSize);
        return requestInfo;
    }
}
