using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

static class SocketState
{
    public const int Normal = 0;//0000 0000
    public const int InClosing = 16;//0001 0000  >= 16
    public const int Closed = 16777216;//256 * 256 * 256; 0x01 0x00 0x00 0x00
    public const int InSending = 1;//0000 0001  > 1
    public const int InReceiving = 2;//0000 0010 > 2
    public const int InSendingReceivingMask = -4;// ~(InSending | InReceiving); 0xf0 0xff 0xff 0xff
}

/// <summary>Socket Session, all application session should base on this class</summary>
abstract partial class SocketSession : ISocketSession
{
    public IAppSession AppSession { get; private set; } = null!;

    protected readonly object SyncRoot = new();

    //0x00 0x00 0x00 0x00
    //1st byte: Closed(Y/N) - 0x01
    //2nd, 3th byte: N/A
    //Last byte: 0000 0000 - normal state
    //0000 0001: in sending
    //0000 0010: in receiving
    //0001 0000: in closing
    private int _state = 0;

    private const int NoCloseReason = -1;

    // The close reason is kept out of _state on purpose. It used to be packed in as
    // ((int)reason + 1) * 256, which the reader undid with a division - and that division picked
    // up the Closed bit (0x01000000) as soon as it was set, yielding a bogus reason.
    private int _closeReasonCode = NoCloseReason;

    protected Pipe? _receivePipe;
    protected PipeWriter? _pipeWriter;
    protected PipeReader? _pipeReader;
    private Task? _receiveProcessingTask;
    private int _receiveProcessingTaskObserved;

    /// <summary>Sets the flag unless the connection has already entered the closing procedure.</summary>
    private bool AddStateFlagIfNotClosing(int stateValue)
    {
        while (true)
        {
            var oldState = _state;

            if (oldState >= SocketState.InClosing)
                return false;

            if (Interlocked.CompareExchange(ref _state, oldState | stateValue, oldState) == oldState)
                return true;
        }
    }

    /// <summary>Sets the flag; false when it was already set (this caller did not win it).</summary>
    private bool TryAddStateFlag(int stateValue)
    {
        return (Interlocked.Or(ref _state, stateValue) & stateValue) != stateValue;
    }

    private void RemoveStateFlag(int stateValue)
    {
        Interlocked.And(ref _state, ~stateValue);
    }

    /// <summary>Clears the flag and returns the state this caller has just published.</summary>
    /// <remarks>
    /// Interlocked.And reports the value from before the update, so the caller has to clear the bit
    /// on it as well. Deciding on this value rather than on a later read of <c>_state</c> is what
    /// lets a caller reason about what a concurrent thread can have seen.
    /// </remarks>
    private int RemoveStateFlagAndGetState(int stateValue)
    {
        return Interlocked.And(ref _state, ~stateValue) & ~stateValue;
    }

    private bool CheckState(int stateValue)
    {
        return (_state & stateValue) == stateValue;
    }

    protected bool SyncSend { get; private set; }

    private ChannelSendingQueue _sendQueue = null!;

    // Reused by every StartSend to avoid allocating a List (plus its backing array) per send cycle.
    // Safe because sending is single-flight per session: StartSend only proceeds after it owns the
    // InSending flag, and the previous batch is detached from the SocketAsyncEventArgs
    // (ClearPrevSendState) before OnSendingCompleted can trigger the next drain.
    private readonly List<ArraySegment<byte>> _sendBatch = [];

    // ArrayPool arrays backing the batch currently being sent. They are returned once the whole
    // batch is done, which is also correct for the partial-send retry path: TrimSegments only
    // points at a different slice of the very same arrays.
    private readonly List<byte[]> _pooledInFlight = [];

    
    public SocketSession(Socket client)
        : this(Guid.NewGuid().ToString())
    {
        if (client == null)
            throw new ArgumentNullException("client");

        _client = client;
        LocalEndPoint = (IPEndPoint?)client.LocalEndPoint;
        RemoteEndPoint = (IPEndPoint?)client.RemoteEndPoint;
    }

    public SocketSession(string sessionID)
    {
        SessionID = sessionID;
    }

    public virtual void Initialize(IAppSession appSession)
    {
        AppSession = appSession;
        Config = appSession.Config;
        SyncSend = Config.SyncSend;

        _sendQueue = new ChannelSendingQueue(Math.Max(Config.SendingQueueSize, 1));

        // Initialize the receive pipeline. PipeOptions.useSynchronizationContext=false keeps
        // ProcessPipeAsync off the captured SynchronizationContext.
        var segmentSize = Math.Max(Config.ReceiveBufferSize, 1);
        var configuredPauseThreshold = (Config as ServerConfig)?.MaxReceivePipeBufferSize ?? 0;

        if (configuredPauseThreshold <= 0)
            configuredPauseThreshold = 65536;   // the System.IO.Pipelines default

        // A pipe throws unless pauseWriterThreshold >= minimumSegmentSize; keep room for at least
        // two full receive buffers so the receive loop can always make progress.
        var pauseThreshold = Math.Max(configuredPauseThreshold, segmentSize * 2L);

        // A zero-copy (sequence) filter leaves an incomplete request in the pipe until it is whole,
        // so the pipe must be able to hold a maximum-size request plus a receive buffer. Otherwise
        // a large MaxRequestLength would pause the receive loop before the request can ever be
        // completed or rejected.
        if (Config.MaxRequestLength > 0)
            pauseThreshold = Math.Max(pauseThreshold, (long)Config.MaxRequestLength + segmentSize * 2L);

        if (pauseThreshold > int.MaxValue)
            pauseThreshold = int.MaxValue;

        var pipeOptions = new PipeOptions(
            minimumSegmentSize: segmentSize,
            pauseWriterThreshold: pauseThreshold,
            resumeWriterThreshold: pauseThreshold / 2,
            useSynchronizationContext: false);
        _receivePipe = new Pipe(pipeOptions);
        _pipeWriter  = _receivePipe.Writer;
        _pipeReader  = _receivePipe.Reader;
    }

    /// <summary>Gets or sets the session ID.</summary>
    public string SessionID { get; private set; }


    /// <summary>Gets or sets the config.</summary>
    public IServerConfig Config { get; set; } = null!;

    /// <summary>Starts this session.</summary>
    public abstract void Start();

    /// <summary>Says the welcome information when a client connectted.</summary>
    protected virtual void StartSession()
    {
        AppSession.StartSession();
    }

    /// <summary>Called when [close].</summary>
    protected virtual void OnClosed(CloseReason reason)
    {
        //Already closed
        if (!TryAddStateFlag(SocketState.Closed))
            return;

        _sendQueue?.Complete();

        var closedHandler = Closed;
        if (closedHandler != null)
        {
            closedHandler(this, reason);
        }
    }

    /// <summary>Occurs when [closed].</summary>
    public Action<ISocketSession, CloseReason>? Closed { get; set; }

    /// <summary>Tries to send array segment.</summary>
    public bool TrySend(IList<ArraySegment<byte>> segments)
    {
        if (IsClosed)
            return false;

        if (!_sendQueue.TryEnqueue(segments))
        {
            RecordSendQueueFull();
            return false;
        }

        StartSend(true);
        return true;
    }

    /// <summary>Tries to send array segment.</summary>
    public bool TrySend(ArraySegment<byte> segment)
    {
        if (IsClosed)
            return false;

        if (!_sendQueue.TryEnqueue(segment))
        {
            RecordSendQueueFull();
            return false;
        }

        StartSend(true);
        return true;
    }

    private void RecordSendQueueFull()
    {
        AppSession?.AppServer?.RecordSendQueueFull();
    }

    /// <summary>Tries to send memory.</summary>
    /// <remarks>
    /// An array-backed memory is sent without copying, so the caller must not modify it until the
    /// data has been sent. Any other memory is copied into a pooled buffer.
    /// </remarks>
    public bool TrySend(ReadOnlyMemory<byte> memory)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            return TrySend(segment);
        }

        return TrySendCopied(memory.Span);
    }

    /// <summary>Tries to send span. The data is always copied into a pooled buffer.</summary>
    public bool TrySend(ReadOnlySpan<byte> span)
    {
        return TrySendCopied(span);
    }

    /// <summary>
    /// Copies <paramref name="data"/> into a pooled buffer and queues it, so the caller may reuse
    /// or overwrite its own buffer as soon as this returns.
    /// </summary>
    /// <param name="data">The data to send.</param>
    /// <returns>false if the session is closed or the sending queue is full.</returns>
    public bool TrySendCopied(ReadOnlySpan<byte> data)
    {
        if (IsClosed)
            return false;

        if (data.IsEmpty)
            return true;

        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);

        if (!_sendQueue.TryEnqueue(new SendItem(new ArraySegment<byte>(buffer, 0, data.Length), buffer)))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            RecordSendQueueFull();
            return false;
        }

        StartSend(true);
        return true;
    }

    /// <summary>Queues <paramref name="data"/>, waiting asynchronously when the sending queue is full.</summary>
    /// <param name="data">The data to send. Array-backed memory is sent without copying.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space.</param>
    /// <returns>false if the session is closed or was closed while waiting.</returns>
    /// <remarks>
    /// The configured <c>SendTimeOut</c> does not apply here; pass a cancellation token created
    /// from a <see cref="CancellationTokenSource"/> with a timeout to bound the wait.
    /// </remarks>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (IsClosed)
            return false;

        if (data.IsEmpty)
            return true;

        //Fast path: there is room right now.
        if (TrySend(data))
            return true;

        byte[]? pooledBuffer = null;
        SendItem item;

        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
        {
            item = new SendItem(segment);
        }
        else
        {
            pooledBuffer = ArrayPool<byte>.Shared.Rent(data.Length);
            data.Span.CopyTo(pooledBuffer);
            item = new SendItem(new ArraySegment<byte>(pooledBuffer, 0, data.Length), pooledBuffer);
        }

        bool enqueued;

        try
        {
            enqueued = await _sendQueue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (pooledBuffer != null)
                ArrayPool<byte>.Shared.Return(pooledBuffer);

            throw;
        }

        if (!enqueued)
        {
            if (pooledBuffer != null)
                ArrayPool<byte>.Shared.Return(pooledBuffer);

            return false;
        }

        StartSend(true);
        return true;
    }

    /// <summary>Sends in async mode.</summary>
    protected abstract void SendAsync(IList<ArraySegment<byte>> items);

    /// <summary>Sends in sync mode.</summary>
    protected abstract void SendSync(IList<ArraySegment<byte>> items);

    private void Send(IList<ArraySegment<byte>> items)
    {
        if (SyncSend)
        {
            SendSync(items);
        }
        else
        {
            SendAsync(items);
        }
    }

    private void StartSend(bool initial)
    {
        if (initial)
        {
            if (!TryAddStateFlag(SocketState.InSending))
            {
                return;
            }

        }

        Socket? client;

        if (IsInClosingOrClosed && TryValidateClosedBySocket(out client))
        {
            OnSendEnd();
            return;
        }

        _sendQueue.DrainAvailable(_sendBatch, _pooledInFlight);

        if (_sendBatch.Count == 0)
        {
            OnSendEnd();
            return;
        }

        Send(_sendBatch);
    }

    /// <summary>Returns the pooled buffers of the batch that has just finished sending.</summary>
    /// <remarks>
    /// Only called from the batch-completion points (<see cref="OnSendingCompleted"/> /
    /// <see cref="OnSendError"/>), never while the socket may still be reading the arrays. If the
    /// session dies with a send in flight the arrays are simply not recycled - the GC reclaims
    /// them, which is far cheaper than risking a buffer that is handed out twice.
    /// </remarks>
    private void ReturnPooledSendBuffers()
    {
        var pooled = _pooledInFlight;

        if (pooled.Count == 0)
            return;

        for (var i = 0; i < pooled.Count; i++)
            ArrayPool<byte>.Shared.Return(pooled[i]);

        pooled.Clear();
    }

    private void OnSendEnd()
    {
        OnSendEnd(CloseReason.Unknown, false);
    }

    private void OnSendEnd(CloseReason closeReason, bool forceClose)
    {
        var state = RemoveStateFlagAndGetState(SocketState.InSending);

        // Outside the closing procedure ValidateClosed only takes SyncRoot and returns again, so
        // every finished send batch was paying for a lock that had nothing to do. Skipping it
        // cannot lose a close: Close() sets InClosing before it looks at InSending, so either it
        // set InClosing before this update - and then the state we just published carries it and we
        // take the slow path below - or it had not set it yet, in which case it goes on to observe
        // InSending already cleared and closes the socket itself.
        if (state < SocketState.InClosing && !forceClose)
        {
            return;
        }

        ValidateClosed(closeReason, forceClose, true);
    }

    protected virtual void OnSendingCompleted(IList<ArraySegment<byte>> sentItems)
    {
        //The batch is done with the socket here, so any pooled payload can go back to the pool
        //before the next drain reuses the tracking list.
        ReturnPooledSendBuffers();

        if (IsInClosingOrClosed)
        {
            Socket? client;

            //has data is being sent and the socket isn't closed
            if (_sendQueue.Count > 0 && !TryValidateClosedBySocket(out client))
            {
                StartSend(false);
                return;
            }

            OnSendEnd();
            return;
        }

        if (_sendQueue.Count == 0)
        {
            OnSendEnd();

            if (_sendQueue.Count > 0)
            {
                StartSend(true);
            }
        }
        else
        {
            StartSend(false);
        }
    }

    /// <summary>Gets whether the session has nothing left to send.</summary>
    public bool IsSendIdle => _sendQueue == null || (_sendQueue.Count == 0 && !CheckState(SocketState.InSending));

    /// <summary>
    /// How many send requests are waiting in this session's queue.
    /// Metrics only; the queue keeps an advisory count, so this may lag by a moment.
    /// </summary>
    internal int SendQueueDepth => _sendQueue?.Count ?? 0;

    private Socket? _client;
    /// <summary>Gets or sets the client.</summary>
    public Socket? Client => _client;

    protected bool IsInClosingOrClosed => _state >= SocketState.InClosing;

    protected bool IsClosed => _state >= SocketState.Closed;

    /// <summary>Gets the local end point.</summary>
    public virtual IPEndPoint? LocalEndPoint { get; protected set; }

/// <summary>Gets the remote end point.</summary>
    public virtual IPEndPoint? RemoteEndPoint { get; protected set; }

    protected virtual bool TryValidateClosedBySocket(out Socket? socket)
    {
        socket = _client;
        //Already closed/closing
        return socket == null;
    }

    public virtual void Close(CloseReason reason)
    {
        //Already in closing procedure
        if (!TryAddStateFlag(SocketState.InClosing))
            return;

        Socket? client;

        //No need to clean the socket instance
        if (TryValidateClosedBySocket(out client))
            return;

        //Some data is in sending
        if (CheckState(SocketState.InSending))
        {
            //Set closing reason only, don't close the socket directly
            TrySetCloseReason(reason);
            return;
        }

        // In the udp mode, we needn't close the socket instance
        if (client != null)
            InternalClose(client, reason, true);
        else //In Udp mode, and the socket is not in the sending state, then fire the closed event directly
            OnClosed(reason);
    }

    private void InternalClose(Socket client, CloseReason reason, bool setCloseReason)
    {
        if (Interlocked.CompareExchange(ref _client, null, client) == client)
        {
            if (setCloseReason)
                TrySetCloseReason(reason);

            client.SafeClose();

            if (ValidateNotInSendingReceiving())
            {
                OnClosed(reason);
            }
        }
    }

    protected void OnSendError(IList<ArraySegment<byte>> sentItems, CloseReason closeReason)
    {
        ReturnPooledSendBuffers();
        AppSession?.AppServer?.RecordSendError();
        OnSendEnd(closeReason, true);
    }

    // the receive action won't be started for this connection any more
    protected void OnReceiveTerminated(CloseReason closeReason)
    {
        OnReceiveEnded();
        ValidateClosed(closeReason, true);
    }


    // return false if the connection has entered the closing procedure or has closed already
    protected bool OnReceiveStarted()
    {
        if (AddStateFlagIfNotClosing(SocketState.InReceiving))
            return true;

        // the connection is in closing
        ValidateClosed(CloseReason.Unknown, false);
        return false;
    }

    protected void OnReceiveEnded()
    {
        RemoveStateFlag(SocketState.InReceiving);
    }

    /// <summary>Validates the socket is not in the sending or receiving operation.</summary>
    private bool ValidateNotInSendingReceiving()
    {
        var oldState = _state;

        if ((oldState & SocketState.InSendingReceivingMask) == oldState)
        {
            return true;
        }

        return false;
    }

    /// <summary>Records the reason of the first close attempt; later attempts are ignored.</summary>
    private void TrySetCloseReason(CloseReason reason)
    {
        Interlocked.CompareExchange(ref _closeReasonCode, (int)reason, NoCloseReason);
    }

    private CloseReason GetCloseReason()
    {
        var code = Volatile.Read(ref _closeReasonCode);
        return code == NoCloseReason ? CloseReason.Unknown : (CloseReason)code;
    }

    private void FireCloseEvent()
    {
        OnClosed(GetCloseReason());
    }

    private void ValidateClosed(CloseReason closeReason, bool forceClose)
    {
        ValidateClosed(closeReason, forceClose, false);
    }

    private void ValidateClosed(CloseReason closeReason, bool forceClose, bool forSend)
    {
        //Locks the private SyncRoot rather than the session instance: application code that happens
        //to lock the session object would otherwise be able to deadlock the close path.
        lock (SyncRoot)
        {
            if (IsClosed)
                return;

            if (CheckState(SocketState.InClosing))
            {
                // we only keep socket instance after InClosing state when the it is sending
                // so we check if the socket instance is alive now
                if (forSend)
                {
                    Socket? client;

                    if (!TryValidateClosedBySocket(out client))
                    {
                        if (forceClose || _sendQueue.Count == 0)
                        {
                            if (client != null)// the socket instance is not closed yet, do it now
                                InternalClose(client, GetCloseReason(), false);
                            else// The UDP mode, the socket instance always is null, fire the closed event directly
                                FireCloseEvent();

                            return;
                        }

                        return;
                    }
                }

                if (ValidateNotInSendingReceiving())
                {
                    FireCloseEvent();
                }
            }
            else if (forceClose)
            {
                Close(closeReason);
            }
        }
    }

    protected void StartReceiveProcessingTask()
    {
        Volatile.Write(ref _receiveProcessingTaskObserved, 0);
        _receiveProcessingTask = ProcessPipeAsync();
    }

    protected void CompleteReceivePipeWriter(Exception? exception = null)
    {
        try
        {
            _pipeWriter?.Complete(exception);
        }
        catch (InvalidOperationException)
        {
        }

        var task = _receiveProcessingTask;
        if (task != null && Interlocked.Exchange(ref _receiveProcessingTaskObserved, 1) == 0)
            _ = ObserveReceiveProcessingTaskAsync(task);
    }

    private async Task ObserveReceiveProcessingTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exc)
        {
            LogError("Receive pipe processing task faulted", exc);
        }
    }

    /// <summary>
    /// Continuously reads from the PipeReader and dispatches complete requests to the AppSession.
    /// Runs as an independent Task, decoupled from the IOCP completion thread.
    /// </summary>
    protected async Task ProcessPipeAsync()
    {
        try
        {
            while (true)
            {
                ReadResult result;

                try
                {
                    result = await _pipeReader!.ReadAsync();
                }
                catch
                {
                    break;
                }

                var buffer = result.Buffer;

                if (buffer.IsEmpty && (result.IsCompleted || result.IsCanceled))
                    break;

                var processResult = new ProcessReceiveResult(buffer.Start, buffer.End);

                try
                {
                    processResult = AppSession.ProcessRequest(buffer);
                }
                catch (Exception exc)
                {
                    LogError("Protocol error", exc);
                    Close(CloseReason.ProtocolError);
                    break;
                }
                finally
                {
                    _pipeReader!.AdvanceTo(processResult.Consumed, processResult.Examined);
                }

                if (result.IsCompleted || result.IsCanceled)
                    break;
            }
        }
        finally
        {
            _pipeReader!.Complete();
        }
    }

    protected virtual bool IsIgnorableSocketError(int socketErrorCode)
    {
        if (socketErrorCode == 10004 //Interrupted
            || socketErrorCode == 10053 //ConnectionAborted
            || socketErrorCode == 10054 //ConnectionReset
            || socketErrorCode == 10058 //Shutdown
            || socketErrorCode == 10060 //TimedOut
            || socketErrorCode == 995 //OperationAborted
            || socketErrorCode == -1073741299)
        {
            return true;
        }

        return false;
    }

    protected virtual bool IsIgnorableException(Exception e, out int socketErrorCode)
    {
        socketErrorCode = 0;

        if (e is ObjectDisposedException || e is NullReferenceException)
            return true;

        SocketException? socketException = null;

        if (e is IOException)
        {
            if (e.InnerException is ObjectDisposedException || e.InnerException is NullReferenceException)
                return true;

            socketException = e.InnerException as SocketException;
        }
        else
        {
            socketException = e as SocketException;
        }

        if (socketException == null)
            return false;

        socketErrorCode = socketException.ErrorCode;

        if (Config.LogAllSocketException)
            return false;

        return IsIgnorableSocketError(socketErrorCode);
    }

    public void SendEndWhenSendingTimeOut()
    {
        OnSendEnd(CloseReason.TimeOut, false);
    }
}
