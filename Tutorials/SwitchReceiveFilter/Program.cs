using System;
using System.Collections.Generic;
using System.Text;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Protocol;

namespace SwitchReceiveFilter;

internal static class Program
{
    private static void Main()
    {
        var config = new ServerConfig
        {
            Port = 2020,
            Ip = "Any",
            MaxConnectionNumber = 100,
            Mode = SocketMode.Tcp,
            Name = "SwitchReceiveFilterServer"
        };

        var appServer = new MyAppServer();
        appServer.NewRequestReceived += OnRequestReceived;

        if (!appServer.Setup(new RootConfig(), config, logFactory: new SuperSocketLite.SocketBase.Logging.ConsoleLogFactory()))
        {
            Console.WriteLine("Setup failed.");
            return;
        }

        if (!appServer.Start())
        {
            Console.WriteLine("Start failed.");
            return;
        }

        Console.WriteLine("SwitchReceiveFilter server started on port 2020.");
        Console.WriteLine("Send lines starting with 'Y' or '*', for example: Y hello");
        Console.WriteLine("Press any key to stop.");
        Console.ReadKey();

        appServer.Stop();
    }

    private static void OnRequestReceived(AppSession session, StringRequestInfo requestInfo)
    {
        Console.WriteLine($"[{requestInfo.Key}] {requestInfo.Body}");
        session.Send(Encoding.UTF8.GetBytes($"{requestInfo.Key}: {requestInfo.Body}\r\n"));
    }
}

public sealed class MyAppServer : AppServer
{
    public MyAppServer()
        : base(new DefaultReceiveFilterFactory<SwitchReceiveFilter, StringRequestInfo>())
    {
    }
}

public sealed class SwitchReceiveFilter : IReceiveFilter<StringRequestInfo>
{
    private readonly LineReceiveFilter m_FilterA;
    private readonly LineReceiveFilter m_FilterB;

    public SwitchReceiveFilter()
    {
        m_FilterA = new LineReceiveFilter(this, (byte)'Y', "FilterA");
        m_FilterB = new LineReceiveFilter(this, (byte)'*', "FilterB");
    }

    public StringRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        rest = length;
        NextReceiveFilter = null;

        if (length <= 0)
            return null;

        var flag = readBuffer[offset];

        if (flag == (byte)'Y')
        {
            NextReceiveFilter = m_FilterA;
        }
        else if (flag == (byte)'*')
        {
            NextReceiveFilter = m_FilterB;
        }
        else
        {
            State = FilterState.Error;
            rest = 0;
        }

        return null;
    }

    public int LeftBufferSize => 0;

    public IReceiveFilter<StringRequestInfo> NextReceiveFilter { get; private set; }

    public void Reset()
    {
        State = FilterState.Normal;
        NextReceiveFilter = null;
        m_FilterA.Reset();
        m_FilterB.Reset();
    }

    public FilterState State { get; private set; }
}

internal sealed class LineReceiveFilter : IReceiveFilter<StringRequestInfo>
{
    private readonly IReceiveFilter<StringRequestInfo> m_SwitchFilter;
    private readonly byte m_Marker;
    private readonly string m_Key;
    private readonly List<byte> m_Buffer = new();

    public LineReceiveFilter(IReceiveFilter<StringRequestInfo> switchFilter, byte marker, string key)
    {
        m_SwitchFilter = switchFilter;
        m_Marker = marker;
        m_Key = key;
    }

    public StringRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        NextReceiveFilter = null;

        for (var i = 0; i < length; i++)
        {
            var value = readBuffer[offset + i];

            if (m_Buffer.Count == 0 && value != m_Marker)
            {
                State = FilterState.Error;
                rest = 0;
                return null;
            }

            m_Buffer.Add(value);

            if (value != (byte)'\n')
                continue;

            rest = length - i - 1;
            var requestInfo = ResolveRequest();
            Reset();
            NextReceiveFilter = m_SwitchFilter;
            return requestInfo;
        }

        rest = 0;
        return null;
    }

    public int LeftBufferSize => m_Buffer.Count;

    public IReceiveFilter<StringRequestInfo> NextReceiveFilter { get; private set; }

    public void Reset()
    {
        m_Buffer.Clear();
        State = FilterState.Normal;
        NextReceiveFilter = null;
    }

    public FilterState State { get; private set; }

    private StringRequestInfo ResolveRequest()
    {
        var line = Encoding.UTF8.GetString(m_Buffer.ToArray()).TrimEnd('\r', '\n');
        var body = line.Length > 0 && line[0] == (char)m_Marker ? line.Substring(1).TrimStart() : line;
        return new StringRequestInfo(m_Key, body, body.Length == 0 ? Array.Empty<string>() : body.Split(' '));
    }
}
