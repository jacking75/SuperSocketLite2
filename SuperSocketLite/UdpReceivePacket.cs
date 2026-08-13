using System.Buffers;
using System.Net;

namespace SuperSocketLite.SocketEngine;

internal sealed class UdpReceivePacket : IDisposable
{
    private byte[]? _buffer;

    public byte[] Buffer => _buffer ?? [];

    public int Offset { get; private set; }

    public int Count { get; private set; }

    public IPEndPoint RemoteEndPoint { get; private set; } = null!;

    public void Initialize(byte[] buffer, int offset, int count, IPEndPoint remoteEndPoint)
    {
        _buffer = buffer;
        Offset = offset;
        Count = count;
        RemoteEndPoint = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer == null)
            return;

        _buffer = null;
        Offset = 0;
        Count = 0;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
