using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

/// <summary>
/// Tcp socket listener in async mode
/// </summary>
class TcpAsyncSocketListener : SocketListenerBase
{
    private int m_ListenBackLog;

    private Socket? m_ListenSocket;

    // CTS that drives the accept loop; cancelled by Stop() to unblock AcceptAsync.
    private CancellationTokenSource? m_StopCts;

    public TcpAsyncSocketListener(ListenerInfo info)
        : base(info)
    {
        m_ListenBackLog = info.BackLog;
    }

    /// <summary>
    /// Starts to listen
    /// </summary>
    /// <param name="config">The server config.</param>
    /// <returns></returns>
    public override bool Start(IServerConfig config)
    {
        var listenSocket = new Socket(this.Info.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        m_ListenSocket = listenSocket;

        try
        {
            listenSocket.Bind(this.Info.EndPoint);
            listenSocket.Listen(m_ListenBackLog);

            m_StopCts = new CancellationTokenSource();
            _ = AcceptLoopAsync(listenSocket, m_StopCts.Token);

            return true;
        }
        catch (Exception e)
        {
            listenSocket.Dispose();
            m_ListenSocket = null;
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
        if (m_ListenSocket == null)
            return;

        lock (this)
        {
            if (m_ListenSocket == null)
                return;

            // Cancel the accept loop first so AcceptAsync unblocks immediately.
            m_StopCts?.Cancel();
            m_StopCts?.Dispose();
            m_StopCts = null;

            try
            {
                m_ListenSocket.Close();
            }
            finally
            {
                m_ListenSocket = null;
            }
        }

        // OnStopped() is invoked by AcceptLoopAsync's finally block once the loop exits,
        // so we do NOT call it here to avoid a double-fire.
    }
}
