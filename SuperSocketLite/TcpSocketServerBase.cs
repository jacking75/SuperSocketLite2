using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Config;


namespace SuperSocketLite.SocketEngine;

abstract class TcpSocketServerBase : SocketServerBase
{
    private readonly int _sendTimeOut;
    private readonly int _receiveBufferSize;
    private readonly int _sendBufferSize;
    private readonly bool _noDelay;
    private readonly int _keepAliveTime;
    private readonly int _keepAliveInterval;
    private readonly int _keepAliveRetryCount;

    //Bit i is set once the i-th keep-alive option has failed, so an unsupported option is logged
    //once per server instead of once per accepted connection.
    private int _loggedKeepAliveFailures;

    public TcpSocketServerBase(IAppServer appServer, ListenerInfo[] listeners)
        : base(appServer, listeners)
    {
        var config = appServer.Config;

        _keepAliveTime = config.KeepAliveTime;
        _keepAliveInterval = config.KeepAliveInterval;

        //KeepAliveRetryCount only exists on ServerConfig (IServerConfig is kept unchanged for
        //backward compatibility), so custom config implementations get the default.
        _keepAliveRetryCount = (config as ServerConfig)?.KeepAliveRetryCount ?? ServerConfig.DefaultKeepAliveRetryCount;

        _sendTimeOut = config.SendTimeOut;
        _receiveBufferSize = config.ReceiveBufferSize;
        _sendBufferSize = config.SendBufferSize;

        _noDelay = config.NoDelay;
    }

    protected IAppSession CreateSession(Socket client, ISocketSession session)
    {
        if (_sendTimeOut > 0)
            client.SendTimeout = _sendTimeOut;

        if (_receiveBufferSize > 0)
            client.ReceiveBufferSize = _receiveBufferSize;

        if (_sendBufferSize > 0)
            client.SendBufferSize = _sendBufferSize;

        ApplyKeepAlive(client);

        client.NoDelay = _noDelay;
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
        if (_keepAliveTime > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _keepAliveTime, 1);

        if (_keepAliveInterval > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _keepAliveInterval, 2);

        if (_keepAliveRetryCount > 0)
            TrySetSocketOption(client, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _keepAliveRetryCount, 3);
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
            var oldValue = _loggedKeepAliveFailures;

            if ((oldValue & mask) == mask)
                return false;

            if (Interlocked.CompareExchange(ref _loggedKeepAliveFailures, oldValue | mask, oldValue) == oldValue)
                return true;
        }
    }

    protected override ISocketListener CreateListener(ListenerInfo listenerInfo)
    {
        return new TcpAsyncSocketListener(listenerInfo);
    }
}
