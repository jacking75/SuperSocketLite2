using System;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Logging;

namespace SuperSocketLite.SocketEngine;

class AsyncSocketSession : SocketSession, IAsyncSocketSession
{
    private bool m_IsReset;
    private bool m_SendSAEAFromPool;
    private SocketAsyncEventArgs? m_SocketEventArgSend;

    public AsyncSocketSession(Socket client, SocketAsyncEventArgsProxy socketAsyncProxy)
        : this(client, socketAsyncProxy, null, false)
    {

    }

    public AsyncSocketSession(Socket client, SocketAsyncEventArgsProxy socketAsyncProxy, bool isReset)
        : this(client, socketAsyncProxy, null, isReset)
    {

    }

    public AsyncSocketSession(Socket client, SocketAsyncEventArgsProxy socketAsyncProxy, SocketAsyncEventArgs? sendSAEA, bool isReset)
        : base(client)
    {
        SocketAsyncProxy = socketAsyncProxy;
        m_SocketEventArgSend = sendSAEA;
        m_SendSAEAFromPool = sendSAEA != null;
        m_IsReset = isReset;
    }

    ILog ILoggerProvider.Logger
    {
        get { return AppSession.Logger; }
    }

    public SocketAsyncEventArgs? SendSAEA => m_SocketEventArgSend;

    public override void Initialize(IAppSession appSession)
    {
        base.Initialize(appSession);

        //Initialize SocketAsyncProxy for receiving
        SocketAsyncProxy.Initialize(this);

        if (!SyncSend)
        {
            //Initialize SocketAsyncEventArgs for sending
            if (m_SocketEventArgSend == null)
                m_SocketEventArgSend = new SocketAsyncEventArgs();
            
            m_SocketEventArgSend.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendingCompleted);
        }
    }

    public override void Start()
    {
        StartReceive();
        _ = ProcessPipeAsync();   // PipeReader consumer loop — independent Task

        if (!m_IsReset)
            StartSession();
    }

    bool ProcessCompleted(SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            if (e.BytesTransferred > 0)
            {
                return true;
            }
        }
        else
        {
            LogError((int)e.SocketError);
        }

        return false;
    }

    void OnSendingCompleted(object? sender, SocketAsyncEventArgs e)
    {
        var queue = e.UserToken as SendingQueue;

        if (!ProcessCompleted(e))
        {
            ClearPrevSendState(e);
            OnSendError(queue!, CloseReason.SocketError);
            return;
        }

        var count = queue!.Sum(q => q.Count);

        if (count != e.BytesTransferred)
        {
            queue!.InternalTrim(e.BytesTransferred);
            AppSession.Logger.Info($"{e.BytesTransferred} of {count} were transferred, send the rest {queue.Sum(q => q.Count)} bytes right now.");
            ClearPrevSendState(e);
            SendAsync(queue);
            return;
        }

        ClearPrevSendState(e);
        base.OnSendingCompleted(queue!);
    }

    private void ClearPrevSendState(SocketAsyncEventArgs e)
    {
        e.UserToken = null;

        //Clear previous sending buffer of sae to avoid memory leak
        if (e.Buffer != null)
        {
            e.SetBuffer(null, 0, 0);
        }
        else if (e.BufferList != null)
        {
            e.BufferList = null;
        }
    }

    /// <summary>
    /// Asks the PipeWriter for a Memory&lt;byte&gt; segment, assigns it to the SAEA, and posts a ReceiveAsync.
    /// No buffer offset tracking needed — the Pipe manages unconsumed data automatically.
    /// </summary>
    private void StartReceive()
    {
        var e = SocketAsyncProxy.SocketEventArgs;

        try
        {
            var memory = _pipeWriter!.GetMemory(Config.ReceiveBufferSize);
            e.SetBuffer(memory);   // .NET 5+ Memory<byte> overload

            if (!OnReceiveStarted())
                return;

            bool willRaiseEvent = Client!.ReceiveAsync(e);
            if (!willRaiseEvent)
                ProcessReceive(e);
        }
        catch (Exception exc)
        {
            LogError(exc);
            OnReceiveTerminated(CloseReason.SocketError);
        }
    }

    protected override void SendSync(SendingQueue queue)
    {
        try
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var item = queue[i];

                var client = Client;

                if (client == null)
                    return;

                client.Send(item.Array!, item.Offset, item.Count, SocketFlags.None);
            }

            OnSendingCompleted(queue);
        }
        catch (Exception e)
        {
            LogError(e);

            OnSendError(queue, CloseReason.SocketError);
            return;
        }
    }

    protected override void SendAsync(SendingQueue queue)
    {
        try
        {
            var sae = m_SocketEventArgSend!;
            sae.UserToken = queue;

            if (queue.Count > 1)
                sae.BufferList = queue;
            else
            {
                var item = queue[0];
                sae.SetBuffer(item.Array, item.Offset, item.Count);
            }

            var client = Client;

            if (client == null)
            {
                OnSendError(queue, CloseReason.SocketError);
                return;
            }

            if (!client.SendAsync(sae))
                OnSendingCompleted(client, sae);
        }
        catch (Exception e)
        {
            LogError(e);

            ClearPrevSendState(m_SocketEventArgSend!);
            OnSendError(queue, CloseReason.SocketError);
        }
    }

    public SocketAsyncEventArgsProxy SocketAsyncProxy { get; private set; }

/// <summary>
    /// Called by the IOCP completion thread when a ReceiveAsync completes.
    /// Advances the PipeWriter and schedules the next receive — no AppSession call here.
    /// </summary>
    public void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (!ProcessCompleted(e))
        {
            _pipeWriter!.Complete();
            OnReceiveTerminated(e.SocketError == SocketError.Success
                ? CloseReason.ClientClosing
                : CloseReason.SocketError);
            return;
        }

        OnReceiveEnded();

        // Track bytes received for metrics
        AppSession?.AppServer.RecordBytesReceived(e.BytesTransferred);

        _pipeWriter!.Advance(e.BytesTransferred);
        var flushTask = _pipeWriter.FlushAsync();

        if (flushTask.IsCompleted)
        {
            // Fast path: no backpressure
            StartReceive();
        }
        else
        {
            // Slow path: wait for PipeReader to catch up, then restart receive
            _ = flushTask.AsTask().ContinueWith(
                _ => StartReceive(),
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        }
}

    protected override void OnClosed(CloseReason reason)
    {
        _pipeWriter?.Complete();

        var sae = m_SocketEventArgSend;

        if (sae == null)
        {
            base.OnClosed(reason);
            return;
        }

        if (Interlocked.CompareExchange(ref m_SocketEventArgSend, null, sae) == sae)
        {
            sae.Completed -= OnSendingCompleted;
            
            // Only dispose if not from pool - pool manages lifecycle
            if (!m_SendSAEAFromPool)
            {
                sae.Dispose();
            }
            
            base.OnClosed(reason);
        }
    }

    public override void ApplySecureProtocol()
    {
        //TODO: Implement async socket SSL/TLS encryption
    }
}
