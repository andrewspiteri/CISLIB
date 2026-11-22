using CIS.Transactions;

namespace CIS.Messaging;

/// <summary>The retained publication lifecycle.</summary>
public enum MessageOutboxState
{
    /// <summary>Committed responsibility eligible for reservation.</summary>
    Pending,
    /// <summary>A fenced database-time publication reservation.</summary>
    Reserved,
    /// <summary>Known broker acceptance was recorded.</summary>
    Published,
    /// <summary>Retained responsibility requires explicit intervention.</summary>
    Blocked
}

/// <summary>The retained logical receiving disposition.</summary>
public enum MessageProcessingDisposition
{
    /// <summary>The current ordinal may perform work under database arbitration.</summary>
    Ready,
    /// <summary>Business processing and required reply responsibility were secured.</summary>
    Handled,
    /// <summary>Exactly one later ordinal has a secured outgoing record.</summary>
    RetryScheduled,
    /// <summary>Canonical terminal responsibility was secured.</summary>
    Terminal
}

/// <summary>Transactional inbox arbitration, without a commit claim.</summary>
public enum MessageClaimDisposition
{
    /// <summary>This transaction holds the eligible processing identity.</summary>
    Acquired,
    /// <summary>A previous committed processing result suppresses the effect.</summary>
    HandledDuplicate,
    /// <summary>A secured later ordinal owns delivery responsibility.</summary>
    RetryScheduled,
    /// <summary>The logical delivery is terminal.</summary>
    Terminal,
    /// <summary>The delivery does not match the expected trusted ordinal/generation.</summary>
    InvalidOrdinal,
    /// <summary>The same MessageId has different immutable bytes.</summary>
    ContentConflict,
    /// <summary>Original verified provenance or resolved operation binding does not match.</summary>
    ProvenanceConflict,
    /// <summary>Retained state cannot be interpreted safely.</summary>
    Corrupt
}

/// <summary>The outcome of securing failed-attempt responsibility in a fresh transaction.</summary>
public enum MessageDispositionResultKind
{
    /// <summary>A transition and exactly one handoff were staged atomically.</summary>
    Secured,
    /// <summary>A concurrent successful attempt won the rollback gap.</summary>
    AlreadyHandled,
    /// <summary>The expected handoff or a later disposition is already secured.</summary>
    AlreadySecured,
    /// <summary>The requested ordinal/generation is not eligible.</summary>
    InvalidOrdinal,
    /// <summary>The immutable transport digest conflicts.</summary>
    ContentConflict,
    /// <summary>Original verified provenance or resolved operation binding conflicts.</summary>
    ProvenanceConflict
}

/// <summary>A row observation; only a coordinator's fresh independent read can establish committed submission.</summary>
public enum MessageSubmissionObservation
{
    /// <summary>The exact receipt identity and digest were observed in this context.</summary>
    Found,
    /// <summary>No matching committed row was observed; this never proves rollback.</summary>
    NotFound,
    /// <summary>A retained identity has incompatible immutable content.</summary>
    ContentConflict
}

/// <summary>Deliberate redrive eligibility, without a transaction completion claim.</summary>
public enum MessageRedriveDisposition
{
    /// <summary>The explicit new generation and targeted outgoing row were staged.</summary>
    Prepared,
    /// <summary>No retained logical delivery was observed.</summary>
    NotFound,
    /// <summary>Successful or active work cannot be transparently reopened.</summary>
    NotTerminal,
    /// <summary>The declared supported replay horizon has ended.</summary>
    OutsideHorizon,
    /// <summary>Required retained payload or provenance is unavailable.</summary>
    ContentUnavailable,
    /// <summary>The expected state version no longer matches.</summary>
    Conflict
}

/// <summary>One immutable outgoing row. Delay is applied against database time, not an alleged commit timestamp.</summary>
/// <param name="RecordId">A new outgoing-record UUID, distinct from the logical MessageId.</param>
/// <param name="Application">The owning server application.</param>
/// <param name="Environment">The owning server environment.</param>
/// <param name="Destination">A registered logical destination.</param>
/// <param name="Message">The exact owned immutable envelope.</param>
/// <param name="Purpose">The internal responsibility, outside that envelope.</param>
/// <param name="Delay">A finite nonnegative eligibility delay from database observation time.</param>
/// <param name="ProcessingKey">The associated logical receiver identity, where applicable.</param>
/// <param name="DeliveryOrdinal">The scheduled ordinal, initially one.</param>
/// <param name="Generation">An explicit audited redrive generation, initially zero.</param>
/// <param name="DiagnosticCode">Safe bounded context without native error text.</param>
/// <param name="OriginalProvenance">Original audit evidence, never delegated authority.</param>
/// <param name="TargetSubscription">An explicit configured target, independent of canonical identity for rejection/quarantine.</param>
/// <param name="RejectionId">The newly owned rejection anchor for a canonical-free protocol reply.</param>
public sealed record MessageOutgoingRecord(Guid RecordId, string Application, string Environment,
    MessageDestination Destination, EncodedMessage Message, MessageRecordPurpose Purpose = MessageRecordPurpose.Application,
    TimeSpan Delay = default, MessageProcessingKey? ProcessingKey = null, int DeliveryOrdinal = 1, int Generation = 0,
    string? DiagnosticCode = null, MessageProvenance? OriginalProvenance = null, string? TargetSubscription = null,
    Guid? RejectionId = null);

/// <summary>Staging identifiers and digest, without a persistence or broker-acceptance claim.</summary>
/// <param name="RecordId">The stable outgoing-record identity.</param>
/// <param name="MessageId">The stable immutable logical envelope identity.</param>
/// <param name="DatabaseName">The original transaction's registered database alias.</param>
/// <param name="Application">The owner application.</param>
/// <param name="Environment">The owner environment.</param>
/// <param name="Digest">The immutable transport digest.</param>
public sealed record MessageStageReceipt(Guid RecordId, Guid MessageId, string DatabaseName,
    string Application, string Environment, MessageDigest Digest);

/// <summary>A bounded metadata-only reservation request.</summary>
/// <param name="Application">The captured owner application.</param>
/// <param name="Environment">The captured owner environment.</param>
/// <param name="Owner">This publisher's opaque reservation owner.</param>
/// <param name="MaximumRecords">At most 32 rows.</param>
/// <param name="Duration">A finite database-time lease.</param>
public sealed record MessageReservationRequest(string Application, string Environment, Guid Owner,
    int MaximumRecords = 32, TimeSpan? Duration = null);

/// <summary>Fenced metadata only. Reserve owned bytes before asking the store to materialize its body.</summary>
/// <param name="RecordId">The outgoing row identity.</param>
/// <param name="MessageId">The immutable logical message identity.</param>
/// <param name="Application">The owner application.</param>
/// <param name="Environment">The owner environment.</param>
/// <param name="Destination">The selected logical destination.</param>
/// <param name="Purpose">The responsibility classification.</param>
/// <param name="DeliveryOrdinal">The scheduled handler ordinal.</param>
/// <param name="Generation">The audited redrive generation.</param>
/// <param name="ProcessingKey">Associated processing identity.</param>
/// <param name="Digest">The expected exact immutable bytes.</param>
/// <param name="EncodedLength">The body size known before allocation.</param>
/// <param name="Owner">The fencing owner.</param>
/// <param name="Version">The monotonically changing fencing version.</param>
/// <param name="ExpiresAtUtc">Lease expiry measured by the database.</param>
/// <param name="CreatedAtUtc">The row's database observation time.</param>
/// <param name="AttemptCount">The retained bounded publication-attempt counter.</param>
/// <param name="DiagnosticCode">Safe prior diagnostic context.</param>
/// <param name="TargetSubscription">The configured targeted consumer, including canonical-free rejection handoffs.</param>
public sealed record MessageReservation(Guid RecordId, Guid MessageId, string Application, string Environment,
    MessageDestination Destination, MessageRecordPurpose Purpose, int DeliveryOrdinal, int Generation,
    MessageProcessingKey? ProcessingKey, MessageDigest Digest, int EncodedLength, Guid Owner, long Version,
    DateTimeOffset ExpiresAtUtc, DateTimeOffset CreatedAtUtc, int AttemptCount, string? DiagnosticCode = null,
    string? TargetSubscription = null);

/// <summary>A conditional publication-state update; a true store result still requires the enclosing commit.</summary>
/// <param name="Reservation">The exact owner/version fence.</param>
/// <param name="Result">The actual settled native acceptance evidence.</param>
/// <param name="RetryDelay">Finite delay for recoverable responsibility, based on database time.</param>
public sealed record MessagePublicationUpdate(MessageReservation Reservation, MessagePublicationResult Result,
    TimeSpan RetryDelay = default);

/// <summary>Metadata retained for safe repeats. It contains no replayable authentication ticket.</summary>
/// <param name="Key">Full receiving duplicate identity.</param>
/// <param name="Digest">The immutable envelope digest.</param>
/// <param name="Provenance">Original verified producer facts.</param>
/// <param name="DeliveryOrdinal">The current scheduled ordinal.</param>
/// <param name="Disposition">The retained responsibility state.</param>
/// <param name="Version">The conditional transition version.</param>
/// <param name="Generation">Explicit redrive generation.</param>
/// <param name="UpdatedAtUtc">Database state observation time.</param>
/// <param name="HandoffRecordId">The unique secured retry/terminal outgoing record.</param>
/// <param name="ReplyRecordId">The secured original reply outgoing record.</param>
/// <param name="OperationBinding">Original trusted operation scope and exact resource.</param>
/// <param name="ProtectionExpiresAtUtc">Explicit full-record deletion eligibility, or indefinite.</param>
/// <param name="HasPayload">Whether controlled payload access remains available for supported redrive.</param>
public sealed record MessageProcessingRecord(MessageProcessingKey Key, MessageDigest Digest, MessageProvenance Provenance,
    int DeliveryOrdinal, MessageProcessingDisposition Disposition, long Version, int Generation, DateTimeOffset UpdatedAtUtc,
    Guid? HandoffRecordId = null, Guid? ReplyRecordId = null, MessageOperationBinding? OperationBinding = null,
    DateTimeOffset? ProtectionExpiresAtUtc = null, bool HasPayload = true);

/// <summary>One verified canonical delivery candidate, never authorization derived from its wire fields.</summary>
/// <param name="Key">The receiving identity.</param>
/// <param name="Message">Exact bounded envelope bytes.</param>
/// <param name="Provenance">Original trusted audit reference.</param>
/// <param name="DeliveryOrdinal">The expected scheduled ordinal.</param>
/// <param name="Generation">The expected explicit generation.</param>
/// <param name="OperationBinding">The trusted original scope/resource, when bound to Operations.</param>
/// <param name="Purpose">Application, Retry or Redrive; internal metadata needs a retained handoff match.</param>
/// <param name="HandoffRecordId">The exact retained outgoing identity for an internal retry/redrive.</param>
public sealed record MessageProcessingAttempt(MessageProcessingKey Key, EncodedMessage Message,
    MessageProvenance Provenance, int DeliveryOrdinal = 1, int Generation = 0, MessageOperationBinding? OperationBinding = null,
    MessageRecordPurpose Purpose = MessageRecordPurpose.Application, Guid? HandoffRecordId = null);

/// <summary>An inbox arbitration observation plus its authoritative retained snapshot.</summary>
/// <param name="Disposition">The exact arbitration fact.</param>
/// <param name="Record">The current bounded retained state.</param>
public sealed record MessageProcessingClaim(MessageClaimDisposition Disposition, MessageProcessingRecord? Record = null);

/// <summary>Expected-state successful processing staged under the original claim's transaction.</summary>
/// <param name="Key">The claimed receiving identity.</param>
/// <param name="ExpectedVersion">The exact current version.</param>
/// <param name="ExpectedOrdinal">The exact current ordinal.</param>
/// <param name="Generation">The exact current generation.</param>
/// <param name="ReplyRecordId">An outgoing reply staged in this same transaction.</param>
/// <param name="OperationBinding">Resolved operation binding retained before a reply or retry.</param>
public sealed record MessageProcessingCompletion(MessageProcessingKey Key, long ExpectedVersion, int ExpectedOrdinal,
    int Generation = 0, Guid? ReplyRecordId = null, MessageOperationBinding? OperationBinding = null);

/// <summary>One reply reference to attach under the same native owner that freshly secured its terminal or rejection anchor.</summary>
/// <param name="ReplyRecordId">The staged reply outgoing identity.</param>
/// <param name="ProcessingKey">The canonical Terminal anchor, exclusive with RejectionId.</param>
/// <param name="ExpectedVersion">The freshly secured canonical version.</param>
/// <param name="ExpectedOrdinal">The exact canonical ordinal.</param>
/// <param name="Generation">The exact canonical generation, or zero for rejection.</param>
/// <param name="RejectionId">The newly created rejection anchor, exclusive with ProcessingKey.</param>
public sealed record MessageReplyAttachment(Guid ReplyRecordId, MessageProcessingKey? ProcessingKey = null,
    long? ExpectedVersion = null, int? ExpectedOrdinal = null, int Generation = 0, Guid? RejectionId = null);

/// <summary>A failed-attempt transition and exactly one handoff to secure in a fresh short owner.</summary>
/// <param name="Attempt">Verified immutable original identity, digest and provenance.</param>
/// <param name="Disposition">RetryScheduled or Terminal.</param>
/// <param name="Handoff">The one targeted outgoing retry/terminal record.</param>
/// <param name="SafeCode">Bounded reason, without native exception text.</param>
public sealed record MessageDispositionRequest(MessageProcessingAttempt Attempt, MessageProcessingDisposition Disposition,
    MessageOutgoingRecord Handoff, string SafeCode);

/// <summary>Conditional handoff facts; Secured is not a commit confirmation.</summary>
/// <param name="Disposition">The winner of fresh state arbitration.</param>
/// <param name="Record">The observed retained processing state.</param>
/// <param name="HandoffRecordId">The secured outgoing record, when available.</param>
public sealed record MessageDispositionResult(MessageDispositionResultKind Disposition,
    MessageProcessingRecord? Record = null, Guid? HandoffRecordId = null);

/// <summary>A separate rejection identity, which cannot reserve or poison canonical processing.</summary>
/// <param name="RejectionId">A new rejection UUID.</param>
/// <param name="Application">The receiving application.</param>
/// <param name="Environment">The receiving environment.</param>
/// <param name="Subscription">The configured subscription.</param>
/// <param name="Message">Bounded parseable immutable bytes. Malformed/oversize input uses the safe native path.</param>
/// <param name="SafeCode">A fixed rejection reason.</param>
/// <param name="Quarantine">The atomic outgoing rejection handoff.</param>
/// <param name="Provenance">Actual evidence if established, never self-asserted identity.</param>
public sealed record MessageRejection(Guid RejectionId, string Application, string Environment, string Subscription,
    EncodedMessage Message, string SafeCode, MessageOutgoingRecord Quarantine, MessageProvenance? Provenance = null);

/// <summary>One explicit authorized terminal reopening, preserving logical identity and incrementing a generation.</summary>
/// <param name="Key">The retained failed logical delivery.</param>
/// <param name="ExpectedVersion">The exact inspected state version.</param>
/// <param name="Destination">The configured targeted consumer route.</param>
/// <param name="SupportedHorizon">The declared administrative replay horizon; null is indefinite while required protection remains.</param>
/// <param name="SafeCode">A bounded audited operator reason.</param>
public sealed record MessageRedriveTransition(MessageProcessingKey Key, long ExpectedVersion,
    MessageDestination Destination, TimeSpan? SupportedHorizon, string SafeCode);

/// <summary>An explicit redrive staging result, requiring its owner's actual completion.</summary>
/// <param name="Disposition">Eligibility and transition fact.</param>
/// <param name="Record">Current retained state, where available.</param>
/// <param name="HandoffRecordId">The new targeted outgoing row.</param>
public sealed record MessageRedriveResult(MessageRedriveDisposition Disposition,
    MessageProcessingRecord? Record = null, Guid? HandoffRecordId = null);

/// <summary>A bounded metadata-only administration page request, at most 256 rows.</summary>
/// <param name="Application">The authorized owning application.</param>
/// <param name="Environment">The authorized owning environment.</param>
/// <param name="MaximumRecords">A finite page limit.</param>
/// <param name="ProcessingKey">An optional exact logical consumer filter.</param>
/// <param name="RouteAlias">An optional registered logical route filter.</param>
/// <param name="AfterRecordId">An opaque metadata cursor, never payload.</param>
public sealed record MessageInspectionRequest(string Application, string Environment, int MaximumRecords = 256,
    MessageProcessingKey? ProcessingKey = null, string? RouteAlias = null, Guid? AfterRecordId = null);

/// <summary>Safe inspectable state without message payload or operation key/scope disclosure.</summary>
/// <param name="RecordId">The metadata record identity.</param>
/// <param name="MessageId">The logical message identity.</param>
/// <param name="Alias">A configured route or subscription alias.</param>
/// <param name="State">A finite store state name.</param>
/// <param name="UpdatedAtUtc">Database observation time.</param>
/// <param name="DeliveryOrdinal">The retained scheduled ordinal.</param>
/// <param name="Generation">The retained explicit generation.</param>
/// <param name="SafeCode">Bounded safe context.</param>
public sealed record MessageInspectionRecord(Guid RecordId, Guid MessageId, string Alias, string State,
    DateTimeOffset UpdatedAtUtc, int DeliveryOrdinal = 1, int Generation = 0, string? SafeCode = null);

/// <summary>A bounded explicit cleanup request; unresolved responsibility must never be removed.</summary>
/// <param name="Application">The authorized owner application.</param>
/// <param name="Environment">The authorized owner environment.</param>
/// <param name="Policy">Validated explicit retention and supported horizons.</param>
/// <param name="MaximumRecords">At most 256 total affected rows.</param>
public sealed record MessageMaintenanceRequest(string Application, string Environment,
    MessageRetentionPolicy Policy, int MaximumRecords = 256);

/// <summary>Candidate cleanup counts, confirmed only by the enclosing transaction's Committed result.</summary>
/// <param name="PayloadsCleared">Published/terminal payloads cleared while retaining required protection.</param>
/// <param name="ProcessingDeleted">Eligible complete protection rows removed beyond the supported horizon.</param>
/// <param name="TerminalDeleted">Eligible resolved terminal/rejection history removed.</param>
public sealed record MessageMaintenanceCounts(int PayloadsCleared, int ProcessingDeleted, int TerminalDeleted);

/// <summary>A finite backlog observation request, not a scrape-time callback.</summary>
/// <param name="Application">The captured owner application.</param>
/// <param name="Environment">The captured owner environment.</param>
/// <param name="MaximumRecords">A bounded query window, at most 256 plus one overflow witness.</param>
public sealed record MessageObservationRequest(string Application, string Environment, int MaximumRecords = 256);

/// <summary>A database-time backlog snapshot. Omit incomplete/unknown counts rather than exposing false zero.</summary>
/// <param name="ObservedAtUtc">Database observation time.</param>
/// <param name="PendingCount">Exact count only when known within the bounded observation.</param>
/// <param name="OldestAgeSeconds">Known nonnegative oldest eligible responsibility age, or absent.</param>
/// <param name="IsComplete">Whether the bounded count covered all matching responsibility.</param>
public sealed record MessageStoreObservation(DateTimeOffset ObservedAtUtc, long? PendingCount,
    double? OldestAgeSeconds, bool IsComplete);

/// <summary>Transaction-participating production inbox/outbox storage; it never owns completion or invokes handlers.</summary>
/// <remarks>Every method receives the original explicit context. Native callbacks remain sequential.
/// A successful mutation result means staged work only. Validate all bounded request fields before native work.</remarks>
public interface IMessagingStore
{
    /// <summary>Stages immutable outgoing responsibility through the original transaction.</summary>
    Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record,
        CancellationToken cancellationToken = default);
    /// <summary>Atomically reserves at most 32 metadata rows using database-time owner/version fencing.</summary>
    Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Loads one body only after owned-byte admission and matching owner/version/unexpired database lease.</summary>
    Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation,
        CancellationToken cancellationToken = default);
    /// <summary>Stages a conditional publication settlement; false means stale ownership.</summary>
    Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update,
        CancellationToken cancellationToken = default);
    /// <summary>Observes exact receipt identity/digest; a fresh independent read is required to infer committed submission.</summary>
    Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt,
        CancellationToken cancellationToken = default);
    /// <summary>Reads bounded retained metadata/provenance without materializing payloads.</summary>
    Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key,
        CancellationToken cancellationToken = default);
    /// <summary>Acquires unique logical identity before effects, comparing immutable content and trusted ordinal.</summary>
    Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt,
        CancellationToken cancellationToken = default);
    /// <summary>Stages successful processing and its already staged reply reference under the expected state.</summary>
    Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion,
        CancellationToken cancellationToken = default);
    /// <summary>Attaches an already staged reply only to its freshly secured same-owner Terminal or separate rejection anchor.</summary>
    Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment,
        CancellationToken cancellationToken = default);
    /// <summary>Reacquires expected failed state and atomically stages one targeted retry/terminal handoff; preserves gap winners.</summary>
    Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Stages separate rejection and quarantine responsibility without touching canonical processing.</summary>
    Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection,
        CancellationToken cancellationToken = default);
    /// <summary>Stages an explicit eligible terminal reopening and audited new generation.</summary>
    Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition,
        CancellationToken cancellationToken = default);
    /// <summary>Returns at most 256 safe metadata records, excluding payloads.</summary>
    Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Stages at most 256 eligible cleanup changes, preserving unresolved provenance/reply dependencies.</summary>
    Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Returns one bounded backlog snapshot for later in-memory metric observation.</summary>
    Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request,
        CancellationToken cancellationToken = default);
}
