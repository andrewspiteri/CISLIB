namespace CIS.Audit;

/// <summary>Portable immutable data. Decoding it establishes neither source eligibility nor authority.</summary>
public sealed class AuditRecord
{
    private readonly byte[] originalBytes;
    private readonly int detailsOffset;
    private readonly int detailsLength;

    internal AuditRecord(byte[] originalBytes, int detailsOffset, int detailsLength, Guid auditId,
        string eventType, int eventVersion, AuditCategory category, DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc, AuditProducer producer, AuditActor actor, AuditOutcome outcome,
        AuditTarget? target, AuditReason? reason, AuditOperationReference? operationReference,
        string? correlationId, Guid? causationAuditId, string? domainEventId)
    {
        this.originalBytes = originalBytes;
        this.detailsOffset = detailsOffset;
        this.detailsLength = detailsLength;
        AuditId = auditId;
        EventType = eventType;
        EventVersion = eventVersion;
        Category = category;
        OccurredAtUtc = occurredAtUtc;
        RecordedAtUtc = recordedAtUtc;
        Producer = producer;
        Actor = actor;
        Outcome = outcome;
        Target = target;
        Reason = reason;
        OperationReference = operationReference;
        CorrelationId = correlationId;
        CausationAuditId = causationAuditId;
        DomainEventId = domainEventId;
    }

    /// <summary>Gets the supported common-envelope version.</summary>
    public int AuditVersion => 1;

    /// <summary>Gets the occurrence identity, independent of transport messages.</summary>
    public Guid AuditId
    {
        get;
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

    /// <summary>Gets when the source fact occurred or became known.</summary>
    public DateTimeOffset OccurredAtUtc
    {
        get;
    }

    /// <summary>Gets when this immutable record was first formed.</summary>
    public DateTimeOffset RecordedAtUtc
    {
        get;
    }

    /// <summary>Gets the source facts.</summary>
    public AuditProducer Producer
    {
        get;
    }

    /// <summary>Gets the initiating actor projection.</summary>
    public AuditActor Actor
    {
        get;
    }

    /// <summary>Gets the fact known at this boundary.</summary>
    public AuditOutcome Outcome
    {
        get;
    }

    /// <summary>Gets the actual target when one was established.</summary>
    public AuditTarget? Target
    {
        get;
    }

    /// <summary>Gets the explicitly selected safe reason.</summary>
    public AuditReason? Reason
    {
        get;
    }

    /// <summary>Gets the available original operation reference.</summary>
    public AuditOperationReference? OperationReference
    {
        get;
    }

    /// <summary>Gets diagnostic correlation, without idempotency or authority.</summary>
    public string? CorrelationId
    {
        get;
    }

    /// <summary>Gets the causing audit occurrence, if supplied.</summary>
    public Guid? CausationAuditId
    {
        get;
    }

    /// <summary>Gets a bounded domain event reference, if supplied.</summary>
    public string? DomainEventId
    {
        get;
    }

    /// <summary>Gets a detached copy of the complete original payload, preserving its exact bytes.</summary>
    public ReadOnlyMemory<byte> OriginalBytes => originalBytes.ToArray();

    /// <summary>Gets a detached copy of the original details object, including unknown application fields.</summary>
    public ReadOnlyMemory<byte> DetailsJson => originalBytes.AsMemory(detailsOffset, detailsLength).ToArray();

    internal ReadOnlyMemory<byte> OriginalMemory => originalBytes;
}

/// <summary>A trusted local capture eligible for source submission. Decoders cannot create this type.</summary>
public sealed class CapturedAuditRecord
{
    internal CapturedAuditRecord(AuditRecord record)
    {
        Record = record;
    }

    /// <summary>Gets the immutable portable record.</summary>
    public AuditRecord Record
    {
        get;
    }

    /// <summary>Gets the captured occurrence identity.</summary>
    public Guid AuditId => Record.AuditId;

    /// <summary>Gets a detached copy of its original bytes.</summary>
    public ReadOnlyMemory<byte> OriginalBytes => Record.OriginalBytes;
}
