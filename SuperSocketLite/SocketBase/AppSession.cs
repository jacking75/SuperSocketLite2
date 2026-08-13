using System.Buffers;
using System.Net;
using System.Text;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>
/// AppSession base class
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class AppSession<TAppSession, TRequestInfo> : IAppSession, IAppSession<TAppSession, TRequestInfo>
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
    where TRequestInfo : class, IRequestInfo
{
    /// <summary>
    /// Gets the app server instance assosiated with the session.
    /// </summary>
    public virtual AppServerBase<TAppSession, TRequestInfo> AppServer { get; private set; } = null!;

    /// <summary>
    /// Gets the app server instance assosiated with the session.
    /// </summary>
    IAppServer IAppSession.AppServer
    {
        get { return this.AppServer; }
    }

    /// <summary>
    /// Gets or sets the charset which is used for transfering text message.
    /// </summary>
    /// <value>
    /// The charset.
    /// </value>
    public Encoding Charset { get; set; } = null!;

    private IDictionary<object, object>? _items;

    /// <summary>
    /// Gets the items dictionary, only support 10 items maximum
    /// </summary>
    public IDictionary<object, object> Items
    {
        get
        {
            if (_items == null)
                _items = new Dictionary<object, object>(10);

            return _items;
        }
    }


    // volatile: set to false on the close thread (OnSocketSessionClosed), read on the
    // sending thread inside InternalSend()'s while(_connected) spin.  Without volatile
    // the write may not be visible on ARM, causing an infinite spin.
    private volatile bool _connected = false;

    /// <summary>
    /// Gets a value indicating whether this <see cref="IAppSession"/> is connected.
    /// </summary>
    /// <value>
    ///   <c>true</c> if connected; otherwise, <c>false</c>.
    /// </value>
    public bool Connected
    {
        get { return _connected; }
        internal set { _connected = value; }
    }

    /// <summary>
    /// Gets or sets the previous command.
    /// </summary>
    /// <value>
    /// The prev command.
    /// </value>
    public string? PrevCommand { get; set; }

    /// <summary>
    /// Gets or sets the current executing command.
    /// </summary>
    /// <value>
    /// The current command.
    /// </value>
public string? CurrentCommand { get; set; }

    /// <summary>
    /// Gets the local listening endpoint.
    /// </summary>
    public IPEndPoint? LocalEndPoint
    {
        get { return SocketSession.LocalEndPoint; }
    }

    /// <summary>
    /// Gets the remote endpoint of client.
    /// </summary>
    public IPEndPoint? RemoteEndPoint
    {
        get { return SocketSession.RemoteEndPoint; }
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    public ILog Logger
    {
        get { return AppServer.Logger; }
    }

    /// <summary>
    /// Gets this session's identity for structured logging.
    /// </summary>
    /// <remarks>
    /// A struct of two references, so reading it allocates nothing; pass it to
    /// <see cref="ILog.Log"/> instead of baking the session ID into the message text.
    /// </remarks>
    public LogSessionContext SessionLogContext => new(SessionID, RemoteEndPoint);

    // The authoritative "last activity" stamp, in Environment.TickCount64 milliseconds.
    // Reading the monotonic tick counter is far cheaper than DateTime.Now (which additionally does
    // a time zone conversion) and this is touched on every successful send and every request.
    private long _lastActiveTimeTicks;

    /// <summary>
    /// Gets or sets the last active time of the session, in UTC.
    /// </summary>
    /// <value>
    /// The last active time.
    /// </value>
    /// <remarks>
    /// The value is derived from a monotonic tick stamp, so it is accurate to a few milliseconds
    /// rather than exact. Idle-session detection uses the tick stamp directly and never goes
    /// through this property.
    /// </remarks>
    public DateTime LastActiveTime
    {
        get { return DateTime.UtcNow.AddMilliseconds(_lastActiveTimeTicks - Environment.TickCount64); }
        set { _lastActiveTimeTicks = Environment.TickCount64 - (long)(DateTime.UtcNow - value.ToUniversalTime()).TotalMilliseconds; }
    }

    /// <summary>
    /// Gets the tick stamp (<see cref="Environment.TickCount64"/>) of the last activity on this session.
    /// </summary>
    internal long LastActiveTimeTicks => Volatile.Read(ref _lastActiveTimeTicks);

    /// <summary>
    /// Stamps the session as active right now. This is the hot-path form of
    /// setting <see cref="LastActiveTime"/>.
    /// </summary>
    internal void MarkActive()
    {
        Volatile.Write(ref _lastActiveTimeTicks, Environment.TickCount64);
    }

    /// <summary>
    /// Gets the start time of the session, in UTC.
    /// </summary>
    public DateTime StartTime { get; private set; }

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public string SessionID { get; private set; } = null!;

    /// <summary>
    /// Gets the socket session of the AppSession.
    /// </summary>
    public ISocketSession SocketSession { get; private set; } = null!;

    /// <summary>
    /// Gets the config of the server.
    /// </summary>
    public IServerConfig Config
    {
        get { return AppServer.Config; }
    }

    IReceiveFilter<TRequestInfo> _receiveFilter = null!;

    // Per-session carry buffer for the Pipelines receive path.
    // Filters accumulate partial packet data in this buffer between reads.
    private byte[]? _filterBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSession&lt;TAppSession, TRequestInfo&gt;"/> class.
    /// </summary>
    public AppSession()
    {
        this.StartTime = DateTime.UtcNow;
        MarkActive();
    }


    /// <summary>
    /// Initializes the specified app session by AppServer and SocketSession.
    /// </summary>
    /// <param name="appServer">The app server.</param>
    /// <param name="socketSession">The socket session.</param>
    public virtual void Initialize(IAppServer<TAppSession, TRequestInfo> appServer, ISocketSession socketSession)
    {
        var castedAppServer = (AppServerBase<TAppSession, TRequestInfo>)appServer;
        AppServer = castedAppServer;
        Charset = castedAppServer.TextEncoding;
        SocketSession = socketSession;
        SessionID = socketSession.SessionID;
        _connected = true;
        _receiveFilter = castedAppServer.ReceiveFilterFactory.CreateFilter(appServer, this, socketSession.RemoteEndPoint);
                    
        var filterInitializer = _receiveFilter as IReceiveFilterInitializer;
        if (filterInitializer != null)
            filterInitializer.Initialize(castedAppServer, this);

        socketSession.Initialize(this);

        OnInit();
    }

    /// <summary>
    /// Starts the session.
    /// </summary>
    void IAppSession.StartSession()
    {
        OnSessionStarted();
    }

    /// <summary>
    /// Called when [init].
    /// </summary>
    protected virtual void OnInit()
    {
        
    }

    /// <summary>
    /// Called when [session started].
    /// </summary>
    protected virtual void OnSessionStarted()
    {

    }

    /// <summary>
    /// Called when [session closed].
    /// </summary>
    /// <param name="reason">The reason.</param>
    internal protected virtual void OnSessionClosed(CloseReason reason)
    {
        // _filterBuffer is returned to the pool by CompleteReceivePipe() which is called
        // at the end of ProcessPipeAsync() — after the pipe loop fully exits.
        // Setting null here is a safe fallback in case the session never started receiving.
        _filterBuffer = null;
    }

    // Called by SocketSession.ProcessPipeAsync() after the PipeReader loop exits,
    // guaranteeing no further access to _filterBuffer before returning it to the pool.
    public void CompleteReceivePipe()
    {
        var buf = _filterBuffer;
        if (buf != null)
        {
            _filterBuffer = null;
            ArrayPool<byte>.Shared.Return(buf);
        }
    }


    /// <summary>
    /// Handles the exceptional error, it only handles application error.
    /// </summary>
    /// <param name="e">The exception.</param>
    protected virtual void HandleException(Exception e)
    {
        Logger.Error(this.ToString()!, e);
        this.Close(CloseReason.ApplicationError);
    }

    /// <summary>
    /// Handles the unknown request.
    /// </summary>
    /// <param name="requestInfo">The request info.</param>
    protected virtual void HandleUnknownRequest(TRequestInfo requestInfo)
    {

    }

    internal void InternalHandleUnknownRequest(TRequestInfo requestInfo)
    {
        HandleUnknownRequest(requestInfo);
    }

    internal void InternalHandleExcetion(Exception e)
    {
        HandleException(e);
    }

    /// <summary>
    /// Closes the session by the specified reason.
    /// </summary>
    /// <param name="reason">The close reason.</param>
    public virtual void Close(CloseReason reason)
    {
        this.SocketSession.Close(reason);
    }

    /// <summary>
    /// Closes this session.
    /// </summary>
    public virtual void Close()
    {
        Close(CloseReason.ServerClosing);
    }

    
    /// <summary>
    /// Try to send the message to client.
    /// </summary>
    /// <param name="message">The message which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(string message)
    {
        var data = this.Charset.GetBytes(message);
        return InternalTrySend(new ArraySegment<byte>(data, 0, data.Length));
    }

    /// <summary>
    /// Sends the message to client.
    /// </summary>
    /// <param name="message">The message which will be sent.</param>
    public virtual void Send(string message)
    {
        var data = this.Charset.GetBytes(message);
        Send(data, 0, data.Length);
    }

    /// <summary>
    /// Try to send the data to client.
    /// </summary>
    /// <param name="data">The data which will be sent.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(byte[] data, int offset, int length)
    {
        return InternalTrySend(new ArraySegment<byte>(data, offset, length));
    }

    /// <summary>
    /// Sends the data to client.
    /// </summary>
    /// <param name="data">The data which will be sent.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    public virtual void Send(byte[] data, int offset, int length)
    {
        InternalSend(new ArraySegment<byte>(data, offset, length));
    }

    private bool InternalTrySend(ArraySegment<byte> segment)
    {
        if (!SocketSession.TrySend(segment))
            return false;

        MarkActive();
        return true;
    }

    /// <summary>
    /// Try to send the data segment to client.
    /// </summary>
    /// <param name="segment">The segment which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(ArraySegment<byte> segment)
    {
        if (!_connected)
            return false;

        return InternalTrySend(segment);
    }


    private void InternalSend(ArraySegment<byte> segment)
    {
        if (!_connected)
            return;

        if (InternalTrySend(segment))
            return;

        var sendTimeOut = Config.SendTimeOut;

        //Don't retry, timeout directly
        if (sendTimeOut < 0)
        {
            throw new TimeoutException("The sending attempt timed out");
        }

        var deadline = Environment.TickCount64 + sendTimeOut;

        var spinWait = new SpinWait();

        while (_connected)
        {
            spinWait.SpinOnce();

            if (InternalTrySend(segment))
                return;

            //If sendTimeOut = 0, don't have timeout check
            if (sendTimeOut > 0 && Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("The sending attempt timed out");
            }
        }
    }

    /// <summary>
    /// Sends the data segment to client.
    /// </summary>
    /// <param name="segment">The segment which will be sent.</param>
    public virtual void Send(ArraySegment<byte> segment)
    {
        InternalSend(segment);
    }

    private bool InternalTrySend(IList<ArraySegment<byte>> segments)
    {
        if (!SocketSession.TrySend(segments))
            return false;

        MarkActive();
        return true;
    }

    /// <summary>
    /// Try to send the data segments to client.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue; if it returns false, the sending queue may be full or the socket is not connected</returns>
    public virtual bool TrySend(IList<ArraySegment<byte>> segments)
    {
        if (!_connected)
            return false;

        return InternalTrySend(segments);
    }

    private void InternalSend(IList<ArraySegment<byte>> segments)
    {
        if (!_connected)
            return;

        if (InternalTrySend(segments))
            return;

        var sendTimeOut = Config.SendTimeOut;

        //Don't retry, timeout directly
        if (sendTimeOut < 0)
        {
            throw new TimeoutException("The sending attempt timed out");
        }

        var deadline = Environment.TickCount64 + sendTimeOut;

        var spinWait = new SpinWait();

        while (_connected)
        {
            spinWait.SpinOnce();

            if (InternalTrySend(segments))
                return;

            //If sendTimeOut = 0, don't have timeout check
            if (sendTimeOut > 0 && Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("The sending attempt timed out");
            }
        }
    }

    /// <summary>
    /// Sends the data segments to client.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <remarks>
    /// The segment list itself is copied, so it can be reused as soon as this returns, but the
    /// underlying arrays are not: do not modify them until the data has been sent. Use
    /// <see cref="TrySendCopied"/> or <see cref="SendCopied"/> when the buffer must be reused right away.
    /// </remarks>
    public virtual void Send(IList<ArraySegment<byte>> segments)
    {
        InternalSend(segments);
    }

    private bool InternalTrySendCopied(ReadOnlySpan<byte> data)
    {
        if (!SocketSession.TrySendCopied(data))
            return false;

        MarkActive();
        return true;
    }

    /// <summary>
    /// Try to send a copy of the data to the client, so the caller's buffer can be reused immediately.
    /// </summary>
    /// <param name="data">The data which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySendCopied(ReadOnlySpan<byte> data)
    {
        if (!_connected)
            return false;

        return InternalTrySendCopied(data);
    }

    /// <summary>
    /// Sends a copy of the data to the client, so the caller's buffer can be reused immediately.
    /// </summary>
    /// <param name="data">The data which will be sent.</param>
    /// <exception cref="TimeoutException">The sending queue stayed full for longer than SendTimeOut.</exception>
    public virtual void SendCopied(ReadOnlySpan<byte> data)
    {
        if (!_connected)
            return;

        if (InternalTrySendCopied(data))
            return;

        var sendTimeOut = Config.SendTimeOut;

        //Don't retry, timeout directly
        if (sendTimeOut < 0)
        {
            throw new TimeoutException("The sending attempt timed out");
        }

        var deadline = Environment.TickCount64 + sendTimeOut;

        var spinWait = new SpinWait();

        while (_connected)
        {
            spinWait.SpinOnce();

            if (InternalTrySendCopied(data))
                return;

            //If sendTimeOut = 0, don't have timeout check
            if (sendTimeOut > 0 && Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("The sending attempt timed out");
            }
        }
    }

    /// <summary>
    /// Sends the data to the client, waiting asynchronously while the sending queue is full.
    /// </summary>
    /// <param name="data">The data which will be sent. Array-backed memory is sent without copying.</param>
    /// <param name="cancellationToken">Cancels the wait for queue space.</param>
    /// <returns>false if the session is not connected, or was closed while waiting.</returns>
    /// <remarks>
    /// The configured <c>SendTimeOut</c> does not apply to this overload - it is the caller's job to
    /// bound the wait, e.g. with <c>new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token</c>.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public virtual async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!_connected)
            return false;

        if (!await SocketSession.SendAsync(data, cancellationToken).ConfigureAwait(false))
            return false;

        MarkActive();
        return true;
    }

    /// <summary>
    /// Sends the response.
    /// </summary>
    /// <param name="message">The message which will be sent.</param>
    /// <param name="paramValues">The parameter values.</param>
    public virtual void Send(string message, params object[] paramValues)
    {
        var data = this.Charset.GetBytes(string.Format(message, paramValues));
        InternalSend(new ArraySegment<byte>(data, 0, data.Length));
    }

    public void SendEndWhenSendingTimeOut()
    {
        this.SocketSession.SendEndWhenSendingTimeOut();
    }


    public bool CollectSend(byte[] source, int pos, int count)
    {
        return this.SocketSession.CollectSend(source, pos, count);
    }

    public ArraySegment<byte> GetCollectSendData()
    {
        return this.SocketSession.GetCollectSendData();
    }

    public void CommitCollectSend(int size)
    {
        this.SocketSession.CommitCollectSend(size);
    }


    /// <summary>
    /// Sets the next Receive filter which will be used when next data block received
    /// </summary>
    /// <param name="nextReceiveFilter">The next receive filter.</param>
    protected void SetNextReceiveFilter(IReceiveFilter<TRequestInfo> nextReceiveFilter)
    {
        _receiveFilter = nextReceiveFilter;
    }

    /// <summary>
    /// Gets the maximum allowed length of the request.
    /// </summary>
    /// <returns></returns>
    protected virtual int GetMaxRequestLength()
    {
        return AppServer.Config.MaxRequestLength;
    }

    /// <summary>
    /// Filters the request.
    /// </summary>
    /// <param name="readBuffer">The read buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <param name="rest">The rest, the size of the data which has not been processed</param>
    /// <param name="offsetDelta">return offset delta of next receiving buffer.</param>
    /// <returns></returns>
    TRequestInfo? FilterRequest(byte[] readBuffer, int offset, int length, bool toBeCopied, out int rest, out int offsetDelta)
    {
        if (!AppServer.OnRawDataReceived(this, readBuffer, offset, length))
        {
            rest = 0;
            offsetDelta = 0;
            return null;
        }

        var currentRequestLength = _receiveFilter.LeftBufferSize;

        var requestInfo = _receiveFilter.Filter(readBuffer, offset, length, toBeCopied, out rest);

        if (_receiveFilter.State == FilterState.Error)
        {
            rest = 0;
            offsetDelta = 0;
            Close(CloseReason.ProtocolError);
            return null;
        }

        var offsetAdapter = _receiveFilter as IOffsetAdapter;

        offsetDelta = offsetAdapter != null ? offsetAdapter.OffsetDelta : 0;

        if (requestInfo == null)
        {
            //current buffered length
            currentRequestLength = _receiveFilter.LeftBufferSize;
        }
        else
        {
            //current request length
            currentRequestLength = currentRequestLength + length - rest;
        }

        var maxRequestLength = GetMaxRequestLength();

        if (currentRequestLength >= maxRequestLength)
        {
            if (Logger.IsErrorEnabled)
            {
                Logger.Log(LogEventLevel.Error, SessionLogContext,
                    string.Format("Max request length: {0}, current processed length: {1}", maxRequestLength, currentRequestLength));
            }

            Close(CloseReason.ProtocolError);
            return null;
        }

        //If next Receive filter wasn't set, still use current Receive filter in next round received data processing
        if (_receiveFilter.NextReceiveFilter != null)
            _receiveFilter = _receiveFilter.NextReceiveFilter;

        return requestInfo;
    }

    /// <summary>
    /// Processes the request data.
    /// </summary>
    /// <param name="readBuffer">The read buffer.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <param name="toBeCopied">if set to <c>true</c> [to be copied].</param>
    /// <returns>
    /// return offset delta of next receiving buffer
    /// </returns>
    int IAppSession.ProcessRequest(byte[] readBuffer, int offset, int length, bool toBeCopied)
    {
        int rest, offsetDelta;

        while (true)
        {
            var requestInfo = FilterRequest(readBuffer, offset, length, toBeCopied, out rest, out offsetDelta);

            if (requestInfo != null)
            {
                try
                {
                    AppServer.ExecuteCommand(this, requestInfo);
                }
                catch (Exception e)
                {
                    HandleException(e);
                }
            }

            if (rest <= 0)
            {
                return offsetDelta;
            }

            //Still have data has not been processed
            offset = offset + length - rest;
            length = rest;
        }
    }

    /// <summary>
    /// Processes the request data from the Pipelines receive path.
    /// Maintains a per-session carry buffer so that existing IReceiveFilter implementations
    /// work correctly without modification: partial data is preserved at offset 0 of the
    /// carry buffer, and new bytes are appended at the filter's current OffsetDelta position.
    /// </summary>
    ProcessReceiveResult IAppSession.ProcessRequest(ReadOnlySequence<byte> sequence)
    {
        if (!AppServer.HasRawDataReceivedHandler && _receiveFilter is ISequenceReceiveFilter<TRequestInfo> sequenceReceiveFilter)
        {
            return ProcessSequenceRequest(sequence, sequenceReceiveFilter);
        }

        // Determine where in the carry buffer the filter expects new bytes.
        // IOffsetAdapter.OffsetDelta == _parsedLength of the current filter (bytes already accumulated).
        var filterOffsetAdapter = _receiveFilter as IOffsetAdapter;
        int writeOffset = filterOffsetAdapter?.OffsetDelta ?? 0;

        if (sequence.Length > int.MaxValue)
        {
            Close(CloseReason.ProtocolError);
            return new ProcessReceiveResult(sequence.End, sequence.End);
        }

        int newLength = (int)sequence.Length;
        int neededSize = writeOffset + newLength;
        var maxRequestLength = GetMaxRequestLength();

        if (maxRequestLength > 0 && neededSize >= maxRequestLength)
        {
            if (Logger.IsErrorEnabled)
            {
                Logger.Log(LogEventLevel.Error, SessionLogContext,
                    string.Format("Max request length: {0}, current processed length: {1}", maxRequestLength, neededSize));
            }

            Close(CloseReason.ProtocolError);
            return new ProcessReceiveResult(sequence.End, sequence.End);
        }

        // Lazily allocate or grow the carry buffer.
        if (_filterBuffer == null || _filterBuffer.Length < neededSize)
        {
            int newSize = Math.Max(Config.ReceiveBufferSize * 2, neededSize);
            var newBuf = ArrayPool<byte>.Shared.Rent(newSize);

            // Preserve any partial data already copied by the filter into _filterBuffer[0..writeOffset].
            if (_filterBuffer != null)
            {
                if (writeOffset > 0)
                    Array.Copy(_filterBuffer, 0, newBuf, 0, writeOffset);
                ArrayPool<byte>.Shared.Return(_filterBuffer);
            }

            _filterBuffer = newBuf;
        }

        // Copy new bytes from the PipeReader sequence into the carry buffer immediately after
        // any existing partial data so the filter sees a contiguous buffer.
        sequence.CopyTo(new Span<byte>(_filterBuffer, writeOffset, newLength));

        // Run the filter loop on the carry buffer — identical logic to the byte[] overload.
        int offset = writeOffset;
        int length = newLength;

        while (true)
        {
            var requestInfo = FilterRequest(_filterBuffer, offset, length, false, out int rest, out _);

            if (requestInfo != null)
            {
                try
                {
                    AppServer.ExecuteCommand(this, requestInfo);
                }
                catch (Exception e)
                {
                    HandleException(e);
                }
            }

            if (rest <= 0)
                break;

            // More requests present in the current buffer.
            offset = offset + length - rest;
            length = rest;
        }

        // Always tell the PipeReader that all bytes have been consumed.
        // Partial-packet state is stored in _filterBuffer + filter's _parsedLength/_offsetDelta.
        return new ProcessReceiveResult(sequence.End, sequence.End);
    }

    private ProcessReceiveResult ProcessSequenceRequest(ReadOnlySequence<byte> sequence, ISequenceReceiveFilter<TRequestInfo> sequenceReceiveFilter)
    {
        var maxRequestLength = GetMaxRequestLength();

        var current = sequence;
        var consumedPosition = sequence.Start;

        while (current.Length > 0)
        {
            var requestInfo = sequenceReceiveFilter.Filter(current, out var consumed, out var examined);

            if (_receiveFilter.State == FilterState.Error)
            {
                Close(CloseReason.ProtocolError);
                return new ProcessReceiveResult(sequence.End, sequence.End);
            }

            if (requestInfo != null)
            {
                consumedPosition = consumed;

                try
                {
                    AppServer.ExecuteCommand(this, requestInfo);
                }
                catch (Exception e)
                {
                    HandleException(e);
                }

                if (_receiveFilter.NextReceiveFilter != null)
                {
                    _receiveFilter = _receiveFilter.NextReceiveFilter;

                    if (_receiveFilter is not ISequenceReceiveFilter<TRequestInfo> nextSequenceReceiveFilter)
                        return new ProcessReceiveResult(consumedPosition, consumedPosition);

                    sequenceReceiveFilter = nextSequenceReceiveFilter;
                }

                current = sequence.Slice(consumedPosition);
                continue;
            }

            // The filter needs more data. Only the bytes it could not turn into a request yet count
            // towards MaxRequestLength: the receive pipe legitimately holds many complete pipelined
            // requests at once, and measuring the whole buffer would kill healthy connections.
            var pendingLength = sequence.Slice(consumed).Length;

            if (maxRequestLength > 0 && pendingLength >= maxRequestLength)
            {
                if (Logger.IsErrorEnabled)
                {
                    Logger.Log(LogEventLevel.Error, SessionLogContext,
                        string.Format("Max request length: {0}, current processed length: {1}", maxRequestLength, pendingLength));
                }

                Close(CloseReason.ProtocolError);
                return new ProcessReceiveResult(sequence.End, sequence.End);
            }

            return new ProcessReceiveResult(consumed, examined);
        }

        return new ProcessReceiveResult(consumedPosition, consumedPosition);
    }

}

/// <summary>
/// AppServer basic class for whose request infoe type is StringRequestInfo
/// </summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
public abstract class AppSession<TAppSession> : AppSession<TAppSession, StringRequestInfo>
    where TAppSession : AppSession<TAppSession, StringRequestInfo>, IAppSession, new()
{

    private bool _appendNewLineForResponse = false;

    private static string s_NewLine = "\r\n";

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSession&lt;TAppSession&gt;"/> class.
    /// </summary>
    public AppSession()
        : this(true)
    {

    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSession&lt;TAppSession&gt;"/> class.
    /// </summary>
    /// <param name="appendNewLineForResponse">if set to <c>true</c> [append new line for response].</param>
    public AppSession(bool appendNewLineForResponse)
    {
        _appendNewLineForResponse = appendNewLineForResponse;
    }

    /// <summary>
    /// Handles the unknown request.
    /// </summary>
    /// <param name="requestInfo">The request info.</param>
    protected override void HandleUnknownRequest(StringRequestInfo requestInfo)
    {
        Send("Unknown request: " + requestInfo.Key);
    }

    /// <summary>
    /// Processes the sending message.
    /// </summary>
    /// <param name="rawMessage">The raw message.</param>
    /// <returns></returns>
    protected virtual string ProcessSendingMessage(string rawMessage)
    {
        if (!_appendNewLineForResponse)
            return rawMessage;

        if (AppServer.Config.Mode == SocketMode.Udp)
            return rawMessage;

        if (string.IsNullOrEmpty(rawMessage) || !rawMessage.EndsWith(s_NewLine))
            return rawMessage + s_NewLine;
        else
            return rawMessage;
    }

    /// <summary>
    /// Sends the specified message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns></returns>
    public override void Send(string message)
    {
        base.Send(ProcessSendingMessage(message));
    }

    /// <summary>
    /// Sends the response.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="paramValues">The param values.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public override void Send(string message, params object[] paramValues)
    {
        base.Send(ProcessSendingMessage(message), paramValues);
    }
}

/// <summary>
/// AppServer basic class for whose request infoe type is StringRequestInfo
/// </summary>
public class AppSession : AppSession<AppSession>
{

}
