using CIS.Logging.Exceptions;

namespace CIS.Logging.Events;

// Owned projection facts. UTC/activity capture and final encoded-entry allocation follow in S04.
internal sealed class CapturedEventFacts
{
    internal CapturedEventFacts(string status, string eventName, string message, string? template,
        string? operationId, string service, string environment, string version, string component,
        LogValue resource, LogValue attributes, CapturedException? exception, bool isTruncated)
    {
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
    }

    internal int SchemaVersion => 1;
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
}
