namespace SuperSocketLite.SocketEngine;

/// <summary>
/// A reading of the two SAEA pools a TCP server keeps.
/// "Available" is what is still in the pool; "total" is how many the pool has created so far,
/// which grows on demand up to <c>MaxConnectionNumber</c>.
/// </summary>
internal readonly record struct SocketAsyncEventArgsPoolUsage(
    int ReceiveAvailable,
    int ReceiveTotal,
    int SendAvailable,
    int SendTotal);
