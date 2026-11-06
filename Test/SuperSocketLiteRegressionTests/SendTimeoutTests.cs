using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

static class SendTimeoutTests
{
    public static void TimeoutKeepsInFlightResourcesOwned()
    {
        foreach (var udp in new[] { false, true })
        {
            foreach (var alreadyClosing in new[] { false, true })
            {
                AssertTimeoutOwnership(udp, alreadyClosing);
            }
        }
    }

    private static void AssertTimeoutOwnership(bool udp, bool alreadyClosing)
    {
        using var socket = new Socket(AddressFamily.InterNetwork,
            udp ? SocketType.Dgram : SocketType.Stream,
            udp ? ProtocolType.Udp : ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var receiveArgs = new SocketAsyncEventArgs();
        using var sendArgs = new SocketAsyncEventArgs();
        var assembly = typeof(ISocketSession).Assembly;
        var sessionType = assembly.GetType("SuperSocketLite.SocketEngine." + (udp ? "UdpSocketSession" : "AsyncSocketSession"), true)!;
        var baseType = sessionType.BaseType!;
        var proxyType = assembly.GetType("SuperSocketLite.SocketEngine.SocketAsyncEventArgsProxy", true)!;
        var queueType = assembly.GetType("SuperSocketLite.Common.ChannelSendingQueue", true)!;
        var session = (ISocketSession)(udp
            ? Activator.CreateInstance(sessionType, socket, new IPEndPoint(IPAddress.Loopback, 12345))!
            : Activator.CreateInstance(sessionType, socket, Activator.CreateInstance(proxyType, receiveArgs)!, sendArgs)!);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        baseType.GetProperty("Config")!.SetValue(session, new ServerConfig());
        baseType.GetField("_sendQueue", fields)!.SetValue(session, Activator.CreateInstance(queueType, 4));
        // Pause at the completion boundary: the native operation may have finished, but its
        // callback still owns the batch and must run before Closed can return the SAEA to a pool.
        const int inSending = 1;
        baseType.GetField("_state", fields)!.SetValue(session, inSending);
        var batch = (List<ArraySegment<byte>>)baseType.GetField("_sendBatch", fields)!.GetValue(session)!;
        batch.Add(new ArraySegment<byte>(new byte[16]));
        var closedCount = 0;
        var closeReason = CloseReason.Unknown;
        session.Closed += (_, reason) =>
        {
            closedCount++;
            closeReason = reason;
        };

        if (alreadyClosing)
        {
            session.Close(CloseReason.ServerClosing);
        }
        session.SendEndWhenSendingTimeOut();
        session.Close(CloseReason.TimeOut);

        Assert.Equal(0, closedCount, "a timeout must not publish Closed before send completion");
        Assert.True(!session.IsSendIdle, "the completion callback must retain send ownership");
        if (udp)
        {
            Assert.True(!socket.SafeHandle.IsClosed, "a UDP timeout must not close the shared listener");
        }
        else
        {
            Assert.True(session.Client == null, "a TCP timeout must abort the stalled native send");
            Assert.True(sessionType.GetProperty("SendSAEA")!.GetValue(session) == null,
                "the send SAEA must not be available to the pool before completion");
        }

        baseType.GetMethod("OnSendError", fields)!.Invoke(session, new object[] { batch, CloseReason.SocketError });
        Assert.Equal(1, closedCount, "send completion should close exactly once");
        Assert.Equal(alreadyClosing ? CloseReason.ServerClosing : CloseReason.TimeOut, closeReason,
            "the first close reason must survive the aborted send");
        Assert.Equal(0, batch.Count, "completion must release the payload references");
        session.SendEndWhenSendingTimeOut();
        Assert.Equal(1, closedCount, "a repeated timeout must not close twice");
    }

    public static void TimeoutAbortsStalledTcpSend()
    {
        var config = LiveServerTests.CreateConfigForTests("stalled-send-timeout");
        config.SendBufferSize = 1024;
        LiveServerTests.RunWithServerForTests(config, (server, port) =>
        {
            using var client = new TcpClient { ReceiveBufferSize = 1024 };
            client.Connect(IPAddress.Loopback, port);
            LiveEchoSession? session = null;
            LiveServerTests.WaitForCondition(() =>
            {
                foreach (var candidate in server.GetAllSessions()!)
                {
                    session = candidate;
                    return true;
                }
                return false;
            }, "the stalled peer should register");
            var closedCount = 0;
            var closeReason = CloseReason.Unknown;
            server.SessionClosed += (_, reason) =>
            {
                closeReason = reason;
                Interlocked.Increment(ref closedCount);
            };
            var payload = new byte[64 * 1024];
            for (var i = 0; i < 128; i++)
            {
                Assert.True(session!.TrySend(payload, 0, payload.Length), "large send should enqueue");
                if (!session.SocketSession.IsSendIdle)
                {
                    break;
                }
            }
            Assert.True(session!.Connected, "the stalled peer must remain connected before the timeout");
            Assert.True(!session.SocketSession.IsSendIdle, "the peer must leave a send in flight");
            session.SendEndWhenSendingTimeOut();
            session.Close(CloseReason.TimeOut);
            LiveServerTests.WaitForCondition(() => Volatile.Read(ref closedCount) == 1,
                "timeout should abort the socket and release both I/O completions");
            Assert.Equal(CloseReason.TimeOut, closeReason, "the abort should preserve the timeout reason");
            Assert.True(session.SocketSession.IsSendIdle, "the aborted send must finish");
        });
    }
}
