namespace CIS.Healthcheck;

/// <summary>Provides nonblocking, owner-controlled admission to one health evaluation.</summary>
public interface IHealthCollector
{
    /// <summary>Acquires owner protection, returning a nonnull lease on success and null on refusal.</summary>
    /// <remarks>The lease prevents disposal of the owner's dependencies until its asynchronous disposal settles.</remarks>
    bool TryAcquire(out IHealthCollectionLease? lease);
}

/// <summary>Protects owner dependencies while an admitted evaluation and its cleanup actually settle.</summary>
public interface IHealthCollectionLease : IAsyncDisposable
{
    /// <summary>Evaluates health using the registration's lifetime cancellation, independently of caller waits.</summary>
    Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation);
}

/// <summary>A single collector registration associated with an externally owned monitor.</summary>
public interface IHealthCollectionRegistration : IAsyncDisposable
{
    /// <summary>Gets the associated monitor identity, including after withdrawal.</summary>
    Guid MonitorId { get; }

    /// <summary>Enables recurring collection after the runner starts; requires a configured interval.</summary>
    void ActivatePeriodic();

    /// <summary>Starts or joins one actual evaluation; cancellation affects only this caller's wait.</summary>
    /// <remarks>A pre-canceled wait does not start a new evaluation. Completion does not imply healthy state.</remarks>
    Task<HealthCollectionAttempt> CollectAsync(CancellationToken waitCancellation = default);

    /// <summary>Closes collection admission without withdrawing the associated monitor.</summary>
    void Withdraw();

    /// <summary>Waits for withdrawal and actual acquired-lease settlement without initiating withdrawal.</summary>
    Task WaitForDisposalAsync(CancellationToken waitCancellation = default);
}
