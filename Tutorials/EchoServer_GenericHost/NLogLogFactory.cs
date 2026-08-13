using NLog;
using SuperSocketLite.SocketBase.Logging;

namespace EchoServer_GenericHost;

#if (__NOT_USE_NLOG__ != true)  //NLog를 사용하지 않는다면 __NOT_USE_NLOG__ 선언한다
/// <summary>
/// Loads an NLog config file and creates NLog-backed ILog instances.
/// </summary>
/// <remarks>
/// LogFactoryBase is used only to resolve the config file path; it is optional. A logging library
/// configured in code can implement ILogFactory directly, or reuse MicrosoftLoggingLogFactory.
/// </remarks>
public class NLogLogFactory : LogFactoryBase
{
    public NLogLogFactory()
        : this("NLog.config")
    {
    }

    public NLogLogFactory(string nlogConfig)
        : base(nlogConfig)
    {
        LogManager.Setup().LoadConfigurationFromFile(new[] { ConfigFile });
    }

    public override ILog GetLog(string name)
    {
        return new NLogLog(LogManager.GetLogger(name));
    }
}
#endif
