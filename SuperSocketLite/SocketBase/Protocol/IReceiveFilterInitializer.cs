namespace SuperSocketLite.SocketBase.Protocol;

/// <summary>Provide the initializing interface for ReceiveFilter</summary>
public interface IReceiveFilterInitializer
{
    /// <summary>Initializes the ReceiveFilter with the specified appServer and appSession</summary>
    void Initialize(IAppServer appServer, IAppSession session);
}
