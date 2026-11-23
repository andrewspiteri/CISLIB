using CIS.Transactions;

namespace CIS.Management.Service.Persistence;

/// <summary>Separates existing receipts or committed-state reads from this owner's actual completion and safe refusal.</summary>
public sealed record ManagementStoreResult<T>(T? Value, string? Code, TransactionCompletion? AttemptCompletion,
    bool PreviouslyCommitted, bool HasMore = false)
{
    /// <summary>Gets whether the returned value is established independently of current read cleanup.</summary>
    public bool IsKnownCommitted => Value is not null && Code is null
        && (PreviouslyCommitted || AttemptCompletion?.Outcome == TransactionOutcome.Committed);
}

/// <summary>A completed bounded telemetry cleanup, never an operation-journal purge.</summary>
public sealed record ManagementCleanupResult(int RemovedRegistrations, long ReleasedInventoryBytes);

/// <summary>Safe explicit deployment/schema readiness failure.</summary>
public sealed class ManagementSchemaException(string message) : InvalidOperationException(message)
{
}
