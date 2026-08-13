using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

/// <summary>
/// Tcp socket listener in async mode
/// </summary>
class TcpAsyncSocketListener : SocketListenerBase
{
    private int _listenBackLog;

    private Socket? _listenSocket;

    // CTS that drives the accept loop; cancelled by Stop() to unblock AcceptAsync.
    private CancellationTokenSource? _stopCts;

    public TcpAsyncSocketListener(ListenerInfo info)
        : base(info)
    {
        _listenBackLog = info.BackLog;
    }

    /// <summary>
    /// Starts to listen
    /// </summary>
    /// <param name="config">The server config.</param>
    /// <returns></returns>
    public override bool Start(IServerConfig config)
    {
        var listenSocket = new Socket(this.Info.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket = listenSocket;

        try
        {
            listenSocket.Bind(this.Info.EndPoint);
            listenSocket.Listen(_listenBackLog);

            _stopCts = new CancellationTokenSource();
            _ = AcceptLoopAsync(listenSocket, _stopCts.Token);

            return true;
        }
        catch (Exception e)
        {
            listenSocket.Dispose();
            _listenSocket = null;
            OnError(e);
            return false;
        }
    }

    /// <summary>
    /// Continuously accepts new connections until the cancellation token is triggered
    /// or the listen socket is closed.  Runs as a fire-and-forget background Task so
    /// that Start() returns immediately, matching the original SAEA-based behaviour.
    /// </summary>
    private async Task AcceptLoopAsync(Socket listenSocket, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                Socket client;

                try
                {
                    // .NET 5+ ValueTask<Socket> overload — cancellable natively.
                    client = await listenSocket.AcceptAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // Stop() was called (token cancelled).
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // Listen socket was closed externally.
                    return;
                }
                catch (SocketException se) when (IsStopError(se.ErrorCode))
                {
                    // Socket-level codes that mean the listener has been shut down:
                    //   995  = OperationAborted
                    //   10004 = Interrupted
                    //   10038 = WSAENOTSOCK (socket already closed)
                    return;
                }
                catch (Exception e)
                {
                    OnError(e);

                    // Non-fatal error: keep accepting unless we've been asked to stop.
                    if (ct.IsCancellationRequested)
                        return;

                    continue;
                }

                OnNewClientAccepted(client, null);
            }
        }
        finally
        {
            // OnStopped is always raised exactly once, after the loop exits for any reason.
            OnStopped();
        }
    }

    /// <summary>
    /// Returns true for socket error codes that indicate the listener was intentionally stopped.
    /// </summary>
    private static bool IsStopError(int errorCode) =>
        errorCode == 995      // OperationAborted
        || errorCode == 10004 // Interrupted
        || errorCode == 10038;// WSAENOTSOCK

    public override void Stop()
    {
        if (_listenSocket == null)
            return;

        lock (this)
        {
            if (_listenSocket == null)
                return;

            // Cancel the accept loop first so AcceptAsync unblocks immediately.
            _stopCts?.Cancel();
            _stopCts?.Dispose();
            _stopCts = null;

            try
            {
                _listenSocket.Close();
            }
            finally
            {
                _listenSocket = null;
            }
        }

        // OnStopped() is invoked by AcceptLoopAsync's finally block once the loop exits,
        // so we do NOT call it here to avoid a double-fire.
    }
}
