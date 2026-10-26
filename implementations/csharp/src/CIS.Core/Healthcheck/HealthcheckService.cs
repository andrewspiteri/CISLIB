using CIS.Healthcheck.Internal;

namespace CIS.Healthcheck;

/// <summary>Owns application health facts and independent, explicitly retained monitor lifetimes.</summary>
/// <remarks>Construction starts no collectors, application work or reporting endpoints.</remarks>
public sealed class HealthcheckService : IHealthMonitorFactory, IHealthSnapshotSource, IAsyncDisposable, IHealthStateAccess
{
    private readonly HealthStateCoordinator state;

    /// <summary>Validates and copies settings and identity without reading module environment overrides.</summary>
    public HealthcheckService(HealthcheckOptions options, HealthApplicationIdentity? application = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        state = new HealthStateCoordinator(options.ValidatedCopy(), application, timeProvider ?? TimeProvider.System);
    }

    /// <summary>Registers an explicit condition with its owner-selected initial flag.</summary>
    public IConditionMonitor CreateCondition(ConditionMonitorOptions options) => (IConditionMonitor)state.Create(options);
    /// <summary>Registers a pending consecutive-failure policy.</summary>
    public IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options) => (IConsecutiveFailureMonitor)state.Create(options);
    /// <summary>Registers a pending finite numeric comparison.</summary>
    public INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options) => (INumericThresholdMonitor)state.Create(options);
    /// <summary>Registers a new availability lifetime without inherited usable data.</summary>
    public IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options) => (IAvailabilityMonitor)state.Create(options);
    /// <summary>Copies one coherent observation without invoking a collector.</summary>
    public HealthSnapshot CaptureSnapshot() => state.CaptureSnapshot();
    /// <summary>Marks explicit startup success once; rejects terminal stopping.</summary>
    public void MarkStartupCompleted() => state.MarkStartupCompleted();
    /// <summary>Closes new admission and readiness while allowing final direct observations.</summary>
    public void BeginStopping() => state.BeginStopping();
    /// <summary>Waits for disposal without initiating it or canceling its shared completion.</summary>
    public Task WaitForDisposalAsync(CancellationToken waitCancellation = default)
    {
        HealthCollectionExecution.ThrowIfSelfWait(state);
        return state.Disposal.WaitAsync(waitCancellation);
    }
    /// <summary>Withdraws all monitors and waits for their actual reserved work and cancellation callbacks.</summary>
    public ValueTask DisposeAsync()
    {
        Task settlement = state.Dispose();
        HealthCollectionExecution.ThrowIfSelfWait(state);
        return new(settlement);
    }

    IHealthCollectionState IHealthStateAccess.CollectionState => state;
    IHealthExecutionState IHealthStateAccess.ExecutionState => state;

    internal HealthcheckOptions Settings => state.Options;
    internal TimeProvider Clock => state.Clock;
    internal void ClaimCollectorRunner() => state.ClaimCollectorRunner();
}
