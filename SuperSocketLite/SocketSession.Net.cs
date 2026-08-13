using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using SuperSocketLite.SocketBase.Logging;


namespace SuperSocketLite.SocketEngine;

abstract partial class SocketSession
{
    private const string m_GeneralErrorMessage = "Unexpected error";
    private const string m_GeneralSocketErrorMessage = "Unexpected socket error: {0}";
    private const string m_CallerInformation = "caller: {0}, file path: {1}, line number: {2}";

    /// <summary>
    /// Gets this session's identity for structured logging.
    /// </summary>
    private LogSessionContext SessionLogContext => new LogSessionContext(SessionID, RemoteEndPoint);

    /// <summary>
    /// Logs the error, skip the ignored exception
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="caller">The caller.</param>
    /// <param name="callerFilePath">The caller file path.</param>
    /// <param name="callerLineNumber">The caller line number.</param>
    protected void LogError(Exception exception, [CallerMemberName] string caller = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
    {
        int socketErrorCode;

        //This exception is ignored, needn't log it
        if (IsIgnorableException(exception, out socketErrorCode))
            return;

        var message = socketErrorCode > 0 ? string.Format(m_GeneralSocketErrorMessage, socketErrorCode) : m_GeneralErrorMessage;

        Write(message, exception, caller, callerFilePath, callerLineNumber);
    }

    /// <summary>
    /// Logs the error, skip the ignored exception
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="caller">The caller.</param>
    /// <param name="callerFilePath">The caller file path.</param>
    /// <param name="callerLineNumber">The caller line number.</param>
    protected void LogError(string message, Exception exception, [CallerMemberName] string caller = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
    {
        //This exception is ignored, needn't log it
        if (IsIgnorableException(exception, out _))
            return;

        Write(message, exception, caller, callerFilePath, callerLineNumber);
    }

    /// <summary>
    /// Logs the socket error, skip the ignored error
    /// </summary>
    /// <param name="socketErrorCode">The socket error code.</param>
    /// <param name="caller">The caller.</param>
    /// <param name="callerFilePath">The caller file path.</param>
    /// <param name="callerLineNumber">The caller line number.</param>
    protected void LogError(int socketErrorCode, [CallerMemberName] string caller = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
    {
        if (!Config.LogAllSocketException)
        {
            //This error is ignored, needn't log it
            if (IsIgnorableSocketError(socketErrorCode))
                return;
        }

        Write(string.Format(m_GeneralSocketErrorMessage, socketErrorCode), new SocketException(socketErrorCode), caller, callerFilePath, callerLineNumber);
    }

    /// <summary>
    /// Emits the entry with the session identity kept as structured properties and the exception
    /// passed through as an exception rather than flattened into the text.
    /// </summary>
    private void Write(string message, Exception exception, string caller, string callerFilePath, int callerLineNumber)
    {
        var logger = AppSession?.Logger;

        if (logger == null || !logger.IsErrorEnabled)
            return;

        logger.Log(LogEventLevel.Error, SessionLogContext,
            string.Concat(message, " (", string.Format(m_CallerInformation, caller, callerFilePath, callerLineNumber), ")"),
            exception);
    }
}
