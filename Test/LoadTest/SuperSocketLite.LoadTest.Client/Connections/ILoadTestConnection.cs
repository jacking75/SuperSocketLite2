using System.Net;

namespace SuperSocketLite.LoadTest.Client.Connections;

public interface ILoadTestConnection : IAsyncDisposable
{
    ValueTask ConnectAsync(CancellationToken cancellationToken);
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }
}
