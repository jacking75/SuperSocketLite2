using System.Net.Sockets;

namespace SuperSocketLite.Common;

/// <summary>Socket extension class</summary>
public static class SocketEx
{
    /// <summary>Close the socket safely.</summary>
    public static void SafeClose(this Socket socket)
    {
        if (socket == null)
            return;

        try
        {
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            socket.Close();
        }
        catch
        {
        }
    }
}
