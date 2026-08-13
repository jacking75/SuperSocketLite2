namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>String type request information</summary>
public class StringRequestInfo : RequestInfo<string>
{
    public StringRequestInfo(string key, string body, string[] parameters)
        : base(key, body)
    {
        Parameters = parameters;
    }

    /// <summary>Gets the parameters.</summary>
    public string[] Parameters { get; private set; }

    /// <summary>Gets the first param.</summary>
    public string GetFirstParam()
    {
        if(Parameters.Length > 0)
            return Parameters[0];

        return string.Empty;
    }

    /// <summary>Gets the <see cref="System.String"/> at the specified index.</summary>
    public string this[int index] => Parameters[index];
}
