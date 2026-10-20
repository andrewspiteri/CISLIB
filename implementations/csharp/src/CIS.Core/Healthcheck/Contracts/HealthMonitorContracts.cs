namespace CIS.Healthcheck;

/// <summary>Captures a detached, coherent health observation without running collectors.</summary>
public interface IHealthSnapshotSource
{
    /// <summary>Captures application facts, aggregate health and complete ordered monitor details.</summary>
    HealthSnapshot CaptureSnapshot();
}

/// <summary>Registers distinct monitor instances with explicitly selected policies.</summary>
public interface IHealthMonitorFactory
{
    /// <summary>Registers an explicitly flagged or cleared condition.</summary>
    IConditionMonitor CreateCondition(ConditionMonitorOptions options);

    /// <summary>Registers a consecutive-failure policy with an explicit threshold.</summary>
    IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options);

    /// <summary>Registers a finite numeric threshold policy.</summary>
    INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options);

    /// <summary>Registers availability that remains pending until its first observation.</summary>
    IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options);
}

/// <summary>An owner-retained registration whose withdrawal removes its health contribution.</summary>
/// <remarks>Identity getters remain available after withdrawal. Reporting after withdrawal throws <see cref="ObjectDisposedException"/>.</remarks>
public interface IHealthMonitor : IAsyncDisposable
{
    /// <summary>The opaque, unique identity assigned at registration.</summary>
    Guid MonitorId { get; }

    /// <summary>The optional display name; it is not registration identity.</summary>
    string? Name { get; }

    /// <summary>Whether this monitor's unhealthy state affects aggregate health.</summary>
    bool IsCritical { get; }

    /// <summary>Reports an optional diagnostic measurement without changing policy health.</summary>
    /// <remarks>The measurement must have <see cref="HealthMeasurement.IsDiagnostic"/> set to true.</remarks>
    void ReportMeasurement(HealthMeasurement measurement);

    /// <summary>Immediately and idempotently removes the contribution and closes collection admission.</summary>
    void Withdraw();

    /// <summary>Waits for the shared owned-work disposal barrier without initiating withdrawal.</summary>
    /// <remarks>Cancellation cancels only this caller's wait.</remarks>
    Task WaitForDisposalAsync(CancellationToken waitCancellation = default);
}

/// <summary>An explicitly flagged or cleared condition.</summary>
public interface IConditionMonitor : IHealthMonitor
{
    /// <summary>Marks this condition unhealthy with an optional detached reason.</summary>
    void Flag(HealthDiagnostic? diagnostic = null);

    /// <summary>Marks this condition healthy and clears its current diagnostic.</summary>
    void Clear();
}

/// <summary>An outcome monitor whose failure streak flags at its explicit threshold.</summary>
public interface IConsecutiveFailureMonitor : IHealthMonitor
{
    /// <summary>Counts a success, resets the failure streak and clears the policy flag.</summary>
    void ReportSuccess();

    /// <summary>Counts a failure and advances the failure streak.</summary>
    void ReportFailure(HealthDiagnostic? diagnostic = null);

    /// <summary>Counts a skip without changing the failure streak.</summary>
    void ReportSkipped();

    /// <summary>Counts expected cancellation without changing the failure streak.</summary>
    void ReportExpectedCancellation();
}

/// <summary>A policy that compares directly reported finite values against a fixed threshold.</summary>
public interface INumericThresholdMonitor : IHealthMonitor
{
    /// <summary>Reports a finite value; invalid numeric input is rejected without state change.</summary>
    void ReportValue(double value, HealthDiagnostic? diagnostic = null);
}

/// <summary>Availability established by usable data and retained through failed refreshes.</summary>
public interface IAvailabilityMonitor : IHealthMonitor
{
    /// <summary>Counts a success and establishes usable data.</summary>
    void ReportPublished();

    /// <summary>Counts a failed refresh without invalidating previously published usable data.</summary>
    void ReportRefreshFailure(HealthDiagnostic? diagnostic = null);

    /// <summary>Explicitly invalidates usable data without creating another execution outcome.</summary>
    void Invalidate(HealthDiagnostic? diagnostic = null);
}
