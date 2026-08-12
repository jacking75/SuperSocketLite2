using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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

/// <summary>
/// Socket Session, all application session should base on this class
/// </summary>
abstract partial class SocketSession : ISocketSession
{
    public IAppSession AppSession { get; private set; } = null!;

    protected readonly object SyncRoot = new object();

    //0x00 0x00 0x00 0x00
    //1st byte: Closed(Y/N) - 0x01
    //2nd byte: N/A
    //3th byte: CloseReason
    //Last byte: 0000 0000 - normal state
    //0000 0001: in sending
    //0000 0010: in receiving
    //0001 0000: in closing
    private int m_State = 0;

    private ReuseLockBaseBuffer? CollectSendBuffer = null;

    protected Pipe? _receivePipe;
    protected PipeWriter? _pipeWriter;
    protected PipeReader? _pipeReader;
    private Task? m_ReceiveProcessingTask;
    private int m_ReceiveProcessingTaskObserved;

    private void AddStateFlag(int stateValue)
    {
        AddStateFlag(stateValue, false);
    }

    private bool AddStateFlag(int stateValue, bool notClosing)
    {
        while(true)
        {
            var oldState = m_State;

            if (notClosing)
            {
                // don't update the state if the connection has entered the closing procedure
                if (oldState >= SocketState.InClosing)
                {
                    return false;
                }
            }

            var newState = m_State | stateValue;

            if(Interlocked.CompareExchange(ref m_State, newState, oldState) == oldState)
                return true;
        }
    }

    private bool TryAddStateFlag(int stateValue)
    {
        while (true)
        {
            var oldState = m_State;
            var newState = m_State | stateValue;

            //Already marked
            if (oldState == newState)
            {
                return false;
            }

            var compareState = Interlocked.CompareExchange(ref m_State, newState, oldState);

            if (compareState == oldState)
                return true;
        }
    }

    private void RemoveStateFlag(int stateValue)
    {
        while(true)
        {
            var oldState = m_State;
            var newState = m_State & (~stateValue);

            if(Interlocked.CompareExchange(ref m_State, newState, oldState) == oldState)
                return;
        }
    }

    private bool CheckState(int stateValue)
    {
        return (m_State & stateValue) == stateValue;
    }

    protected bool SyncSend { get; private set; }

    private ChannelSendingQueue m_SendQueue = null!;

    // Reused by every StartSend to avoid allocating a List (plus its backing array) per send cycle.
    // Safe because sending is single-flight per session: StartSend only proceeds after it owns the
    // InSending flag, and the previous batch is detached from the SocketAsyncEventArgs
    // (ClearPrevSendState) before OnSendingCompleted can trigger the next drain.
    private readonly List<ArraySegment<byte>> m_SendBatch = new List<ArraySegment<byte>>();

    
    public SocketSession(Socket client)
        : this(Guid.NewGuid().ToString())
    {
        if (client == null)
            throw new ArgumentNullException("client");

        m_Client = client;
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

        m_SendQueue = new ChannelSendingQueue(Math.Max(Config.SendingQueueSize, 1));

        if (Config.CollectSendIntervalMillSec > 0)
        {
            CollectSendBuffer = new ReuseLockBaseBuffer(Config.ReceiveBufferSize);
            SyncSend = true;
        }

        // Initialize the receive pipeline. PipeOptions.useSynchronizationContext=false keeps
        // ProcessPipeAsync off the captured SynchronizationContext.
        var pipeOptions = new PipeOptions(
            minimumSegmentSize: Config.ReceiveBufferSize,
            useSynchronizationContext: false);
        _receivePipe = new Pipe(pipeOptions);
        _pipeWriter  = _receivePipe.Writer;
        _pipeReader  = _receivePipe.Reader;
    }

    /// <summary>
    /// Gets or sets the session ID.
    /// </summary>
    /// <value>The session ID.</value>
    public string SessionID { get; private set; }


    /// <summary>
    /// Gets or sets the config.
    /// </summary>
    /// <value>
    /// The config.
    /// </value>
    public IServerConfig Config { get; set; } = null!;

    /// <summary>
    /// Starts this session.
    /// </summary>
    public abstract void Start();

    /// <summary>
    /// Says the welcome information when a client connectted.
    /// </summary>
    protected virtual void StartSession()
    {
        AppSession.StartSession();
    }

    /// <summary>
    /// Called when [close].
    /// </summary>
    protected virtual void OnClosed(CloseReason reason)
    {
        //Already closed
        if (!TryAddStateFlag(SocketState.Closed))
            return;

        m_SendQueue?.Complete();

        var closedHandler = Closed;
        if (closedHandler != null)
        {
            closedHandler(this, reason);
        }
    }

    /// <summary>
    /// Occurs when [closed].
    /// </summary>
    public Action<ISocketSession, CloseReason>? Closed { get; set; }

    public bool CollectSend(byte[] source, int pos, int count)
    {
        return CollectSendBuffer!.Copy(source, pos, count);
    }

    public ArraySegment<byte> GetCollectSendData()
    {
        return CollectSendBuffer!.GetData();
    }

    public void CommitCollectSend(int size)
    {
        CollectSendBuffer!.Commit(size);
    }


    /// <summary>
    /// Tries to send array segment.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <returns></returns>
    public bool TrySend(IList<ArraySegment<byte>> segments)
    {
        if (IsClosed)
            return false;

        if (!m_SendQueue.TryEnqueue(segments))
            return false;

        StartSend(true);
        return true;
    }

    /// <summary>
    /// Tries to send array segment.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns></returns>
    public bool TrySend(ArraySegment<byte> segment)
    {
        if (IsClosed)
            return false;

        if (!m_SendQueue.TryEnqueue(segment))
            return false;

        StartSend(true);
        return true;
    }

    /// <summary>
    /// Tries to send memory.
    /// </summary>
    /// <param name="memory">The memory.</param>
    public bool TrySend(ReadOnlyMemory<byte> memory)
    {
        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
        {
            return TrySend(segment);
        }

        return TrySend(new ArraySegment<byte>(memory.ToArray()));
    }

    /// <summary>
    /// Tries to send span.
    /// </summary>
    /// <param name="span">The span.</param>
    public bool TrySend(ReadOnlySpan<byte> span)
    {
        return TrySend(new ArraySegment<byte>(span.ToArray()));
    }

    /// <summary>
    /// Sends in async mode.
    /// </summary>
    /// <param name="items">The items.</param>
    protected abstract void SendAsync(IList<ArraySegment<byte>> items);

    /// <summary>
    /// Sends in sync mode.
    /// </summary>
    /// <param name="items">The items.</param>
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

        m_SendQueue.DrainAvailable(m_SendBatch);

        if (m_SendBatch.Count == 0)
        {
            OnSendEnd();
            return;
        }

        Send(m_SendBatch);
    }

    private void OnSendEnd()
    {
        OnSendEnd(CloseReason.Unknown, false);
    }

    private void OnSendEnd(CloseReason closeReason, bool forceClose)
    {
        RemoveStateFlag(SocketState.InSending);
        ValidateClosed(closeReason, forceClose, true);
    }

    protected virtual void OnSendingCompleted(IList<ArraySegment<byte>> sentItems)
    {
        if (IsInClosingOrClosed)
        {
            Socket? client;

            //has data is being sent and the socket isn't closed
            if (m_SendQueue.Count > 0 && !TryValidateClosedBySocket(out client))
            {
                StartSend(false);
                return;
            }

            OnSendEnd();
            return;
        }

        if (m_SendQueue.Count == 0)
        {
            OnSendEnd();

            if (m_SendQueue.Count > 0)
            {
                StartSend(true);
            }
        }
        else
        {
            StartSend(false);
        }
    }

    private Socket? m_Client;
    /// <summary>
    /// Gets or sets the client.
    /// </summary>
    /// <value>The client.</value>
    public Socket? Client
    {
        get { return m_Client; }
    }

    protected bool IsInClosingOrClosed
    {
        get { return m_State >= SocketState.InClosing; }
    }

    protected bool IsClosed
    {
        get { return m_State >= SocketState.Closed; }
    }

    /// <summary>
    /// Gets the local end point.
    /// </summary>
    /// <value>The local end point.</value>
    public virtual IPEndPoint? LocalEndPoint { get; protected set; }

/// <summary>
    /// Gets the remote end point.
    /// </summary>
    /// <value>The remote end point.</value>
    public virtual IPEndPoint? RemoteEndPoint { get; protected set; }

    protected virtual bool TryValidateClosedBySocket(out Socket? socket)
    {
        socket = m_Client;
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
            AddStateFlag(GetCloseReasonValue(reason));
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
        if (Interlocked.CompareExchange(ref m_Client, null, client) == client)
        {
            if (setCloseReason)
                AddStateFlag(GetCloseReasonValue(reason));

            client.SafeClose();

            if (ValidateNotInSendingReceiving())
            {
                OnClosed(reason);
            }
        }
    }

    protected void OnSendError(IList<ArraySegment<byte>> sentItems, CloseReason closeReason)
    {
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
        if (AddStateFlag(SocketState.InReceiving, true))
            return true;

        // the connection is in closing
        ValidateClosed(CloseReason.Unknown, false);
        return false;
    }

    protected void OnReceiveEnded()
    {
        RemoveStateFlag(SocketState.InReceiving);
    }

    /// <summary>
    /// Validates the socket is not in the sending or receiving operation.
    /// </summary>
    /// <returns></returns>
    private bool ValidateNotInSendingReceiving()
    {
        var oldState = m_State;

        if ((oldState & SocketState.InSendingReceivingMask) == oldState)
        {
            return true;
        }

        return false;
    }

    private const int m_CloseReasonMagic = 256;

    private int GetCloseReasonValue(CloseReason reason)
    {
        return ((int)reason + 1) * m_CloseReasonMagic;
    }

    private CloseReason GetCloseReasonFromState()
    {
        return (CloseReason)(m_State / m_CloseReasonMagic - 1);
    }

    private void FireCloseEvent()
    {
        OnClosed(GetCloseReasonFromState());
    }

    private void ValidateClosed()
    {
        // CloseReason.Unknown won't be used
        ValidateClosed(CloseReason.Unknown, false);
    }

    private void ValidateClosed(CloseReason closeReason, bool forceClose)
    {
        ValidateClosed(closeReason, forceClose, false);
    }

    private void ValidateClosed(CloseReason closeReason, bool forceClose, bool forSend)
    {
        lock (this)
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
                        if (forceClose || m_SendQueue.Count == 0)
                        {
                            if (client != null)// the socket instance is not closed yet, do it now
                                InternalClose(client, GetCloseReasonFromState(), false);
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

    [Obsolete("OrigReceiveOffset is not used in the Pipelines receive path and always returns 0.")]
    public virtual int OrigReceiveOffset => 0;

    protected void StartReceiveProcessingTask()
    {
        Volatile.Write(ref m_ReceiveProcessingTaskObserved, 0);
        m_ReceiveProcessingTask = ProcessPipeAsync();
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

        var task = m_ReceiveProcessingTask;
        if (task != null && Interlocked.Exchange(ref m_ReceiveProcessingTaskObserved, 1) == 0)
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

            // Return the per-session filter carry buffer AFTER the pipe loop has fully exited,
            // so no code can access it after ArrayPool.Return() is called.
            // This is the only place where the buffer is returned to the pool; OnSessionClosed()
            // only nulls the reference as a safety fallback.
            AppSession?.CompleteReceivePipe();
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
