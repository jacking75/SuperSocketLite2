using System;
using System.Net.Sockets;
using SuperSocketLite.SocketBase;


namespace SuperSocketLite.SocketEngine;

class SocketAsyncEventArgsProxy
{
    public SocketAsyncEventArgs SocketEventArgs { get; }

    public SocketAsyncEventArgsProxy(SocketAsyncEventArgs socketEventArgs)
    {
        SocketEventArgs = socketEventArgs;
        SocketEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(SocketEventArgs_Completed);
    }

    static void SocketEventArgs_Completed(object? sender, SocketAsyncEventArgs e)
    {
        var socketSession = e.UserToken as IAsyncSocketSession;

        if (socketSession == null)
            return;

        if (e.LastOperation != SocketAsyncOperation.Receive)
        {
            //Never throw from an IOCP completion callback - an unhandled exception here takes the
            //whole process down.
            LogError(socketSession, $"The last operation completed on the socket was not a receive but {e.LastOperation}", null);
            return;
        }

        if (!socketSession.ReceiveInlineOnIocpThread)
        {
            socketSession.AsyncRun(() => socketSession.ProcessReceive(e));
            return;
        }

        //ProcessReceive only advances the receive pipe and posts the next receive; the application
        //handlers run on the pipe-reader task. Running it inline saves a thread hop plus one
        //closure and two Task allocations on every received packet.
        try
        {
            socketSession.ProcessReceive(e);
        }
        catch (Exception exc)
        {
            LogError(socketSession, "Failed to process the completed receive", exc);
        }
    }

    static void LogError(IAsyncSocketSession socketSession, string message, Exception? exception)
    {
        try
        {
            var logger = socketSession.Logger;

            if (logger == null || !logger.IsErrorEnabled)
                return;

            if (exception == null)
                logger.Error(message);
            else
                logger.Error(message, exception);
        }
        catch
        {
            //The session may already be torn down (no AppSession yet / logger disposed). A logging
            //failure must never escape the IOCP completion thread.
        }
    }

    public void Initialize(IAsyncSocketSession socketSession)
    {
        SocketEventArgs.UserToken = socketSession;
    }

    public void Reset()
    {
        SocketEventArgs.UserToken = null;
    }
}
