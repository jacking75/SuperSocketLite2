using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;
using SuperSocketLite.SocketEngine.Protocol;

/// <summary>
/// Tests that need a real listening server and a real TCP connection.
/// </summary>
static class LiveServerTests
{
    /// <summary>
    /// TODO-01: keep-alive used to be configured through an <c>#if WINDOWS</c> IOControl block that
    /// was never compiled, so no keep-alive option reached the accepted socket on any platform.
    /// </summary>
    public static void KeepAliveOptionsAreAppliedToAcceptedSockets()
    {
        const int keepAliveTime = 120;
        const int keepAliveInterval = 15;
        const int keepAliveRetryCount = 3;

        var config = CreateConfig("keepalive-test");
        config.KeepAliveTime = keepAliveTime;
        config.KeepAliveInterval = keepAliveInterval;
        config.KeepAliveRetryCount = keepAliveRetryCount;

        RunWithServer(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);

            var session = WaitForSession(server);
            var acceptedSocket = session.SocketSession.Client;

            Assert.True(acceptedSocket != null, "the accepted socket should still be alive");

            Assert.True(
                (int)acceptedSocket!.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)! != 0,
                "keep-alive should be enabled on the accepted socket");
            Assert.Equal(
                keepAliveTime,
                (int)acceptedSocket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!,
                "TcpKeepAliveTime should match ServerConfig.KeepAliveTime (seconds)");
            Assert.Equal(
                keepAliveInterval,
                (int)acceptedSocket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!,
                "TcpKeepAliveInterval should match ServerConfig.KeepAliveInterval (seconds)");
            Assert.Equal(
                keepAliveRetryCount,
                (int)acceptedSocket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!,
                "TcpKeepAliveRetryCount should match ServerConfig.KeepAliveRetryCount");
        });
    }

    /// <summary>
    /// TODO-01: the UDP listener used to call the Windows-only SIO_UDP_CONNRESET ioctl unguarded,
    /// which made Start() fail outright on Linux.
    /// </summary>
    public static void UdpListenerStartsOnEveryPlatform()
    {
        var port = GetFreePort(ProtocolType.Udp);

        var config = new ServerConfig
        {
            Ip = "127.0.0.1",
            Port = port,
            Mode = SocketMode.Udp,
            Name = "udp-start-test",
            MaxConnectionNumber = 16,
            ClearIdleSession = false
        };

        var server = new LiveEchoServer();

        Assert.True(
            server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()),
            "UDP server setup should succeed");

        try
        {
            Assert.True(server.Start(), "UDP listener should start on every platform");
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// TODO-03: a loopback connection completes most receives synchronously. The receive path used
    /// to recurse (StartReceive -> ProcessReceive -> StartReceive), so a sustained burst could
    /// exhaust the stack. The loop rewrite must still deliver every request in order.
    /// </summary>
    public static void LoopbackEchoSurvivesSynchronousCompletionBurst()
    {
        const int packetCount = 4000;
        const int bodySize = 240;

        var config = CreateConfig("echo-burst-test");
        config.SendingQueueSize = 200;

        // The sequence receive path compares MaxRequestLength against the whole pending pipe
        // buffer, not against the current partial request, so a pipelining client trips it once
        // the pipe reaches its 64KB pause threshold. Keep the limit above that so this test stays
        // focused on the receive loop.
        config.MaxRequestLength = 1024 * 1024;

        RunWithServer(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, port);

            var stream = client.GetStream();
            stream.ReadTimeout = 30000;
            stream.WriteTimeout = 30000;

            var writer = Task.Run(() =>
            {
                for (var i = 0; i < packetCount; i++)
                {
                    stream.Write(BuildPacket(i, bodySize));
                }

                stream.Flush();
            });

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];

            for (var i = 0; i < packetCount; i++)
            {
                ReadExactly(stream, header, header.Length);

                var totalSize = BinaryPrimitives.ReadInt16LittleEndian(header);
                Assert.Equal(
                    LiveEchoReceiveFilter.PacketHeaderSize + bodySize,
                    totalSize,
                    $"echoed packet {i} should keep its size");

                ReadExactly(stream, body, bodySize);
                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"echoed packet {i} should keep its order");
            }

            writer.GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// TODO-02: SendSync returned without ending the send when another thread had already dropped
    /// the socket, which left SocketState.InSending latched forever and stopped the Closed event
    /// (and therefore the pooled SocketAsyncEventArgs) from ever being released.
    /// </summary>
    public static void SendSyncClearsInSendingWhenSocketIsAlreadyGone()
    {
        var sessionType = Type.GetType("SuperSocketLite.SocketEngine.AsyncSocketSession, SuperSocketLite", throwOnError: true)!;
        var socketSessionType = sessionType.BaseType!;

        var stateField = socketSessionType.GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(stateField != null, "SocketSession should keep its state in m_State");

        var clientField = socketSessionType.GetField("m_Client", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(clientField != null, "SocketSession should keep the socket in m_Client");

        var sendSync = sessionType.GetMethod("SendSync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(sendSync != null, "AsyncSocketSession should implement SendSync");

        // Build the session without running any constructor: the test only drives the state machine,
        // and the null-socket path must not touch the pipe / send queue at all.
        var session = RuntimeHelpers.GetUninitializedObject(sessionType);

        const int inSending = 1;
        const int inClosing = 16;

        stateField!.SetValue(session, inSending);
        clientField!.SetValue(session, null);

        sendSync!.Invoke(session, new object[]
        {
            new List<ArraySegment<byte>> { new ArraySegment<byte>(new byte[] { 1, 2, 3 }) }
        });

        var state = (int)stateField.GetValue(session)!;

        Assert.Equal(0, state & inSending, "SendSync must release InSending when the socket has been dropped");
        Assert.Equal(inClosing, state & inClosing, "SendSync should push the session into the closing procedure");
    }

    private static ServerConfig CreateConfig(string name)
    {
        return new ServerConfig
        {
            Ip = "127.0.0.1",
            Port = GetFreePort(ProtocolType.Tcp),
            Mode = SocketMode.Tcp,
            Name = name,
            MaxConnectionNumber = 16,
            ClearIdleSession = false,
            DisableSessionSnapshot = true
        };
    }

    private static void RunWithServer(ServerConfig config, Action<LiveEchoServer, int> body)
    {
        var server = new LiveEchoServer();

        Assert.True(
            server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()),
            $"server setup should succeed for {config.Name}");
        Assert.True(server.Start(), $"server should start for {config.Name}");

        try
        {
            body(server, config.Port);
        }
        finally
        {
            server.Stop();
        }
    }

    private static LiveEchoSession WaitForSession(LiveEchoServer server)
    {
        var timeout = Stopwatch.StartNew();

        while (timeout.ElapsedMilliseconds < 5000)
        {
            var session = server.GetAllSessions()?.FirstOrDefault();

            if (session != null)
                return session;

            Thread.Sleep(10);
        }

        throw new InvalidOperationException("the server did not register the connected session in time");
    }

    private static int GetFreePort(ProtocolType protocol)
    {
        using var probe = new Socket(
            AddressFamily.InterNetwork,
            protocol == ProtocolType.Udp ? SocketType.Dgram : SocketType.Stream,
            protocol);

        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    private static byte[] BuildPacket(int sequence, int bodySize)
    {
        var totalSize = LiveEchoReceiveFilter.PacketHeaderSize + bodySize;
        var packet = new byte[totalSize];

        BinaryPrimitives.WriteInt16LittleEndian(packet, (short)totalSize);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(LiveEchoReceiveFilter.PacketHeaderSize), sequence);

        for (var i = LiveEchoReceiveFilter.PacketHeaderSize + sizeof(int); i < totalSize; i++)
            packet[i] = (byte)i;

        return packet;
    }

    private static void ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        var read = 0;

        while (read < count)
        {
            var received = stream.Read(buffer, read, count - read);

            if (received <= 0)
                throw new InvalidOperationException("the connection was closed before the expected data arrived");

            read += received;
        }
    }
}

sealed class LiveEchoRequestInfo : BinaryRequestInfo
{
    public LiveEchoRequestInfo(byte[] body)
        : base(string.Empty, body)
    {
    }
}

sealed class LiveEchoReceiveFilter : FixedHeaderSequenceReceiveFilter<LiveEchoRequestInfo>
{
    /// <summary>Little-endian total packet size, header included.</summary>
    public const int PacketHeaderSize = 2;

    public LiveEchoReceiveFilter()
        : base(PacketHeaderSize)
    {
    }

    protected override int GetBodyLengthFromHeader(ReadOnlySequence<byte> header)
    {
        Span<byte> headerBuffer = stackalloc byte[PacketHeaderSize];
        header.CopyTo(headerBuffer);

        return BinaryPrimitives.ReadInt16LittleEndian(headerBuffer) - PacketHeaderSize;
    }

    protected override LiveEchoRequestInfo ResolveRequestInfo(ReadOnlySequence<byte> header, ReadOnlySequence<byte> body)
    {
        return new LiveEchoRequestInfo(body.ToArray());
    }
}

class LiveEchoSession : AppSession<LiveEchoSession, LiveEchoRequestInfo>
{
}

class LiveEchoServer : AppServer<LiveEchoSession, LiveEchoRequestInfo>
{
    public LiveEchoServer()
        : base(new DefaultReceiveFilterFactory<LiveEchoReceiveFilter, LiveEchoRequestInfo>())
    {
        NewRequestReceived += OnRequestReceived;
    }

    private static void OnRequestReceived(LiveEchoSession session, LiveEchoRequestInfo requestInfo)
    {
        var totalSize = LiveEchoReceiveFilter.PacketHeaderSize + requestInfo.Body.Length;
        var packet = new byte[totalSize];

        BinaryPrimitives.WriteInt16LittleEndian(packet, (short)totalSize);
        requestInfo.Body.CopyTo(packet, LiveEchoReceiveFilter.PacketHeaderSize);

        session.Send(packet, 0, packet.Length);
    }
}

sealed class SilentLogFactory : ILogFactory
{
    public ILog GetLog(string name) =>
        Environment.GetEnvironmentVariable("SSLITE_TEST_VERBOSE") == "1"
            ? new ConsoleLog(name)
            : new SilentLog();
}

sealed class SilentLog : ILog
{
    public bool IsDebugEnabled => false;

    public bool IsErrorEnabled => false;

    public bool IsFatalEnabled => false;

    public bool IsInfoEnabled => false;

    public bool IsWarnEnabled => false;

    public void Debug(string message) { }

    public void Error(string message) { }

    public void Error(string message, Exception exception) { }

    public void Fatal(string message) { }

    public void Fatal(string message, Exception exception) { }

    public void Info(string message) { }

    public void Warn(string message) { }
}

static class Assert
{
    public static void True(bool actual, string message)
    {
        if (!actual)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException($"{message}. Expected {expected}, actual {actual}.");
    }
}
