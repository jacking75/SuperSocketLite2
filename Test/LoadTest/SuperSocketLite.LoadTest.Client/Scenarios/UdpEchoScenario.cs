using System.Text;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

public static class UdpEchoScenario
{
    public static byte[] Encode(string key, Guid sessionId, string payload)
    {
        if (key.Length > 4)
            throw new ArgumentException("UDP key must be at most 4 ASCII bytes.", nameof(key));

        var keyBytes = Encoding.ASCII.GetBytes(key.PadRight(4));
        var sessionBytes = Encoding.ASCII.GetBytes(sessionId.ToString("D"));
        if (sessionBytes.Length != 36)
            throw new InvalidOperationException("GUID session id must encode to 36 ASCII bytes.");

        return [.. keyBytes, .. sessionBytes, .. Encoding.UTF8.GetBytes(payload)];
    }
}
