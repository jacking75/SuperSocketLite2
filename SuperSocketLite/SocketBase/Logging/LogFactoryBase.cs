using System;
using System.IO;


namespace SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Optional base class for an <see cref="ILogFactory"/> that is configured from a config file
/// (NLog.config, log4net.config, ...). It only resolves the config file path for you.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheriting from this is not required.</b> A logging library configured in code or through DI
/// - Serilog, ZLogger, anything behind <c>Microsoft.Extensions.Logging</c> - has no config file to
/// resolve, so implement <see cref="ILogFactory"/> directly, or just use
/// <see cref="MicrosoftLoggingLogFactory"/>.
/// </para>
/// </remarks>
public abstract class LogFactoryBase : ILogFactory
{
    /// <summary>
    /// Gets the resolved config file path.
    /// </summary>
    /// <remarks>
    /// A relative path is searched for in the application base directory and then in its
    /// <c>Config</c> subdirectory; if it is found in neither, the value is returned unchanged so the
    /// logging library can apply its own resolution.
    /// </remarks>
    protected string ConfigFile { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LogFactoryBase"/> class.
    /// </summary>
    /// <param name="configFile">The config file.</param>
    protected LogFactoryBase(string configFile)
    {
        if (Path.IsPathRooted(configFile))
        {
            ConfigFile = configFile;
            return;
        }

        var filePath = Path.Combine(AppContext.BaseDirectory, configFile);

        if (File.Exists(filePath))
        {
            ConfigFile = filePath;
            return;
        }

        filePath = Path.Combine(AppContext.BaseDirectory, "Config", configFile);

        if (File.Exists(filePath))
        {
            ConfigFile = filePath;
            return;
        }

        ConfigFile = configFile;
    }

    /// <summary>
    /// Gets the log by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public abstract ILog GetLog(string name);
}
