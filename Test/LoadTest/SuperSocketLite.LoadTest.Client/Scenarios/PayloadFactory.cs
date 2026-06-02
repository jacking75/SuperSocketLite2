using System.Text;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

public static class PayloadFactory
{
    public static byte[] Create(int clientId, int sequence, string profile)
    {
        var size = profile switch
        {
            "medium" => 256,
            "large" => 4096,
            "mixed" => sequence % 20 == 0 ? 4096 : sequence % 5 == 0 ? 256 : 32,
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
