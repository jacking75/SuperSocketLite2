using System.Net.Sockets;
using SuperSocketLite.SocketBase;
using SuperSocketLite.SocketBase.Logging;


namespace SuperSocketLite.SocketEngine;

abstract class SocketServerBase : ISocketServer, IDisposable
{
    protected object SyncRoot = new object();

    public IAppServer AppServer { get; private set; }

    public bool IsRunning { get; protected set; }

    protected ListenerInfo[] ListenerInfos { get; private set; }

    protected List<ISocketListener> Listeners { get; private set; }

    protected bool IsStopped { get; set; }

    public SocketServerBase(IAppServer appServer, ListenerInfo[] listeners)
    {
        AppServer = appServer;
        IsRunning = false;
        ListenerInfos = listeners;
        Listeners = new List<ISocketListener>(listeners.Length);
    }

    public virtual bool Start()
    {
        IsStopped = false;

        ILog log = AppServer.Logger;

        var config = AppServer.Config;

        for (var i = 0; i < ListenerInfos.Length; i++)
        {
            var listener = CreateListener(ListenerInfos[i]);
            listener.Error += new ErrorHandler(OnListenerError);
            listener.Stopped += new EventHandler(OnListenerStopped);
            listener.NewClientAccepted += new NewClientAcceptHandler(OnNewClientAccepted);

            if (listener.Start(AppServer.Config))
            {
                Listeners.Add(listener);

                if (log.IsDebugEnabled)
                {
                    log.Debug($"Listener ({listener.EndPoint}) was started");
                }
            }
            else //If one listener failed to start, stop started listeners
            {
                if (log.IsDebugEnabled)
                {
                    log.Debug($"Listener ({listener.EndPoint}) failed to start");
                }

                for (var j = 0; j < Listeners.Count; j++)
                {
                    Listeners[j].Stop();
                }

                Listeners.Clear();
                return false;
            }
        }

        IsRunning = true;
        return true;
    }

    protected abstract void OnNewClientAccepted(ISocketListener listener, Socket client, object? state);

    void OnListenerError(ISocketListener listener, Exception e)
    {
        var logger = this.AppServer.Logger;

        if(!logger.IsErrorEnabled)
            return;

        logger.Error(string.Format("Listener ({0}) error: {1}", listener.EndPoint, e.Message), e);
    }

    void OnListenerStopped(object? sender, EventArgs e)
    {
        var listener = sender as ISocketListener;

        ILog log = AppServer.Logger;

        if (log.IsDebugEnabled)
            log.Debug($"Listener ({listener?.EndPoint}) was stoppped");
    }

    protected abstract ISocketListener CreateListener(ListenerInfo listenerInfo);

    /// <summary>
    /// Stops accepting new connections while leaving the existing sessions running.
    /// </summary>
    internal void StopListeners()
    {
        IsStopped = true;

        for (var i = 0; i < Listeners.Count; i++)
        {
            var listener = Listeners[i];

            listener.Stop();
        }

        Listeners.Clear();
    }

    public virtual void Stop()
    {
        StopListeners();

        IsRunning = false;
    }

    #region IDisposable Members

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (IsRunning)
                Stop();
        }
    }

    #endregion
}
