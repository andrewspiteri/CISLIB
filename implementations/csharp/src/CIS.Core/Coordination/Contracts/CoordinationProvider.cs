using CIS.Transactions;

namespace CIS.Coordination;

/// <summary>A requested duration for one exact fresh acquisition, never an instruction to replay a prior mutation.</summary>
public sealed class CoordinationAcquireRequest
{
    /// <summary>Copies the immutable acquisition and finite requested duration.</summary>
    public CoordinationAcquireRequest(CoordinationAcquisition acquisition, TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        Acquisition = acquisition;
        LeaseDuration = CoordinationText.Duration(leaseDuration, nameof(leaseDuration));
    }

    /// <summary>Gets the detached authorized acquisition identity.</summary>
    public CoordinationAcquisition Acquisition
    {
        get;
    }
    /// <summary>Gets requested authority validity.</summary>
    public TimeSpan LeaseDuration
    {
        get;
    }
}

/// <summary>Physical authority operations. Callers own authorization, orchestration and unresolved-operation observation.</summary>
public interface ICoordinationProvider
{
    /// <summary>Gets the immutable installed authority selected by this provider.</summary>
    Guid AuthorityId
    {
        get;
    }
    /// <summary>Validates required authority/schema availability without installing or migrating it.</summary>
    Task ValidateAsync(CancellationToken cancellationToken = default);
    /// <summary>Attempts one atomic fresh acquisition; usable success requires committed authority state.</summary>
    Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default);
    /// <summary>Renews only this exact current, unexpired grant and preserves its generation.</summary>
    /// <remarks>Succeeded requires a matching grant and positive confirmed remaining validity; a grant-free success is valid only for release/prune.</remarks>
    Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    /// <summary>Conditionally releases only this exact grant; a successor cannot be released by an old request.</summary>
    Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default);
    /// <summary>Observes this exact acquisition. NotObserved does not prove an earlier mutation cannot still commit.</summary>
    Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default);
}

/// <summary>Enforces ownership through the application's original explicit transaction.</summary>
public interface ICoordinationGuard
{
    /// <summary>Retains the ownership-row lock through application completion; rejection marks that context rollback-only.</summary>
    Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant,
        CancellationToken cancellationToken = default);
}
