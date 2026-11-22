namespace CIS.Coordination;

/// <summary>The local managed lifecycle; local state is not a substitute for protected-resource enforcement.</summary>
public enum CoordinationLeaseState
{
    /// <summary>An unpublished acquisition is being resolved.</summary>
    Acquiring,
    /// <summary>The local confirmed conservative ownership budget permits new admission.</summary>
    Owned,
    /// <summary>New business admission is closed while admitted work may finish.</summary>
    Draining,
    /// <summary>Authority has been lost permanently for this handle.</summary>
    Lost,
    /// <summary>The handle was locally released permanently, independently of remote release certainty.</summary>
    Released
}

/// <summary>Finite terminal loss reasons, also suitable for bounded diagnostics.</summary>
public enum CoordinationLossReason
{
    /// <summary>The confirmed conservative validity expired.</summary>
    Expired,
    /// <summary>The authority explicitly rejected the old grant.</summary>
    Superseded,
    /// <summary>No renewal was confirmed before the local stop deadline.</summary>
    RenewalDeadlineExceeded,
    /// <summary>The authority could not establish continued ownership.</summary>
    AuthorityUnavailable,
    /// <summary>Admitted work exceeded the finite graceful drain budget.</summary>
    ShutdownDeadlineExceeded,
    /// <summary>The acquiring caller abandoned its unpublished attempt.</summary>
    CallerCancelled
}

/// <summary>A once-only local terminal transition, not proof of business rollback or callback completion.</summary>
public sealed class CoordinationLeaseTermination
{
    /// <summary>Creates either permanent loss with a reason or normal local release.</summary>
    public CoordinationLeaseTermination(CoordinationLeaseState state, CoordinationLossReason? lossReason = null)
    {
        if (state is not (CoordinationLeaseState.Lost or CoordinationLeaseState.Released)
            || (state == CoordinationLeaseState.Lost) != lossReason.HasValue
            || (lossReason.HasValue && !Enum.IsDefined(lossReason.Value)))
        {
            throw new ArgumentException("Supply a terminal loss with a reason or a normal release.");
        }
        State = state;
        LossReason = lossReason;
    }

    /// <summary>Gets Lost or Released.</summary>
    public CoordinationLeaseState State
    {
        get;
    }
    /// <summary>Gets the stable reason on Lost.</summary>
    public CoordinationLossReason? LossReason
    {
        get;
    }
}

/// <summary>One managed authority handle, with no unrestricted native database access.</summary>
/// <remarks>Direct consumers own and drain their business tasks/scopes before release. Cancellation cannot physically stop them.</remarks>
public interface ICoordinationLease : IAsyncDisposable
{
    /// <summary>Gets the latest immutable confirmed grant observation; guard it in the original business transaction.</summary>
    CoordinationGrant Grant
    {
        get;
    }
    /// <summary>Gets synchronized local lifecycle state, conservatively checking its monotonic deadline.</summary>
    CoordinationLeaseState State
    {
        get;
    }
    /// <summary>Gets the stable terminal loss reason, if any.</summary>
    CoordinationLossReason? LossReason
    {
        get;
    }
    /// <summary>Signals cooperative stopping when ownership ends; cancellation does not prove work settled.</summary>
    CancellationToken OwnershipLost
    {
        get;
    }
    /// <summary>Observes the once-only terminal state, not an arbitrary consumer callback drain.</summary>
    Task<CoordinationLeaseTermination> Termination
    {
        get;
    }
    /// <summary>Closes new admission while allowing bounded renewal for work already admitted.</summary>
    void BeginDraining(TimeSpan? drainBudget = null);
    /// <summary>Releases this exact grant after the consumer has actually drained its work; local release is terminal even if remote uncertainty remains.</summary>
    Task<CoordinationMutationResult> ReleaseAsync(CancellationToken cancellationToken = default);
}
