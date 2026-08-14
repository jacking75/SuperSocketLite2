using System;
using System.Buffers;
using System.Buffers.Binary;

namespace MultiPortServer;

// 예전에는 여기에 세션과 요청 정보를 함께 담아 두는 PacketData 클래스가 있었다(쓰이지는 않았다).
// 지금은 EFBinaryRequestInfo를 필터가 돌려 쓰고 Body가 수신 파이프를 가리키므로,
// 요청 정보를 이렇게 보관하면 핸들러가 리턴한 순간 내용이 바뀌어 버린다.
// 패킷을 다른 곳으로 넘겨야 한다면 Docs/GC_Copy_Minimization.md의 개선 2를 따른다.

public enum PACKETID : int
{
    REQ_ECHO = 101,
}

public class CommonHandler
{
    /// <summary>스택에 담아 보낼 응답의 상한입니다. 이보다 크면 풀에서 빌립니다.</summary>
    private const int StackBufferSize = 512;

    /// <summary>
    /// 받은 패킷을 그대로 돌려보냅니다.
    /// </summary>
    /// <remarks>
    /// 응답 버퍼를 스택이나 <see cref="ArrayPool{T}"/>에서 마련하므로 응답마다 배열을 새로 만들지
    /// 않습니다. <c>SendCopied</c>는 라이브러리 풀 버퍼로 복사해 큐에 넣고 전송이 끝나면 그 버퍼를
    /// 스스로 반납하므로, 이 메서드가 리턴하는 즉시 여기 버퍼를 다시 써도 됩니다.
    /// 큐가 <c>SendTimeOut</c>동안 계속 가득 차 있으면 <c>Send</c>와 똑같이 예외를 던집니다.
    /// </remarks>
    public void RequestEcho(NetworkSession session, EFBinaryRequestInfo requestInfo)
    {
        var totalSize = EFBinaryRequestInfo.HeaderSize + checked((int)requestInfo.Body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> packet = stackalloc byte[StackBufferSize];
            session.SendCopied(packet.Slice(0, WritePacket(packet, requestInfo.Body)));
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            session.SendCopied(rented.AsSpan(0, WritePacket(rented, requestInfo.Body)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>헤더와 본문을 <paramref name="destination"/>에 써 넣고 쓴 바이트 수를 돌려줍니다.</summary>
    private static int WritePacket(Span<byte> destination, ReadOnlySequence<byte> body)
    {
        var totalSize = EFBinaryRequestInfo.HeaderSize + checked((int)body.Length);

        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), (short)totalSize);
        BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), (short)PACKETID.REQ_ECHO);
        destination[4] = 0;
        body.CopyTo(destination.Slice(EFBinaryRequestInfo.HeaderSize));

        return totalSize;
    }
}

public class PK_ECHO
{
    public string msg;
}
