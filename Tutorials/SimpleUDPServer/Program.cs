using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleUDPServer;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Start Simple UDP Server !");

        var config = new ServerConfig
        {
            Port = 555,
            Ip = "Any",
            MaxConnectionNumber = 10,
            Mode = SocketMode.Udp,
            Name = "GPSServer"
        };

        var appServer = new UdpAppServer();
        appServer.Setup(new RootConfig(), config, logFactory: new SuperSocketLite.SocketBase.Logging.ConsoleLogFactory());


        appServer.Start();

        Console.WriteLine("key를 누르면 종료한다....");
        Console.ReadKey();
    }
}


public class MyUdpRequestInfo : UdpRequestInfo
{
    public MyUdpRequestInfo(string key, string sessionID)
        : base(key, sessionID)
    {

    }

    public string Value { get; set; }

    public byte[] ToData()
    {
        List<byte> data = new List<byte>();

        data.AddRange(Encoding.ASCII.GetBytes(Key));
        data.AddRange(Encoding.ASCII.GetBytes(SessionID));

        int expectedLen = 36 + 4;
        int maxLen = expectedLen - data.Count;

        if (maxLen > 0)
        {
            for (var i = 0; i < maxLen; i++)
            {
                data.Add(0x00);
            }
        }

        data.AddRange(Encoding.UTF8.GetBytes(Value));

        return data.ToArray();
    }
}


class UdpAppServer : AppServer<UdpTestSession, MyUdpRequestInfo>
{
    public UdpAppServer()
        : base(new DefaultReceiveFilterFactory<MyReceiveFilter, MyUdpRequestInfo>())
    {

    }               
}


public class UdpTestSession : AppSession<UdpTestSession, MyUdpRequestInfo>
{

}

    

class MyUdpProtocol : IReceiveFilterFactory<MyUdpRequestInfo>
{
    public IReceiveFilter<MyUdpRequestInfo> CreateFilter(IAppServer appServer, IAppSession appSession, System.Net.IPEndPoint remoteEndPoint)
    {
        return new MyReceiveFilter();
    }
}



sealed class MyReceiveFilter : IReceiveFilter<MyUdpRequestInfo>
{
    private const int KeyLength = 4;
    private const int SessionIdLength = 36;
    private const int HeaderLength = KeyLength + SessionIdLength;

    public MyUdpRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        return Parse(new ReadOnlySpan<byte>(readBuffer, offset, length), out rest);
    }

    public MyUdpRequestInfo Filter(ReadOnlySpan<byte> buffer, bool toBeCopied, out int rest)
    {
        return Parse(buffer, out rest);
    }

    private static MyUdpRequestInfo Parse(ReadOnlySpan<byte> buffer, out int rest)
    {
        rest = 0;

        if (buffer.Length <= HeaderLength)
            return null;

        var key = Encoding.ASCII.GetString(buffer.Slice(0, KeyLength));
        var sessionID = Encoding.ASCII.GetString(buffer.Slice(KeyLength, SessionIdLength));

        var data = Encoding.UTF8.GetString(buffer.Slice(HeaderLength));

        return new MyUdpRequestInfo(key, sessionID) { Value = data };
    }

    public int LeftBufferSize
    {
        get { return 0; }
    }

    public IReceiveFilter<MyUdpRequestInfo> NextReceiveFilter
    {
        get { return null; }
    }

    /// <summary>
    /// Gets the filter state.
    /// </summary>
    /// <value>
    /// The filter state.
    /// </value>
    public FilterState State { get; private set; }

    public void Reset()
    {

    }
}
