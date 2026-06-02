namespace SuperSocketLite.LoadTest.Client.Connections;

public sealed class TextLineConnection : TcpBinaryConnection
{
    public TextLineConnection(string host, int port)
        : base(host, port)
    {
    }
}
