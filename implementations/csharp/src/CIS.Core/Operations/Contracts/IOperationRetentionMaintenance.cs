using CIS.Transactions;

namespace CIS.Operations;

/// <summary>One explicitly requested privileged retention batch for one operation and application-resolved scope.</summary>
public sealed class OperationMaintenanceRequest
{
    /// <summary>Gets the largest supported single batch, without selecting a schedule or loop.</summary>
    public const int MaximumBatchSize = 256;

    /// <summary>Captures bounded requested metadata. RequestedScope grants no authority.</summary>
    public OperationMaintenanceRequest(string operationName, string requestedScope, int maximumRows = MaximumBatchSize)
    {
        OperationName = OperationText.Alias(operationName, nameof(operationName));
        RequestedScope = OperationText.Utf8(requestedScope, 256, nameof(requestedScope));
        if (maximumRows is < 1 or > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }
        MaximumRows = maximumRows;
    }

    /// <summary>Gets the exact registered operation alias.</summary>
    public string OperationName
    {
        get;
    }

    /// <summary>Gets requested scope text that the application must resolve and authorize.</summary>
    public string RequestedScope
    {
        get;
    }

    /// <summary>Gets this batch's positive row bound, at most256.</summary>
    public int MaximumRows
    {
        get;
    }
}

/// <summary>The maintenance call's admission fact, independently of actual database persistence.</summary>
public enum OperationMaintenanceDisposition
{
    /// <summary>The authorized batch settled its own transaction; inspect Completion for persistence.</summary>
    Executed,
    /// <summary>The complete capability/target check denied maintenance; no counts are disclosed.</summary>
    Denied,
    /// <summary>No maintenance capabilities were configured; no counts are disclosed.</summary>
    Disabled
}

/// <summary>Candidate batch changes with actual RM-02 completion, never a fabricated committed deletion count.</summary>
public sealed class OperationMaintenanceResult
{
    /// <summary>Creates either a nondisclosing admission result or bounded candidate changes with their actual completion.</summary>
    public OperationMaintenanceResult(OperationMaintenanceDisposition disposition, int? candidatePayloadsCleared = null,
        int? candidateRowsDeleted = null, TransactionCompletion? completion = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (disposition == OperationMaintenanceDisposition.Executed)
        {
            if (candidatePayloadsCleared is null || candidateRowsDeleted is null || completion is null
                || candidatePayloadsCleared < 0 || candidateRowsDeleted < 0
                || candidatePayloadsCleared > OperationMaintenanceRequest.MaximumBatchSize
                || candidateRowsDeleted > OperationMaintenanceRequest.MaximumBatchSize
                || candidatePayloadsCleared + candidateRowsDeleted > OperationMaintenanceRequest.MaximumBatchSize)
            {
                throw new ArgumentException("An executed batch carries bounded candidate changes and its actual transaction completion.");
            }
        }
        else if (candidatePayloadsCleared is not null || candidateRowsDeleted is not null || completion is not null)
        {
            throw new ArgumentException("Denied or disabled maintenance discloses no database counts or completion.");
        }
        Disposition = disposition;
        CandidatePayloadsCleared = candidatePayloadsCleared;
        CandidateRowsDeleted = candidateRowsDeleted;
        Completion = completion;
    }

    /// <summary>Gets whether this call executed, was denied or was disabled.</summary>
    public OperationMaintenanceDisposition Disposition
    {
        get;
    }

    /// <summary>Gets staged payload-clear count, or null when the call did not execute.</summary>
    public int? CandidatePayloadsCleared
    {
        get;
    }

    /// <summary>Gets staged whole-row deletion count, or null when the call did not execute.</summary>
    public int? CandidateRowsDeleted
    {
        get;
    }

    /// <summary>Gets the owned batch's actual completion, including Unknown and cleanup diagnostics.</summary>
    public TransactionCompletion? Completion
    {
        get;
    }

    /// <summary>Gets a confirmed payload-clear count only when the owned batch is known Committed.</summary>
    public int? ConfirmedPayloadsCleared => Completion?.Outcome == TransactionOutcome.Committed ? CandidatePayloadsCleared : null;

    /// <summary>Gets a confirmed deletion count only when the owned batch is known Committed.</summary>
    public int? ConfirmedRowsDeleted => Completion?.Outcome == TransactionOutcome.Committed ? CandidateRowsDeleted : null;
}

/// <summary>Privileged explicitly invoked maintenance, separate from application execution and result lookup.</summary>
public interface IOperationRetentionMaintenance
{
    /// <summary>Authorizes one scope before SQL or counts, then settles at most one bounded batch in its own transaction.</summary>
    Task<OperationMaintenanceResult> RunAsync(OperationMaintenanceRequest request, CancellationToken cancellationToken = default);
}
