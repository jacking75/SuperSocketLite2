namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Binary type request information</summary>
public class BinaryRequestInfo :  RequestInfo<byte[]>
{
    public BinaryRequestInfo(string key, byte[] body)
        : base(key, body)
    {

    }
}
