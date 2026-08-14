using System.Buffers;
using SuperSocketLite.LoadTest.ServerProbe;
using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Server;

public static class PacketHandlers
{
    public const short EchoRequest = 101;
    public const short LoginRequest = 201;
    public const short LoginResponse = 202;
    public const short HeartbeatRequest = 203;
    public const short HeartbeatResponse = 204;
    public const short ChatRequest = 205;
    public const short ChatResponse = 206;
    public const short RoomEnterRequest = 207;
    public const short RoomEnterResponse = 208;
    public const short RoomLeaveRequest = 209;
    public const short RoomLeaveResponse = 210;

    /// <summary>스택에 담아 보낼 응답의 상한입니다. 이보다 크면 풀에서 빌립니다.</summary>
    private const int StackBufferSize = 512;

    /// <summary>
    /// 요청을 되돌려 보냅니다.
    /// <paramref name="metrics"/>가 null이면 계측 없이 응답만 합니다(<c>--metrics off</c>).
    /// </summary>
    public static void Handle(
        LoadTestSession session,
        LoadTestRequestInfo request,
        ServerMetricsCollector? metrics,
        AllocationMode allocation)
    {
        var responsePacketId = request.PacketId switch
        {
            HeartbeatRequest => HeartbeatResponse,
            LoginRequest => LoginResponse,
            ChatRequest => ChatResponse,
            RoomEnterRequest => RoomEnterResponse,
            RoomLeaveRequest => RoomLeaveResponse,
            _ => request.PacketId
        };

        if (allocation == AllocationMode.Legacy)
        {
            SendLegacy(session, BinaryPacket.Encode(responsePacketId, request.Value1, request.Body), metrics);
            return;
        }

        var totalSize = BinaryPacket.SizeOf(request.Body.Length);

        if (totalSize <= StackBufferSize)
        {
            Span<byte> stackBuffer = stackalloc byte[StackBufferSize];
            var written = BinaryPacket.Encode(stackBuffer, responsePacketId, request.Value1, request.Body);
            Send(session, stackBuffer.Slice(0, written), metrics);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            var written = BinaryPacket.Encode(rented, responsePacketId, request.Value1, request.Body);
            Send(session, rented.AsSpan(0, written), metrics);
        }
        finally
        {
            // SendCopied가 이미 자기 풀 버퍼로 복사했으므로 여기서 바로 돌려줘도 된다.
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// 응답을 보냅니다. <c>SendCopied</c>는 라이브러리 풀 버퍼로 복사해 큐에 넣고
    /// 전송이 끝나면 그 버퍼를 스스로 반납하므로, 호출자 버퍼는 즉시 재사용할 수 있습니다.
    /// 큐가 <c>SendTimeOut</c>동안 계속 가득 차 있으면 <c>Send</c>와 똑같이 예외를 던집니다.
    /// </summary>
    private static void Send(LoadTestSession session, ReadOnlySpan<byte> response, ServerMetricsCollector? metrics)
    {
        try
        {
            session.SendCopied(response);
            metrics?.OnBytesOut(response.Length);
        }
        catch (Exception ex)
        {
            metrics?.OnSendFailed(session.SessionID, response.Length, ex.Message);
            throw;
        }
    }

    /// <summary>개선 전 경로입니다. 응답마다 배열을 새로 만들어 그대로 큐에 넘깁니다.</summary>
    private static void SendLegacy(LoadTestSession session, byte[] response, ServerMetricsCollector? metrics)
    {
        try
        {
            session.Send(response, 0, response.Length);
            metrics?.OnBytesOut(response.Length);
        }
        catch (Exception ex)
        {
            metrics?.OnSendFailed(session.SessionID, response.Length, ex.Message);
            throw;
        }
    }
}
