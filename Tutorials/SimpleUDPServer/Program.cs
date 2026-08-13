using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;
using System;
using System.Buffers;
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

    /// <summary>
    /// UDP 데이터그램은 언제나 하나의 완전한 요청으로 도착하므로 버퍼 전체가 곧 한 요청이다.
    /// </summary>
    public MyUdpRequestInfo Filter(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        if (buffer.Length <= HeaderLength)
            return null;

        consumed = buffer.End;
        examined = consumed;

        var data = buffer.ToArray();

        var key = Encoding.ASCII.GetString(data, 0, KeyLength);
        var sessionID = Encoding.ASCII.GetString(data, KeyLength, SessionIdLength);
        var value = Encoding.UTF8.GetString(data, HeaderLength, data.Length - HeaderLength);

        return new MyUdpRequestInfo(key, sessionID) { Value = value };
    }

    public IReceiveFilter<MyUdpRequestInfo> NextReceiveFilter
    {
        get { return null; }
    }

    /// <summary>
    /// Gets the filter state.
    /// </summary>
    public FilterState State { get; private set; }

    public void Reset()
    {
        State = FilterState.Normal;
    }
}
