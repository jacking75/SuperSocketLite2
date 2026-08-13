using SuperSocketLite.SocketBase.Protocol;


namespace SuperSocketLite.SocketBase;

/// <summary>Request handler</summary>
/// <typeparam name="TAppSession">The type of the app session.</typeparam>
/// <typeparam name="TRequestInfo">The type of the request info.</typeparam>
public delegate void RequestHandler<TAppSession, TRequestInfo>(TAppSession session, TRequestInfo requestInfo)
    where TAppSession : IAppSession, IAppSession<TAppSession, TRequestInfo>, new()
    where TRequestInfo : IRequestInfo;
