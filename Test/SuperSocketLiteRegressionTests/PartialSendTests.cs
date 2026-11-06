using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

static class PartialSendTests
{
    public static void RealSendCompletionRetriesUnsentSegments()
    {
        // Seed a real completed SAEA with a known short prefix. This exercises the actual retry
        // callback deterministically instead of relying on the OS to produce a partial send.
        foreach (var prefixLength in new[] { 1, 4, 5, 8, 11 })
        {
            AssertPartialSend(prefixLength);
        }
    }

    private static void AssertPartialSend(int prefixLength)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        peer.ReceiveTimeout = 5000;
        peer.Connect(listener.LocalEndPoint!);
        using var sender = listener.Accept();
        using var receiveArgs = new SocketAsyncEventArgs();
        using var sendArgs = new SocketAsyncEventArgs();
        using var seedCompleted = new ManualResetEventSlim();

        var expected = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        sendArgs.BufferList = new List<ArraySegment<byte>> { new(expected, 0, prefixLength) };
        EventHandler<SocketAsyncEventArgs> seedHandler = (_, _) => seedCompleted.Set();
        sendArgs.Completed += seedHandler;
        if (sender.SendAsync(sendArgs))
        {
            Assert.True(seedCompleted.Wait(5000), "seed send should complete");
        }
        sendArgs.Completed -= seedHandler;
        Assert.Equal(SocketError.Success, sendArgs.SocketError, "seed send status");
        Assert.Equal(prefixLength, sendArgs.BytesTransferred, "seed send prefix length");

        var assembly = typeof(ISocketSession).Assembly;
        var sessionType = assembly.GetType("SuperSocketLite.SocketEngine.AsyncSocketSession", true)!;
        var baseType = sessionType.BaseType!;
        var proxyType = assembly.GetType("SuperSocketLite.SocketEngine.SocketAsyncEventArgsProxy", true)!;
        var queueType = assembly.GetType("SuperSocketLite.Common.ChannelSendingQueue", true)!;
        var proxy = Activator.CreateInstance(proxyType, new object[] { receiveArgs })!;
        var session = (ISocketSession)Activator.CreateInstance(sessionType, new object[] { sender, proxy, sendArgs })!;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        baseType.GetProperty("Config")!.SetValue(session, new ServerConfig());
        baseType.GetField("_sendQueue", fields)!.SetValue(session, Activator.CreateInstance(queueType, new object[] { 4 }));
        const int inSending = 1;
        baseType.GetField("_state", fields)!.SetValue(session, inSending);
        var batch = (List<ArraySegment<byte>>)baseType.GetField("_sendBatch", fields)!.GetValue(session)!;
        batch.Add(new ArraySegment<byte>(expected, 0, 4));
        batch.Add(new ArraySegment<byte>(expected, 4, 4));
        batch.Add(new ArraySegment<byte>(expected, 8, 4));
        sendArgs.BufferList = batch;
        sendArgs.UserToken = batch;
        var callback = sessionType.GetMethod("OnSendingCompleted", fields, null,
                new[] { typeof(object), typeof(SocketAsyncEventArgs) }, null)!
            .CreateDelegate<EventHandler<SocketAsyncEventArgs>>(session);
        sendArgs.Completed += callback;
        try
        {
            callback(sender, sendArgs);
            var received = new byte[expected.Length];
            var read = 0;
            while (read < received.Length)
            {
                var count = peer.Receive(received.AsSpan(read));
                Assert.True(count > 0, "partial send retry must deliver the full payload");
                read += count;
            }
            Assert.True(expected.SequenceEqual(received), "partial retry must neither duplicate nor omit bytes");
            LiveServerTests.WaitForCondition(() => session.IsSendIdle, "partial retry should complete the batch");
            Assert.True(sendArgs.UserToken == null && sendArgs.BufferList == null && sendArgs.Buffer == null,
                "completed send must detach its socket buffers");
        }
        finally
        {
            session.Close(CloseReason.ServerClosing);
            sendArgs.Completed -= callback;
        }
    }
}
