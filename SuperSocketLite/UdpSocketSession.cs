using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SuperSocketLite.SocketBase;


namespace SuperSocketLite.SocketEngine;

class UdpSocketSession : SocketSession
{
    private Socket m_ServerSocket;

    public UdpSocketSession(Socket serverSocket, IPEndPoint remoteEndPoint)
        : base(remoteEndPoint.ToString())
    {
        m_ServerSocket = serverSocket;
        RemoteEndPoint = remoteEndPoint;
    }

    public UdpSocketSession(Socket serverSocket, IPEndPoint remoteEndPoint, string sessionID)
        : base(sessionID)
    {
        m_ServerSocket = serverSocket;
        RemoteEndPoint = remoteEndPoint;
    }

    public override IPEndPoint LocalEndPoint
    {
        get { return (IPEndPoint)m_ServerSocket.LocalEndPoint!; }
    }

    /// <summary>
    /// Updates the remote end point of the client.
    /// </summary>
    /// <param name="remoteEndPoint">The remote end point.</param>
    internal void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        this.RemoteEndPoint = remoteEndPoint;
    }

    public override void Start()
    {
        StartSession();
    }

    protected override void SendAsync(IList<ArraySegment<byte>> queue)
    {
        var e = new SocketAsyncEventArgs();

        e.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);
        e.RemoteEndPoint = RemoteEndPoint;
        e.UserToken = new UdpSendState(queue);

        var item = queue[0];
        e.SetBuffer(item.Array, item.Offset, item.Count);

        if (!m_ServerSocket.SendToAsync(e))
            OnSendingCompleted(this, e);
    }

    void CleanSocketAsyncEventArgs(SocketAsyncEventArgs e)
    {
        e.UserToken = null;
        e.Completed -= new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);
        e.Dispose();
    }

    void OnSendingCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var state = e.UserToken as UdpSendState;
        var queue = state?.Items;

        if (state == null || queue == null)
        {
            CleanSocketAsyncEventArgs(e);
            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            var log = AppSession.Logger;

            if (log.IsErrorEnabled)
                log.Error(new SocketException((int)e.SocketError).ToString());

            CleanSocketAsyncEventArgs(e);
            OnSendError(queue, CloseReason.SocketError);
            return;
        }

        CleanSocketAsyncEventArgs(e);

        var newPos = state.Position + 1;

        if (newPos >= queue.Count)
        {
            OnSendingCompleted(queue);
            return;
        }

        state.Position = newPos;
        e = new SocketAsyncEventArgs();
        e.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);
        e.RemoteEndPoint = RemoteEndPoint;
        e.UserToken = state;

        var item = queue[newPos];
        e.SetBuffer(item.Array, item.Offset, item.Count);

        if (!m_ServerSocket.SendToAsync(e))
            OnSendingCompleted(this, e);
    }

    protected override void SendSync(IList<ArraySegment<byte>> queue)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            m_ServerSocket.SendTo(item.Array!, item.Offset, item.Count, SocketFlags.None, RemoteEndPoint!);
        }

        OnSendingCompleted(queue);
    }
       
    protected override bool TryValidateClosedBySocket(out Socket socket)
    {
        socket = null!;
        return false;
    }

    [Obsolete("OrigReceiveOffset is not used in the Pipelines receive path and always returns 0.")]
    public override int OrigReceiveOffset
    {
        get { return 0; }
    }

    private sealed class UdpSendState
    {
        public UdpSendState(IList<ArraySegment<byte>> items)
        {
            Items = items;
        }

        public IList<ArraySegment<byte>> Items { get; }

        public int Position { get; set; }
    }
}
