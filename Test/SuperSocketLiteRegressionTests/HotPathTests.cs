using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

/// <summary>
/// Tests for the hot-path changes: the pooled SocketAsyncEventArgs hand-back, the send-completion
/// fast path, the parallel accept loops and the zero-byte receive mode.
/// </summary>
static class HotPathTests
{
    /// <summary>
    /// The request counter is fed from the request-processing tasks and the byte counters from the
    /// IOCP and send-completion threads, so all three are updated concurrently on every packet.
    /// They have to come out of a real echo round trip exact, not approximately - the load-test
    /// reports are read off them.
    /// </summary>
    public static void ServerTotalsStayExactUnderConcurrentUpdates()
    {
        const int packetCount = 1500;
        const int bodySize = 96;

        var config = LiveServerTests.CreateConfigForTests("exact-totals-test");
        config.MaxRequestLength = 4096;
        config.SendingQueueSize = 256;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
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
                    stream.Write(LiveServerTests.BuildPacketForTests(i, bodySize));
                }

                stream.Flush();
            });

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];

            for (var i = 0; i < packetCount; i++)
            {
                LiveServerTests.ReadExactlyForTests(stream, header, header.Length);
                LiveServerTests.ReadExactlyForTests(stream, body, bodySize);
            }

            writer.GetAwaiter().GetResult();

            var expectedBytes = (long)packetCount * (LiveEchoReceiveFilter.PacketHeaderSize + bodySize);

            LiveServerTests.WaitForCondition(
                () => server.TotalBytesSent >= expectedBytes && server.HandledRequests >= packetCount,
                "the server should have accounted for every echoed packet");

            Assert.Equal(expectedBytes, server.TotalBytesReceived, "received bytes must be counted exactly once");
            Assert.Equal(expectedBytes, server.TotalBytesSent, "sent bytes must be counted exactly once");
            Assert.Equal((long)packetCount, server.HandledRequests, "handled requests must be counted exactly once");
        });
    }

    /// <summary>
    /// P2: a finished send batch no longer takes SyncRoot unless the session is closing. A close
    /// racing against sends that are still draining must therefore still fire exactly once, and
    /// must still hand the session's pooled SocketAsyncEventArgs back.
    /// </summary>
    /// <remarks>
    /// A round waits for the session to be gone before the next one connects. The client observes
    /// the socket closing slightly before the server has finished tearing the session down, so
    /// reconnecting immediately would only exhaust the connection limit and would prove nothing
    /// about the close. A session that failed to close is still caught: the wait would time out,
    /// and the connection limit is small enough that a leaked pool entry shows up as a rejection.
    /// </remarks>
    public static void ClosingWhileSendsAreDrainingFiresExactlyOnce()
    {
        const int rounds = 150;
        const int burstSize = 32;
        const int bodySize = 64;

        var config = LiveServerTests.CreateConfigForTests("close-race-test");
        config.MaxConnectionNumber = 8;
        config.SendingQueueSize = burstSize * 2;
        config.MaxRequestLength = 4096;

        var server = new LiveEchoServer();
        var closedCount = 0;

        server.SessionClosed += (_, _) => Interlocked.Increment(ref closedCount);

        // Queue a burst and then close on top of it, so the close lands while InSending is still set.
        server.RequestInterceptor = (session, _) =>
        {
            for (var i = 0; i < burstSize; i++)
            {
                session.TrySend(new ArraySegment<byte>(LiveServerTests.BuildPacketForTests(i, bodySize)));
            }

            session.Close(CloseReason.ServerClosing);
            return true;
        };

        Assert.True(
            server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()),
            "server setup should succeed");
        Assert.True(server.Start(), "server should start");

        try
        {
            for (var round = 0; round < rounds; round++)
            {
                var roundNumber = round + 1;

                using (var client = new TcpClient())
                {
                    client.NoDelay = true;
                    client.Connect(IPAddress.Loopback, config.Port);

                    var stream = client.GetStream();
                    stream.ReadTimeout = 15000;

                    stream.Write(LiveServerTests.BuildPacketForTests(0, 8));
                    stream.Flush();

                    DrainUntilClosed(stream);
                }

                LiveServerTests.WaitForCondition(
                    () => Volatile.Read(ref closedCount) >= roundNumber,
                    $"round {roundNumber} should have closed its session, saw {Volatile.Read(ref closedCount)} closes",
                    timeoutMs: 15000);
            }

            Assert.Equal(rounds, Volatile.Read(ref closedCount), "a session must fire its close exactly once");

            Assert.Equal(
                0L,
                server.TotalSessionsRejected,
                "no round should have been refused, which is what a leaked pool entry would cause");

            LiveServerTests.WaitForCondition(
                () => server.SessionCount == 0,
                $"every session should have been removed, {server.SessionCount} left");
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>
    /// A closed session has to hand its pooled SocketAsyncEventArgs back, both of them. The sending
    /// one was being dropped: the field holding it is cleared before the close handlers run, and the
    /// handler read that same cleared field, so it found nothing to return. A server therefore
    /// stopped accepting once it had served MaxConnectionNumber connections in total, however
    /// long ago those had disconnected.
    /// </summary>
    public static void ClosedSessionsReturnBothPooledSocketEventArgs()
    {
        const int maxConnections = 4;
        const int rounds = maxConnections * 4;

        var config = LiveServerTests.CreateConfigForTests("pool-return-test");
        config.MaxConnectionNumber = maxConnections;
        config.MaxRequestLength = 4096;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            for (var round = 1; round <= rounds; round++)
            {
                using (var client = new TcpClient { NoDelay = true })
                {
                    client.Connect(IPAddress.Loopback, port);
                    AssertEchoRoundTrip(client, sequence: round, bodySize: 32);
                }

                // Wait for the session to be fully gone, so the next round genuinely depends on the
                // pooled instances coming back rather than on spare capacity.
                LiveServerTests.WaitForCondition(
                    () => server.SessionCount == 0,
                    $"round {round} should have released its session",
                    timeoutMs: 15000);
            }

            Assert.Equal(
                0L,
                server.TotalSessionsRejected,
                $"{rounds} sequential connections must fit through a pool of {maxConnections}");
        });
    }

    /// <summary>
    /// P3: several accept loops share one listening socket. Every connection of a burst must still
    /// become exactly one registered session.
    /// </summary>
    public static void ParallelAcceptLoopsRegisterEveryConnection()
    {
        const int connectionCount = 64;

        Assert.Equal(1, new ServerConfig().AcceptLoopCount, "a single accept loop should stay the default");

        var config = LiveServerTests.CreateConfigForTests("accept-loops-test");
        config.AcceptLoopCount = 4;
        config.MaxConnectionNumber = connectionCount * 2;
        config.MaxRequestLength = 4096;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            var clients = new TcpClient[connectionCount];
            var connects = new Task[connectionCount];

            try
            {
                for (var i = 0; i < connectionCount; i++)
                {
                    var index = i;

                    connects[i] = Task.Run(() =>
                    {
                        var client = new TcpClient { NoDelay = true };
                        client.Connect(IPAddress.Loopback, port);
                        clients[index] = client;
                    });
                }

                Task.WaitAll(connects, TimeSpan.FromSeconds(30));

                LiveServerTests.WaitForCondition(
                    () => server.SessionCount == connectionCount,
                    $"every connection should be registered, {server.SessionCount} of {connectionCount}",
                    timeoutMs: 20000);

                // Each accepted socket must be a working session, not just a counted one.
                for (var i = 0; i < connectionCount; i++)
                {
                    AssertEchoRoundTrip(clients[i], sequence: i, bodySize: 32);
                }
            }
            finally
            {
                foreach (var client in clients)
                {
                    client?.Dispose();
                }
            }
        });
    }

    /// <summary>
    /// P3: an out-of-range accept loop count is clamped rather than taken literally. Zero loops
    /// would mean a listener that accepts nothing at all.
    /// </summary>
    public static void AcceptLoopCountIsClampedIntoRange()
    {
        var config = LiveServerTests.CreateConfigForTests("accept-loops-clamp-test");
        config.AcceptLoopCount = 0;
        config.MaxRequestLength = 4096;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            using var client = new TcpClient { NoDelay = true };
            client.Connect(IPAddress.Loopback, port);

            AssertEchoRoundTrip(client, sequence: 1, bodySize: 32);
        });
    }

    /// <summary>
    /// P4: with the zero-byte receive mode a session waits on an empty buffer and only takes a real
    /// one once data has arrived. The echo path must be unchanged by that extra step.
    /// </summary>
    public static void ZeroByteReceiveEchoesEveryPacket()
    {
        const int packetCount = 800;
        const int bodySize = 120;

        Assert.True(!new ServerConfig().UseZeroByteReceive, "the zero-byte receive mode should be opt-in");

        var config = LiveServerTests.CreateConfigForTests("zero-byte-echo-test");
        config.UseZeroByteReceive = true;
        config.MaxRequestLength = 4096;
        config.SendingQueueSize = 256;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
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
                    stream.Write(LiveServerTests.BuildPacketForTests(i, bodySize));
                }

                stream.Flush();
            });

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];

            for (var i = 0; i < packetCount; i++)
            {
                LiveServerTests.ReadExactlyForTests(stream, header, header.Length);
                LiveServerTests.ReadExactlyForTests(stream, body, bodySize);

                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"echoed packet {i} should keep its order");
            }

            writer.GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// P4: a session that has been sitting on a zero-byte receive for a while must wake up and
    /// serve the next request. This is the case the mode exists for.
    /// </summary>
    public static void ZeroByteReceiveResumesAfterAnIdlePeriod()
    {
        var config = LiveServerTests.CreateConfigForTests("zero-byte-idle-test");
        config.UseZeroByteReceive = true;
        config.MaxRequestLength = 4096;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            using var client = new TcpClient { NoDelay = true };
            client.Connect(IPAddress.Loopback, port);

            AssertEchoRoundTrip(client, sequence: 1, bodySize: 48);

            // Long enough that the session is certainly parked on a probe rather than mid-burst.
            Thread.Sleep(1500);

            AssertEchoRoundTrip(client, sequence: 2, bodySize: 48);

            Thread.Sleep(1500);

            AssertEchoRoundTrip(client, sequence: 3, bodySize: 48);
        });
    }

    /// <summary>
    /// P4: a zero-byte receive completes both when data arrives and when the peer goes away, so the
    /// close must still be detected - it is the following real receive that tells them apart.
    /// </summary>
    public static void ZeroByteReceiveStillDetectsAClientClose()
    {
        var config = LiveServerTests.CreateConfigForTests("zero-byte-close-test");
        config.UseZeroByteReceive = true;
        config.MaxRequestLength = 4096;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            var client = new TcpClient { NoDelay = true };
            client.Connect(IPAddress.Loopback, port);

            AssertEchoRoundTrip(client, sequence: 1, bodySize: 48);

            LiveServerTests.WaitForCondition(() => server.SessionCount == 1, "the session should be registered");

            client.Close();

            LiveServerTests.WaitForCondition(
                () => server.SessionCount == 0,
                "closing the client should close the session even while it waits on a zero-byte receive",
                timeoutMs: 15000);
        });
    }

    /// <summary>
    /// P4: a payload far larger than one receive buffer spans several real receives, each of which
    /// has to be preceded by its own probe without losing or duplicating a byte.
    /// </summary>
    public static void ZeroByteReceiveHandlesPayloadsLargerThanTheBuffer()
    {
        const int bodySize = 20000;
        const int packetCount = 20;

        var config = LiveServerTests.CreateConfigForTests("zero-byte-large-test");
        config.UseZeroByteReceive = true;
        config.ReceiveBufferSize = 2048;
        config.MaxRequestLength = 65536;
        config.SendingQueueSize = 64;

        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            client.Connect(IPAddress.Loopback, port);

            var stream = client.GetStream();
            stream.ReadTimeout = 30000;
            stream.WriteTimeout = 30000;

            var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
            var body = new byte[bodySize];
            var expected = LiveServerTests.BuildPacketForTests(0, bodySize);

            for (var i = 0; i < packetCount; i++)
            {
                stream.Write(LiveServerTests.BuildPacketForTests(i, bodySize));
                stream.Flush();

                LiveServerTests.ReadExactlyForTests(stream, header, header.Length);
                LiveServerTests.ReadExactlyForTests(stream, body, bodySize);

                Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(body), $"large packet {i} should keep its order");

                for (var b = sizeof(int); b < bodySize; b++)
                {
                    if (body[b] != expected[LiveEchoReceiveFilter.PacketHeaderSize + b])
                    {
                        throw new InvalidOperationException($"large packet {i} differs at byte {b}");
                    }
                }
            }
        });
    }

    private static void AssertEchoRoundTrip(TcpClient client, int sequence, int bodySize)
    {
        var stream = client.GetStream();
        stream.ReadTimeout = 20000;
        stream.WriteTimeout = 20000;

        stream.Write(LiveServerTests.BuildPacketForTests(sequence, bodySize));
        stream.Flush();

        var header = new byte[LiveEchoReceiveFilter.PacketHeaderSize];
        var body = new byte[bodySize];

        LiveServerTests.ReadExactlyForTests(stream, header, header.Length);
        LiveServerTests.ReadExactlyForTests(stream, body, bodySize);

        Assert.Equal(sequence, BinaryPrimitives.ReadInt32LittleEndian(body), "the echo should carry the sequence back");
    }

    /// <summary>Reads until the server closes the connection, ignoring what it sent.</summary>
    private static void DrainUntilClosed(NetworkStream stream)
    {
        var scratch = new byte[4096];

        try
        {
            while (stream.Read(scratch, 0, scratch.Length) > 0)
            {
            }
        }
        catch (IOException)
        {
            // An abortive close from the other side is an equally valid end of the round.
        }
    }
}

