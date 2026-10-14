using System.Diagnostics;
using CIS.Logging.Exceptions;
using CIS.Logging.Scopes;
using CIS.Logging.Values;
using CIS.Text;

namespace CIS.Logging.Events;

internal static class EventProjection
{
    internal const int MaximumPayloadScalars = 8192;

    internal static CapturedEventFacts? Capture(EventMetadata metadata, CapturedScopeContext scope,
        string component, CoreLogLevel severity, LogEventData data, TimeProvider? timeProvider = null)
    {
        string? status = severity switch
        {
            CoreLogLevel.Trace => "trace",
            CoreLogLevel.Debug => "debug",
            CoreLogLevel.Information => "info",
            CoreLogLevel.Warning => "warn",
            CoreLogLevel.Error => "error",
            CoreLogLevel.Critical => "critical",
            CoreLogLevel.None => null,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), "Use a declared logging severity.")
        };
        if (status is null) { return null; }
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(data);
        ValidateName(component, nameof(component));

        // Read named input references before invoking cooperative collection/exception accessors.
        string eventName = data.EventName;
        string message = data.Message;
        string? template = data.MessageTemplate;
        string? operationId = data.OperationId?.Trim();
        IReadOnlyDictionary<string, LogValue>? sourceAttributes = data.Attributes;
        Exception? sourceException = data.Exception;
        Func<(string? Template, LogValue Attributes)>? sourceState = data.CaptureState;
        ValidateName(eventName, nameof(data));
        // Own Activity IDs before clock/payload callbacks can replace the ambient context.
        (string? traceId, string? spanId) = CaptureActivity();
        DateTimeOffset timestampUtc = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
        (string? Template, LogValue Attributes)? adapted = sourceState?.Invoke();
        template = adapted?.Template ?? template;
        string capturedMessage = CaptureText(message, out bool messageClipped);
        bool templateClipped = false;
        string? capturedTemplate = template is null ? null : CaptureText(template, out templateClipped);
        if (string.IsNullOrEmpty(operationId)) { operationId = scope.OperationId; }
        else if (!UnicodeScalarText.IsValid(operationId, 255, allowControls: false))
        {
            throw new ArgumentException("Use an operation identifier of at most 255 valid Unicode scalars without controls.", nameof(data));
        }
        LogValue explicitAttributes = adapted?.Attributes ?? LogValue.Map(sourceAttributes ?? new Dictionary<string, LogValue>());
        LogValue attributes = Merge(scope.Attributes, explicitAttributes);
        CapturedException? exception = ExceptionProjection.Capture(sourceException);
        return new CapturedEventFacts(timestampUtc, traceId, spanId, status, eventName, capturedMessage, capturedTemplate, operationId,
            metadata.ApplicationName, metadata.EnvironmentName, metadata.ServiceVersion, component,
            Resource(metadata), attributes, exception,
            messageClipped || templateClipped || attributes.IsTruncated || exception?.HasTruncatedContent == true);
    }

    private static (string? TraceId, string? SpanId) CaptureActivity()
    {
        Activity? activity = Activity.Current;
        if (activity?.IdFormat != ActivityIdFormat.W3C) { return (null, null); }
        string traceId = activity.TraceId.ToHexString();
        string spanId = activity.SpanId.ToHexString();
        return IsValidHexId(traceId, 32) && IsValidHexId(spanId, 16) ? (traceId, spanId) : (null, null);
    }

    private static bool IsValidHexId(string value, int length)
    {
        if (value.Length != length) { return false; }
        bool nonzero = false;
        foreach (char character in value)
        {
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f')) { return false; }
            nonzero |= character != '0';
        }
        return nonzero;
    }

    private static void ValidateName(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length == 0 || !UnicodeScalarText.IsValid(value, 255, allowControls: false))
        {
            throw new ArgumentException("Use a name of 1-255 valid Unicode scalars without controls.", parameter);
        }
    }

    private static string CaptureText(string value, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(value, "data");
        // Unlike bounded exception accessors, the named message/template is a supplied string.
        // Malformed input remains invalid even in the suffix that the scalar limit would omit.
        if (!LogValueText.IsValidUnicode(value))
        {
            throw new ArgumentException("Use valid Unicode scalar text.", "data");
        }
        return UnicodeScalarText.Prefix(value, MaximumPayloadScalars, out truncated);
    }

    private static LogValue Merge(LogValue scope, LogValue explicitAttributes)
    {
        var winners = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        bool truncated = scope.IsTruncated || explicitAttributes.IsTruncated;
        foreach (LogValue map in new[] { scope, explicitAttributes })
        {
            foreach ((string key, LogValue value) in map.MapProperties)
            {
                if (winners.ContainsKey(key) || winners.Count < LogValueProjection.MaximumNodes - 1) { winners[key] = value; }
                else { truncated = true; }
            }
        }
        return new LogValueProjection().Map(winners, depth: 1, truncated);
    }

    private static LogValue Resource(EventMetadata metadata)
    {
        var resource = new Dictionary<string, LogValue>(StringComparer.Ordinal)
        {
            ["build_version"] = LogValue.String(metadata.BuildVersion),
            ["instance_id"] = LogValue.String(metadata.InstanceId),
            ["initialized_at"] = LogValue.Timestamp(metadata.InitializedAtUtc),
            ["environment_kind"] = LogValue.String(metadata.EnvironmentKind switch
            {
                EnvironmentKind.Unknown => "unknown",
                EnvironmentKind.Console => "console",
                EnvironmentKind.Service => "service",
                EnvironmentKind.Web => "web",
                EnvironmentKind.Function => "function",
                _ => throw new InvalidOperationException("Invalid owned environment kind.")
            }),
            ["os_family"] = LogValue.String(metadata.OperatingSystem)
        };
        if (metadata.ProcessStartedAtUtc is { } started) { resource["process_started_at"] = LogValue.Timestamp(started); }
        Add("host_name", metadata.HostName);
        Add("os_description", metadata.OperatingSystemDescription);
        Add("os_architecture", metadata.OSArchitecture);
        Add("process_architecture", metadata.ProcessArchitecture);
        Add("runtime_description", metadata.RuntimeDescription);
        if (metadata.ProcessId is { } processId) { resource["process_id"] = LogValue.Integer(processId); }
        Add("commit_id", metadata.CommitId);
        Add("build_id", metadata.BuildId);
        return LogValue.Map(resource);

        void Add(string key, string? value) { if (value is not null) { resource[key] = LogValue.String(value); } }
    }
}
