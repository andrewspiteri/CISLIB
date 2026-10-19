using CIS.Logging.Exceptions;

namespace CIS.Logging.Events;
// Owned call-boundary facts; final encoded-entry allocation follows in S04.
internal sealed class CapturedEventFacts
{
    internal CapturedEventFacts(DateTimeOffset timestampUtc, string? traceId, string? spanId, string status, string eventName, string message, string? template, string? operationId, string service, string environment, string version, string component, LogValue resource, LogValue attributes, CapturedException? exception, bool isTruncated, bool hasProviderEventId = false, LogValue? explicitAttributes = null, CapturedInterpolation? interpolation = null)
    {
        TimestampUtc = timestampUtc;
        TraceId = traceId;
        SpanId = spanId;
        Status = status;
        EventName = eventName;
        Message = message;
        MessageTemplate = template;
        OperationId = operationId;
        Service = service;
        Environment = environment;
        Version = version;
        Component = component;
        Resource = resource;
        Attributes = attributes;
        Exception = exception;
        IsTruncated = isTruncated;
        HasProviderEventId = hasProviderEventId;
        ExplicitAttributes = explicitAttributes;
        Interpolation = interpolation;
    }

    internal int SchemaVersion => 1;
    internal DateTimeOffset TimestampUtc { get; }
    internal string? TraceId { get; }
    internal string? SpanId { get; }
    internal string Status { get; }
    internal string EventName { get; }
    internal string Message { get; }
    internal string? MessageTemplate { get; }
    internal string? OperationId { get; }
    internal string Service { get; }
    internal string Environment { get; }
    internal string Version { get; }
    internal string Component { get; }
    internal LogValue Resource { get; }
    internal LogValue Attributes { get; }
    internal CapturedException? Exception { get; }
    internal bool IsTruncated { get; }
    internal bool HasProviderEventId { get; }
    internal LogValue? ExplicitAttributes { get; }
    internal CapturedInterpolation? Interpolation { get; }
}
