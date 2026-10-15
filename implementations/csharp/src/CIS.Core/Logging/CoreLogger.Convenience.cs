namespace CIS.Logging;

public sealed partial class CoreLogger
{
    /// <summary>Writes a Trace message with the stable event name log.message.</summary>
    public LogWriteResult Trace(string message) => Write(CoreLogLevel.Trace, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Trace event.</summary>
    public LogWriteResult Trace(string eventName, string message) => Write(CoreLogLevel.Trace, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Trace event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Trace(LogEventData data) => Write(CoreLogLevel.Trace, data);

    /// <summary>Builds a Trace event at most once after early interest checks.</summary>
    public LogWriteResult TraceLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Trace, factory);
    /// <summary>Writes a Debug message with the stable event name log.message.</summary>
    public LogWriteResult Debug(string message) => Write(CoreLogLevel.Debug, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Debug event.</summary>
    public LogWriteResult Debug(string eventName, string message) => Write(CoreLogLevel.Debug, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Debug event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Debug(LogEventData data) => Write(CoreLogLevel.Debug, data);

    /// <summary>Builds a Debug event at most once after early interest checks.</summary>
    public LogWriteResult DebugLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Debug, factory);
    /// <summary>Writes a Information message with the stable event name log.message.</summary>
    public LogWriteResult Info(string message) => Write(CoreLogLevel.Information, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Information event.</summary>
    public LogWriteResult Info(string eventName, string message) => Write(CoreLogLevel.Information, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Information event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Info(LogEventData data) => Write(CoreLogLevel.Information, data);

    /// <summary>Builds a Information event at most once after early interest checks.</summary>
    public LogWriteResult InfoLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Information, factory);
    /// <summary>Writes a Warning message with the stable event name log.message.</summary>
    public LogWriteResult Warn(string message) => Write(CoreLogLevel.Warning, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Warning event.</summary>
    public LogWriteResult Warn(string eventName, string message) => Write(CoreLogLevel.Warning, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Warning event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Warn(LogEventData data) => Write(CoreLogLevel.Warning, data);

    /// <summary>Builds a Warning event at most once after early interest checks.</summary>
    public LogWriteResult WarnLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Warning, factory);
    /// <summary>Writes a Error message with the stable event name log.message.</summary>
    public LogWriteResult Error(string message) => Write(CoreLogLevel.Error, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Error event.</summary>
    public LogWriteResult Error(string eventName, string message) => Write(CoreLogLevel.Error, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Error event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Error(LogEventData data) => Write(CoreLogLevel.Error, data);

    /// <summary>Builds a Error event at most once after early interest checks.</summary>
    public LogWriteResult ErrorLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Error, factory);
    /// <summary>Writes a Critical message with the stable event name log.message.</summary>
    public LogWriteResult Critical(string message) => Write(CoreLogLevel.Critical, new LogEventData { EventName = "log.message", Message = message });

    /// <summary>Writes a named Critical event.</summary>
    public LogWriteResult Critical(string eventName, string message) => Write(CoreLogLevel.Critical, new LogEventData { EventName = eventName, Message = message });

    /// <summary>Writes a Critical event with copied typed attributes, context and exception facts.</summary>
    public LogWriteResult Critical(LogEventData data) => Write(CoreLogLevel.Critical, data);

    /// <summary>Builds a Critical event at most once after early interest checks.</summary>
    public LogWriteResult CriticalLazy(Func<LogEventData> factory) => WriteLazy(CoreLogLevel.Critical, factory);
}
