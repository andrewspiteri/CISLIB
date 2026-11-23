using System.Text.Json;
using CIS.Messaging;
using CIS.Security;

namespace CIS.Audit;

/// <summary>An application-selected explicit details mapping and its producer-side schema validation.</summary>
public interface IAuditDetailsCodec<in T>
{
    /// <summary>Validates the selected event schema. Do not accept arbitrary object graphs or secrets.</summary>
    void Validate(T details);

    /// <summary>Writes one JSON object containing only the schema's explicitly selected fields.</summary>
    void Write(Utf8JsonWriter writer, T details);
}

/// <summary>One explicitly selected application event schema; no global registry or schema fetch is needed.</summary>
public sealed class AuditEventDefinition<T>
{
    /// <summary>Creates an immutable event definition and its required common-field selections.</summary>
    public AuditEventDefinition(string eventType, int eventVersion, AuditCategory category,
        IAuditDetailsCodec<T> detailsCodec, bool requireTarget = false, bool requireReason = false,
        bool requireOperationReference = false)
    {
        EventType = MessageText.Contract(eventType, nameof(eventType));
        ArgumentOutOfRangeException.ThrowIfLessThan(eventVersion, 1);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }
        ArgumentNullException.ThrowIfNull(detailsCodec);

        EventVersion = eventVersion;
        Category = category;
        DetailsCodec = detailsCodec;
        RequireTarget = requireTarget;
        RequireReason = requireReason;
        RequireOperationReference = requireOperationReference;
    }

    /// <summary>Gets the stable application event name.</summary>
    public string EventType
    {
        get;
    }

    /// <summary>Gets its positive details-schema version.</summary>
    public int EventVersion
    {
        get;
    }

    /// <summary>Gets the broad event purpose.</summary>
    public AuditCategory Category
    {
        get;
    }

    /// <summary>Gets the explicit trusted application mapping.</summary>
    public IAuditDetailsCodec<T> DetailsCodec
    {
        get;
    }

    /// <summary>Gets whether this event requires a target.</summary>
    public bool RequireTarget
    {
        get;
    }

    /// <summary>Gets whether this event requires a reason.</summary>
    public bool RequireReason
    {
        get;
    }

    /// <summary>Gets whether this event requires an original operation reference.</summary>
    public bool RequireOperationReference
    {
        get;
    }
}

/// <summary>Explicit fact data. Producer and authenticated actor identity cannot be overridden here.</summary>
public sealed class AuditCaptureOptions
{
    /// <summary>Copies selected event data without inventing a successful outcome or missing identity.</summary>
    public AuditCaptureOptions(AuditOutcome outcome = AuditOutcome.Unknown, DateTimeOffset? occurredAtUtc = null,
        AuditTarget? target = null, AuditReason? reason = null, AuditOperationReference? operationReference = null,
        string? correlationId = null, Guid? causationAuditId = null, string? domainEventId = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Outcome = outcome;
        OccurredAtUtc = occurredAtUtc?.ToUniversalTime();
        Target = target;
        Reason = reason;
        OperationReference = operationReference;
        CorrelationId = correlationId is null ? null : SecurityText.Bounded(correlationId, 128, nameof(correlationId));
        CausationAuditId = causationAuditId is { } cause ? MessageText.Id(cause, nameof(causationAuditId)) : null;
        DomainEventId = SecurityText.OptionalIdentifier(domainEventId, nameof(domainEventId));
    }

    /// <summary>Gets the actual known outcome.</summary>
    public AuditOutcome Outcome
    {
        get;
    }

    /// <summary>Gets the known source occurrence time, or null to use this capture's observation time.</summary>
    public DateTimeOffset? OccurredAtUtc
    {
        get;
    }

    /// <summary>Gets the actual resolved target.</summary>
    public AuditTarget? Target
    {
        get;
    }

    /// <summary>Gets the selected safe reason.</summary>
    public AuditReason? Reason
    {
        get;
    }

    /// <summary>Gets the actual available operation reference.</summary>
    public AuditOperationReference? OperationReference
    {
        get;
    }

    /// <summary>Gets diagnostic correlation.</summary>
    public string? CorrelationId
    {
        get;
    }

    /// <summary>Gets a causing audit identity.</summary>
    public Guid? CausationAuditId
    {
        get;
    }

    /// <summary>Gets a domain event reference, bounded to 1024 Unicode scalars.</summary>
    public string? DomainEventId
    {
        get;
    }
}

/// <summary>Trusted source capture. It performs no persistence or network I/O.</summary>
public interface IAuditCapture
{
    /// <summary>Captures the current trusted scope; a missing caller remains unknown.</summary>
    CapturedAuditRecord Capture<T>(AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Explicitly captures local system work, independently of any current caller.</summary>
    CapturedAuditRecord CaptureLocalSystem<T>(AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>An expected finite capture admission refusal.</summary>
public enum AuditCaptureFailure
{
    /// <summary>The existing shared buffer capacity could not admit capture.</summary>
    Overloaded,
    /// <summary>The owning lifecycle no longer admits capture.</summary>
    Unavailable
}

/// <summary>A capture admission refusal with a safe allocated identity and no persistence claim.</summary>
public sealed class AuditCaptureException : InvalidOperationException
{
    /// <summary>Creates an explicit known pre-submission refusal.</summary>
    public AuditCaptureException(Guid auditId, AuditCaptureFailure failure)
        : base(failure == AuditCaptureFailure.Overloaded ? "Audit capture capacity is unavailable." : "Audit capture is unavailable.")
    {
        AuditId = MessageText.Id(auditId, nameof(auditId));
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }
        Failure = failure;
    }

    /// <summary>Gets the allocated occurrence identity without implying acceptance.</summary>
    public Guid AuditId
    {
        get;
    }

    /// <summary>Gets the finite refusal category.</summary>
    public AuditCaptureFailure Failure
    {
        get;
    }
}
