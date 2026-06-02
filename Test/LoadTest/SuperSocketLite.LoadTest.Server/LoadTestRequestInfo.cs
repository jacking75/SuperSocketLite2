using SuperSocketLite.SocketBase.Protocol;

namespace SuperSocketLite.LoadTest.Server;

public sealed class LoadTestRequestInfo : BinaryRequestInfo
{
    public const int HeaderSize = 5;

    public LoadTestRequestInfo(short totalSize, short packetId, sbyte value1, byte[] body)
        : base(string.Empty, body)
    {
        TotalSize = totalSize;
        PacketId = packetId;
        Value1 = value1;
    }

    public short TotalSize { get; }
    public short PacketId { get; }
    public sbyte Value1 { get; }
}
