namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>RequestInfo basic class</summary>
/// <typeparam name="TRequestBody">The type of the request body.</typeparam>
public class RequestInfo<TRequestBody> : IRequestInfo<TRequestBody>
{
    protected RequestInfo()
    {
        
    }

    public RequestInfo(string key, TRequestBody body)
    {
        Initialize(key, body);
    }

    /// <summary>Initializes the specified key.</summary>
    protected void Initialize(string key, TRequestBody body)
    {
        Key = key;
        Body = body;
    }

    /// <summary>Gets the key of this request.</summary>
    public string Key { get; private set; } = null!;

    /// <summary>Gets the body.</summary>
    public TRequestBody Body { get; private set; } = default!;
}

/// <summary>RequestInfo with header</summary>
/// <typeparam name="TRequestHeader">The type of the request header.</typeparam>
/// <typeparam name="TRequestBody">The type of the request body.</typeparam>
public class RequestInfo<TRequestHeader, TRequestBody> : RequestInfo<TRequestBody>, IRequestInfo<TRequestHeader, TRequestBody>
{
    public RequestInfo()
    {
        
    }
    public RequestInfo(string key, TRequestHeader header, TRequestBody body)
        : base(key, body)
    {
        Header = header;
    }

    /// <summary>Initializes the specified key.</summary>
    public void Initialize(string key, TRequestHeader header, TRequestBody body)
    {
        base.Initialize(key, body);
        Header = header;
    }
    /// <summary>Gets the header.</summary>
    public TRequestHeader Header { get; private set; } = default!;
}
