using System.Buffers;
using System.Net;
using System.Text;
using SuperSocketLite.SocketBase.Config;
using SuperSocketLite.SocketBase.Logging;
using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>AppSession base class</summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public abstract class AppSession<TAppSession, TRequestInfo> : IAppSession, IAppSession<TAppSession, TRequestInfo>
    where TAppSession : AppSession<TAppSession, TRequestInfo>, IAppSession, new()
    where TRequestInfo : class, IRequestInfo
{
    /// <summary>Gets the app server instance assosiated with the session.</summary>
    public virtual AppServerBase<TAppSession, TRequestInfo> AppServer { get; private set; } = null!;

    /// <summary>Gets the app server instance assosiated with the session.</summary>
    IAppServer IAppSession.AppServer => this.AppServer;

    /// <summary>Gets or sets the charset which is used for transfering text message.</summary>
    public Encoding Charset { get; set; } = null!;

    // volatile: set to false on the close thread (OnSocketSessionClosed), read on the
    // sending thread inside InternalSend()'s while(_connected) spin.  Without volatile
    // the write may not be visible on ARM, causing an infinite spin.
    private volatile bool _connected = false;

    /// <summary>Gets a value indicating whether this <see cref="IAppSession"/> is connected.</summary>
    public bool Connected
    {
        get { return _connected; }
        internal set { _connected = value; }
    }

    /// <summary>Gets the local listening endpoint.</summary>
    public IPEndPoint? LocalEndPoint => SocketSession.LocalEndPoint;

    /// <summary>Gets the remote endpoint of client.</summary>
    public IPEndPoint? RemoteEndPoint => SocketSession.RemoteEndPoint;

    /// <summary>Gets the logger.</summary>
    public ILog Logger => AppServer.Logger;

    /// <summary>Gets this session's identity for structured logging.</summary>
    /// <remarks>
    /// A struct of two references, so reading it allocates nothing; pass it to
    /// <see cref="ILog.Log"/> instead of baking the session ID into the message text.
    /// </remarks>
    public LogSessionContext SessionLogContext => new(SessionID, RemoteEndPoint);

    // The authoritative "last activity" stamp, in Environment.TickCount64 milliseconds.
    // Reading the monotonic tick counter is far cheaper than DateTime.Now (which additionally does
    // a time zone conversion) and this is touched on every successful send and every request.
    private long _lastActiveTimeTicks;

    /// <summary>Gets or sets the last active time of the session, in UTC.</summary>
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

    /// <summary>Gets the tick stamp (<see cref="Environment.TickCount64"/>) of the last activity on this session.</summary>
    internal long LastActiveTimeTicks => Volatile.Read(ref _lastActiveTimeTicks);

    /// <summary>
    /// Stamps the session as active right now. This is the hot-path form of
    /// setting <see cref="LastActiveTime"/>.
    /// </summary>
    internal void MarkActive()
    {
        Volatile.Write(ref _lastActiveTimeTicks, Environment.TickCount64);
    }

    /// <summary>Gets the start time of the session, in UTC.</summary>
    public DateTime StartTime { get; private set; }

    /// <summary>Gets the session ID.</summary>
    public string SessionID { get; private set; } = null!;

    /// <summary>Gets the socket session of the AppSession.</summary>
    public ISocketSession SocketSession { get; private set; } = null!;

    /// <summary>Gets the config of the server.</summary>
    public IServerConfig Config => AppServer.Config;

    IReceiveFilter<TRequestInfo> _receiveFilter = null!;

    public AppSession()
    {
        this.StartTime = DateTime.UtcNow;
        MarkActive();
    }


    /// <summary>Initializes the specified app session by AppServer and SocketSession.</summary>
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

    /// <summary>Starts the session.</summary>
    void IAppSession.StartSession()
    {
        OnSessionStarted();
    }

    /// <summary>Called when [init].</summary>
    protected virtual void OnInit()
    {
        
    }

    /// <summary>Called when [session started].</summary>
    protected virtual void OnSessionStarted()
    {

    }

    /// <summary>Called when [session closed].</summary>
    internal protected virtual void OnSessionClosed(CloseReason reason)
    {
    }

    /// <summary>Handles the exceptional error, it only handles application error.</summary>
    /// <param name="e">The exception.</param>
    protected virtual void HandleException(Exception e)
    {
        Logger.Error(this.ToString()!, e);
        this.Close(CloseReason.ApplicationError);
    }

    /// <summary>Handles the unknown request.</summary>
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

    /// <summary>Closes the session by the specified reason.</summary>
    /// <param name="reason">The close reason.</param>
    public virtual void Close(CloseReason reason)
    {
        this.SocketSession.Close(reason);
    }

    /// <summary>Closes this session.</summary>
    public virtual void Close()
    {
        Close(CloseReason.ServerClosing);
    }

    
    /// <summary>Try to send the message to client.</summary>
    /// <param name="message">The message which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(string message)
    {
        var data = this.Charset.GetBytes(message);
        return InternalTrySend(new ArraySegment<byte>(data, 0, data.Length));
    }

    /// <summary>Sends the message to client.</summary>
    /// <param name="message">The message which will be sent.</param>
    public virtual void Send(string message)
    {
        var data = this.Charset.GetBytes(message);
        Send(data, 0, data.Length);
    }

    /// <summary>Try to send the data to client.</summary>
    /// <param name="data">The data which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(byte[] data, int offset, int length)
    {
        return InternalTrySend(new ArraySegment<byte>(data, offset, length));
    }

    /// <summary>Sends the data to client.</summary>
    /// <param name="data">The data which will be sent.</param>
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

    /// <summary>Try to send the data segment to client.</summary>
    /// <param name="segment">The segment which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySend(ArraySegment<byte> segment)
    {
        if (!_connected)
            return false;

        return InternalTrySend(segment);
    }


    /// <summary>
    /// The spin-and-timeout policy shared by every blocking Send overload.
    /// </summary>
    /// <remarks>
    /// A struct so the retry loop allocates nothing on the sending hot path. The single blocking
    /// Send that <see cref="SendCopied"/> performs takes a <c>ReadOnlySpan</c>, which cannot be
    /// captured by a lambda - that is why the retry is expressed as a policy the caller drives
    /// rather than as a delegate the policy calls.
    /// </remarks>
    private struct SendRetryPolicy
    {
        private const string TimedOutMessage = "The sending attempt timed out";

        private readonly int _sendTimeOut;
        private readonly long _deadline;
        private SpinWait _spinWait;

        public SendRetryPolicy(int sendTimeOut)
        {
            //Don't retry, timeout directly
            if (sendTimeOut < 0)
            {
                throw new TimeoutException(TimedOutMessage);
            }

            _sendTimeOut = sendTimeOut;
            _deadline = Environment.TickCount64 + sendTimeOut;
            _spinWait = new SpinWait();
        }

        /// <summary>Spins once before the next attempt; false once the session is gone.</summary>
        public bool SpinBeforeRetry(bool connected)
        {
            if (!connected)
                return false;

            _spinWait.SpinOnce();
            return true;
        }

        /// <summary>Called after a failed attempt.</summary>
        public readonly void ThrowIfTimedOut()
        {
            //If sendTimeOut = 0, don't have timeout check
            if (_sendTimeOut > 0 && Environment.TickCount64 >= _deadline)
            {
                throw new TimeoutException(TimedOutMessage);
            }
        }
    }

    private void InternalSend(ArraySegment<byte> segment)
    {
        if (!_connected)
            return;

        if (InternalTrySend(segment))
            return;

        var retry = new SendRetryPolicy(Config.SendTimeOut);

        while (retry.SpinBeforeRetry(_connected))
        {
            if (InternalTrySend(segment))
                return;

            retry.ThrowIfTimedOut();
        }
    }

    /// <summary>Sends the data segment to client.</summary>
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

    /// <summary>Try to send the data segments to client.</summary>
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

        var retry = new SendRetryPolicy(Config.SendTimeOut);

        while (retry.SpinBeforeRetry(_connected))
        {
            if (InternalTrySend(segments))
                return;

            retry.ThrowIfTimedOut();
        }
    }

    /// <summary>Sends the data segments to client.</summary>
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

    /// <summary>Try to send a copy of the data to the client, so the caller's buffer can be reused immediately.</summary>
    /// <param name="data">The data which will be sent.</param>
    /// <returns>Indicate whether the message was pushed into the sending queue</returns>
    public virtual bool TrySendCopied(ReadOnlySpan<byte> data)
    {
        if (!_connected)
            return false;

        return InternalTrySendCopied(data);
    }

    /// <summary>Sends a copy of the data to the client, so the caller's buffer can be reused immediately.</summary>
    /// <param name="data">The data which will be sent.</param>
    /// <exception cref="TimeoutException">The sending queue stayed full for longer than SendTimeOut.</exception>
    public virtual void SendCopied(ReadOnlySpan<byte> data)
    {
        if (!_connected)
            return;

        if (InternalTrySendCopied(data))
            return;

        var retry = new SendRetryPolicy(Config.SendTimeOut);

        while (retry.SpinBeforeRetry(_connected))
        {
            if (InternalTrySendCopied(data))
                return;

            retry.ThrowIfTimedOut();
        }
    }

    /// <summary>Sends the data to the client, waiting asynchronously while the sending queue is full.</summary>
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

    /// <summary>Sends the response.</summary>
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


    /// <summary>Sets the next Receive filter which will be used when next data block received</summary>
    protected void SetNextReceiveFilter(IReceiveFilter<TRequestInfo> nextReceiveFilter)
    {
        _receiveFilter = nextReceiveFilter;
    }

    /// <summary>Gets the maximum allowed length of the request.</summary>
    protected virtual int GetMaxRequestLength()
    {
        return AppServer.Config.MaxRequestLength;
    }

    /// <summary>Processes the request data from the receive pipe.</summary>
    /// <remarks>
    /// The filter parses straight out of the pipe: whatever it cannot turn into a request yet is
    /// reported as unconsumed and stays there, so there is no per-session carry buffer.
    /// </remarks>
    ProcessReceiveResult IAppSession.ProcessRequest(ReadOnlySequence<byte> sequence)
    {
        var maxRequestLength = GetMaxRequestLength();

        var current = sequence;
        var consumedPosition = sequence.Start;

        while (current.Length > 0)
        {
            var requestInfo = _receiveFilter.Filter(current, out var consumed, out var examined);

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

                //If next Receive filter wasn't set, keep using the current one
                if (_receiveFilter.NextReceiveFilter != null)
                    _receiveFilter = _receiveFilter.NextReceiveFilter;

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

