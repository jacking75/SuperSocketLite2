namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Basic request info parser, which parse request info by separating</summary>
public class BasicRequestInfoParser : IRequestInfoParser<StringRequestInfo>
{
    private readonly string _spliter;
    private readonly string[] _parameterSpliters;

    private const string OneSpace = " ";

    /// <summary>The default singlegton instance</summary>
    public static readonly BasicRequestInfoParser DefaultInstance = new();

    public BasicRequestInfoParser()
        : this(OneSpace, OneSpace)
    {
    }

    /// <param name="spliter">The spliter between command name and command parameters.</param>
    public BasicRequestInfoParser(string spliter, string parameterSpliter)
    {
        _spliter = spliter;
        _parameterSpliters = [parameterSpliter];
    }

    

    /// <summary>Parses the request info.</summary>
    public StringRequestInfo ParseRequestInfo(string source)
    {
        int pos = source.IndexOf(_spliter);

        string name = string.Empty;
        string param = string.Empty;

        if (pos > 0)
        {
            name = source.Substring(0, pos);
            param = source.Substring(pos + _spliter.Length);
        }
        else
        {
            name = source;
        }

        return new StringRequestInfo(name, param,
            param.Split(_parameterSpliters, StringSplitOptions.RemoveEmptyEntries));
    }
            
}
