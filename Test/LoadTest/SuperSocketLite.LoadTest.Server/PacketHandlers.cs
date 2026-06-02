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

    public static void Handle(LoadTestSession session, LoadTestRequestInfo request, ServerMetricsCollector metrics)
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

        var response = BinaryPacket.Encode(responsePacketId, request.Value1, request.Body);

        try
        {
            session.Send(response, 0, response.Length);
            metrics.OnBytesOut(response.Length);
        }
        catch (Exception ex)
        {
            metrics.OnSendFailed(session.SessionID, response.Length, ex.Message);
            throw;
        }
    }
}
