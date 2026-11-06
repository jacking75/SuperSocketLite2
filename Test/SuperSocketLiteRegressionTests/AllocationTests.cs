using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

static class AllocationTests
{
    private static readonly Type s_serverType = typeof(AppServer<LiveEchoSession, LiveEchoRequestInfo>);
    private static readonly FieldInfo s_snapshotField = s_serverType.GetField("_sessionsSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_takeSnapshot = s_serverType.GetMethod("TakeSessionSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Func<IList<ArraySegment<byte>>, int, IList<ArraySegment<byte>>> s_trim =
        typeof(ISocketSession).Assembly.GetType("SuperSocketLite.SocketEngine.AsyncSocketSession", true)!
            .GetMethod("TrimSegments", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<IList<ArraySegment<byte>>, int, IList<ArraySegment<byte>>>>();

    public static void UnchangedSessionSnapshotDoesNotAllocate()
    {
        var server = new SnapshotServer();
        for (var i = 0; i < 10000; i++)
        {
            server.Add(i.ToString());
        }
        var takeSnapshot = s_takeSnapshot.CreateDelegate<Action>(server);
        takeSnapshot();
        var original = s_snapshotField.GetValue(server);
        var allocated = Measure("snapshot: 10,000 unchanged sessions", takeSnapshot, 1000);
        Assert.Equal(0L, allocated, "unchanged snapshots should not allocate another large array");
        Assert.True(ReferenceEquals(original, s_snapshotField.GetValue(server)), "an unchanged snapshot should be reused");
    }

    public static void SnapshotRefreshPreservesOldReadersAndMembership()
    {
        var server = new SnapshotServer();
        var first = server.Add("first");
        var takeSnapshot = s_takeSnapshot.CreateDelegate<Action>(server);
        takeSnapshot();
        var old = (KeyValuePair<string, LiveEchoSession>[])s_snapshotField.GetValue(server)!;

        // Remove and add with the same count: count alone cannot be a cache key.
        server.Remove(first);
        var second = server.Add("second");
        takeSnapshot();
        var current = (KeyValuePair<string, LiveEchoSession>[])s_snapshotField.GetValue(server)!;
        Assert.Equal(1, current.Length, "snapshot membership count");
        Assert.True(ReferenceEquals(second, current[0].Value), "replacement session must become visible");
        Assert.True(ReferenceEquals(first, old[0].Value), "an existing reader must retain its immutable snapshot");

        server.Remove(second);
        takeSnapshot();
        Assert.Equal(0, ((Array)s_snapshotField.GetValue(server)!).Length, "removing the last session must publish an empty snapshot");
    }

    public static void SnapshotRefreshSurvivesConcurrentChurn()
    {
        var server = new SnapshotServer();
        var takeSnapshot = s_takeSnapshot.CreateDelegate<Action>(server);
        var writers = Task.Run(() => Parallel.For(0, 2000, i =>
        {
            var session = server.Add(i.ToString());
            if ((i & 1) == 0)
            {
                server.Remove(session);
            }
        }));
        while (!writers.IsCompleted)
        {
            takeSnapshot();
        }
        writers.GetAwaiter().GetResult();
        takeSnapshot();
        var snapshot = (KeyValuePair<string, LiveEchoSession>[])s_snapshotField.GetValue(server)!;
        Assert.Equal(1000, snapshot.Length, "the next snapshot after churn must contain every remaining session");
        Assert.True(snapshot.All(p => int.Parse(p.Key) % 2 == 1), "removed sessions must not survive in the snapshot");
    }

    public static void SnapshotStopAndRestartCannotRepublishStoppedSessions()
    {
        var server = new LiveEchoServer();
        var config = LiveServerTests.CreateConfigForTests("snapshot-restart-test");
        config.DisableSessionSnapshot = false;
        var takeSnapshot = s_takeSnapshot.CreateDelegate<Action>(server);
        Assert.True(server.Setup(new RootConfig(), config, logFactory: new SilentLogFactory()), "snapshot server setup");
        try
        {
            for (var round = 0; round < 2; round++)
            {
                Assert.True(server.Start(), "snapshot server start/restart");
                Assert.True(server.GetAllSessions() != null, "restart must initialize an empty snapshot before its first timer tick");
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, config.Port);
                    LiveServerTests.WaitForCondition(() => server.SessionCount == 1, "a session should register");
                    takeSnapshot();
                    Assert.Equal(1, server.GetAllSessions()!.Count(), "registered session must be published");
                }
                LiveServerTests.WaitForCondition(() => server.SessionCount == 0, "the client close should remove its session");
                takeSnapshot();
                Assert.Equal(0, server.GetAllSessions()!.Count(), "closed session must disappear");
                server.Stop();
                takeSnapshot(); // A callback already queued before Timer.Dispose may still run.
                Assert.True(server.GetAllSessions() == null, "a late timer callback must not republish after Stop");
            }
        }
        finally
        {
            server.Stop();
        }
    }

    public static void PartialSendTrimmingDoesNotAllocate()
    {
        var buffer = new byte[64];
        var batch = new List<ArraySegment<byte>>(4);
        var allocated = Measure("partial send: four segments", () =>
        {
            batch.Clear();
            for (var i = 0; i < 4; i++)
            {
                batch.Add(new ArraySegment<byte>(buffer, i * 16, 16));
            }
            var remaining = s_trim(batch, 20);
            if (remaining.Count != 3 || remaining[0].Offset != 20 || remaining[0].Count != 12)
            {
                throw new InvalidOperationException("partial send trimming changed the remaining payload");
            }
        }, 100000);
        Assert.Equal(0L, allocated, "partial send retries should reuse the owned batch");
    }

    public static void PartialSendTrimmingPreservesEveryRemainingByte()
    {
        var bytes = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        for (var consumed = 0; consumed <= bytes.Length; consumed++)
        {
            var batch = new List<ArraySegment<byte>>
            {
                new(bytes, 0, 0), new(bytes, 0, 3), new(bytes, 3, 0),
                new(bytes, 3, 4), new(bytes, 7, 5), new(bytes, 12, 0)
            };
            var remaining = s_trim(batch, consumed);
            Assert.True(ReferenceEquals(batch, remaining), "partial retries must retain the session-owned batch");
            Assert.True(remaining.SelectMany(s => s).SequenceEqual(bytes.Skip(consumed)), "segment boundaries and empty segments must preserve the unsent bytes");
        }
    }

    private static long Measure(string name, Action action, int iterations)
    {
        for (var i = 0; i < 100; i++)
        {
            action();
        }
        var start = Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var elapsed = Stopwatch.GetElapsedTime(start);
        Console.WriteLine($"ALLOC {name}: {allocated / (double)iterations:F2} B/op, {elapsed.TotalMilliseconds:F2} ms / {iterations} operations");
        return allocated;
    }

    private sealed class SnapshotServer : LiveEchoServer
    {
        public LiveEchoSession Add(string id)
        {
            var session = new LiveEchoSession();
            typeof(AppSession<LiveEchoSession, LiveEchoRequestInfo>).GetProperty("SessionID")!.SetValue(session, id);
            Assert.True(RegisterSession(id, session), "test session registration");
            return session;
        }

        public void Remove(LiveEchoSession session) => OnSessionClosed(session, CloseReason.ServerClosing);
    }
}
