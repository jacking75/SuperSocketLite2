using System.Net;
using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketEngine;

class UdpSocketServer<TRequestInfo> : SocketServerBase, IActiveConnector
    where TRequestInfo : IRequestInfo
{
    private bool _isUdpRequestInfo = false;

    private IReceiveFilterFactory<TRequestInfo> _receiveFilterFactory;

    private int _connectionCount = 0;

    private IRequestHandler<TRequestInfo>? _requestHandler;

    public UdpSocketServer(IAppServer appServer, ListenerInfo[] listeners)
        : base(appServer, listeners)
    {
        _requestHandler = appServer as IRequestHandler<TRequestInfo>;

        _isUdpRequestInfo = typeof(UdpRequestInfo).IsAssignableFrom(typeof(TRequestInfo));

        _receiveFilterFactory = (IReceiveFilterFactory<TRequestInfo>)appServer.ReceiveFilterFactory;
    }

    /// <summary>Called when [new client accepted].</summary>
    protected override void OnNewClientAccepted(ISocketListener listener, Socket client, object? state)
    {
        var packet = state as UdpReceivePacket;

        if (packet == null)
            return;

        try
        {
            if (_isUdpRequestInfo)
            {
                ProcessPackageWithSessionID(client, packet.RemoteEndPoint, packet.Buffer, packet.Offset, packet.Count);
            }
            else
            {
                ProcessPackageWithoutSessionID(client, packet.RemoteEndPoint, packet.Buffer, packet.Offset, packet.Count);
            }
        }
        catch (Exception e)
        {
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error("Process UDP package error!", e);
        }
        finally
        {
            packet.Dispose();
        }
    }

    IAppSession? CreateNewSession(Socket listenSocket, IPEndPoint remoteEndPoint, string sessionID)
    {
        if (!DetectConnectionNumber(remoteEndPoint))
            return null;

        var socketSession = new UdpSocketSession(listenSocket, remoteEndPoint, sessionID);
        var appSession = AppServer.CreateAppSession(socketSession);

        if (appSession == null)
            return null;

        if (!DetectConnectionNumber(remoteEndPoint))
            return null;

        if (!AppServer.RegisterSession(appSession))
            return null;

        Interlocked.Increment(ref _connectionCount);

        socketSession.Closed += OnSocketSessionClosed;
        socketSession.Start();

        return appSession;
    }


    // A UdpRequestInfo filter parses one self-contained datagram, so one instance per receive
    // thread can be reused instead of allocating a filter for every packet. It is Reset() before
    // each use. CONTRACT: a filter used with UdpRequestInfo must be stateless across datagrams and
    // must not capture the remote endpoint passed to CreateFilter.
    [ThreadStatic]
    private static IReceiveFilter<TRequestInfo>? s_SessionIdFilter;

    void ProcessPackageWithSessionID(Socket listenSocket, IPEndPoint remoteEndPoint, byte[] receivedData, int offset, int count)
    {
        TRequestInfo? requestInfo = default;

        string sessionID;

        int rest;

        try
        {
            var requestFilter = s_SessionIdFilter;

            if (requestFilter == null)
            {
                requestFilter = _receiveFilterFactory.CreateFilter(AppServer, null!, remoteEndPoint);
                s_SessionIdFilter = requestFilter;
            }
            else
            {
                requestFilter.Reset();
            }

            requestInfo = requestFilter.Filter(receivedData, offset, count, false, out rest);
        }
        catch (Exception exc)
        {
            if(AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error("Failed to parse UDP package!", exc);
            return;
        }

        var udpRequestInfo = requestInfo as UdpRequestInfo;

        if (rest > 0)
        {
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error("The output parameter rest must be zero in this case!");
            return;
        }

        if (udpRequestInfo == null)
        {
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error("Invalid UDP package format!");
            return;
        }

        if (string.IsNullOrEmpty(udpRequestInfo.SessionID))
        {
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error("Failed to get session key from UDP package!");
            return;
        }

        sessionID = udpRequestInfo.SessionID;

        var appSession = AppServer.GetSessionByID(sessionID);

        if (appSession == null)
        {
            appSession = CreateNewSession(listenSocket, remoteEndPoint, sessionID);

            //Failed to create a new session
            if (appSession == null)
                return;
        }
        else
        {
            var socketSession = appSession.SocketSession as UdpSocketSession;
            //Client remote endpoint may change, so update session to ensure the server can find client correctly
            socketSession?.UpdateRemoteEndPoint(remoteEndPoint);
        }

        _requestHandler?.ExecuteCommand(appSession, requestInfo!);
    }

    void ProcessPackageWithoutSessionID(Socket listenSocket, IPEndPoint remoteEndPoint, byte[] receivedData, int offset, int count)
    {
        var sessionID = remoteEndPoint.ToString();
        var appSession = AppServer.GetSessionByID(sessionID);

        if (appSession == null) //New session
        {
            appSession = CreateNewSession(listenSocket, remoteEndPoint, sessionID);

            //Failed to create a new session
            if (appSession == null)
                return;

            appSession.ProcessRequest(receivedData, offset, count, false);
        }
        else //Existing session
        {
            appSession.ProcessRequest(receivedData, offset, count, false);
        }
    }

    void OnSocketSessionClosed(ISocketSession socketSession, CloseReason closeReason)
    {
        Interlocked.Decrement(ref _connectionCount);
    }

    bool DetectConnectionNumber(EndPoint remoteEndPoint)
    {
        if (_connectionCount >= AppServer.Config.MaxConnectionNumber)
        {
            if (AppServer.Logger.IsErrorEnabled)
                AppServer.Logger.Error($"Cannot accept a new UDP connection from {remoteEndPoint.ToString()}, the max connection number {AppServer.Config.MaxConnectionNumber} has been exceed!");

            return false;
        }

        return true;
    }

    protected override ISocketListener CreateListener(ListenerInfo listenerInfo)
    {
        return new UdpSocketListener(listenerInfo);
    }

    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint)
    {
        return ((IActiveConnector)this).ActiveConnect(targetEndPoint, null);
    }

    Task<ActiveConnectResult> IActiveConnector.ActiveConnect(EndPoint targetEndPoint, EndPoint? localEndPoint)
    {
        var taskSource = new TaskCompletionSource<ActiveConnectResult>();
        var socket = new Socket(targetEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        if (localEndPoint != null)
        {
            socket.ExclusiveAddressUse = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(localEndPoint);
        }

        var session = CreateNewSession(socket, (IPEndPoint)targetEndPoint, targetEndPoint.ToString()!);

        if (session == null)
            taskSource.SetException(new Exception("Failed to create session for this socket."));
        else
            taskSource.SetResult(new ActiveConnectResult { Result = true, Session = session });

        return taskSource.Task;
    }
}
