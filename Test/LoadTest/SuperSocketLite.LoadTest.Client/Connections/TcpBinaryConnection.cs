using System.Net;
using System.Net.Sockets;

namespace SuperSocketLite.LoadTest.Client.Connections;

public class TcpBinaryConnection : ILoadTestConnection
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpBinaryConnection(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public EndPoint? LocalEndPoint => _client?.Client.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _client?.Client.RemoteEndPoint;

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Connection is not open.");

        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Connection is not open.");

        return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Linger를 0으로 두고 닫아 FIN 대신 RST가 나가게 합니다.
    /// 서버가 정상 종료가 아닌 끊김을 어떻게 처리하는지 보기 위한 것입니다.
    /// </summary>
    public void Abort()
    {
        if (_client is null)
            return;

        try
        {
            _client.LingerState = new LingerOption(enable: true, seconds: 0);
            _client.Client.Close(timeout: 0);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }
}
