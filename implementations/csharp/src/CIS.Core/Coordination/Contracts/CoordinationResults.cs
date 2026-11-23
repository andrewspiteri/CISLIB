namespace CIS.Coordination;

/// <summary>Distinct acquisition facts; contention cannot conceal an uncertain mutation.</summary>
public enum CoordinationAcquireOutcome
{
    /// <summary>A usable grant was committed and confirmed.</summary>
    Acquired,
    /// <summary>Another current grant exists.</summary>
    Busy,
    /// <summary>The actual caller lacks permission.</summary>
    Unauthorized,
    /// <summary>The requested target is not supported or valid.</summary>
    InvalidRequest,
    /// <summary>The acquisition is known not to have committed.</summary>
    Unavailable,
    /// <summary>A bounded local resource cannot admit this attempt.</summary>
    CapacityExceeded,
    /// <summary>The mutation may have committed and cannot be replayed blindly.</summary>
    Indeterminate
}

/// <summary>Finite exact-grant mutation facts.</summary>
public enum CoordinationMutationOutcome
{
    /// <summary>The exact mutation committed.</summary>
    Succeeded,
    /// <summary>The supplied grant is no longer current or live.</summary>
    NotCurrent,
    /// <summary>This mutation is known not to have committed.</summary>
    Unavailable,
    /// <summary>The mutation may have committed.</summary>
    Indeterminate
}

/// <summary>Finite reasons for refusing protected admission or an exact-grant mutation.</summary>
public enum CoordinationRejectionReason
{
    /// <summary>No refusal applies.</summary>
    None,
    /// <summary>The ownership row is absent.</summary>
    Missing,
    /// <summary>The exact grant was released.</summary>
    Released,
    /// <summary>Authority time has reached expiry.</summary>
    Expired,
    /// <summary>A different owner, acquisition or generation is current.</summary>
    Superseded,
    /// <summary>The grant belongs to another installed authority.</summary>
    WrongAuthority,
    /// <summary>The original transaction uses another database alias.</summary>
    WrongDatabase,
    /// <summary>The transaction cannot support the required guard.</summary>
    InvalidTransaction,
    /// <summary>The authority could not establish admission.</summary>
    AuthorityUnavailable
}

/// <summary>A physical acquire result, distinct from a managed ownership handle.</summary>
public sealed class CoordinationAcquireResult
{
    /// <summary>Enforces that only Acquired carries a positive usable grant acknowledgement.</summary>
    public CoordinationAcquireResult(CoordinationAcquireOutcome outcome, CoordinationGrant? grant = null,
        TimeSpan remainingValidity = default, string? reasonCode = null, CoordinationOwnerObservation? observedOwner = null)
    {
        if (!Enum.IsDefined(outcome) || (outcome == CoordinationAcquireOutcome.Acquired) != (grant is not null)
            || (grant is not null ? remainingValidity <= TimeSpan.Zero : remainingValidity != TimeSpan.Zero)
            || (observedOwner is not null && outcome != CoordinationAcquireOutcome.Busy))
        {
            throw new ArgumentException("Only a confirmed acquisition carries a grant and positive remaining validity.");
        }
        Outcome = outcome;
        Grant = grant;
        RemainingValidity = CoordinationText.Duration(remainingValidity, nameof(remainingValidity), allowZero: true);
        ReasonCode = CoordinationText.Code(reasonCode);
        ObservedOwner = observedOwner;
    }

    /// <summary>Gets the actual acquisition disposition.</summary>
    public CoordinationAcquireOutcome Outcome
    {
        get;
    }
    /// <summary>Gets the confirmed grant only on Acquired.</summary>
    public CoordinationGrant? Grant
    {
        get;
    }
    /// <summary>Gets authority-observed remaining validity; clients must subtract request elapsed time and their safety margin.</summary>
    public TimeSpan RemainingValidity
    {
        get;
    }
    /// <summary>Gets a bounded stable diagnostic code without native exception/connection data.</summary>
    public string? ReasonCode
    {
        get;
    }
    /// <summary>Gets an optional Busy observation, never an ownership handle or authorization for later work.</summary>
    public CoordinationOwnerObservation? ObservedOwner
    {
        get;
    }
}

/// <summary>An exact renewal/release/prune fact. A successful release has no renewed grant.</summary>
public sealed class CoordinationMutationResult
{
    /// <summary>Copies exact mutation evidence without inferring anything about a business transaction.</summary>
    public CoordinationMutationResult(CoordinationMutationOutcome outcome, CoordinationGrant? grant = null,
        TimeSpan remainingValidity = default, CoordinationRejectionReason rejectionReason = CoordinationRejectionReason.None,
        string? reasonCode = null)
    {
        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(rejectionReason)
            || (grant is not null && (outcome != CoordinationMutationOutcome.Succeeded || remainingValidity <= TimeSpan.Zero))
            || (grant is null && remainingValidity != TimeSpan.Zero)
            || (outcome == CoordinationMutationOutcome.NotCurrent
                ? rejectionReason is not (CoordinationRejectionReason.Missing or CoordinationRejectionReason.Released
                    or CoordinationRejectionReason.Expired or CoordinationRejectionReason.Superseded or CoordinationRejectionReason.WrongAuthority)
                : rejectionReason != CoordinationRejectionReason.None))
        {
            throw new ArgumentException("Supply consistent exact-grant mutation evidence.");
        }
        Outcome = outcome;
        Grant = grant;
        RemainingValidity = CoordinationText.Duration(remainingValidity, nameof(remainingValidity), allowZero: true);
        RejectionReason = rejectionReason;
        ReasonCode = CoordinationText.Code(reasonCode);
    }

    /// <summary>Gets the committed, refused or uncertain mutation disposition.</summary>
    public CoordinationMutationOutcome Outcome
    {
        get;
    }
    /// <summary>Gets updated expiry for a confirmed renewal; renewal callers must require this and exact matching immutable identity.</summary>
    public CoordinationGrant? Grant
    {
        get;
    }
    /// <summary>Gets confirmed remaining validity only for a renewal.</summary>
    public TimeSpan RemainingValidity
    {
        get;
    }
    /// <summary>Gets the finite NotCurrent reason.</summary>
    public CoordinationRejectionReason RejectionReason
    {
        get;
    }
    /// <summary>Gets an optional bounded safe code.</summary>
    public string? ReasonCode
    {
        get;
    }
}

/// <summary>Exact-acquisition observation, not a proof that an unobserved mutation cannot commit later.</summary>
public enum CoordinationObservationOutcome
{
    /// <summary>The exact acquisition is current, committed and live.</summary>
    FoundLive,
    /// <summary>No exact live acquisition was observed at this instant.</summary>
    NotObserved,
    /// <summary>The authority could not be read.</summary>
    Unavailable
}

/// <summary>Authoritative exact-acquisition read evidence.</summary>
public sealed class CoordinationObservation
{
    /// <summary>Enforces positive acknowledgement data only for FoundLive.</summary>
    public CoordinationObservation(CoordinationObservationOutcome outcome, CoordinationGrant? grant = null,
        TimeSpan remainingValidity = default)
    {
        if (!Enum.IsDefined(outcome) || (outcome == CoordinationObservationOutcome.FoundLive) != (grant is not null)
            || (grant is not null ? remainingValidity <= TimeSpan.Zero : remainingValidity != TimeSpan.Zero))
        {
            throw new ArgumentException("Only an exact live observation carries a grant and remaining validity.");
        }
        Outcome = outcome;
        Grant = grant;
        RemainingValidity = CoordinationText.Duration(remainingValidity, nameof(remainingValidity), allowZero: true);
    }

    /// <summary>Gets this read's disposition.</summary>
    public CoordinationObservationOutcome Outcome
    {
        get;
    }
    /// <summary>Gets the exact observed grant.</summary>
    public CoordinationGrant? Grant
    {
        get;
    }
    /// <summary>Gets observed remaining validity, subject to conservative request timing.</summary>
    public TimeSpan RemainingValidity
    {
        get;
    }
}

/// <summary>Same-transaction admission evidence, with no transaction completion ownership.</summary>
public sealed class CoordinationGuardResult
{
    /// <summary>Creates admission or a finite rejection.</summary>
    public CoordinationGuardResult(CoordinationRejectionReason rejectionReason = CoordinationRejectionReason.None)
    {
        if (!Enum.IsDefined(rejectionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(rejectionReason));
        }
        RejectionReason = rejectionReason;
    }

    /// <summary>Gets whether the exact current grant was admitted under its retained row lock.</summary>
    public bool Admitted => RejectionReason == CoordinationRejectionReason.None;
    /// <summary>Gets the finite refusal; a provider must also mark the original context rollback-only.</summary>
    public CoordinationRejectionReason RejectionReason
    {
        get;
    }

    /// <summary>Allows an application handler to unwind its owned transaction after inspecting a structured rejection.</summary>
    public void ThrowIfRejected()
    {
        if (!Admitted)
        {
            throw new CoordinationGuardRejectedException(RejectionReason);
        }
    }
}

/// <summary>An explicit guard rejection for application orchestration; it does not complete or retry a transaction.</summary>
public sealed class CoordinationGuardRejectedException : InvalidOperationException
{
    /// <summary>Retains the finite guard refusal.</summary>
    public CoordinationGuardRejectedException(CoordinationRejectionReason reason)
        : base("Protected work was refused by the coordination ownership guard.")
    {
        if (reason == CoordinationRejectionReason.None || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        Reason = reason;
    }

    /// <summary>Gets the actual guard reason.</summary>
    public CoordinationRejectionReason Reason
    {
        get;
    }
}
