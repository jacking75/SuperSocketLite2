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
    private readonly LineReceiveFilter _filterA;
    private readonly LineReceiveFilter _filterB;

    public SwitchReceiveFilter()
    {
        _filterA = new LineReceiveFilter(this, (byte)'Y', "FilterA");
        _filterB = new LineReceiveFilter(this, (byte)'*', "FilterB");
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
            NextReceiveFilter = _filterA;
        }
        else if (flag == (byte)'*')
        {
            NextReceiveFilter = _filterB;
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
        _filterA.Reset();
        _filterB.Reset();
    }

    public FilterState State { get; private set; }
}

internal sealed class LineReceiveFilter : IReceiveFilter<StringRequestInfo>
{
    private readonly IReceiveFilter<StringRequestInfo> _switchFilter;
    private readonly byte _marker;
    private readonly string _key;
    private readonly List<byte> _buffer = new();

    public LineReceiveFilter(IReceiveFilter<StringRequestInfo> switchFilter, byte marker, string key)
    {
        _switchFilter = switchFilter;
        _marker = marker;
        _key = key;
    }

    public StringRequestInfo Filter(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest)
    {
        NextReceiveFilter = null;

        for (var i = 0; i < length; i++)
        {
            var value = readBuffer[offset + i];

            if (_buffer.Count == 0 && value != _marker)
            {
                State = FilterState.Error;
                rest = 0;
                return null;
            }

            _buffer.Add(value);

            if (value != (byte)'\n')
                continue;

            rest = length - i - 1;
            var requestInfo = ResolveRequest();
            Reset();
            NextReceiveFilter = _switchFilter;
            return requestInfo;
        }

        rest = 0;
        return null;
    }

    public int LeftBufferSize => _buffer.Count;

    public IReceiveFilter<StringRequestInfo> NextReceiveFilter { get; private set; }

    public void Reset()
    {
        _buffer.Clear();
        State = FilterState.Normal;
        NextReceiveFilter = null;
    }

    public FilterState State { get; private set; }

    private StringRequestInfo ResolveRequest()
    {
        var line = Encoding.UTF8.GetString(_buffer.ToArray()).TrimEnd('\r', '\n');
        var body = line.Length > 0 && line[0] == (char)_marker ? line.Substring(1).TrimStart() : line;
        return new StringRequestInfo(_key, body, body.Length == 0 ? Array.Empty<string>() : body.Split(' '));
    }
}
