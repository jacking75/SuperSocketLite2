using System.Buffers;
using System.Net;
using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

class UdpSocketListener : SocketListenerBase
{
    /// <summary>
    /// Upper bound on the number of concurrent ReceiveFrom operations. A single outstanding receive
    /// serialises the whole UDP server, but there is no benefit in going much wider than the CPU.
    /// </summary>
    private const int MaxConcurrentReceives = 8;

    private Socket? _listenSocket;

    private SocketAsyncEventArgs[]? _receiveSAEs;

    public UdpSocketListener(ListenerInfo info)
        : base(info)
    {

    }

    /// <summary>Starts to listen</summary>
    /// <param name="config">The server config.</param>
    public override bool Start(IServerConfig config)
    {
        try
        {
            _listenSocket = new Socket(this.EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenSocket.Bind(this.EndPoint);

            //SIO_UDP_CONNRESET is a Windows only ioctl that stops an ICMP port-unreachable from
            //failing the next receive. It is an improvement, not a requirement, so a failure here
            //must not abort the listener startup (on Linux the raw ioctl throws).
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    uint IOC_IN = 0x80000000;
                    uint IOC_VENDOR = 0x18000000;
                    uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;

                    byte[] optionInValue = [Convert.ToByte(false)];
                    byte[] optionOutValue = new byte[4];
                    _listenSocket.IOControl((int)SIO_UDP_CONNRESET, optionInValue, optionOutValue);
                }
                catch (Exception ioControlException)
                {
                    OnError(ioControlException);
                }
            }

            int receiveBufferSize = config.ReceiveBufferSize <= 0 ? 2048 : config.ReceiveBufferSize;
            var receiveCount = Math.Max(1, Math.Min(Environment.ProcessorCount, MaxConcurrentReceives));
            var receiveSAEs = new SocketAsyncEventArgs[receiveCount];
            _receiveSAEs = receiveSAEs;

            for (var i = 0; i < receiveCount; i++)
            {
                var eventArgs = new SocketAsyncEventArgs();
                receiveSAEs[i] = eventArgs;

                eventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(eventArgs_Completed);
                eventArgs.RemoteEndPoint = CreateAnyEndPoint();

                var buffer = ArrayPool<byte>.Shared.Rent(receiveBufferSize);
                eventArgs.SetBuffer(buffer, 0, buffer.Length);
                eventArgs.UserToken = receiveBufferSize;
            }

            //Post every receive only after all of them exist, so a synchronous completion cannot
            //run against a half-built array.
            for (var i = 0; i < receiveCount; i++)
            {
                var eventArgs = receiveSAEs[i];

                if (!_listenSocket.ReceiveFromAsync(eventArgs))
                {
                    eventArgs_Completed(_listenSocket, eventArgs);
                }
            }

            return true;
        }
        catch (Exception e)
        {
            OnError(e);
            return false;
        }
    }

    void eventArgs_Completed(object? sender, SocketAsyncEventArgs e)
    {
        //A synchronously completing ReceiveFromAsync used to re-enter this handler recursively.
        //Under a packet flood that grows the stack without bound, so the synchronous completions
        //are drained in a loop instead.
        while (true)
        {
            if (!ProcessReceivedFrom(e))
                return;

            var listenSocket = _listenSocket;

            if (listenSocket == null)
                return;

            try
            {
                if (listenSocket.ReceiveFromAsync(e))
                    return;
            }
            catch (Exception exc)
            {
                OnError(exc);
                return;
            }
        }
    }

    /// <summary>Handles a single completed ReceiveFrom operation.</summary>
    /// <returns><c>true</c> when the next receive should be posted; otherwise <c>false</c>.</returns>
    private bool ProcessReceivedFrom(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            var errorCode = (int)e.SocketError;

            //The listen socket was closed
            if (errorCode == 995 || errorCode == 10004 || errorCode == 10038)
                return false;

            OnError(new SocketException(errorCode));
            return false;
        }

        if (e.LastOperation != SocketAsyncOperation.ReceiveFrom)
            return false;

        try
        {
            var receiveBufferSize = (int)e.UserToken!;
            var packet = new UdpReceivePacket();
            packet.Initialize(e.Buffer!, e.Offset, e.BytesTransferred, (IPEndPoint)e.RemoteEndPoint!);

            var nextBuffer = ArrayPool<byte>.Shared.Rent(receiveBufferSize);
            e.SetBuffer(nextBuffer, 0, nextBuffer.Length);
            e.RemoteEndPoint = CreateAnyEndPoint();

            //Handled inline: with several outstanding receives the parallelism comes from the
            //receive loops themselves, so there is no need to pay a Task plus closure per datagram.
            OnNewClientAccepted(_listenSocket!, packet);
        }
        catch (Exception exc)
        {
            OnError(exc);
        }

        return true;
    }

    public override void Stop()
    {
        if (_listenSocket == null)
            return;

        lock(this)
        {
            if (_listenSocket == null)
                return;

            var listenSocket = _listenSocket;
            var receiveSAEs = _receiveSAEs;

            try
            {
                listenSocket.Shutdown(SocketShutdown.Both);
            }
            catch { }

            try
            {
                listenSocket.Close();
            }
            catch { }
            finally
            {
                _listenSocket = null;
            }

            if (receiveSAEs != null)
            {
                for (var i = 0; i < receiveSAEs.Length; i++)
                    CleanupReceiveSAE(receiveSAEs[i]);

                _receiveSAEs = null;
            }
        }

        OnStopped();
    }

    private void CleanupReceiveSAE(SocketAsyncEventArgs receiveSAE)
    {
        receiveSAE.Completed -= new EventHandler<SocketAsyncEventArgs>(eventArgs_Completed);

        //Closing the socket aborts the outstanding ReceiveFrom, but that completion is
        //delivered asynchronously. Until it arrives the SocketAsyncEventArgs is still busy,
        //so SetBuffer throws - and the buffer must NOT be handed back to the pool while the
        //kernel may still write into it. Detach first, return only on success.
        var buffer = receiveSAE.Buffer;

        if (buffer != null)
        {
            try
            {
                receiveSAE.SetBuffer(null, 0, 0);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            catch (InvalidOperationException)
            {
                //still in flight: leave the buffer to the GC instead of failing Stop()
            }
        }

        try
        {
            receiveSAE.Dispose();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private EndPoint CreateAnyEndPoint()
    {
        return this.EndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);
    }
}
