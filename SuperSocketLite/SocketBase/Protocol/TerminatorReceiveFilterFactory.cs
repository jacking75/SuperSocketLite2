using System.Net;
using System.Text;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Terminator ReceiveFilter Factory</summary>
public class TerminatorReceiveFilterFactory : IReceiveFilterFactory<StringRequestInfo>
{
    private readonly Encoding _encoding;
    private readonly byte[] _terminator;
    private readonly IRequestInfoParser<StringRequestInfo> _requestInfoParser;

    public TerminatorReceiveFilterFactory(string terminator)
        : this(terminator, Encoding.ASCII, BasicRequestInfoParser.DefaultInstance)
    {

    }

    public TerminatorReceiveFilterFactory(string terminator, Encoding encoding)
        : this(terminator, encoding, BasicRequestInfoParser.DefaultInstance)
    {

    }

    /// <param name="requestInfoParser">The line parser.</param>
    public TerminatorReceiveFilterFactory(string terminator, Encoding encoding, IRequestInfoParser<StringRequestInfo> requestInfoParser)
    {
        _encoding = encoding;
        _terminator = encoding.GetBytes(terminator);
        _requestInfoParser = requestInfoParser;
    }

    /// <summary>Creates the Receive filter.</summary>
    /// <returns>the new created request filer assosiated with this socketSession</returns>
    public virtual IReceiveFilter<StringRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, IPEndPoint? remoteEndPoint)
    {
        return new TerminatorReceiveFilter(_terminator, _encoding, _requestInfoParser);
    }
}
