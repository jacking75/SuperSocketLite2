using System.Net;

namespace SuperSocketLite.LoadTest.Client.Connections;

public interface ILoadTestConnection : IAsyncDisposable
{
    ValueTask ConnectAsync(CancellationToken cancellationToken);
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// 정상 종료 절차를 밟지 않고 연결을 끊습니다.
    /// TCP에서는 RST를 보내므로 서버가 비정상 종료 경로를 타게 됩니다.
    /// 연결 개념이 없는 전송 방식에서는 아무 일도 하지 않습니다.
    /// </summary>
    void Abort();
}
