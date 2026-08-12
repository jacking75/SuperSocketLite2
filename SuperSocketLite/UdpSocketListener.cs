using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using SuperSocketLite.Common;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

class UdpSocketListener : SocketListenerBase
{
    private Socket? m_ListenSocket;

    private SocketAsyncEventArgs? m_ReceiveSAE;

    public UdpSocketListener(ListenerInfo info)
        : base(info)
    {

    }

    /// <summary>
    /// Starts to listen
    /// </summary>
    /// <param name="config">The server config.</param>
    /// <returns></returns>
    public override bool Start(IServerConfig config)
    {
        try
        {
            m_ListenSocket = new Socket(this.EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            m_ListenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            m_ListenSocket.Bind(this.EndPoint);

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

                    byte[] optionInValue = { Convert.ToByte(false) };
                    byte[] optionOutValue = new byte[4];
                    m_ListenSocket.IOControl((int)SIO_UDP_CONNRESET, optionInValue, optionOutValue);
                }
                catch (Exception ioControlException)
                {
                    OnError(ioControlException);
                }
            }

            var eventArgs = new SocketAsyncEventArgs();
            m_ReceiveSAE = eventArgs;

            eventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(eventArgs_Completed);
            eventArgs.RemoteEndPoint = CreateAnyEndPoint();

            int receiveBufferSize = config.ReceiveBufferSize <= 0 ? 2048 : config.ReceiveBufferSize;
            var buffer = ArrayPool<byte>.Shared.Rent(receiveBufferSize);
            eventArgs.SetBuffer(buffer, 0, buffer.Length);
            eventArgs.UserToken = receiveBufferSize;

            if (!m_ListenSocket.ReceiveFromAsync(eventArgs))
            {
                eventArgs_Completed(m_ListenSocket, eventArgs);
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

            var listenSocket = m_ListenSocket;

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

    /// <summary>
    /// Handles a single completed ReceiveFrom operation.
    /// </summary>
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

            OnNewClientAcceptedAsync(m_ListenSocket!, packet);
        }
        catch (Exception exc)
        {
            OnError(exc);
        }

        return true;
    }

    public override void Stop()
    {
        if (m_ListenSocket == null)
            return;

        lock(this)
        {
            if (m_ListenSocket == null)
                return;

            var listenSocket = m_ListenSocket;
            var receiveSAE = m_ReceiveSAE;

            if(!Platform.IsMono)
            {
                try
                {
                    listenSocket.Shutdown(SocketShutdown.Both);
                }
                catch { }
            }

            try
            {
                listenSocket.Close();
            }
            catch { }
            finally
            {
                m_ListenSocket = null;
            }

            if (receiveSAE != null)
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

                m_ReceiveSAE = null;
            }
        }

        OnStopped();
    }

    private EndPoint CreateAnyEndPoint()
    {
        return this.EndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new IPEndPoint(IPAddress.IPv6Any, 0)
            : new IPEndPoint(IPAddress.Any, 0);
    }
}
