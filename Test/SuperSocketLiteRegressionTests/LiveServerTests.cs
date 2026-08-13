using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // Deliberately far below the pipe's back-pressure threshold: a pipelining client fills the
        // receive pipe with many complete requests, and MaxRequestLength must only be measured
        // against the incomplete tail (TODO-19), never against the whole buffer.
        config.MaxRequestLength = 1024;

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

            // TODO-06: sent bytes used to be recorded nowhere at all.
            var expectedBytes = (long)packetCount * (LiveEchoReceiveFilter.PacketHeaderSize + bodySize);

            WaitFor(
                () => server.TotalBytesSent >= expectedBytes,
                $"the server should report {expectedBytes} sent bytes, reported {server.TotalBytesSent}");

            Assert.Equal(expectedBytes, server.TotalBytesSent, "sent-byte metric should count each echoed packet exactly once");
            Assert.Equal(expectedBytes, server.TotalBytesReceived, "received-byte metric should count each request exactly once");
        });
    }

    /// <summary>
    /// TODO-04: receives are now processed inline on the IOCP completion thread. The opt-out
    /// (ServerConfig.ReceiveInlineOnIocpThread = false) must keep working.
    /// </summary>
    public static void EchoWorksWithIocpInliningDisabled()
    {
        const int packetCount = 200;
        const int bodySize = 64;

        var config = CreateConfig("inline-off-test");
        config.ReceiveInlineOnIocpThread = false;
        config.MaxRequestLength = 1024 * 1024;

        Assert.True(new ServerConfig().ReceiveInlineOnIocpThread, "inlining should be the default");

        RunWithServer(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, port);

            var stream = client.GetStream();
            stream.ReadTimeout = 30000;
            stream.WriteTimeout = 30000;

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];

            for (var i = 0; i < packetCount; i++)
            {
                stream.Write(BuildPacket(i, bodySize));
                ReadExactly(stream, header, header.Length);
                ReadExactly(stream, body, bodySize);

                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"echoed packet {i} should keep its order");
            }
        });
    }

    /// <summary>
    /// TODO-07: idle detection moved from DateTime.Now comparisons to Environment.TickCount64.
    /// </summary>
    public static void IdleSessionsAreClosedByTheClearIdleSessionTimer()
    {
        var config = CreateConfig("idle-timeout-test");
        config.ClearIdleSession = true;
        config.ClearIdleSessionInterval = 1;
        config.IdleSessionTimeOut = 1;

        RunWithServer(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);

            var session = WaitForSession(server);

            // The session never sends or receives anything, so the timer must reap it.
            WaitFor(
                () => !session.Connected,
                "an idle session should be closed by the ClearIdleSession timer",
                timeoutMs: 15000);
        });
    }

    /// <summary>
    /// TODO-07: LastActiveTime is now derived from a monotonic tick stamp; it must still behave
    /// like a UTC timestamp for both reads and writes.
    /// </summary>
    public static void LastActiveTimeRoundTripsThroughTheTickStamp()
    {
        var session = new LiveEchoSession();

        // MarkActive is internal to the library; it is the hot-path stamp used by Send and
        // ExecuteCommand, so drive it directly.
        var markActive = typeof(LiveEchoSession).GetMethod("MarkActive", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(markActive != null, "AppSession should expose an internal MarkActive hot-path stamp");

        var beforeMark = DateTime.UtcNow;
        markActive!.Invoke(session, Array.Empty<object>());
        var afterMark = DateTime.UtcNow;

        var lastActive = session.LastActiveTime;

        Assert.Equal(DateTimeKind.Utc, lastActive.Kind, "LastActiveTime should be expressed in UTC");
        Assert.True(
            lastActive >= beforeMark.AddMilliseconds(-50) && lastActive <= afterMark.AddMilliseconds(50),
            $"MarkActive should stamp the current time, got {lastActive:O} outside [{beforeMark:O}, {afterMark:O}]");

        var target = DateTime.UtcNow.AddSeconds(-30);
        session.LastActiveTime = target;

        var delta = Math.Abs((session.LastActiveTime - target).TotalMilliseconds);
        Assert.True(delta <= 50, $"an assigned LastActiveTime should round-trip within 50ms, drifted {delta}ms");

        Assert.True(session.StartTime.Kind == DateTimeKind.Utc, "StartTime should be expressed in UTC");
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

        // Construct a real session (so field initializers run) but skip Initialize: the null-socket
        // path must not touch the receive pipe or the send queue at all.
        var proxyType = Type.GetType("SuperSocketLite.SocketEngine.SocketAsyncEventArgsProxy, SuperSocketLite", throwOnError: true)!;
        using var probeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var saea = new SocketAsyncEventArgs();
        var proxy = Activator.CreateInstance(proxyType, new object[] { saea })!;
        var session = Activator.CreateInstance(sessionType, new object?[] { probeSocket, proxy, null })!;

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

    /// <summary>
    /// TODO-08: the pooled copy-on-send path must own its data - the caller's buffer is overwritten
    /// the instant the send call returns.
    /// </summary>
    public static void CopyOnSendIsUnaffectedByCallerBufferReuse()
    {
        RunEchoRoundTrip("copy-on-send-test", EchoSendMode.SendCopied, packetCount: 500, bodySize: 120);
    }

    /// <summary>
    /// TODO-09: the awaitable send path must deliver the same bytes, including when it has to wait
    /// for queue space.
    /// </summary>
    public static void AwaitableSendDeliversEveryPacket()
    {
        RunEchoRoundTrip("send-async-test", EchoSendMode.SendAsync, packetCount: 500, bodySize: 120, sendingQueueSize: 1);
    }

    private static void RunEchoRoundTrip(string name, EchoSendMode sendMode, int packetCount, int bodySize, int sendingQueueSize = 16)
    {
        var config = CreateConfig(name);
        config.SendingQueueSize = sendingQueueSize;
        config.MaxRequestLength = 4096;

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
                    stream.Write(BuildPacket(i, bodySize));

                stream.Flush();
            });

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];
            var expected = BuildPacket(0, bodySize);

            for (var i = 0; i < packetCount; i++)
            {
                ReadExactly(stream, header, header.Length);
                ReadExactly(stream, body, bodySize);

                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"echoed packet {i} should keep its order");

                // Everything after the sequence number is a fixed filler; a stale/clobbered buffer
                // would show up here as 0xEE.
                for (var b = sizeof(int); b < bodySize; b++)
                {
                    Assert.Equal(
                        expected[LiveEchoReceiveFilter.PacketHeaderSize + b],
                        body[b],
                        $"echoed packet {i} byte {b} should match the original payload");
                }
            }

            writer.GetAwaiter().GetResult();
        }, sendMode);
    }

    /// <summary>
    /// TODO-10: StopAsync must let the already queued responses reach the client before it closes
    /// the sessions.
    /// </summary>
    public static void StopAsyncDrainsQueuedSends()
    {
        const int responsePackets = 300;
        const int bodySize = 512;

        var config = CreateConfig("graceful-stop-test");
        config.SendingQueueSize = responsePackets + 16;
        config.MaxRequestLength = 4096;
        config.SendBufferSize = 2048;

        var server = new LiveEchoServer();
        var queued = new ManualResetEventSlim(false);

        server.RequestInterceptor = (session, _) =>
        {
            for (var i = 0; i < responsePackets; i++)
                session.Send(BuildPacket(i, bodySize), 0, LiveEchoReceiveFilter.PacketHeaderSize + bodySize);

            queued.Set();
            return true;
        };

        Assert.True(server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()), "server setup should succeed");
        Assert.True(server.Start(), "server should start");

        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, config.Port);

            var stream = client.GetStream();
            stream.ReadTimeout = 30000;

            stream.Write(BuildPacket(0, 8));
            stream.Flush();

            Assert.True(queued.Wait(10000), "the handler should have queued the whole response");

            var stopTask = server.StopAsync(TimeSpan.FromSeconds(15));

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];

            for (var i = 0; i < responsePackets; i++)
            {
                ReadExactly(stream, header, header.Length);
                ReadExactly(stream, body, bodySize);

                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"drained packet {i} should arrive in order");
            }

            stopTask.GetAwaiter().GetResult();
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// TODO-19: MaxRequestLength must still reject a single oversized request, even though it is no
    /// longer measured against the whole receive buffer.
    /// </summary>
    public static void OversizedSingleRequestIsStillRejected()
    {
        const int declaredSize = 8192;

        var config = CreateConfig("oversize-request-test");
        config.MaxRequestLength = 1024;

        RunWithServer(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, port);

            var stream = client.GetStream();
            stream.ReadTimeout = 15000;

            var oversized = new byte[declaredSize];
            BinaryPrimitives.WriteInt16LittleEndian(oversized, (short)declaredSize);

            try
            {
                stream.Write(oversized);
                stream.Flush();
            }
            catch (IOException)
            {
                // The server may have reset the connection mid-write, which is the expected outcome.
                return;
            }

            var session = server.GetAllSessions()?.FirstOrDefault();

            WaitFor(
                () => session == null || !session.Connected,
                "a request larger than MaxRequestLength should close the session",
                timeoutMs: 10000);
        });
    }

    /// <summary>
    /// TODO-16: with SyncSessionConnectedEvent the connected handler must run before the first
    /// request handler, even for a client that sends immediately after connecting.
    /// </summary>
    public static void SyncSessionConnectedEventOrdersBeforeFirstRequest()
    {
        var config = CreateConfig("event-order-test");
        config.SyncSessionConnectedEvent = true;
        config.MaxRequestLength = 4096;

        Assert.True(!new ServerConfig().SyncSessionConnectedEvent, "the ordering guarantee should be opt-in");

        var server = new LiveEchoServer();
        var events = new List<string>();
        var firstRequest = new ManualResetEventSlim(false);

        server.NewSessionConnected += _ =>
        {
            lock (events)
                events.Add("connected");
        };

        server.RequestInterceptor = (_, _) =>
        {
            lock (events)
                events.Add("request");

            firstRequest.Set();
            return true;
        };

        Assert.True(server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()), "server setup should succeed");
        Assert.True(server.Start(), "server should start");

        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, config.Port);

            var stream = client.GetStream();
            stream.Write(BuildPacket(0, 8));
            stream.Flush();

            Assert.True(firstRequest.Wait(10000), "the server should have processed the first request");

            lock (events)
            {
                Assert.True(events.Count >= 2, $"both events should have fired, saw {events.Count}");
                Assert.Equal("connected", events[0], "NewSessionConnected must run before the first request");
                Assert.Equal("request", events[1], "the first request must be handled after the connected event");
            }
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// TODO-12: connections refused by the connection limit must be counted.
    /// </summary>
    public static void RejectedSessionsAreCounted()
    {
        var config = CreateConfig("rejected-metric-test");
        config.MaxConnectionNumber = 1;

        RunWithServer(config, (server, port) =>
        {
            using var accepted = new TcpClient();
            accepted.Connect(IPAddress.Loopback, port);
            WaitForSession(server);

            using var refused = new TcpClient();
            refused.Connect(IPAddress.Loopback, port);

            WaitFor(
                () => server.TotalSessionsRejected > 0,
                $"the connection over the limit should be counted, counter is {server.TotalSessionsRejected}",
                timeoutMs: 10000);
        });
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

    private static void RunWithServer(ServerConfig config, Action<LiveEchoServer, int> body, EchoSendMode sendMode = EchoSendMode.Send)
    {
        var server = new LiveEchoServer(sendMode);

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

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 5000)
    {
        var timer = Stopwatch.StartNew();

        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return;

            Thread.Sleep(10);
        }

        if (!condition())
            throw new InvalidOperationException(message);
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

enum EchoSendMode
{
    /// <summary>Zero-copy Send(byte[], int, int).</summary>
    Send,

    /// <summary>Pooled copy-on-send; the test buffer is deliberately clobbered right after.</summary>
    SendCopied,

    /// <summary>Awaited ValueTask send.</summary>
    SendAsync
}

class LiveEchoServer : AppServer<LiveEchoSession, LiveEchoRequestInfo>
{
    private readonly EchoSendMode m_SendMode;

    // Reused across requests on purpose: with SendCopied the session must not depend on it.
    [ThreadStatic]
    private static byte[]? s_ScratchBuffer;

    /// <summary>Set by the test to have the handler queue extra traffic before returning.</summary>
    public Func<LiveEchoSession, LiveEchoRequestInfo, bool>? RequestInterceptor { get; set; }

    public LiveEchoServer()
        : this(EchoSendMode.Send)
    {
    }

    public LiveEchoServer(EchoSendMode sendMode)
        : base(new DefaultReceiveFilterFactory<LiveEchoReceiveFilter, LiveEchoRequestInfo>())
    {
        m_SendMode = sendMode;
        NewRequestReceived += OnRequestReceived;
    }

    private void OnRequestReceived(LiveEchoSession session, LiveEchoRequestInfo requestInfo)
    {
        var interceptor = RequestInterceptor;

        if (interceptor != null && interceptor(session, requestInfo))
            return;

        var totalSize = LiveEchoReceiveFilter.PacketHeaderSize + requestInfo.Body.Length;

        if (m_SendMode == EchoSendMode.SendCopied)
        {
            // Deliberately reuses one buffer: SendCopied must already own the bytes when it returns.
            var scratch = s_ScratchBuffer;

            if (scratch == null || scratch.Length < totalSize)
            {
                scratch = new byte[Math.Max(totalSize, 1024)];
                s_ScratchBuffer = scratch;
            }

            BinaryPrimitives.WriteInt16LittleEndian(scratch, (short)totalSize);
            requestInfo.Body.CopyTo(scratch, LiveEchoReceiveFilter.PacketHeaderSize);

            session.SendCopied(new ReadOnlySpan<byte>(scratch, 0, totalSize));

            // Poison it immediately - a correct copy-on-send is unaffected.
            scratch.AsSpan(0, totalSize).Fill(0xEE);
            return;
        }

        // Send and SendAsync are both zero-copy for array-backed data, so each response needs its
        // own buffer until the send completes.
        var packet = new byte[totalSize];
        BinaryPrimitives.WriteInt16LittleEndian(packet, (short)totalSize);
        requestInfo.Body.CopyTo(packet, LiveEchoReceiveFilter.PacketHeaderSize);

        if (m_SendMode == EchoSendMode.Send)
            session.Send(packet, 0, packet.Length);
        else
            session.SendAsync(packet).AsTask().GetAwaiter().GetResult();
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
