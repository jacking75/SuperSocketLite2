using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;

namespace SuperSocketLite.SocketEngine;

class AsyncSocketServer : TcpSocketServerBase, IActiveConnector
{
    public AsyncSocketServer(IAppServer appServer, ListenerInfo[] listeners)
        : base(appServer, listeners)
    {

    }

    private ISmartPool<SocketAsyncEventArgsProxy>? m_ReceiveSAEAPool;
    private ISmartPool<SocketAsyncEventArgs>? m_SendSAEAPool;

    public override bool Start()
    {
        try
        {
            // Initialize receive SAEA pool
            m_ReceiveSAEAPool = new SmartPool<SocketAsyncEventArgsProxy>();
            var receiveCreator = new SAEAProxyCreator();
            
            if (AppServer.Config.PreAllocateSAEA)
            {
                // Pre-allocate all SAEA objects at startup for maximum performance
                m_ReceiveSAEAPool.Initialize(
                    AppServer.Config.MaxConnectionNumber,
                    AppServer.Config.MaxConnectionNumber,
                    receiveCreator);
            }
            else
            {
                // Start with minimum and grow dynamically
                m_ReceiveSAEAPool.Initialize(
                    AppServer.Config.MinPoolSize,
                    AppServer.Config.MaxConnectionNumber,
                    receiveCreator);
            }

            // Initialize send SAEA pool
            m_SendSAEAPool = new SmartPool<SocketAsyncEventArgs>();
            var sendCreator = new SAEACreator();
            
            if (AppServer.Config.PreAllocateSAEA)
            {
                m_SendSAEAPool.Initialize(
                    AppServer.Config.MaxConnectionNumber,
                    AppServer.Config.MaxConnectionNumber,
                    sendCreator);
            }
            else
            {
                m_SendSAEAPool.Initialize(
                    AppServer.Config.MinPoolSize,
                    AppServer.Config.MaxConnectionNumber,
                    sendCreator);
            }

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
        if (!m_ReceiveSAEAPool!.TryGet(out socketEventArgsProxy))
        {
            AppServer.RecordSessionRejected();
            AppServer.AsyncRun(client.SafeClose);
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error($"Max connection number {AppServer.Config.MaxConnectionNumber} was reached!");

            return null;
        }

        // Get send SAEA from pool
        SocketAsyncEventArgs? sendSAEA;
        if (!m_SendSAEAPool!.TryGet(out sendSAEA))
        {
            socketEventArgsProxy.Reset();
            m_ReceiveSAEAPool.Push(socketEventArgsProxy);
            AppServer.RecordSessionRejected();
            AppServer.AsyncRun(client.SafeClose);
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error($"Max connection number {AppServer.Config.MaxConnectionNumber} was reached!");
            return null;
        }

        var socketSession = new AsyncSocketSession(client, socketEventArgsProxy, sendSAEA, false);

        var session = CreateSession(client, socketSession);

        if (session == null)
        {
            socketEventArgsProxy.Reset();
            m_ReceiveSAEAPool.Push(socketEventArgsProxy);
            m_SendSAEAPool.Push(sendSAEA);
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
        var socketSession = session as IAsyncSocketSessionBase;
        if (socketSession == null)
            return;

        var proxy = socketSession.SocketAsyncProxy;
        proxy.Reset();
        var args = proxy.SocketEventArgs;

        var serverState = AppServer.State;
        var receivePool = this.m_ReceiveSAEAPool;
        var sendPool = this.m_SendSAEAPool;

        if (receivePool == null || sendPool == null || serverState == ServerState.Stopping || serverState == ServerState.NotStarted)
        {
            if (!Environment.HasShutdownStarted && !AppDomain.CurrentDomain.IsFinalizingForUnload())
            {
                args.Dispose();
                socketSession.SendSAEA?.Dispose();
            }
            return;
        }

        if (!proxy.IsRecyclable)
        {
            args.Dispose();
            socketSession.SendSAEA?.Dispose();
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
            if (m_ReceiveSAEAPool != null)
            {
                while (m_ReceiveSAEAPool.TryGet(out var proxy))
                {
                    proxy.SocketEventArgs.Dispose();
                }
                m_ReceiveSAEAPool = null;
            }

            // Dispose all send SAEA objects by draining the pool
            if (m_SendSAEAPool != null)
            {
                while (m_SendSAEAPool.TryGet(out var saea))
                {
                    saea.Dispose();
                }
                m_SendSAEAPool = null;
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

/// <summary>
/// Creator for SocketAsyncEventArgsProxy objects used in SmartPool
/// </summary>
class SAEAProxyCreator : ISmartPoolSourceCreator<SocketAsyncEventArgsProxy>
{
    public ISmartPoolSource Create(int size, out SocketAsyncEventArgsProxy[] poolItems)
    {
        poolItems = new SocketAsyncEventArgsProxy[size];
        for (int i = 0; i < size; i++)
        {
            poolItems[i] = new SocketAsyncEventArgsProxy(new SocketAsyncEventArgs());
        }
        return new SmartPoolSource(poolItems, size);
    }
}

/// <summary>
/// Creator for SocketAsyncEventArgs objects used in SmartPool (for send operations)
/// </summary>
class SAEACreator : ISmartPoolSourceCreator<SocketAsyncEventArgs>
{
    public ISmartPoolSource Create(int size, out SocketAsyncEventArgs[] poolItems)
    {
        poolItems = new SocketAsyncEventArgs[size];
        for (int i = 0; i < size; i++)
        {
            poolItems[i] = new SocketAsyncEventArgs();
        }
        return new SmartPoolSource(poolItems, size);
    }
}