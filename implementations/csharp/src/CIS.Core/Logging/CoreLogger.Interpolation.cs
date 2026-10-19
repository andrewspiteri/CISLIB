namespace CIS.Logging;

public sealed partial class CoreLogger
{
    /// <summary>Captures a C# interpolated message with the event name log.message.</summary>
    public LogWriteResult Write(CoreLogLevel level, ref LogInterpolatedStringHandler message)
        => WriteInterpolation(level, ref message, new() { EventName = "log.message" });

    /// <summary>Captures an interpolated message and explicit structured attributes.</summary>
    public LogWriteResult Write(CoreLogLevel level, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return WriteInterpolation(level, ref message, new() { EventName = "log.message", Attributes = values });
    }

    /// <summary>Captures a named interpolated event and optional structured attributes.</summary>
    public LogWriteResult Write(CoreLogLevel level, string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null)
        => WriteInterpolation(level, ref message, new() { EventName = eventName, Attributes = values });

    /// <summary>Captures an interpolated message with named event, exception and operation metadata.</summary>
    /// <remarks>Context.Message must be empty and Context.MessageTemplate absent. Supplied text conflicts
    /// return ConflictingInterpolationInput. Expressions execute before this method on the caller.</remarks>
    public LogWriteResult Write(CoreLogLevel level, ref LogInterpolatedStringHandler message, LogEventData context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return WriteInterpolation(level, ref message, context);
    }

    private LogWriteResult WriteInterpolation(CoreLogLevel level, ref LogInterpolatedStringHandler message, LogEventData data)
    {
        var interpolation = message.Freeze();
        if (data.Message != string.Empty || data.MessageTemplate is not null)
        { interpolation = interpolation.WithError("ConflictingInterpolationInput"); }
        return Write(level, new LogEventData
        {
            EventName = data.EventName,
            MessageTemplate = interpolation.Template,
            Attributes = data.Attributes,
            Exception = data.Exception,
            OperationId = data.OperationId,
            Interpolation = interpolation
        });
    }

    /// <summary>Captures an interpolated Trace message.</summary>
    public LogWriteResult Trace(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Trace, ref message);
    /// <summary>Captures an interpolated Trace message with structured attributes.</summary>
    public LogWriteResult Trace(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Trace, ref message, values);
    /// <summary>Captures a named interpolated Trace event and optional attributes.</summary>
    public LogWriteResult Trace(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Trace, eventName, ref message, values);
    /// <summary>Captures an interpolated Debug message.</summary>
    public LogWriteResult Debug(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Debug, ref message);
    /// <summary>Captures an interpolated Debug message with structured attributes.</summary>
    public LogWriteResult Debug(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Debug, ref message, values);
    /// <summary>Captures a named interpolated Debug event and optional attributes.</summary>
    public LogWriteResult Debug(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Debug, eventName, ref message, values);
    /// <summary>Captures an interpolated Information message.</summary>
    public LogWriteResult Info(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Information, ref message);
    /// <summary>Captures an interpolated Information message with structured attributes.</summary>
    public LogWriteResult Info(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Information, ref message, values);
    /// <summary>Captures a named interpolated Information event and optional attributes.</summary>
    public LogWriteResult Info(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Information, eventName, ref message, values);
    /// <summary>Captures an interpolated Warning message.</summary>
    public LogWriteResult Warn(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Warning, ref message);
    /// <summary>Captures an interpolated Warning message with structured attributes.</summary>
    public LogWriteResult Warn(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Warning, ref message, values);
    /// <summary>Captures a named interpolated Warning event and optional attributes.</summary>
    public LogWriteResult Warn(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Warning, eventName, ref message, values);
    /// <summary>Captures an interpolated Error message.</summary>
    public LogWriteResult Error(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Error, ref message);
    /// <summary>Captures an interpolated Error message with structured attributes.</summary>
    public LogWriteResult Error(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Error, ref message, values);
    /// <summary>Captures a named interpolated Error event and optional attributes.</summary>
    public LogWriteResult Error(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Error, eventName, ref message, values);
    /// <summary>Captures an interpolated Critical message.</summary>
    public LogWriteResult Critical(ref LogInterpolatedStringHandler message) => Write(CoreLogLevel.Critical, ref message);
    /// <summary>Captures an interpolated Critical message with structured attributes.</summary>
    public LogWriteResult Critical(ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue> values) => Write(CoreLogLevel.Critical, ref message, values);
    /// <summary>Captures a named interpolated Critical event and optional attributes.</summary>
    public LogWriteResult Critical(string eventName, ref LogInterpolatedStringHandler message, IReadOnlyDictionary<string, LogValue>? values = null) => Write(CoreLogLevel.Critical, eventName, ref message, values);
}
