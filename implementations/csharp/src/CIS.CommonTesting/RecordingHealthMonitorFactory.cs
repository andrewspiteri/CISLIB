using CIS.Healthcheck;

namespace CIS.CommonTesting;

/// <summary>The public report observed by a recording monitor; no health policy is evaluated.</summary>
public enum HealthReportKind
{
    /// <summary>A monitor was created.</summary>
    Created,
    /// <summary>A condition was flagged.</summary>
    Flagged,
    /// <summary>A condition was cleared.</summary>
    Cleared,
    /// <summary>A successful execution was reported.</summary>
    Success,
    /// <summary>A failed execution was reported.</summary>
    Failure,
    /// <summary>A skipped execution was reported.</summary>
    Skipped,
    /// <summary>An expected cancellation was reported.</summary>
    ExpectedCancellation,
    /// <summary>A numeric value was reported.</summary>
    Value,
    /// <summary>Usable data was published.</summary>
    Published,
    /// <summary>A refresh failed.</summary>
    RefreshFailure,
    /// <summary>Usable data was explicitly invalidated.</summary>
    Invalidated,
    /// <summary>A diagnostic measurement was reported.</summary>
    Measurement,
    /// <summary>The monitor was withdrawn.</summary>
    Withdrawn
}

/// <summary>One immutable report; supplied diagnostic objects remain caller-owned immutable values.</summary>
/// <param name="Kind">The exact reported operation.</param>
/// <param name="Diagnostic">The supplied detached diagnostic.</param>
/// <param name="Value">The supplied numeric value.</param>
/// <param name="Measurement">The supplied diagnostic measurement.</param>
public sealed record HealthReport(HealthReportKind Kind, HealthDiagnostic? Diagnostic = null,
    double? Value = null, HealthMeasurement? Measurement = null);

/// <summary>Creates bounded recording monitors without reproducing Core health evaluation or collection.</summary>
public sealed class RecordingHealthMonitorFactory : IHealthMonitorFactory
{
    private readonly BoundedRecords<RecordingHealthMonitor> monitors;
    private readonly int reportCapacity;

    /// <summary>Chooses retained creation/report capacities. Old records are evicted at these bounds.</summary>
    public RecordingHealthMonitorFactory(int capacity = 64, int reportCapacity = 256)
    {
        monitors = new(capacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(reportCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reportCapacity, 65_536);
        this.reportCapacity = reportCapacity;
    }

    /// <summary>Gets a detached bounded creation snapshot; clearing/eviction does not withdraw caller-owned monitors.</summary>
    public IReadOnlyList<RecordingHealthMonitor> Monitors => monitors.Snapshot();
    /// <summary>Gets the number of creation records evicted since Clear.</summary>
    public long DroppedMonitors => monitors.Dropped;
    /// <summary>Clears only recorded creation references, without changing any live monitor.</summary>
    public void Clear() => monitors.Clear();

    /// <inheritdoc />
    public IConditionMonitor CreateCondition(ConditionMonitorOptions options) => Add(new Condition(options, reportCapacity));
    /// <inheritdoc />
    public IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options)
        => Add(new Consecutive(options, reportCapacity));
    /// <inheritdoc />
    public INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options)
        => Add(new Numeric(options, reportCapacity));
    /// <inheritdoc />
    public IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options) => Add(new Availability(options, reportCapacity));

    private T Add<T>(T monitor) where T : RecordingHealthMonitor
    {
        monitors.Add(monitor);
        return monitor;
    }

    private sealed class Condition(ConditionMonitorOptions options, int capacity) : RecordingHealthMonitor(options, capacity), IConditionMonitor
    {
        public void Flag(HealthDiagnostic? diagnostic = null) => Record(new(HealthReportKind.Flagged, diagnostic));
        public void Clear() => Record(new(HealthReportKind.Cleared));
    }

    private sealed class Consecutive(ConsecutiveFailureMonitorOptions options, int capacity) : RecordingHealthMonitor(options, capacity), IConsecutiveFailureMonitor
    {
        public void ReportSuccess() => Record(new(HealthReportKind.Success));
        public void ReportFailure(HealthDiagnostic? diagnostic = null) => Record(new(HealthReportKind.Failure, diagnostic));
        public void ReportSkipped() => Record(new(HealthReportKind.Skipped));
        public void ReportExpectedCancellation() => Record(new(HealthReportKind.ExpectedCancellation));
    }

    private sealed class Numeric(NumericThresholdMonitorOptions options, int capacity) : RecordingHealthMonitor(options, capacity), INumericThresholdMonitor
    {
        public void ReportValue(double value, HealthDiagnostic? diagnostic = null)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            Record(new(HealthReportKind.Value, diagnostic, value));
        }
    }

    private sealed class Availability(AvailabilityMonitorOptions options, int capacity) : RecordingHealthMonitor(options, capacity), IAvailabilityMonitor
    {
        public void ReportPublished() => Record(new(HealthReportKind.Published));
        public void ReportRefreshFailure(HealthDiagnostic? diagnostic = null) => Record(new(HealthReportKind.RefreshFailure, diagnostic));
        public void Invalidate(HealthDiagnostic? diagnostic = null) => Record(new(HealthReportKind.Invalidated, diagnostic));
    }
}

/// <summary>Bounded public report inspection shared by the factory's policy-specific monitor implementations.</summary>
public abstract class RecordingHealthMonitor : IHealthMonitor
{
    private readonly object gate = new();
    private readonly BoundedRecords<HealthReport> reports;
    private readonly long[] counts = new long[Enum.GetValues<HealthReportKind>().Length];
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool withdrawn;

    internal RecordingHealthMonitor(HealthMonitorOptions options, int capacity)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        MonitorId = Guid.NewGuid();
        reports = new(capacity);
        Record(new(HealthReportKind.Created));
    }

    /// <summary>Gets the immutable creation options. This helper does not evaluate them.</summary>
    public HealthMonitorOptions Options
    {
        get;
    }
    /// <inheritdoc />
    public Guid MonitorId
    {
        get;
    }
    /// <inheritdoc />
    public string? Name => Options.Name;
    /// <inheritdoc />
    public bool IsCritical => Options.IsCritical;
    /// <summary>Gets bounded, detached report history.</summary>
    public IReadOnlyList<HealthReport> Reports => reports.Snapshot();
    /// <summary>Gets evicted report count; fixed per-kind counters still include those reports.</summary>
    public long DroppedReports => reports.Dropped;
    /// <summary>Gets whether withdrawal/disposal has closed report admission.</summary>
    public bool IsWithdrawn
    {
        get
        {
            lock (gate)
            {
                return withdrawn;
            }
        }
    }

    /// <summary>Gets the saturating total for one finite report kind, including evicted records.</summary>
    public long Count(HealthReportKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        lock (gate)
        {
            return counts[(int)kind];
        }
    }

    /// <inheritdoc />
    public void ReportMeasurement(HealthMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (!measurement.IsDiagnostic)
        {
            throw new ArgumentException("A diagnostic measurement is required.", nameof(measurement));
        }
        Record(new(HealthReportKind.Measurement, Measurement: measurement));
    }

    private protected void Record(HealthReport report)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(withdrawn, this);
            Add(report);
        }
    }

    private void Add(HealthReport report)
    {
        counts[(int)report.Kind] = BoundedRecords<HealthReport>.Increment(counts[(int)report.Kind]);
        reports.Add(report);
    }

    /// <inheritdoc />
    public void Withdraw()
    {
        lock (gate)
        {
            if (withdrawn)
            {
                return;
            }
            withdrawn = true;
            Add(new(HealthReportKind.Withdrawn));
            disposal.TrySetResult();
        }
    }

    /// <inheritdoc />
    public Task WaitForDisposalAsync(CancellationToken waitCancellation = default)
        => disposal.Task.IsCompleted ? disposal.Task : disposal.Task.WaitAsync(waitCancellation);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Withdraw();
        return new(disposal.Task);
    }
}

/// <summary>Returns an explicitly supplied genuine snapshot without inventing a policy evaluator.</summary>
public sealed class HealthSnapshotStub : IHealthSnapshotSource
{
    private HealthSnapshot snapshot;
    /// <summary>Retains the caller's immutable snapshot.</summary>
    public HealthSnapshotStub(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        this.snapshot = snapshot;
    }
    /// <summary>Replaces the explicitly configured immutable snapshot.</summary>
    public HealthSnapshot Snapshot
    {
        get => Volatile.Read(ref snapshot);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Volatile.Write(ref snapshot, value);
        }
    }
    /// <inheritdoc />
    public HealthSnapshot CaptureSnapshot() => Snapshot;
}
