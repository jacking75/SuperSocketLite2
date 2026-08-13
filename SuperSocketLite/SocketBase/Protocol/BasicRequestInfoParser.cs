namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>
/// Basic request info parser, which parse request info by separating
/// </summary>
public class BasicRequestInfoParser : IRequestInfoParser<StringRequestInfo>
{
    private readonly string _spliter;
    private readonly string[] _parameterSpliters;

    private const string OneSpace = " ";

    /// <summary>
    /// The default singlegton instance
    /// </summary>
    public static readonly BasicRequestInfoParser DefaultInstance = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicRequestInfoParser"/> class.
    /// </summary>
    public BasicRequestInfoParser()
        : this(OneSpace, OneSpace)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicRequestInfoParser"/> class.
    /// </summary>
    /// <param name="spliter">The spliter between command name and command parameters.</param>
    /// <param name="parameterSpliter">The parameter spliter.</param>
    public BasicRequestInfoParser(string spliter, string parameterSpliter)
    {
        _spliter = spliter;
        _parameterSpliters = [parameterSpliter];
    }

    

    /// <summary>
    /// Parses the request info.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns></returns>
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
