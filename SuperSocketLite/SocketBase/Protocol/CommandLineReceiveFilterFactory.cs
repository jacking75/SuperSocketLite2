using System.Text;

namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>CommandLine RequestFilter Factory</summary>
public class CommandLineReceiveFilterFactory : TerminatorReceiveFilterFactory
{
    public CommandLineReceiveFilterFactory()
        : this(Encoding.ASCII)
    {
        
    }

    public CommandLineReceiveFilterFactory(Encoding encoding)
        : this(encoding, new BasicRequestInfoParser())
    {

    }

    public CommandLineReceiveFilterFactory(Encoding encoding, IRequestInfoParser<StringRequestInfo> requestInfoParser)
        : base("\r\n", encoding, requestInfoParser)
    {

    }
}
