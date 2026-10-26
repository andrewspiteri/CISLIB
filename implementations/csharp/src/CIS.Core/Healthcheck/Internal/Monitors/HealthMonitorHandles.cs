namespace CIS.Healthcheck.Internal;

internal abstract class HealthMonitorHandle(HealthStateCoordinator owner, HealthMonitorEntry entry) : IHealthMonitor, IHealthStateAccess
{
    protected HealthStateCoordinator Owner { get; } = owner;
    protected HealthMonitorEntry Entry { get; } = entry;
    public Guid MonitorId => Entry.Id;
    public string? Name => Entry.Name;
    public bool IsCritical => Entry.IsCritical;
    public void ReportMeasurement(HealthMeasurement measurement)
    {
        try { Owner.ReportMeasurement(Entry, measurement); }
        finally { GC.KeepAlive(this); }
    }
    public void Withdraw()
    {
        try { Owner.Withdraw(Entry); }
        finally { GC.KeepAlive(this); }
    }
    public Task WaitForDisposalAsync(CancellationToken waitCancellation = default)
    {
        try
        {
            HealthCollectionExecution.ThrowIfSelfWait(Owner, monitorId: Entry.Id);
            return Entry.Disposal.Task.WaitAsync(waitCancellation);
        }
        finally { GC.KeepAlive(this); }
    }
    public ValueTask DisposeAsync()
    {
        try
        {
            Withdraw();
            HealthCollectionExecution.ThrowIfSelfWait(Owner, monitorId: Entry.Id);
            return new(Entry.Disposal.Task);
        }
        finally { GC.KeepAlive(this); }
    }
    IHealthCollectionState IHealthStateAccess.CollectionState => Owner;
    IHealthExecutionState IHealthStateAccess.ExecutionState => Owner;
}

internal sealed class ConditionMonitorHandle(HealthStateCoordinator owner, HealthMonitorEntry entry) : HealthMonitorHandle(owner, entry), IConditionMonitor
{
    public void Flag(HealthDiagnostic? diagnostic = null)
    {
        try { Owner.ReportCondition(Entry, true, diagnostic); }
        finally { GC.KeepAlive(this); }
    }
    public void Clear()
    {
        try { Owner.ReportCondition(Entry, false, null); }
        finally { GC.KeepAlive(this); }
    }
}

internal sealed class FailureMonitorHandle(HealthStateCoordinator owner, HealthMonitorEntry entry) : HealthMonitorHandle(owner, entry), IConsecutiveFailureMonitor
{
    public void ReportSuccess()
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.Succeeded, null); }
        finally { GC.KeepAlive(this); }
    }
    public void ReportFailure(HealthDiagnostic? diagnostic = null)
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.Failed, diagnostic); }
        finally { GC.KeepAlive(this); }
    }
    public void ReportSkipped()
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.Skipped, null); }
        finally { GC.KeepAlive(this); }
    }
    public void ReportExpectedCancellation()
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.ExpectedCancellation, null); }
        finally { GC.KeepAlive(this); }
    }
}

internal sealed class NumericMonitorHandle(HealthStateCoordinator owner, HealthMonitorEntry entry) : HealthMonitorHandle(owner, entry), INumericThresholdMonitor
{
    public void ReportValue(double value, HealthDiagnostic? diagnostic = null)
    {
        try { Owner.ReportNumber(Entry, value, diagnostic); }
        finally { GC.KeepAlive(this); }
    }
}

internal sealed class AvailabilityMonitorHandle(HealthStateCoordinator owner, HealthMonitorEntry entry) : HealthMonitorHandle(owner, entry), IAvailabilityMonitor
{
    public void ReportPublished()
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.Succeeded, null); }
        finally { GC.KeepAlive(this); }
    }
    public void ReportRefreshFailure(HealthDiagnostic? diagnostic = null)
    {
        try { Owner.ReportOutcome(Entry, HealthExecutionOutcome.Failed, diagnostic); }
        finally { GC.KeepAlive(this); }
    }
    public void Invalidate(HealthDiagnostic? diagnostic = null)
    {
        try { Owner.ReportCondition(Entry, true, diagnostic); }
        finally { GC.KeepAlive(this); }
    }
}
