using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;

static class ReceiveLifecycleTests
{
    public static void CloseDuringBufferPreparationKeepsReceiveResourcesOwned()
    {
        using var fixture = new ReceiveFixture();
        var closedDuringGetMemory = -1;
        fixture.Writer.OnGetMemory = () =>
        {
            fixture.Session.Close(CloseReason.ServerClosing);
            closedDuringGetMemory = fixture.CloseCount;
        };

        fixture.StartReceive();

        Assert.Equal(0, closedDuringGetMemory, "buffer preparation must hold receive ownership before touching the pipe or SAEA");
        Assert.Equal(1, fixture.CloseCount, "the receive loop should finish closing exactly once");
        Assert.True(fixture.Writer.Completed, "the writer must complete when receiving terminates");
    }

    public static void CloseDuringPendingFlushDefersResourceReturn()
    {
        using var fixture = new ReceiveFixture();
        using var received = ReceiveOneByte();
        fixture.BeginReceive();
        var closedDuringAdvance = -1;
        fixture.Writer.OnAdvance = () =>
        {
            fixture.Session.Close(CloseReason.ServerClosing);
            closedDuringAdvance = fixture.CloseCount;
        };

        fixture.ProcessReceive(received);

        Assert.Equal(0, closedDuringAdvance, "a close during Advance must not return receive resources");
        Assert.Equal(0, fixture.CloseCount, "a pending flush must keep receive resources owned");
        Assert.True(!fixture.Writer.Completed, "the writer cannot complete while FlushAsync is pending");

        fixture.Writer.FinishFlush();
        LiveServerTests.WaitForCondition(() => fixture.CloseCount == 1, "flush completion should release the closing session");
        Assert.True(fixture.Writer.Completed, "the writer should complete after flushing stops");
    }

    public static void ReceiveProcessingFailureClosesSession()
    {
        using var fixture = new ReceiveFixture();
        using var received = ReceiveOneByte();
        fixture.BeginReceive();
        fixture.Writer.OnAdvance = () => throw new InvalidOperationException("injected pipe failure");

        fixture.ProcessReceive(received);

        Assert.Equal(1, fixture.CloseCount, "a receive-processing failure must release and close the session");
        Assert.True(fixture.Writer.Completed, "a failed receive must complete its pipe writer");
    }

    private static SocketAsyncEventArgs ReceiveOneByte()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(listener.LocalEndPoint!);
        using var accepted = listener.Accept();
        client.Send(new byte[] { 42 });

        var args = new SocketAsyncEventArgs();
        args.SetBuffer(new byte[1], 0, 1);
        using var completed = new ManualResetEventSlim();
        EventHandler<SocketAsyncEventArgs> handler = (_, _) => completed.Set();
        args.Completed += handler;
        try
        {
            if (accepted.ReceiveAsync(args))
            {
                Assert.True(completed.Wait(5000), "the test receive should complete");
            }
            Assert.Equal(SocketError.Success, args.SocketError, "test receive status");
            Assert.Equal(1, args.BytesTransferred, "test receive byte count");
            return args;
        }
        catch
        {
            args.Dispose();
            throw;
        }
        finally
        {
            args.Completed -= handler;
        }
    }

    private sealed class ReceiveFixture : IDisposable
    {
        private static readonly Type s_sessionType = typeof(ISocketSession).Assembly.GetType("SuperSocketLite.SocketEngine.AsyncSocketSession", true)!;
        private static readonly Type s_baseType = s_sessionType.BaseType!;
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly SocketAsyncEventArgs _receiveArgs = new();
        private int _closeCount;

        public ISocketSession Session { get; }
        public ControlledWriter Writer { get; } = new();
        public int CloseCount => Volatile.Read(ref _closeCount);

        public ReceiveFixture()
        {
            var proxyType = s_sessionType.Assembly.GetType("SuperSocketLite.SocketEngine.SocketAsyncEventArgsProxy", true)!;
            var proxy = Activator.CreateInstance(proxyType, new object[] { _receiveArgs })!;
            Session = (ISocketSession)Activator.CreateInstance(s_sessionType, new object?[] { _socket, proxy, null })!;
            s_baseType.GetProperty("Config")!.SetValue(Session, new ServerConfig());
            Session.Closed += (_, _) => Interlocked.Increment(ref _closeCount);
            s_baseType.GetField("_pipeWriter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Writer);
        }

        public void BeginReceive() => s_baseType.GetMethod("OnReceiveStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Session, null);
        public void StartReceive() => s_sessionType.GetMethod("StartReceive", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Session, null);
        public void ProcessReceive(SocketAsyncEventArgs args) => s_sessionType.GetMethod("ProcessReceive")!.Invoke(Session, new object[] { args });

        public void Dispose()
        {
            Writer.FinishFlush();
            _socket.Dispose();
            _receiveArgs.Dispose();
        }
    }

    private sealed class ControlledWriter : PipeWriter
    {
        private readonly byte[] _buffer = new byte[4096];
        private readonly TaskCompletionSource<FlushResult> _flush = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OnGetMemory { get; set; }
        public Action? OnAdvance { get; set; }
        public bool Completed { get; private set; }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            OnGetMemory?.Invoke();
            return _buffer;
        }

        public override Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;
        public override void Advance(int bytes) => OnAdvance?.Invoke();
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) => new(_flush.Task);
        public override void CancelPendingFlush() => _flush.TrySetResult(new FlushResult(true, false));
        public override void Complete(Exception? exception = null) => Completed = true;
        public void FinishFlush() => _flush.TrySetResult(new FlushResult(false, false));
    }
}
