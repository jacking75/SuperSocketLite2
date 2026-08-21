namespace SuperSocketLite2.GameServerTemplate;

/// <summary>패킷 핸들러. 패킷을 추가하면 여기에 메서드를 하나 만든다.</summary>
internal static class PacketHandlers
{
    /// <summary>받은 본문을 그대로 돌려보낸다.</summary>
    /// <remarks>
    /// <paramref name="request"/> 의 본문은 이 메서드가 리턴하면 무효가 되므로,
    /// 여기서 전부 소비하거나 복사해야 한다. <see cref="PacketWriter.Send"/> 는
    /// 리턴하기 전에 <c>SendCopied</c> 로 라이브러리 풀 버퍼에 복사하므로 안전하다.
    /// </remarks>
    public static void HandleEcho(NetworkSession session, PacketRequestInfo request)
    {
        PacketWriter.Send(session, (short)PacketId.ResEcho, request.Body);
    }
}
