using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SuperSocketLite.SocketBase.Logging;

/// <summary>
/// Covers the ILog contract that third-party logging libraries are adapted to.
/// </summary>
static class LoggingTests
{
    private static readonly IPEndPoint s_EndPoint = new IPEndPoint(IPAddress.Loopback, 5000);

    /// <summary>
    /// An adapter that implements only the required members must keep compiling and must still
    /// receive session-scoped entries, flattened into the message by the default implementation.
    /// </summary>
    public static void MinimalAdapterStillReceivesSessionScopedEntries()
    {
        var sink = new MinimalLog();
        //Default interface members are reachable only through the interface, which is how the
        //library always holds a log.
        ILog log = sink;

        log.Log(LogEventLevel.Error, new LogSessionContext("abc", s_EndPoint), "boom");

        Assert.Equal(1, sink.Entries.Count, "the entry should reach the plain Error overload");

        var entry = sink.Entries[0];
        Assert.Equal("Error", entry.Level, "Error level should map to the Error overload");
        Assert.True(entry.Message.Contains("abc"), "the flattened message should carry the session ID");
        Assert.True(entry.Message.Contains("127.0.0.1:5000"), "the flattened message should carry the remote end point");
        Assert.True(entry.Message.Contains("boom"), "the flattened message should carry the message");
    }

    /// <summary>
    /// No log entry produced by the library may span more than one line: line-oriented collectors
    /// would otherwise split a single event into two records.
    /// </summary>
    public static void FlattenedEntriesAreSingleLine()
    {
        var sink = new MinimalLog();
        ILog log = sink;
        var context = new LogSessionContext("abc", s_EndPoint);

        log.Log(LogEventLevel.Info, context, "first");
        log.Log(LogEventLevel.Error, context, "second", new InvalidOperationException("bad"));
        log.Warn("third", new InvalidOperationException("worse"));

        Assert.Equal(3, sink.Entries.Count, "every call should produce one entry");

        foreach (var entry in sink.Entries)
        {
            Assert.True(!entry.Message.Contains('\n') && !entry.Message.Contains('\r'),
                $"the entry must stay on one line but was: {entry.Message}");
        }
    }

    /// <summary>
    /// Every level accepts an exception, not just Error and Fatal.
    /// </summary>
    public static void EveryLevelAcceptsAnException()
    {
        var sink = new MinimalLog();
        ILog log = sink;
        var exception = new InvalidOperationException("bad");

        log.Trace("t", exception);
        log.Debug("d", exception);
        log.Info("i", exception);
        log.Warn("w", exception);
        log.Error("e", exception);
        log.Fatal("f", exception);

        Assert.Equal(6, sink.Entries.Count, "each level should emit one entry");

        foreach (var entry in sink.Entries)
        {
            Assert.True(entry.Message.Contains("bad"),
                $"the exception should survive into the entry but got: {entry.Message}");
        }

        //Trace has no dedicated overload on a minimal adapter, so it folds into Debug.
        Assert.Equal("Debug", sink.Entries[0].Level, "Trace should fold into Debug by default");
    }

    /// <summary>
    /// An adapter that overrides Log gets the session identity as separate values rather than as
    /// pre-formatted text - that is what lets a structured sink emit real fields.
    /// </summary>
    public static void StructuredAdapterReceivesSessionIdentitySeparately()
    {
        var sink = new StructuredLog();
        ILog log = sink;

        log.Log(LogEventLevel.Warn, new LogSessionContext("session-7", s_EndPoint), "queue full");

        Assert.Equal(LogEventLevel.Warn, sink.Level, "the level should be passed through");
        Assert.Equal("session-7", sink.SessionId, "the session ID should arrive as its own value");
        Assert.Equal(s_EndPoint, sink.RemoteEndPoint, "the remote end point should arrive as its own value");
        Assert.Equal("queue full", sink.Message, "the message should not be polluted with session text");
    }

    /// <summary>
    /// The built-in Microsoft.Extensions.Logging bridge must hand the exception to MEL as an
    /// exception, and the session identity as named properties.
    /// </summary>
    public static void MicrosoftLoggingBridgePassesExceptionAndProperties()
    {
        var recorder = new RecordingLogger();
        ILog log = new MicrosoftLoggingLog(recorder);
        var exception = new InvalidOperationException("bad");

        log.Log(LogEventLevel.Error, new LogSessionContext("session-9", s_EndPoint), "send failed", exception);

        Assert.Equal(1, recorder.Entries.Count, "the bridge should write exactly one entry");

        var entry = recorder.Entries[0];
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level, "Error should map to MEL Error");
        Assert.True(ReferenceEquals(exception, entry.Exception), "the exception must be passed as an exception, not flattened into text");
        Assert.Equal("session-9", entry.Property("SessionId"), "SessionId should be a structured property");
        Assert.Equal(s_EndPoint, entry.Property("RemoteEndPoint"), "RemoteEndPoint should be a structured property");
        Assert.Equal("send failed", entry.Property("Message"), "Message should be a structured property");
    }

    /// <summary>
    /// The bridge must not build the entry at all when the underlying logger filters the level out.
    /// </summary>
    public static void MicrosoftLoggingBridgeHonoursLevelFiltering()
    {
        var recorder = new RecordingLogger { MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Warning };
        ILog log = new MicrosoftLoggingLog(recorder);

        Assert.True(!log.IsDebugEnabled, "Debug should report disabled when MEL filters it out");
        Assert.True(log.IsErrorEnabled, "Error should report enabled");

        log.Debug("ignored");
        log.Log(LogEventLevel.Info, new LogSessionContext("s", s_EndPoint), "ignored too");

        Assert.Equal(0, recorder.Entries.Count, "filtered levels must not reach the logger");
    }

    private sealed record Entry(string Level, string Message);

    /// <summary>
    /// Implements only the members ILog requires, the way a pre-existing adapter would.
    /// </summary>
    private sealed class MinimalLog : ILog
    {
        public List<Entry> Entries { get; } = new List<Entry>();

        public bool IsDebugEnabled => true;
        public bool IsInfoEnabled => true;
        public bool IsWarnEnabled => true;
        public bool IsErrorEnabled => true;
        public bool IsFatalEnabled => true;

        public void Debug(string message) => Entries.Add(new Entry("Debug", message));
        public void Info(string message) => Entries.Add(new Entry("Info", message));
        public void Warn(string message) => Entries.Add(new Entry("Warn", message));
        public void Error(string message) => Entries.Add(new Entry("Error", message));
        public void Fatal(string message) => Entries.Add(new Entry("Fatal", message));

        public void Error(string message, Exception exception) => Error(Flatten(message, exception));
        public void Fatal(string message, Exception exception) => Fatal(Flatten(message, exception));

        private static string Flatten(string message, Exception exception) => $"{message} | {exception.Message}";
    }

    /// <summary>
    /// Overrides Log, the way an adapter over a structured logging library would.
    /// </summary>
    private sealed class StructuredLog : ILog
    {
        public LogEventLevel Level { get; private set; }
        public string? SessionId { get; private set; }
        public IPEndPoint? RemoteEndPoint { get; private set; }
        public string? Message { get; private set; }

        public bool IsDebugEnabled => true;
        public bool IsInfoEnabled => true;
        public bool IsWarnEnabled => true;
        public bool IsErrorEnabled => true;
        public bool IsFatalEnabled => true;

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Fatal(string message) { }
        public void Error(string message, Exception exception) { }
        public void Fatal(string message, Exception exception) { }

        public void Log(LogEventLevel level, in LogSessionContext session, string message, Exception? exception = null)
        {
            Level = level;
            SessionId = session.SessionId;
            RemoteEndPoint = session.RemoteEndPoint;
            Message = message;
        }
    }

    private sealed class RecordedEntry
    {
        public Microsoft.Extensions.Logging.LogLevel Level { get; init; }

        public Exception? Exception { get; init; }

        public IReadOnlyList<KeyValuePair<string, object?>> Values { get; init; } = Array.Empty<KeyValuePair<string, object?>>();

        public object? Property(string name)
        {
            foreach (var pair in Values)
            {
                if (pair.Key == name)
                    return pair.Value;
            }

            return null;
        }
    }

    /// <summary>
    /// A Microsoft.Extensions.Logging logger that records what it was handed.
    /// </summary>
    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public Microsoft.Extensions.Logging.LogLevel MinimumLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Trace;

        public List<RecordedEntry> Entries { get; } = new List<RecordedEntry>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= MinimumLevel;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>>
                         ?? Array.Empty<KeyValuePair<string, object?>>();

            Entries.Add(new RecordedEntry
            {
                Level = logLevel,
                Exception = exception,
                Values = values,
            });
        }
    }
}
