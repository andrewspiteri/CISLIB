using CIS.Transactions;

namespace CIS.Operations;

/// <summary>Retention captured for one new completed row; null durations mean indefinite retention.</summary>
public sealed record OperationRetentionPolicy
{
    /// <summary>Validates positive optional durations without choosing a short cache TTL.</summary>
    public OperationRetentionPolicy(TimeSpan? resultRetention = null, TimeSpan? duplicateProtectionRetention = null)
    {
        if (resultRetention is { } result && result <= TimeSpan.Zero
            || duplicateProtectionRetention is { } protection && protection <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(resultRetention), "Finite retention durations must be positive.");
        }
        if (duplicateProtectionRetention is not null
            && (resultRetention is null || duplicateProtectionRetention < resultRetention))
        {
            throw new ArgumentException("Duplicate protection cannot expire before full-result retention.");
        }
        ResultRetention = resultRetention;
        DuplicateProtectionRetention = duplicateProtectionRetention;
    }

    /// <summary>Gets full-result retention from database recording time, or null for indefinite.</summary>
    public TimeSpan? ResultRetention
    {
        get;
    }
    /// <summary>Gets eligibility for full-row deletion; expiry alone never removes duplicate protection.</summary>
    public TimeSpan? DuplicateProtectionRetention
    {
        get;
    }
}

/// <summary>Immutable encoded terminal business data for a store adapter, without CLR type identity.</summary>
public sealed class OperationStoredResult
{
    private readonly byte[]? value;

    /// <summary>Copies a success payload or a final rejection, never both.</summary>
    public OperationStoredResult(OperationContract contract, OperationBusinessDecision decision,
        ReadOnlyMemory<byte>? value = null, OperationError? error = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision));
        }
        if (decision == OperationBusinessDecision.Succeeded ? value is null || error is not null : value is not null || error is null)
        {
            throw new ArgumentException("A stored success has bytes; a stored rejection has an application error.");
        }
        if (value is { } bytes && bytes.Length > 1_048_576)
        {
            throw new ArgumentException("A stored result payload cannot exceed one MiB.", nameof(value));
        }
        Contract = contract;
        Decision = decision;
        this.value = value?.ToArray();
        Error = error;
    }

    /// <summary>Gets the stable result contract and version.</summary>
    public OperationContract Contract
    {
        get;
    }
    /// <summary>Gets the independent business decision.</summary>
    public OperationBusinessDecision Decision
    {
        get;
    }
    /// <summary>Gets a detached success payload, or null for rejection.</summary>
    public ReadOnlyMemory<byte>? Value => value is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(value.ToArray());
    /// <summary>Gets the original bounded application rejection, or null.</summary>
    public OperationError? Error
    {
        get;
    }
    /// <summary>Gets the stored value or error UTF-8 byte count used by the operation's result-size limit.</summary>
    public int PayloadBytes => value?.Length
        ?? OperationText.Utf8Encoding.GetByteCount(Error!.Code) + OperationText.Utf8Encoding.GetByteCount(Error.Message);
}

/// <summary>Database-observed recording/expiry facts; RecordedAtUtc is not an observed commit time.</summary>
public sealed class OperationJournalTimes
{
    /// <summary>Copies UTC facts supplied by the authoritative backend.</summary>
    public OperationJournalTimes(DateTimeOffset observedAtUtc, DateTimeOffset? recordedAtUtc = null,
        DateTimeOffset? resultExpiresAtUtc = null, DateTimeOffset? protectionExpiresAtUtc = null)
    {
        if (protectionExpiresAtUtc is not null
            && (resultExpiresAtUtc is null || protectionExpiresAtUtc < resultExpiresAtUtc))
        {
            throw new ArgumentException("Protection expiry cannot precede payload expiry.");
        }
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        RecordedAtUtc = recordedAtUtc?.ToUniversalTime();
        ResultExpiresAtUtc = resultExpiresAtUtc?.ToUniversalTime();
        ProtectionExpiresAtUtc = protectionExpiresAtUtc?.ToUniversalTime();
    }

    /// <summary>Gets authoritative database time for this observation.</summary>
    public DateTimeOffset ObservedAtUtc
    {
        get;
    }
    /// <summary>Gets pre-commit database recording time, or null for an incomplete claim.</summary>
    public DateTimeOffset? RecordedAtUtc
    {
        get;
    }
    /// <summary>Gets logical full-result expiry, independent of whether maintenance cleared bytes.</summary>
    public DateTimeOffset? ResultExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets eligibility for explicit full-row deletion; protection continues until deletion.</summary>
    public DateTimeOffset? ProtectionExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets whether the full result has logically expired at the database observation time.</summary>
    public bool IsResultExpired => ResultExpiresAtUtc is { } expiry && expiry <= ObservedAtUtc;
}

/// <summary>A retained authoritative row or protection tombstone, without a claim that a caller's staged row committed.</summary>
public sealed class OperationJournalRecord
{
    /// <summary>Copies adapter facts. Corrupt or incomplete retained rows must block execution.</summary>
    public OperationJournalRecord(OperationIdentity identity, OperationFingerprint? fingerprint,
        OperationStoredResult? result, OperationJournalTimes times, bool isFinalized, bool isCorrupt = false)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(times);
        if (fingerprint is null && !isCorrupt)
        {
            throw new ArgumentException("A noncorrupt retained identity has its original fingerprint.", nameof(fingerprint));
        }
        Identity = identity;
        Fingerprint = fingerprint;
        Result = result;
        Times = times;
        IsFinalized = isFinalized;
        IsCorrupt = isCorrupt;
    }

    /// <summary>Gets the complete retained identity.</summary>
    public OperationIdentity Identity
    {
        get;
    }
    /// <summary>Gets the retained versioned fingerprint, including after payload expiry.</summary>
    public OperationFingerprint? Fingerprint
    {
        get;
    }
    /// <summary>Gets original encoded business data, or null for an expired tombstone/incomplete/corrupt row.</summary>
    public OperationStoredResult? Result
    {
        get;
    }
    /// <summary>Gets authoritative recording, observation and expiry facts.</summary>
    public OperationJournalTimes Times
    {
        get;
    }
    /// <summary>Gets whether finalization occurred; a committed incomplete row is never an execution invitation.</summary>
    public bool IsFinalized
    {
        get;
    }
    /// <summary>Gets whether the adapter detected retained data that it could not interpret safely.</summary>
    public bool IsCorrupt
    {
        get;
    }
}

/// <summary>The finite arbitration states returned only by a journal adapter.</summary>
public enum OperationClaimDisposition
{
    /// <summary>This transaction inserted its private uncommitted claim.</summary>
    Acquired,
    /// <summary>A subsequent authoritative statement observed the committed original row.</summary>
    Existing,
    /// <summary>The conflicting winner disappeared before its subsequent read; do not invoke the handler or loop.</summary>
    MissingAfterConflict,
    /// <summary>A known claim lock failure escaped native access and poisoned this transaction.</summary>
    Contended,
    /// <summary>A known claim serialization/deadlock failure escaped and requires a fresh transaction.</summary>
    RetryRequired
}

/// <summary>Adapter arbitration facts; this is not an application claim/complete lifecycle API.</summary>
public sealed class OperationClaim
{
    /// <summary>Creates an acquired/no-owner outcome or an existing committed row.</summary>
    public OperationClaim(OperationClaimDisposition disposition, OperationJournalRecord? existing = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == OperationClaimDisposition.Existing) != (existing is not null))
        {
            throw new ArgumentException("Only Existing carries a retained row.", nameof(existing));
        }
        Disposition = disposition;
        Existing = existing;
    }

    /// <summary>Gets the arbitration fact.</summary>
    public OperationClaimDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the existing row, including any retained incompatibility, or null.</summary>
    public OperationJournalRecord? Existing
    {
        get;
    }
}

/// <summary>Infrastructure extension port for the durable coordinator; applications use IDurableOperationService.</summary>
/// <remarks>Implementations use the supplied original context for all writes, await native settlement and retain no handles.
/// Only a committed prior row may be reported as Existing. No method commits/disposes an owner or application data source.</remarks>
public interface IOperationJournal
{
    /// <summary>Attempts one database-enforced claim, then separately observes a winner without invoking application work.</summary>
    Task<OperationClaim> TryClaimAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, CancellationToken cancellationToken = default);
    /// <summary>Reads through a coordinator-owned fresh authoritative context, never a caller's active write context.</summary>
    Task<OperationJournalRecord?> ReadAsync(ILocalTransactionContext context, OperationIdentity identity,
        CancellationToken cancellationToken = default);
    /// <summary>Finalizes exactly this transaction's incomplete claim once, preserving original identity/fingerprint.</summary>
    Task<OperationJournalTimes> WriteCompletedAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, OperationStoredResult result, OperationRetentionPolicy retention,
        CancellationToken cancellationToken = default);
}
