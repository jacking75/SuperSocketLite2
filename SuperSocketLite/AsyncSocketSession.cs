using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;

namespace SuperSocketLite.SocketEngine;

class AsyncSocketSession : SocketSession, IAsyncSocketSession
{
    private bool m_SendSAEAFromPool;
    private SocketAsyncEventArgs? m_SocketEventArgSend;
    private bool m_ReceiveInlineOnIocpThread = true;

    public AsyncSocketSession(Socket client, SocketAsyncEventArgsProxy socketAsyncProxy, SocketAsyncEventArgs? sendSAEA)
        : base(client)
    {
        SocketAsyncProxy = socketAsyncProxy;
        m_SocketEventArgSend = sendSAEA;
        m_SendSAEAFromPool = sendSAEA != null;
    }

    ILog ILogProvider.Logger
    {
        get { return AppSession.Logger; }
    }

    public SocketAsyncEventArgs? SendSAEA => m_SocketEventArgSend;

    public bool ReceiveInlineOnIocpThread => m_ReceiveInlineOnIocpThread;

    public override void Initialize(IAppSession appSession)
    {
        base.Initialize(appSession);

        m_ReceiveInlineOnIocpThread = (Config as ServerConfig)?.ReceiveInlineOnIocpThread ?? true;

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
        StartReceiveProcessingTask();
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
        var queue = e.UserToken as IList<ArraySegment<byte>>;

        if (queue == null)
        {
            ClearPrevSendState(e);
            return;
        }

        if (!ProcessCompleted(e))
        {
            ClearPrevSendState(e);
            OnSendError(queue, CloseReason.SocketError);
            return;
        }

        AppSession?.AppServer.RecordBytesSent(e.BytesTransferred);

        var count = SumSegments(queue);

        if (count != e.BytesTransferred)
        {
            queue = TrimSegments(queue, e.BytesTransferred);
            AppSession?.Logger.Info($"{e.BytesTransferred} of {count} were transferred, send the rest {SumSegments(queue)} bytes right now.");
            ClearPrevSendState(e);
            SendAsync(queue);
            return;
        }

        ClearPrevSendState(e);
        base.OnSendingCompleted(queue);
    }

    /// <summary>
    /// Sums the segment lengths without allocating a delegate or an enumerator; this runs on every
    /// asynchronous send completion.
    /// </summary>
    private static int SumSegments(IList<ArraySegment<byte>> segments)
    {
        var total = 0;

        for (var i = 0; i < segments.Count; i++)
            total += segments[i].Count;

        return total;
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
    /// No buffer offset tracking needed ??the Pipe manages unconsumed data automatically.
    /// </summary>
    /// <remarks>
    /// Synchronous completions are drained in a loop rather than by calling back into
    /// <see cref="ProcessReceive"/>. A socket whose receive buffer is always ready (typically a
    /// loopback connection under load) would otherwise build an unbounded StartReceive /
    /// ProcessReceive recursion and overflow the stack.
    /// </remarks>
    private void StartReceive()
    {
        var e = SocketAsyncProxy.SocketEventArgs;

        try
        {
            while (true)
            {
                var memory = _pipeWriter!.GetMemory(Config.ReceiveBufferSize);
                e.SetBuffer(memory);   // .NET 5+ Memory<byte> overload

                if (!OnReceiveStarted())
                    return;

                var client = Client;

                if (client == null)
                {
                    OnReceiveTerminated(CloseReason.SocketError);
                    return;
                }

                if (client.ReceiveAsync(e))
                    return;   // completing asynchronously, the Completed event calls ProcessReceive

                if (!ProcessReceiveCore(e))
                    return;   // terminated, or the pending flush will restart receiving
            }
        }
        catch (Exception exc)
        {
            LogError(exc);
            OnReceiveTerminated(CloseReason.SocketError);
        }
    }

    protected override void SendSync(IList<ArraySegment<byte>> queue)
    {
        try
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var item = queue[i];

                var client = Client;

                //Another thread closed the socket underneath us. Bailing out without ending the
                //send would leave the InSending flag set forever, which blocks the Closed event
                //and leaks the session's pooled SocketAsyncEventArgs.
                if (client == null)
                {
                    OnSendError(queue, CloseReason.SocketError);
                    return;
                }

                var sentTotal = 0;

                while (sentTotal < item.Count)
                {
                    var sent = client.Send(item.Array!, item.Offset + sentTotal, item.Count - sentTotal, SocketFlags.None);

                    if (sent <= 0)
                        throw new SocketException((int)SocketError.ConnectionReset);

                    sentTotal += sent;
                    AppSession?.AppServer.RecordBytesSent(sent);

                    client = Client;

                    if (client == null)
                    {
                        OnSendError(queue, CloseReason.SocketError);
                        return;
                    }
                }
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

    protected override void SendAsync(IList<ArraySegment<byte>> queue)
    {
        try
        {
            var sae = m_SocketEventArgSend!;

            //SocketAsyncEventArgs throws if Buffer and BufferList are set at the same time, so the
            //previous send must always have been cleared by ClearPrevSendState.
            Debug.Assert(sae.Buffer == null && sae.BufferList == null, "the previous send state was not cleared");

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
                ClearPrevSendState(sae);
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

    private static IList<ArraySegment<byte>> TrimSegments(IList<ArraySegment<byte>> segments, int offset)
    {
        var result = new List<ArraySegment<byte>>(segments.Count);
        var remainingOffset = offset;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            if (remainingOffset >= segment.Count)
            {
                remainingOffset -= segment.Count;
                continue;
            }

            if (remainingOffset > 0)
            {
                result.Add(new ArraySegment<byte>(segment.Array!, segment.Offset + remainingOffset, segment.Count - remainingOffset));
                remainingOffset = 0;
                continue;
            }

            result.Add(segment);
        }

        return result;
    }

    /// <summary>
    /// Called by the IOCP completion thread when a ReceiveAsync completes.
    /// Advances the PipeWriter and schedules the next receive ??no AppSession call here.
    /// This entry point always runs on a fresh stack, so restarting the receive here cannot recurse.
    /// </summary>
    public void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (ProcessReceiveCore(e))
            StartReceive();
    }

    /// <summary>
    /// Handles one completed receive without posting the next one.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the caller should post the next receive; <c>false</c> when receiving has
    /// been terminated or when a pending flush will restart it through
    /// <see cref="FlushPipeAndStartReceiveAsync"/>.
    /// </returns>
    private bool ProcessReceiveCore(SocketAsyncEventArgs e)
    {
        if (!ProcessCompleted(e))
        {
            _pipeWriter!.Complete();
            OnReceiveTerminated(e.SocketError == SocketError.Success
                ? CloseReason.ClientClosing
                : CloseReason.SocketError);
            return false;
        }

        OnReceiveEnded();

        // Track bytes received for metrics
        AppSession?.AppServer.RecordBytesReceived(e.BytesTransferred);

        _pipeWriter!.Advance(e.BytesTransferred);
        var flushTask = _pipeWriter.FlushAsync();

        if (flushTask.IsCompletedSuccessfully)
            return ShouldContinueReceive(flushTask.GetAwaiter().GetResult());

        _ = FlushPipeAndStartReceiveAsync(flushTask);
        return false;
    }

    private async Task FlushPipeAndStartReceiveAsync(ValueTask<FlushResult> flushTask)
    {
        try
        {
            var result = await flushTask.ConfigureAwait(false);

            if (ShouldContinueReceive(result))
                StartReceive();
        }
        catch (Exception exc)
        {
            LogError(exc);
            OnReceiveTerminated(CloseReason.SocketError);
        }
    }

    private bool ShouldContinueReceive(FlushResult result)
    {
        if (!result.IsCanceled && !result.IsCompleted)
            return true;

        OnReceiveTerminated(CloseReason.ClientClosing);
        return false;
    }

    protected override void OnClosed(CloseReason reason)
    {
        CompleteReceivePipeWriter();

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
       
}
