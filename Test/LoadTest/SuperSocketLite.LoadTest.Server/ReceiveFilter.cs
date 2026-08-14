using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

namespace SuperSocketLite.LoadTest.Server;

public sealed class ReceiveFilter : FixedHeaderReceiveFilter<LoadTestRequestInfo>
{
    private readonly AllocationMode _allocation;

    // 필터는 세션마다 하나이고, 그 세션의 요청 처리는 파이프 태스크에서 동기로 끝난다.
    // 다음 패킷을 파싱할 때는 이전 핸들러가 이미 리턴한 뒤이므로 인스턴스를 돌려 써도 된다.
    private readonly LoadTestRequestInfo _reusable = new();

    public ReceiveFilter(AllocationMode allocation)
        : base(LoadTestRequestInfo.HeaderSize)
    {
        _allocation = allocation;
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> buffer = stackalloc byte[LoadTestRequestInfo.HeaderSize];
        header.CopyTo(buffer);
        var totalSize = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2));
        if (totalSize < LoadTestRequestInfo.HeaderSize)
            throw new InvalidOperationException($"Invalid packet total size {totalSize}.");

        return totalSize - LoadTestRequestInfo.HeaderSize;
    }

    protected override LoadTestRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        Span<byte> buffer = stackalloc byte[LoadTestRequestInfo.HeaderSize];
        header.CopyTo(buffer);

        // Legacy는 개선 전 동작을 그대로 재현한다: 패킷마다 요청 인스턴스 1개 + 본문 배열 1개.
        var request = _allocation == AllocationMode.Pooled ? _reusable : new LoadTestRequestInfo();
        var requestBody = _allocation == AllocationMode.Pooled ? body : new ReadOnlySequence<byte>(body.ToArray());

        request.Set(
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(0, 2)),
            BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(2, 2)),
            unchecked((sbyte)buffer[4]),
            requestBody);

        return request;
    }
}

/// <summary>세션마다 <see cref="ReceiveFilter"/>를 만들면서 할당 모드를 넘겨줍니다.</summary>
/// <remarks>
/// 라이브러리의 <c>DefaultReceiveFilterFactory</c>는 매개변수 없는 생성자만 부르므로,
/// 필터에 설정을 전달하려면 팩토리를 직접 구현해야 합니다.
/// </remarks>
public sealed class ReceiveFilterFactory : IReceiveFilterFactory<LoadTestRequestInfo>
{
    private readonly AllocationMode _allocation;

    public ReceiveFilterFactory(AllocationMode allocation)
    {
        _allocation = allocation;
    }

    public IReceiveFilter<LoadTestRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, IPEndPoint? remoteEndPoint)
    {
        return new ReceiveFilter(_allocation);
    }
}
