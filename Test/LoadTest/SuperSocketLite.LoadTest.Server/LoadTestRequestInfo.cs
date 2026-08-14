using System.Buffers;
using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.LoadTest.Server;

/// <summary>이진 프로토콜 요청 하나입니다.</summary>
/// <remarks>
/// <see cref="Body"/>는 수신 파이프의 메모리를 그대로 가리키고, 인스턴스 자체도
/// <see cref="ReceiveFilter"/>가 세션마다 하나만 두고 돌려 씁니다
/// (<see cref="AllocationMode.Pooled"/>). 그래서 패킷을 받는 데 드는 할당이 없습니다.
///
/// 대신 <c>NewRequestReceived</c> 핸들러가 리턴하면 인스턴스도 본문도 유효하지 않습니다.
/// 값을 남기려면 핸들러 안에서 복사하거나 역직렬화해 두어야 합니다.
/// 자세한 근거는 <c>Docs/GC_Copy_Minimization.md</c>의 개선 1을 보세요.
/// </remarks>
public sealed class LoadTestRequestInfo : IRequestInfo
{
    public const int HeaderSize = 5;

    public string Key => string.Empty;

    public short TotalSize { get; private set; }

    public short PacketId { get; private set; }

    public sbyte Value1 { get; private set; }

    /// <summary>수신 파이프를 가리키는 본문입니다. 핸들러가 리턴하면 무효가 됩니다.</summary>
    public ReadOnlySequence<byte> Body { get; private set; }

    public void Set(short totalSize, short packetId, sbyte value1, ReadOnlySequence<byte> body)
    {
        TotalSize = totalSize;
        PacketId = packetId;
        Value1 = value1;
        Body = body;
    }
}
