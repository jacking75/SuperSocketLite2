using System.Text;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

public static class PayloadFactory
{
    /// <summary>
    /// 프로토콜이 허용하는 최대 본문 크기입니다.
    /// 헤더의 totalSize가 Int16이므로 헤더 5바이트를 뺀 값이 상한입니다.
    /// </summary>
    public const int MaxBodySize = short.MaxValue - Shared.BinaryPacket.HeaderSize;

    public static byte[] Create(int clientId, int sequence, string profile)
    {
        var size = profile switch
        {
            "medium" => 256,
            "large" => 4096,
            // 패킷 헤더의 totalSize가 Int16이라 헤더 포함 32,767바이트가 상한이다.
            // 그 한계에 최대한 붙여 서버의 조립 경로를 흔든다.
            "huge" => MaxBodySize,
            "mixed" => sequence % 20 == 0 ? 4096 : sequence % 5 == 0 ? 256 : 32,
            // 대부분은 작지만 가끔 매우 큰 요청이 섞이는, 실제 게임 트래픽에 가까운 조합이다.
            "mixed-huge" => sequence % 50 == 0 ? MaxBodySize : sequence % 5 == 0 ? 256 : 32,
            _ => 32
        };

        var prefix = Encoding.UTF8.GetBytes($"{clientId:D8}:{sequence:D8}:");
        var payload = new byte[size];
        prefix.AsSpan(0, Math.Min(prefix.Length, payload.Length)).CopyTo(payload);
        for (var i = prefix.Length; i < payload.Length; i++)
            payload[i] = (byte)('a' + i % 26);
        return payload;
    }
}
