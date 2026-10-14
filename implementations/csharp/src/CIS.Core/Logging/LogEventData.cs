namespace CIS.Logging;

/// <summary>Caller-owned named event input, copied during capture.</summary>
/// <remarks>Do not mutate this input or its collections concurrently with capture.
/// Immutable LogValue children may be shared. Event severity is supplied by the logging method.</remarks>
public sealed class LogEventData
{
    /// <summary>The required event name, with at most 255 valid Unicode scalars and no controls.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>The supplied message; an empty message is valid.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The optional supplied template; null is absent and empty is retained.</summary>
    public string? MessageTemplate { get; set; }

    /// <summary>The optional event identifier, overriding the captured scope when nonblank.</summary>
    public string? OperationId { get; set; }

    /// <summary>The optional typed attributes, overriding matching ordinal scope keys.</summary>
    public IReadOnlyDictionary<string, LogValue>? Attributes { get; set; }

    /// <summary>The optional exception, projected without retaining the caller exception or its Data.</summary>
    public Exception? Exception { get; set; }
    // Adapter capture runs inside core validation after Activity/clock capture, before admission.
    internal Func<(string? Template, LogValue Attributes)>? CaptureState { get; init; }
    internal Func<int, Scopes.CapturedScopeContext>? CaptureScopes { get; init; }
}
