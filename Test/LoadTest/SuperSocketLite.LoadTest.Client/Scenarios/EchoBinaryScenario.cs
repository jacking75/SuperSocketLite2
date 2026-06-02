using SuperSocketLite.LoadTest.Shared;

namespace SuperSocketLite.LoadTest.Client.Scenarios;

public sealed class EchoBinaryScenario
{
    public BinaryPacket CreateRequest(int clientId, int sequence, LoadTestOptions options)
    {
        var body = PayloadFactory.Create(clientId, sequence, options.Payload);
        return new BinaryPacket(101, 0, body);
    }
}
