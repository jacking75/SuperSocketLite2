using System.Net;
using System.Net.Sockets;

namespace SuperSocketLite.LoadTest.Client.Connections;

public sealed class UdpConnection : ILoadTestConnection
{
    private readonly string _host;
    private readonly int _port;
    private UdpClient? _client;

    public UdpConnection(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public EndPoint? LocalEndPoint => _client?.Client.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _client?.Client.RemoteEndPoint;

    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        _client = new UdpClient();
        DisableConnectionReset(_client.Client);
        _client.Connect(_host, _port);
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("Connection is not open.");

        await _client.SendAsync(payload.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("Connection is not open.");

        var result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        result.Buffer.AsSpan(0, Math.Min(buffer.Length, result.Buffer.Length)).CopyTo(buffer.Span);
        return Math.Min(buffer.Length, result.Buffer.Length);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static void DisableConnectionReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            const int sioUdpConnReset = -1744830452;
            socket.IOControl(sioUdpConnReset, [0], null);
        }
        catch (SocketException)
        {
            // Older Windows/socket configurations may reject the control code; timeout handling still works elsewhere.
        }
    }
}
