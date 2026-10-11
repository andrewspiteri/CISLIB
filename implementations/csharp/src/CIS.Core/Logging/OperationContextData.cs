namespace CIS.Logging;

/// <summary>Caller-owned operation identity and explicitly typed scope attributes.</summary>
/// <remarks>Scope creation captures both properties and owns a bounded copy of the attribute map.
/// Later caller changes do not change an existing scope. This input does not modify process identity.</remarks>
public sealed class OperationContextData
{
    /// <summary>Gets or sets the optional operation identifier, trimmed when the scope is captured.</summary>
    /// <remarks>Blank is absent. A retained identifier has at most 255 valid Unicode scalars without controls.</remarks>
    public string? OperationId { get; set; }

    /// <summary>Gets or sets optional attributes; null means an empty map.</summary>
    /// <remarks>Use immutable LogValue instances. Scope creation copies the caller map and preserves truncation.</remarks>
    public IReadOnlyDictionary<string, LogValue>? Attributes { get; set; }
}
