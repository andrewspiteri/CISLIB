using System.Numerics;

namespace CIS.Healthcheck;

/// <summary>A complete immutable health observation detached from live monitors and collectors.</summary>
public sealed class HealthSnapshot
{
    internal HealthSnapshot(HealthSnapshotData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(data.Application);
        ArgumentNullException.ThrowIfNull(data.MonitorCounts);
        ArgumentNullException.ThrowIfNull(data.Monitors);
        SchemaVersion = data.SchemaVersion;
        Revision = data.Revision;
        CapturedAtUtc = data.CapturedAtUtc.ToUniversalTime();
        Application = data.Application;
        HealthStatus = data.HealthStatus;
        Ready = data.Ready;
        MonitorCounts = data.MonitorCounts;
        Monitors = Array.AsReadOnly(data.Monitors.ToArray());
    }

    /// <summary>The report schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>The nonnegative non-reusing publication revision.</summary>
    public BigInteger Revision { get; }

    /// <summary>The fresh UTC snapshot capture time.</summary>
    public DateTimeOffset CapturedAtUtc { get; }

    /// <summary>Detached application identity and lifecycle facts.</summary>
    public HealthApplicationSnapshot Application { get; }

    /// <summary>The health aggregate evaluated from active critical monitors.</summary>
    public HealthStatus HealthStatus { get; }

    /// <summary>Whether startup completed, stopping has not begun and aggregate health is healthy.</summary>
    public bool Ready { get; }

    /// <summary>Complete counts evaluated at the same revision as monitor details.</summary>
    public HealthMonitorCounts MonitorCounts { get; }

    /// <summary>Complete monitor details ordered by registration ordinal.</summary>
    public IReadOnlyList<HealthMonitorSnapshot> Monitors { get; }
}

/// <summary>Detached application identity and lifecycle facts at snapshot capture.</summary>
public sealed class HealthApplicationSnapshot
{
    internal HealthApplicationSnapshot(HealthApplicationIdentity identity, bool startupCompleted, bool isStopping)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Name = identity.Name;
        BuildVersion = identity.BuildVersion;
        ServiceVersion = identity.ServiceVersion;
        InstanceId = identity.InstanceId;
        HostName = identity.HostName;
        OsDescription = identity.OsDescription;
        StartupCompleted = startupCompleted;
        IsStopping = isStopping;
    }

    /// <summary>The application name.</summary>
    public string Name { get; }

    /// <summary>The optional actual build version.</summary>
    public string? BuildVersion { get; }

    /// <summary>The optional selected deployment version.</summary>
    public string? ServiceVersion { get; }

    /// <summary>The application instance identity.</summary>
    public Guid InstanceId { get; }

    /// <summary>The optional host name.</summary>
    public string? HostName { get; }

    /// <summary>The optional operating-system description.</summary>
    public string? OsDescription { get; }

    /// <summary>Whether the application explicitly marked startup successful.</summary>
    public bool StartupCompleted { get; }

    /// <summary>Whether terminal stopping has begun.</summary>
    public bool IsStopping { get; }
}

/// <summary>Complete monitor counts from one coherent snapshot.</summary>
public sealed class HealthMonitorCounts
{
    internal HealthMonitorCounts(int active, int critical, int failing, int criticalFailing, int pending)
    {
        Active = active;
        Critical = critical;
        Failing = failing;
        CriticalFailing = criticalFailing;
        Pending = pending;
    }

    /// <summary>The number of active monitor registrations.</summary>
    public int Active { get; }

    /// <summary>The number of active critical registrations.</summary>
    public int Critical { get; }

    /// <summary>The number of active unhealthy registrations, including noncritical ones.</summary>
    public int Failing { get; }

    /// <summary>The number of active critical unhealthy registrations.</summary>
    public int CriticalFailing { get; }

    /// <summary>The number of active registrations awaiting a required first observation.</summary>
    public int Pending { get; }
}

/// <summary>The detached state of one monitor at snapshot capture.</summary>
public sealed class HealthMonitorSnapshot
{
    internal HealthMonitorSnapshot(HealthMonitorSnapshotData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(data.Measurements);
        MonitorId = data.MonitorId;
        Name = data.Name;
        Policy = data.Policy;
        IsCritical = data.IsCritical;
        State = data.State;
        RegisteredAtUtc = data.RegisteredAtUtc.ToUniversalTime();
        LastObservationAtUtc = data.LastObservationAtUtc?.ToUniversalTime();
        Diagnostic = data.Diagnostic;
        Outcomes = data.Outcomes;
        Collection = data.Collection;
        Numeric = data.Numeric;
        Measurements = Array.AsReadOnly(data.Measurements.ToArray());
    }

    /// <summary>The opaque unique registration identity.</summary>
    public Guid MonitorId { get; }

    /// <summary>The optional display name.</summary>
    public string? Name { get; }

    /// <summary>The fixed evaluation policy.</summary>
    public HealthPolicyKind Policy { get; }

    /// <summary>Whether unhealthy state contributes to aggregate failure.</summary>
    public bool IsCritical { get; }

    /// <summary>The policy's evaluated state.</summary>
    public MonitorState State { get; }

    /// <summary>The UTC registration instant.</summary>
    public DateTimeOffset RegisteredAtUtc { get; }

    /// <summary>The UTC instant of the latest applicable accepted health observation.</summary>
    public DateTimeOffset? LastObservationAtUtc { get; }

    /// <summary>The latest applicable detached health reason, cleared on success or clear.</summary>
    public HealthDiagnostic? Diagnostic { get; }

    /// <summary>Outcome facts where this policy or binding actually has outcomes.</summary>
    public HealthOutcomeSnapshot? Outcomes { get; }

    /// <summary>Applicable collector lifecycle and retained diagnostic facts.</summary>
    public HealthCollectionSnapshot? Collection { get; }

    /// <summary>The numeric policy's separate required value facts, where applicable.</summary>
    public HealthNumericSnapshot? Numeric { get; }

    /// <summary>Owned optional diagnostic measurements ordered ordinally by key.</summary>
    public IReadOnlyList<HealthMeasurement> Measurements { get; }
}

/// <summary>Detached lifetime outcome accounting, independent of the evaluated policy state.</summary>
public sealed class HealthOutcomeSnapshot
{
    internal HealthOutcomeSnapshot(HealthOutcomeSnapshotData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Successful = data.Successful;
        Failed = data.Failed;
        Skipped = data.Skipped;
        ExpectedCanceled = data.ExpectedCanceled;
        ConsecutiveFailures = data.ConsecutiveFailures;
        CountersSaturated = data.CountersSaturated;
        LastOutcome = data.LastOutcome;
        LastSuccessAtUtc = data.LastSuccessAtUtc?.ToUniversalTime();
        LastFailureAtUtc = data.LastFailureAtUtc?.ToUniversalTime();
        LastFailureDiagnostic = data.LastFailureDiagnostic;
        Execution = data.Execution;
    }

    /// <summary>The nonnegative saturating lifetime success count.</summary>
    public long Successful { get; }

    /// <summary>The nonnegative saturating lifetime failure count.</summary>
    public long Failed { get; }

    /// <summary>The nonnegative saturating lifetime skipped count.</summary>
    public long Skipped { get; }

    /// <summary>The nonnegative saturating lifetime expected-cancellation count.</summary>
    public long ExpectedCanceled { get; }

    /// <summary>The current failure streak, unaffected by skips or expected cancellation.</summary>
    public long ConsecutiveFailures { get; }

    /// <summary>Whether a represented lifetime counter has reached its limit.</summary>
    public bool CountersSaturated { get; }

    /// <summary>The latest recorded outcome, where available.</summary>
    public HealthExecutionOutcome? LastOutcome { get; }

    /// <summary>The UTC instant of the latest successful outcome.</summary>
    public DateTimeOffset? LastSuccessAtUtc { get; }

    /// <summary>The UTC instant of the latest failed outcome.</summary>
    public DateTimeOffset? LastFailureAtUtc { get; }

    /// <summary>The latest failed outcome's detached diagnostic, retained through later success.</summary>
    public HealthDiagnostic? LastFailureDiagnostic { get; }

    /// <summary>The bounded execution lifecycle facts, where an execution binding supplies them.</summary>
    public HealthExecutionSnapshot? Execution { get; }
}

/// <summary>Detached numeric policy configuration and its latest accepted finite value.</summary>
public sealed class HealthNumericSnapshot
{
    internal HealthNumericSnapshot(NumericComparison comparison, double threshold, string unit, double? lastValue, DateTimeOffset? lastValueAtUtc)
    {
        Comparison = comparison;
        Threshold = threshold;
        Unit = unit;
        LastValue = lastValue;
        LastValueAtUtc = lastValueAtUtc?.ToUniversalTime();
    }

    /// <summary>The comparison whose result flags this monitor unhealthy.</summary>
    public NumericComparison Comparison { get; }

    /// <summary>The fixed finite threshold.</summary>
    public double Threshold { get; }

    /// <summary>The explicitly selected unit, without automatic conversion.</summary>
    public string Unit { get; }

    /// <summary>The latest accepted finite value, unchanged by an evaluation error.</summary>
    public double? LastValue { get; }

    /// <summary>The value's actual UTC observation time, unchanged by snapshot capture.</summary>
    public DateTimeOffset? LastValueAtUtc { get; }
}

/// <summary>Detached collection lifecycle and diagnostic facts.</summary>
public sealed class HealthCollectionSnapshot
{
    internal HealthCollectionSnapshot(bool inProgress, DateTimeOffset? lastCompletedAtUtc, HealthDiagnostic? lastError, DateTimeOffset? lastErrorAtUtc)
    {
        InProgress = inProgress;
        LastCompletedAtUtc = lastCompletedAtUtc?.ToUniversalTime();
        LastError = lastError;
        LastErrorAtUtc = lastErrorAtUtc?.ToUniversalTime();
    }

    /// <summary>Whether an actual tracked collection has not yet settled.</summary>
    public bool InProgress { get; }

    /// <summary>The latest actual collection completion instant in UTC.</summary>
    public DateTimeOffset? LastCompletedAtUtc { get; }

    /// <summary>The latest retained detached collection error.</summary>
    public HealthDiagnostic? LastError { get; }

    /// <summary>The UTC observation instant of the retained collection error.</summary>
    public DateTimeOffset? LastErrorAtUtc { get; }
}
