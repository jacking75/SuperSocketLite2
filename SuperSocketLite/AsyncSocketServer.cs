using System.Net;
using System.Net.Sockets;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;

namespace SuperSocketLite.SocketEngine;

class AsyncSocketServer : TcpSocketServerBase, IActiveConnector
{
    public AsyncSocketServer(IAppServer appServer, ListenerInfo[] listeners)
        : base(appServer, listeners)
    {

    }

    private SmartPool<SocketAsyncEventArgsProxy>? _receiveSAEAPool;
    private SmartPool<SocketAsyncEventArgs>? _sendSAEAPool;

    public override bool Start()
    {
        try
        {
            var maxPoolSize = AppServer.Config.MaxConnectionNumber;

            //PreAllocateSAEA creates every SAEA at startup (best latency); otherwise the pools start
            //at MinPoolSize and grow on demand.
            var minPoolSize = AppServer.Config.PreAllocateSAEA ? maxPoolSize : AppServer.Config.MinPoolSize;

            _receiveSAEAPool = new SmartPool<SocketAsyncEventArgsProxy>(
                minPoolSize, maxPoolSize, static () => new SocketAsyncEventArgsProxy(new SocketAsyncEventArgs()));

            _sendSAEAPool = new SmartPool<SocketAsyncEventArgs>(
                minPoolSize, maxPoolSize, static () => new SocketAsyncEventArgs());

            if (!base.Start())
                return false;

            IsRunning = true;
            return true;
        }
        catch (Exception e)
        {
            AppServer.Logger.Error(e.ToString());
            return false;
        }
    }

    protected override void OnNewClientAccepted(ISocketListener listener, Socket client, object? state)
    {
        if (IsStopped)
            return;

        ProcessNewClient(client);
    }

    private IAppSession? ProcessNewClient(Socket client)
    {
        // Get receive SAEA from pool
        SocketAsyncEventArgsProxy? socketEventArgsProxy;
        if (!_receiveSAEAPool!.TryGet(out socketEventArgsProxy))
        {
            AppServer.RecordSessionRejected();
            AppServer.AsyncRun(client.SafeClose);
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error($"Max connection number {AppServer.Config.MaxConnectionNumber} was reached!");

            return null;
        }

        // Get send SAEA from pool
        SocketAsyncEventArgs? sendSAEA;
        if (!_sendSAEAPool!.TryGet(out sendSAEA))
        {
            socketEventArgsProxy.Reset();
            _receiveSAEAPool.Push(socketEventArgsProxy);
            AppServer.RecordSessionRejected();
            AppServer.AsyncRun(client.SafeClose);
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error($"Max connection number {AppServer.Config.MaxConnectionNumber} was reached!");
            return null;
        }

        var socketSession = new AsyncSocketSession(client, socketEventArgsProxy, sendSAEA);

        var session = CreateSession(client, socketSession);

        if (session == null)
        {
            socketEventArgsProxy.Reset();
            _receiveSAEAPool.Push(socketEventArgsProxy);
            _sendSAEAPool.Push(sendSAEA);
            AppServer.AsyncRun(client.SafeClose);
            return null;
        }

        socketSession.Closed += SessionClosed;

        if (RegisterSession(session))
        {
            AppServer.AsyncRun(() => socketSession.Start());
        }

        return session;
    }

    private bool RegisterSession(IAppSession appSession)
    {
        if (AppServer.RegisterSession(appSession))
            return true;

        appSession.SocketSession.Close(CloseReason.InternalError);
        return false;
    }

    void SessionClosed(ISocketSession session, CloseReason reason)
    {
        var socketSession = session as IAsyncSocketSession;
        if (socketSession == null)
            return;

        var proxy = socketSession.SocketAsyncProxy;
        proxy.Reset();
        var args = proxy.SocketEventArgs;

        var serverState = AppServer.State;
        var receivePool = this._receiveSAEAPool;
        var sendPool = this._sendSAEAPool;

        if (receivePool == null || sendPool == null || serverState == ServerState.Stopping || serverState == ServerState.NotStarted)
        {
            if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
            {
                args.Dispose();
                socketSession.SendSAEA?.Dispose();
            }
            return;
        }

        // Release any buffer or Memory<byte> GCHandle the SAEA may still hold.
        args.SetBuffer(null, 0, 0);
        receivePool.Push(proxy);

        // Return send SAEA to pool
        var sendSAEA = socketSession.SendSAEA;
        if (sendSAEA != null)
        {
            sendSAEA.SetBuffer(null, 0, 0);
            sendPool.Push(sendSAEA);
        }
    }

    public override void Stop()
    {
        if (IsStopped)
            return;

        lock (SyncRoot)
        {
            if (IsStopped)
                return;

            base.Stop();

            // Dispose all receive SAEA objects by draining the pool
            if (_receiveSAEAPool != null)
            {
                while (_receiveSAEAPool.TryGet(out var proxy))
                {
                    proxy.SocketEventArgs.Dispose();
                }
                _receiveSAEAPool = null;
            }

            // Dispose all send SAEA objects by draining the pool
            if (_sendSAEAPool != null)
            {
                while (_sendSAEAPool.TryGet(out var saea))
                {
                    saea.Dispose();
                }
                _sendSAEAPool = null;
            }

            IsRunning = false;
        }
    }

    class ActiveConnectState
    {
        public TaskCompletionSource<ActiveConnectResult> TaskSource { get; private set; }

        public Socket Socket { get; private set; }

        public ActiveConnectState(TaskCompletionSource<ActiveConnectResult> taskSource, Socket socket)
        {
            TaskSource = taskSource;
            Socket = socket;
        }
    }

    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint)
    {
        return ((IActiveConnector)this).ActiveConnect(targetEndPoint, null);
    }

    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint, EndPoint? localEndPoint)
    {
        var taskSource = new TaskCompletionSource<ActiveConnectResult>();
        var socket = new Socket(targetEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        if (localEndPoint != null)
        {
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(localEndPoint);
        }

        socket.BeginConnect(targetEndPoint, OnActiveConnectCallback, new ActiveConnectState(taskSource, socket));
        return taskSource.Task;
    }

    private void OnActiveConnectCallback(IAsyncResult result)
    {
        var connectState = result.AsyncState as ActiveConnectState;

        if (connectState == null)
            return;

        try
        {
            var socket = connectState.Socket;
            socket.EndConnect(result);

            var session = ProcessNewClient(socket);

            if (session == null)
                connectState.TaskSource.SetException(new Exception("Failed to create session for this socket."));
            else
                connectState.TaskSource.SetResult(new ActiveConnectResult { Result = true, Session = session });
        }
        catch (Exception e)
        {
            connectState.TaskSource.SetException(e);
        }
    }
}
