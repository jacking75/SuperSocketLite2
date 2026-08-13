using System;
using Microsoft.Extensions.Logging;
using SuperSocketLite.SocketBase.Logging;
using ZLogger;
using ZLogger.Providers;


namespace GameServer_01;

/// <summary>
/// Configures ZLogger and hands the resulting ILoggerFactory to SuperSocketLite through the
/// built-in <see cref="MicrosoftLoggingLogFactory"/> bridge - no per-library ILog adapter needed.
/// </summary>
public sealed class ZLoggerLogFactory : ILogFactory
{
    private readonly ILogFactory _inner;

    public ZLoggerLogFactory()
    {
        var startTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        //LoggerFactory.Create builds only a logging pipeline; it does not spin up a second host.
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders()
                .SetMinimumLevel(LogLevel.Trace)
                .AddZLoggerRollingFile(options =>
                {
                    options.FilePathSelector = (timestamp, sequenceNumber) => $"logs/{startTime}_{sequenceNumber:000}.log";
                    options.RollingInterval = RollingInterval.Day;
                    options.RollingSizeKB = 1024;
                    options.UseJsonFormatter();
                })
                .AddZLoggerConsole(options =>
                {
                    options.UseJsonFormatter();
                });
        });

        _inner = new MicrosoftLoggingLogFactory(loggerFactory);
    }

    public ILog GetLog(string name) => _inner.GetLog(name);
}
