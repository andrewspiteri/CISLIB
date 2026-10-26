namespace CIS.Healthcheck.Internal;

/// <summary>Atomic collection coordination implemented by the sole monitor-state owner.</summary>
/// <remarks>No method invokes collector, lease, runner, or owner callbacks while holding a state gate. Cancellation is signaled outside that gate.</remarks>
internal interface IHealthCollectionState
{
    /// <summary>Signals terminal service stopping outside the state gate.</summary>
    CancellationToken Stopping { get; }

    /// <summary>Validates and atomically attaches one collection registration to an active local monitor.</summary>
    /// <remarks>Rejects stopping state, duplicate attachment, invalid options, or foreign/withdrawn monitors without partial attachment.</remarks>
    HealthCollectionTarget RegisterCollection(
        Guid monitorId,
        Guid collectionRegistrationId,
        HealthCollectorOptions options);

    /// <summary>Reserves one tracked wrapper and its observation baselines, joins its existing identity, or reports retirement.</summary>
    /// <remarks>The runner owns the shared operation task keyed by OperationId and reserves global capacity separately. No owner code runs here.</remarks>
    HealthCollectionReservationResult TryReserve(HealthCollectionTarget target);

    /// <summary>Atomically enters the owner-invocation boundary against withdrawal and application stopping.</summary>
    bool TryEnter(HealthCollectionReservation reservation);

    /// <summary>Atomically applies fresh policy and per-key measurement observations or classifies an obsolete or retired result.</summary>
    /// <remarks>Lifecycle bookkeeping does not change observation baselines; stale facts cannot recreate monitor state.</remarks>
    HealthCollectionAttempt CommitResult(HealthCollectionReservation reservation, HealthCollectionResult result);

    /// <summary>Records one fresh admitted evaluation failure, or only an optional collection error for an optional registration.</summary>
    /// <remarks>Expected lifetime cancellation and lease-acquisition errors do not enter this operation.</remarks>
    HealthCollectionAttempt CommitEvaluationFailure(HealthCollectionReservation reservation, HealthDiagnostic diagnostic);

    /// <summary>Settles wrapper bookkeeping exactly once after actual callback and acquired-lease disposal have settled.</summary>
    /// <remarks>OwnerUnavailable retires collection and withdraws the monitor without a policy failure. An acquisition fault records an integration error without inventing a callback outcome. The runner releases capacity only after this call and the operation's actual settlement barrier.</remarks>
    HealthCollectionAttempt Complete(HealthCollectionReservation reservation, HealthCollectionCompletion completion);

    /// <summary>Gets the shared actual-settlement barrier for a state-issued operation.</summary>
    /// <remarks>Completion includes cancellation callbacks as well as wrapper and lease cleanup. Waiting does not initiate retirement or cancel the operation.</remarks>
    Task WaitForSettlementAsync(HealthCollectionReservation reservation);

    /// <summary>Closes collection admission, signals lifetime cancellation outside the state gate, and returns its shared actual-settlement barrier.</summary>
    /// <remarks>Does not withdraw an externally owned monitor. Canceling a caller wait must not cancel this barrier.</remarks>
    Task WithdrawCollection(HealthCollectionTarget target);
}
