using System.Net;


namespace SuperSocketLite.SocketBase;

/// <summary>Active connect result model</summary>
public class ActiveConnectResult
{
    /// <summary>Gets or sets a value indicating whether the conecting is sucessfull</summary>
    public bool Result { get; set; }

    /// <summary>Gets or sets the connected session.</summary>
    public IAppSession? Session { get; set; }
}

/// <summary>The inerface to connect the remote endpoint actively</summary>
public interface IActiveConnector
{
    /// <summary>Connect the target endpoint actively, binding no local endpoint.</summary>
    Task<ActiveConnectResult> ActiveConnect(EndPoint targetEndPoint) => ActiveConnect(targetEndPoint, null);

    /// <summary>Connect the target endpoint actively.</summary>
    Task<ActiveConnectResult> ActiveConnect(EndPoint targetEndPoint, EndPoint? localEndPoint);
}
