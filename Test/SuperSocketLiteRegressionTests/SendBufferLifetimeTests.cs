using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

static class SendBufferLifetimeTests
{
    public static void IdleUdpSessionDoesNotRetainCompletedSendPayload()
    {
        const int payloadSize = 32 * 1024;
        var config = LiveServerTests.CreateConfigForTests("udp-send-payload-lifetime");
        config.Mode = SocketMode.Udp;
        using (var portProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            config.Port = ((IPEndPoint)portProbe.Client.LocalEndPoint!).Port;
        }
        var server = new LiveEchoServer();
        WeakReference<byte[]>? payloadReference = null;
        LiveEchoSession? sendingSession = null;
        server.RequestInterceptor = (session, _) =>
        {
            sendingSession = session;
            Volatile.Write(ref payloadReference, SendPayload(session, payloadSize));
            return true;
        };
        Assert.True(server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()), "UDP lifetime server setup");
        Assert.True(server.Start(), "UDP lifetime server start");
        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 10000;
            client.Connect(IPAddress.Loopback, config.Port);
            client.Send(LiveServerTests.BuildPacketForTests(0, 8));
            IPEndPoint remote = new(IPAddress.Any, 0);
            var received = client.Receive(ref remote);
            Assert.Equal(payloadSize, received.Length, "UDP payload length");
            Assert.True(Array.TrueForAll(received, b => b == 0x5A), "UDP payload integrity");
            LiveServerTests.WaitForCondition(
                () => Volatile.Read(ref payloadReference) != null && sendingSession!.SocketSession.IsSendIdle,
                "UDP send should complete");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(!IsAlive(payloadReference!), "idle UDP session and SAEA must release the completed payload");
            Assert.True(sendingSession!.Connected, "UDP payload release must not close its session");
        }
        finally
        {
            server.Stop();
        }
    }

    public static void IdleSessionDoesNotRetainCompletedSendPayload()
    {
        const int payloadSize = 128 * 1024;
        var config = LiveServerTests.CreateConfigForTests("send-payload-lifetime");
        var server = new LiveEchoServer();
        WeakReference<byte[]>? payloadReference = null;
        LiveEchoSession? sendingSession = null;
        server.RequestInterceptor = (session, _) =>
        {
            sendingSession = session;
            Volatile.Write(ref payloadReference, SendPayload(session, payloadSize));
            return true;
        };
        Assert.True(server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()), "lifetime server setup");
        Assert.True(server.Start(), "lifetime server start");
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, config.Port);
            var stream = client.GetStream();
            stream.ReadTimeout = 10000;
            stream.Write(LiveServerTests.BuildPacketForTests(0, 8));
            var received = new byte[payloadSize];
            LiveServerTests.ReadExactlyForTests(stream, received, received.Length);
            Assert.True(Array.TrueForAll(received, b => b == 0x5A), "the payload must remain intact until sent");
            LiveServerTests.WaitForCondition(
                () => Volatile.Read(ref payloadReference) != null && sendingSession!.SocketSession.IsSendIdle,
                "the send must complete before checking the payload lifetime");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(!IsAlive(payloadReference!), "an idle, connected session must not retain its last completed large payload");
            Assert.True(sendingSession!.Connected, "payload release must not require closing the session");
        }
        finally
        {
            server.Stop();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<byte[]> SendPayload(LiveEchoSession session, int size)
    {
        var payload = new byte[size];
        Array.Fill(payload, (byte)0x5A);
        session.Send(payload, 0, payload.Length);
        return new WeakReference<byte[]>(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<byte[]> reference) => reference.TryGetTarget(out _);
}
