using CIS.Messaging;
using CIS.Transactions;

namespace CIS.Audit;

/// <summary>Local staging admission, without a commit, broker or receiver claim.</summary>
public enum AuditStageDisposition
{
    /// <summary>Outgoing responsibility was staged in the caller's transaction.</summary>
    Staged,
    /// <summary>Finite local capacity prevented staging.</summary>
    Overloaded,
    /// <summary>Configuration or lifecycle prevented staging.</summary>
    Unavailable
}

/// <summary>Known source submission persistence, independent of the observed business outcome.</summary>
public enum AuditSubmissionOutcome
{
    /// <summary>The submission is known not to have committed.</summary>
    NotSubmitted,
    /// <summary>The outgoing record is known committed.</summary>
    OutboxCommitted,
    /// <summary>Submission commit may have occurred.</summary>
    Unknown
}

/// <summary>An Audit occurrence linked to the exact underlying staging facts.</summary>
public sealed class AuditStageResult
{
    /// <summary>Preserves the actual immutable Messaging result without completing the caller's owner.</summary>
    public AuditStageResult(Guid auditId, MessageStageResult staging)
    {
        if (auditId == Guid.Empty)
        {
            throw new ArgumentException("An audit occurrence has a nonempty identity.", nameof(auditId));
        }
        ArgumentNullException.ThrowIfNull(staging);

        AuditId = auditId;
        Staging = staging;
        Disposition = staging.Disposition switch
        {
            MessageStageDisposition.Staged => AuditStageDisposition.Staged,
            MessageStageDisposition.Overloaded => AuditStageDisposition.Overloaded,
            MessageStageDisposition.Unavailable => AuditStageDisposition.Unavailable,
            _ => throw new ArgumentException("Use a declared staging disposition.", nameof(staging))
        };
    }

    /// <summary>Gets the immutable occurrence identity, distinct from the transport identity.</summary>
    public Guid AuditId
    {
        get;
    }

    /// <summary>Gets local admission only.</summary>
    public AuditStageDisposition Disposition
    {
        get;
    }

    /// <summary>Gets the exact actual Messaging result.</summary>
    public MessageStageResult Staging
    {
        get;
    }

    /// <summary>Gets available transport identities without asserting commit.</summary>
    public MessageStageReceipt? Receipt => Staging.Receipt;

    /// <summary>Gets safe bounded failure context.</summary>
    public MessageError? Error => Staging.Error;
}

/// <summary>An Audit occurrence linked to the exact underlying submission and cleanup facts.</summary>
public sealed class AuditSubmissionResult
{
    /// <summary>Preserves the actual three-state submission; provisional request-ticket states are invalid here.</summary>
    public AuditSubmissionResult(Guid auditId, MessageSubmission submission)
    {
        if (auditId == Guid.Empty)
        {
            throw new ArgumentException("An audit occurrence has a nonempty identity.", nameof(auditId));
        }
        ArgumentNullException.ThrowIfNull(submission);

        Outcome = submission.Outcome switch
        {
            MessageSubmissionOutcome.NotSubmitted => AuditSubmissionOutcome.NotSubmitted,
            MessageSubmissionOutcome.OutboxCommitted => AuditSubmissionOutcome.OutboxCommitted,
            MessageSubmissionOutcome.Unknown => AuditSubmissionOutcome.Unknown,
            _ => throw new ArgumentException("An Audit submission requires actual standalone submission facts.", nameof(submission))
        };
        AuditId = auditId;
        Submission = submission;
    }

    /// <summary>Gets the immutable occurrence identity.</summary>
    public Guid AuditId
    {
        get;
    }

    /// <summary>Gets known submission persistence only.</summary>
    public AuditSubmissionOutcome Outcome
    {
        get;
    }

    /// <summary>Gets the exact underlying result, including cleanup facts.</summary>
    public MessageSubmission Submission
    {
        get;
    }

    /// <summary>Gets available transport identities, including after an uncertain commit.</summary>
    public MessageStageReceipt? Receipt => Submission.Receipt;

    /// <summary>Gets actual transaction completion where the underlying operation supplied it.</summary>
    public TransactionCompletion? Completion => Submission.Completion;

    /// <summary>Gets safe bounded failure context.</summary>
    public MessageError? Error => Submission.Error;
}

/// <summary>Explicit publication of locally captured Audit occurrences through existing Messaging.</summary>
public interface IAuditWriter
{
    /// <summary>Stages using the original participant; Audit-side refusal or fault prohibits a usable owner's commit.</summary>
    Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>Owns no business transaction; preserves actual Messaging submission and cleanup facts.</summary>
    Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default);
}
