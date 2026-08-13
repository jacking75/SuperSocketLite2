using System.Buffers;
using System.Net;

namespace SuperSocketLite.SocketEngine;

internal sealed class UdpReceivePacket : IDisposable
{
    private byte[]? m_Buffer;

    public byte[] Buffer => m_Buffer ?? Array.Empty<byte>();

    public int Offset { get; private set; }

    public int Count { get; private set; }

    public IPEndPoint RemoteEndPoint { get; private set; } = null!;

    public void Initialize(byte[] buffer, int offset, int count, IPEndPoint remoteEndPoint)
    {
        m_Buffer = buffer;
        Offset = offset;
        Count = count;
        RemoteEndPoint = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
    }

    public void Dispose()
    {
        var buffer = m_Buffer;
        if (buffer == null)
            return;

        m_Buffer = null;
        Offset = 0;
        Count = 0;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
