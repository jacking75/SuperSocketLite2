using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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

    // One SocketAsyncEventArgs per session, reused for every datagram. Sending is single-flight
    // per session (the InSending state flag), so it can never be used concurrently. The previous
    // code allocated and disposed a SocketAsyncEventArgs for every single segment.
    private SocketAsyncEventArgs? m_SendSAE;
    private readonly UdpSendState m_SendState = new UdpSendState();

    protected override void SendAsync(IList<ArraySegment<byte>> queue)
    {
        var e = m_SendSAE;

        if (e == null)
        {
            e = new SocketAsyncEventArgs();
            e.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);
            m_SendSAE = e;
        }

        m_SendState.Items = queue;
        m_SendState.Position = 0;

        if (PostCurrentSegment(e, queue))
            OnSendingCompleted(this, e);
    }

    /// <summary>
    /// Posts the segment at the current position.
    /// </summary>
    /// <returns>true when it completed synchronously and the caller must process the completion.</returns>
    private bool PostCurrentSegment(SocketAsyncEventArgs e, IList<ArraySegment<byte>> queue)
    {
        var item = queue[m_SendState.Position];

        try
        {
            e.RemoteEndPoint = RemoteEndPoint;
            e.SetBuffer(item.Array, item.Offset, item.Count);

            return !m_ServerSocket.SendToAsync(e);
        }
        catch (Exception exc)
        {
            LogError(exc);
            m_SendState.Items = null;
            OnSendError(queue, CloseReason.SocketError);
            return false;
        }
    }

    void OnSendingCompleted(object? sender, SocketAsyncEventArgs e)
    {
        //Synchronous completions are drained in a loop instead of recursing per segment.
        while (true)
        {
            var queue = m_SendState.Items;

            if (queue == null)
                return;

            if (e.SocketError != SocketError.Success)
            {
                var log = AppSession?.Logger;

                if (log != null && log.IsErrorEnabled)
                    log.Error(new SocketException((int)e.SocketError).ToString());

                m_SendState.Items = null;
                OnSendError(queue, CloseReason.SocketError);
                return;
            }

            AppSession?.AppServer.RecordBytesSent(e.BytesTransferred);

            var newPos = m_SendState.Position + 1;

            if (newPos >= queue.Count)
            {
                m_SendState.Items = null;
                OnSendingCompleted(queue);
                return;
            }

            m_SendState.Position = newPos;

            if (!PostCurrentSegment(e, queue))
                return;
        }
    }

    protected override void SendSync(IList<ArraySegment<byte>> queue)
    {
        try
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var item = queue[i];
                var sent = m_ServerSocket.SendTo(item.Array!, item.Offset, item.Count, SocketFlags.None, RemoteEndPoint!);
                AppSession?.AppServer.RecordBytesSent(sent);
            }
        }
        catch (Exception e)
        {
            LogError(e);
            OnSendError(queue, CloseReason.SocketError);
            return;
        }

        OnSendingCompleted(queue);
    }

    protected override void OnClosed(CloseReason reason)
    {
        var e = Interlocked.Exchange(ref m_SendSAE, null);

        if (e != null)
        {
            e.Completed -= new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);

            //A send may still be in flight; touching or disposing the args then throws, and that
            //must not stop the session from reporting itself closed.
            try
            {
                e.SetBuffer(null, 0, 0);
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                e.Dispose();
            }
            catch (InvalidOperationException)
            {
            }
        }

        base.OnClosed(reason);
    }


    protected override bool TryValidateClosedBySocket(out Socket socket)
    {
        socket = null!;
        return false;
    }

    private sealed class UdpSendState
    {
        /// <summary>The batch being sent, or null when no send is in progress.</summary>
        public IList<ArraySegment<byte>>? Items { get; set; }

        public int Position { get; set; }
    }
}
