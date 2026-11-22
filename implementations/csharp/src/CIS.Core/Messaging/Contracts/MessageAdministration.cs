using CIS.Transactions;

namespace CIS.Messaging;

/// <summary>The authorization disposition of one explicit bounded administrative call.</summary>
public enum MessageAdministrationDisposition
{
    /// <summary>No capability policy was selected for this entry point.</summary>
    Disabled,
    /// <summary>The actual current caller was not authorized.</summary>
    Denied,
    /// <summary>The shared runtime work or encoded-byte capacity refused admission.</summary>
    Unavailable,
    /// <summary>The call reached the authoritative store; inspect actual completion separately.</summary>
    Executed
}

/// <summary>One bounded metadata page, optionally including one exact processing identity.</summary>
/// <remarks>Application and environment are captured by the server and cannot be supplied by the caller.</remarks>
public sealed class MessageInspectQuery
{
    /// <summary>Validates a page of at most 256 records. Subscription and message identity must be supplied together.</summary>
    public MessageInspectQuery(int maximumRecords = 256, string? subscription = null, Guid? messageId = null,
        string? routeAlias = null, Guid? afterRecordId = null)
    {
        MaximumRecords = MessageText.Range(maximumRecords, 1, 256, nameof(maximumRecords));
        if ((subscription is null) != (messageId is null))
        {
            throw new ArgumentException("Supply the subscription and message identity together.");
        }
        Subscription = subscription is null ? null : MessageText.Alias(subscription, nameof(subscription));
        MessageId = messageId is { } id ? MessageText.Id(id, nameof(messageId)) : null;
        RouteAlias = routeAlias is null ? null : MessageText.Alias(routeAlias, nameof(routeAlias));
        AfterRecordId = afterRecordId is { } cursor ? MessageText.Id(cursor, nameof(afterRecordId)) : null;
    }

    /// <summary>Gets the finite page limit.</summary>
    public int MaximumRecords { get; }
    /// <summary>Gets the registered subscription filter.</summary>
    public string? Subscription { get; }
    /// <summary>Gets the exact logical message filter.</summary>
    public Guid? MessageId { get; }
    /// <summary>Gets an optional registered route filter.</summary>
    public string? RouteAlias { get; }
    /// <summary>Gets an opaque record cursor.</summary>
    public Guid? AfterRecordId { get; }
}

/// <summary>An explicit terminal reopening of an exact inspected state version.</summary>
public sealed class MessageRedriveCommand
{
    /// <summary>Validates the configured subscription, identity, expected version and safe operator reason.</summary>
    public MessageRedriveCommand(string subscription, Guid messageId, long expectedVersion, string safeCode)
    {
        Subscription = MessageText.Alias(subscription, nameof(subscription));
        MessageId = MessageText.Id(messageId, nameof(messageId));
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);
        ExpectedVersion = expectedVersion;
        SafeCode = MessageText.Code(safeCode, nameof(safeCode));
    }

    /// <summary>Gets the registered logical receiving subscription.</summary>
    public string Subscription { get; }
    /// <summary>Gets the original logical message identity.</summary>
    public Guid MessageId { get; }
    /// <summary>Gets the version observed by a prior authorized inspection.</summary>
    public long ExpectedVersion { get; }
    /// <summary>Gets the explicit safe audited operator reason.</summary>
    public string SafeCode { get; }
}

/// <summary>Safe exact processing metadata for versioned administration, excluding provenance, payload and operation keys.</summary>
public sealed record MessageProcessingInspection(long Version, MessageProcessingDisposition Disposition,
    int DeliveryOrdinal, int Generation, bool HasPayload);

/// <summary>A bounded authorized observation. Absent records on Denied/Disabled are not an empty store.</summary>
public sealed record MessageInspectResult(MessageAdministrationDisposition Disposition,
    IReadOnlyList<MessageInspectionRecord>? Records = null, MessageProcessingInspection? Processing = null,
    TransactionCompletion? Completion = null);

/// <summary>A deliberate redrive candidate and its actual owner completion, never an inferred delivery success.</summary>
public sealed record MessageAdminRedriveResult(MessageAdministrationDisposition Disposition,
    MessageRedriveDisposition? Transition = null, Guid? HandoffRecordId = null, int? Generation = null,
    TransactionCompletion? Completion = null);

/// <summary>Candidate cleanup counts and actual completion; counts are confirmed only when Committed.</summary>
public sealed record MessageAdminMaintenanceResult(MessageAdministrationDisposition Disposition,
    MessageMaintenanceCounts? CandidateCounts = null, TransactionCompletion? Completion = null)
{
    /// <summary>Gets confirmed counts only after known committed settlement.</summary>
    public MessageMaintenanceCounts? ConfirmedCounts => Completion?.Outcome == TransactionOutcome.Committed ? CandidateCounts : null;
}

/// <summary>Authorized explicit metadata inspection, terminal redrive and bounded maintenance.</summary>
/// <remarks>No method runs a business handler, impersonates retained provenance or installs a background worker.</remarks>
public interface IMessageAdministration
{
    /// <summary>Authorizes before querying metadata; exact queries additionally expose a safe expected-state version.</summary>
    Task<MessageInspectResult> InspectAsync(MessageInspectQuery query, CancellationToken cancellationToken = default);
    /// <summary>Authorizes the actual subscription and stages one eligible explicit generation.</summary>
    Task<MessageAdminRedriveResult> RedriveAsync(MessageRedriveCommand command, CancellationToken cancellationToken = default);
    /// <summary>Runs at most 256 cleanup changes using the captured retention policy; defaults preserve history indefinitely.</summary>
    Task<MessageAdminMaintenanceResult> MaintainAsync(int maximumRecords = 256, CancellationToken cancellationToken = default);
}
