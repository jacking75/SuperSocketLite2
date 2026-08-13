using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

abstract class TcpSocketServerBase : SocketServerBase
{
    private readonly int m_SendTimeOut;
    private readonly int m_ReceiveBufferSize;
    private readonly int m_SendBufferSize;
    private readonly bool m_NoDelay;
    private readonly int m_KeepAliveTime;
    private readonly int m_KeepAliveInterval;
    private readonly int m_KeepAliveRetryCount;

    //Bit i is set once the i-th keep-alive option has failed, so an unsupported option is logged
    //once per server instead of once per accepted connection.
    private int m_LoggedKeepAliveFailures;

    public TcpSocketServerBase(IAppServer appServer, ListenerInfo[] listeners)
        : base(appServer, listeners)
    {
        var config = appServer.Config;

        m_KeepAliveTime = config.KeepAliveTime;
        m_KeepAliveInterval = config.KeepAliveInterval;

        //KeepAliveRetryCount only exists on ServerConfig (IServerConfig is kept unchanged for
        //backward compatibility), so custom config implementations get the default.
        m_KeepAliveRetryCount = (config as ServerConfig)?.KeepAliveRetryCount ?? ServerConfig.DefaultKeepAliveRetryCount;

        m_SendTimeOut = config.SendTimeOut;
        m_ReceiveBufferSize = config.ReceiveBufferSize;
        m_SendBufferSize = config.SendBufferSize;

        m_NoDelay = config.NoDelay;
    }

    protected IAppSession CreateSession(Socket client, ISocketSession session)
    {
        if (m_SendTimeOut > 0)
            client.SendTimeout = m_SendTimeOut;

        if (m_ReceiveBufferSize > 0)
            client.ReceiveBufferSize = m_ReceiveBufferSize;

        if (m_SendBufferSize > 0)
            client.SendBufferSize = m_SendBufferSize;

        ApplyKeepAlive(client);

        client.NoDelay = m_NoDelay;
        // enable:false = SO_LINGER off, i.e. the default graceful close: Close() returns immediately
        // and the OS keeps draining the send buffer in the background. (An abortive RST close would
        // be LingerOption(enable:true, seconds:0) - deliberately NOT used, it would discard data
        // still queued for the client.)
        client.LingerState = new LingerOption(enable: false, seconds: 0);

        return this.AppServer.CreateAppSession(session);
    }

    /// <summary>
    /// Enables TCP keep-alive with the configured timings using the cross platform socket options
    /// available since .NET Core 3.0. Every option is applied independently: a platform that
    /// doesn't support one of them must not prevent the session from being created.
    /// </summary>
    private void ApplyKeepAlive(Socket client)
    {
        TrySetSocketOption(client, SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1, 0);

        //The TCP level options are expressed in seconds, same unit as the config values.
        if (m_KeepAliveTime > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, m_KeepAliveTime, 1);

        if (m_KeepAliveInterval > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, m_KeepAliveInterval, 2);

        if (m_KeepAliveRetryCount > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, m_KeepAliveRetryCount, 3);
    }

    private void TrySetSocketOption(Socket client, SocketOptionLevel level, SocketOptionName name, int value, int failureLogBit)
    {
        try
        {
            client.SetSocketOption(level, name, value);
        }
        catch (Exception e)
        {
            if (!TryMarkKeepAliveFailureLogged(failureLogBit))
                return;

            var logger = AppServer.Logger;

            if (logger != null && logger.IsWarnEnabled)
                logger.Warn($"Failed to apply the socket option {name}, keep-alive detection may not work as configured.", e);
        }
    }

    private bool TryMarkKeepAliveFailureLogged(int failureLogBit)
    {
        var mask = 1 << failureLogBit;

        while (true)
        {
            var oldValue = m_LoggedKeepAliveFailures;

            if ((oldValue & mask) == mask)
                return false;

            if (Interlocked.CompareExchange(ref m_LoggedKeepAliveFailures, oldValue | mask, oldValue) == oldValue)
                return true;
        }
    }

    protected override ISocketListener CreateListener(ListenerInfo listenerInfo)
    {
        return new TcpAsyncSocketListener(listenerInfo);
    }
}
