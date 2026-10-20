using System.Numerics;
using CIS.Text;

namespace CIS.Healthcheck.Internal;

internal sealed partial class HealthStateCoordinator : IHealthCollectionState, IHealthExecutionState
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, HealthMonitorEntry> active = [];
    private readonly Dictionary<Guid, CollectionOperation> operations = [];
    private readonly Dictionary<Guid, ExecutionBinding> bindings = [];
    private readonly HealthApplicationIdentity application;
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stoppingCancellation = new();
    private BigInteger revision, ordinal;
    private bool startupCompleted, stopping, disposalStarted, disposed, runnerClaimed;

    internal HealthStateCoordinator(HealthcheckOptions options, HealthApplicationIdentity? identity, TimeProvider clock)
    {
        Options = options;
        Clock = clock;
        identity ??= new HealthApplicationIdentity
        {
            Name = EnvironmentOptions.ApplicationName,
            BuildVersion = EnvironmentOptions.BuildVersion,
            ServiceVersion = EnvironmentOptions.ServiceVersion,
            InstanceId = Guid.Parse(EnvironmentOptions.InstanceId),
            HostName = EnvironmentOptions.HostName,
            OsDescription = EnvironmentOptions.OperatingSystemDescription
        };
        CheckText(identity.Name, 255, nameof(identity.Name), required: true);
        CheckText(identity.BuildVersion, 512, nameof(identity.BuildVersion));
        CheckText(identity.ServiceVersion, 512, nameof(identity.ServiceVersion));
        CheckText(identity.HostName, 255, nameof(identity.HostName));
        CheckText(identity.OsDescription, 1024, nameof(identity.OsDescription));
        if (identity.InstanceId == Guid.Empty) { throw new ArgumentException("An application instance identity is required.", nameof(identity)); }
        application = new HealthApplicationIdentity
        {
            Name = identity.Name,
            BuildVersion = identity.BuildVersion,
            ServiceVersion = identity.ServiceVersion,
            InstanceId = identity.InstanceId,
            HostName = identity.HostName,
            OsDescription = identity.OsDescription
        };
    }

    internal HealthcheckOptions Options { get; }
    internal TimeProvider Clock { get; }
    internal Task Disposal => disposal.Task;
    public CancellationToken Stopping => stoppingCancellation.Token;
    private DateTimeOffset Now() => Clock.GetUtcNow().ToUniversalTime();

    private static void CheckText(string? text, int maximum, string parameter, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(text)) || (text is not null && !UnicodeScalarText.IsValid(text, maximum, allowControls: false)))
        { throw new ArgumentException("Supply bounded, valid Unicode identity text without controls.", parameter); }
    }

    private static void ValidatePolicy(HealthMonitorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateCommon();
        switch (options)
        {
            case ConsecutiveFailureMonitorOptions failure when failure.FailureThreshold < 1:
                throw new ArgumentOutOfRangeException(nameof(options), "A failure threshold must be positive.");
            case NumericThresholdMonitorOptions numeric:
                if (!Enum.IsDefined(numeric.Comparison) || !double.IsFinite(numeric.Threshold))
                { throw new ArgumentException("Supply a defined comparison and finite threshold.", nameof(options)); }
                if (string.IsNullOrWhiteSpace(numeric.Unit) || !UnicodeScalarText.IsValid(numeric.Unit, 32, allowControls: true))
                { throw new ArgumentException("Supply an explicit bounded unit.", nameof(options)); }
                break;
            case ConditionMonitorOptions or ConsecutiveFailureMonitorOptions or AvailabilityMonitorOptions:
                break;
            default: throw new ArgumentException("The monitor policy is unsupported.", nameof(options));
        }
    }

    internal IHealthMonitor Create(HealthMonitorOptions options)
    {
        ValidatePolicy(options);
        DateTimeOffset now = Now();
        var effects = new Effects();
        try
        {
            lock (gate)
            {
                EnsureAdmission();
                SweepDead(effects);
                return CreateLocked(options, now);
            }
        }
        finally { Dispatch(effects); }
    }

    private IHealthMonitor CreateLocked(HealthMonitorOptions options, DateTimeOffset now)
    {
        if (active.Count >= Options.MaxMonitors) { throw new HealthcheckCapacityException(Options.MaxMonitors); }
        var entry = new HealthMonitorEntry
        {
            Ordinal = ++ordinal,
            Name = options.Name,
            IsCritical = options.IsCritical,
            RegisteredAt = now,
            Policy = options switch
            {
                ConditionMonitorOptions => HealthPolicyKind.Condition,
                ConsecutiveFailureMonitorOptions => HealthPolicyKind.ConsecutiveFailures,
                NumericThresholdMonitorOptions => HealthPolicyKind.NumericThreshold,
                AvailabilityMonitorOptions => HealthPolicyKind.FirstSuccessAvailability,
                _ => throw new ArgumentException("Unsupported policy.", nameof(options))
            },
            FailureThreshold = (options as ConsecutiveFailureMonitorOptions)?.FailureThreshold ?? 0,
            Comparison = (options as NumericThresholdMonitorOptions)?.Comparison ?? default,
            Threshold = (options as NumericThresholdMonitorOptions)?.Threshold ?? 0,
            Unit = (options as NumericThresholdMonitorOptions)?.Unit,
            State = options is ConditionMonitorOptions condition ? (condition.InitiallyFlagged ? MonitorState.Unhealthy : MonitorState.Healthy) : MonitorState.Pending,
            Diagnostic = options is ConditionMonitorOptions { InitiallyFlagged: true } initial ? initial.InitialDiagnostic : null,
            LastObservation = options is ConditionMonitorOptions ? now : null
        };
        IHealthMonitor handle = entry.Policy switch
        {
            HealthPolicyKind.Condition => new ConditionMonitorHandle(this, entry),
            HealthPolicyKind.ConsecutiveFailures => new FailureMonitorHandle(this, entry),
            HealthPolicyKind.NumericThreshold => new NumericMonitorHandle(this, entry),
            _ => new AvailabilityMonitorHandle(this, entry)
        };
        entry.Handle = new(handle);
        active.Add(entry.Id, entry);
        revision++;
        return handle;
    }

    private void EnsureAdmission()
    {
        ObjectDisposedException.ThrowIf(disposalStarted, typeof(HealthcheckService));
        if (stopping) { throw new InvalidOperationException("Healthcheck is stopping."); }
    }

    private void EnsureActive(HealthMonitorEntry entry)
    {
        ObjectDisposedException.ThrowIf(disposed || entry.Withdrawn || !active.TryGetValue(entry.Id, out var current) || !ReferenceEquals(current, entry), typeof(IHealthMonitor));
    }

    internal void ClaimCollectorRunner()
    {
        lock (gate)
        {
            EnsureAdmission();
            if (runnerClaimed) { throw new InvalidOperationException("A collector runner already belongs to this service."); }
            runnerClaimed = true;
        }
    }

    internal void MarkStartupCompleted()
    {
        lock (gate)
        {
            EnsureAdmission();
            if (!startupCompleted) { startupCompleted = true; revision++; }
        }
    }

    internal void BeginStopping()
    {
        var effects = new Effects();
        lock (gate) { StopLocked(effects); }
        Dispatch(effects);
    }

    private void StopLocked(Effects effects)
    {
        if (stopping) { return; }
        stopping = true;
        revision++;
        effects.SignalStopping = true;
        foreach (var operation in operations.Values) { RequestCancellation(operation, effects); }
    }

    internal Task Dispose()
    {
        var effects = new Effects();
        lock (gate)
        {
            if (!disposalStarted)
            {
                StopLocked(effects);
                disposalStarted = true;
                foreach (var entry in active.Values.ToArray()) { WithdrawLocked(entry, effects); }
                FinishService(effects);
            }
        }
        Dispatch(effects);
        return disposal.Task;
    }

    internal void Withdraw(HealthMonitorEntry entry)
    {
        var effects = new Effects();
        lock (gate) { WithdrawLocked(entry, effects); }
        Dispatch(effects);
    }

    private void SweepDead(Effects effects)
    {
        foreach (var entry in active.Values.ToArray())
        { if (!entry.Handle.TryGetTarget(out _)) { WithdrawLocked(entry, effects); } }
    }

    private void WithdrawLocked(HealthMonitorEntry entry, Effects effects)
    {
        if (entry.Withdrawn) { return; }
        entry.Withdrawn = true;
        active.Remove(entry.Id);
        if (entry.Generation < long.MaxValue) { entry.Generation++; }
        if (entry.Collection is not null) { CloseAttachmentLocked(attachments.GetValue(entry.Collection, _ => throw new InvalidOperationException()), effects); }
        entry.Collection = null;
        RetireBindingsLocked(entry);
        foreach (var operation in operations.Values.Where(operation => ReferenceEquals(operation.Entry, entry))) { RequestCancellation(operation, effects); }
        revision++;
        if (!HasPendingCollectionSettlement(entry)) { effects.Complete.Add(entry.Disposal); }
    }

    private void FinishService(Effects effects)
    {
        if (disposalStarted && operations.Count == 0 && retiringCollections.Count == 0 && !disposed)
        { disposed = true; effects.Complete.Add(disposal); }
    }

    internal void ReportMeasurement(HealthMonitorEntry entry, HealthMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (!measurement.IsDiagnostic) { throw new ArgumentException("Common measurements must be diagnostic.", nameof(measurement)); }
        lock (gate)
        {
            EnsureActive(entry);
            if (!entry.Measurements.ContainsKey(measurement.Key) && entry.Measurements.Count == 32)
            { throw new ArgumentException("A monitor supports at most 32 metric keys.", nameof(measurement)); }
            entry.Measurements[measurement.Key] = new(measurement, ++revision);
        }
    }

    internal void ReportCondition(HealthMonitorEntry entry, bool flagged, HealthDiagnostic? diagnostic)
    {
        DateTimeOffset now = Now();
        lock (gate) { EnsureActive(entry); SetCondition(entry, flagged, diagnostic, now); }
    }

    private void SetCondition(HealthMonitorEntry entry, bool flagged, HealthDiagnostic? diagnostic, DateTimeOffset now)
    {
        entry.State = flagged ? MonitorState.Unhealthy : MonitorState.Healthy;
        if (entry.Policy == HealthPolicyKind.FirstSuccessAvailability && flagged) { entry.HasUsableData = false; }
        entry.Diagnostic = flagged ? diagnostic : null;
        Observe(entry, now);
    }

    internal void ReportNumber(HealthMonitorEntry entry, double value, HealthDiagnostic? diagnostic)
    {
        if (!double.IsFinite(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
        DateTimeOffset now = Now();
        lock (gate) { EnsureActive(entry); SetNumber(entry, value, diagnostic, now); }
    }

    private void SetNumber(HealthMonitorEntry entry, double value, HealthDiagnostic? diagnostic, DateTimeOffset now)
    {
        entry.LastValue = value;
        entry.LastValueAt = now;
        bool breach = entry.Comparison switch
        {
            NumericComparison.GreaterThan => value > entry.Threshold,
            NumericComparison.GreaterThanOrEqual => value >= entry.Threshold,
            NumericComparison.LessThan => value < entry.Threshold,
            NumericComparison.LessThanOrEqual => value <= entry.Threshold,
            _ => throw new InvalidOperationException("Unknown numeric policy.")
        };
        SetCondition(entry, breach, diagnostic, now);
    }

    internal void ReportOutcome(HealthMonitorEntry entry, HealthExecutionOutcome outcome, HealthDiagnostic? diagnostic)
    {
        DateTimeOffset now = Now();
        lock (gate) { EnsureActive(entry); SetOutcome(entry, outcome, diagnostic, now); }
    }

    private void SetOutcome(HealthMonitorEntry entry, HealthExecutionOutcome outcome, HealthDiagnostic? diagnostic, DateTimeOffset now)
    {
        entry.LastOutcome = outcome;
        switch (outcome)
        {
            case HealthExecutionOutcome.Succeeded:
                Increment(ref entry.Successful, entry);
                entry.ConsecutiveFailures = 0;
                entry.LastSuccess = now;
                entry.HasUsableData = true;
                entry.State = MonitorState.Healthy;
                entry.Diagnostic = null;
                break;
            case HealthExecutionOutcome.Failed:
                Increment(ref entry.Failed, entry);
                Increment(ref entry.ConsecutiveFailures, entry);
                entry.LastFailure = now;
                entry.LastFailureDiagnostic = diagnostic;
                entry.Diagnostic = diagnostic;
                entry.State = entry.Policy == HealthPolicyKind.ConsecutiveFailures
                    ? (entry.ConsecutiveFailures >= entry.FailureThreshold ? MonitorState.Unhealthy : MonitorState.Healthy)
                    : (entry.HasUsableData ? MonitorState.Healthy : MonitorState.Unhealthy);
                break;
            case HealthExecutionOutcome.Skipped: Increment(ref entry.Skipped, entry); break;
            case HealthExecutionOutcome.ExpectedCancellation: Increment(ref entry.ExpectedCanceled, entry); break;
            default: throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        Observe(entry, now);
    }

    private static void Increment(ref long counter, HealthMonitorEntry entry)
    { if (counter < long.MaxValue) { counter++; } if (counter == long.MaxValue) { entry.Saturated = true; } }

    private void Observe(HealthMonitorEntry entry, DateTimeOffset now)
    { entry.LastObservation = now; entry.HealthRevision = ++revision; }

    internal HealthSnapshot CaptureSnapshot()
    {
        DateTimeOffset now = Now();
        var effects = new Effects();
        List<(BigInteger Ordinal, HealthMonitorSnapshotData Data)> copies;
        HealthApplicationSnapshot app;
        BigInteger capturedRevision;
        HealthMonitorCounts counts;
        HealthStatus health;
        bool ready;
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, typeof(HealthcheckService));
                SweepDead(effects);
                copies = active.Values.Select(entry => (entry.Ordinal, Copy(entry))).ToList();
                int critical = active.Values.Count(entry => entry.IsCritical);
                int failing = active.Values.Count(entry => entry.State == MonitorState.Unhealthy);
                int criticalFailing = active.Values.Count(entry => entry.IsCritical && entry.State == MonitorState.Unhealthy);
                counts = new(active.Count, critical, failing, criticalFailing, active.Values.Count(entry => entry.State == MonitorState.Pending));
                health = criticalFailing == 0 ? HealthStatus.Healthy : HealthStatus.Unhealthy;
                ready = startupCompleted && !stopping && health == HealthStatus.Healthy;
                app = new(application, startupCompleted, stopping);
                capturedRevision = revision;
            }
        }
        finally { Dispatch(effects); }
        var monitors = copies.OrderBy(copy => copy.Ordinal).Select(copy => new HealthMonitorSnapshot(CopyWithOrderedMetrics(copy.Data))).ToArray();
        return new(new HealthSnapshotData
        {
            Revision = capturedRevision,
            CapturedAtUtc = now,
            Application = app,
            HealthStatus = health,
            Ready = ready,
            MonitorCounts = counts,
            Monitors = monitors
        });
    }

    private static HealthMonitorSnapshotData CopyWithOrderedMetrics(HealthMonitorSnapshotData data) => new()
    {
        MonitorId = data.MonitorId,
        Name = data.Name,
        Policy = data.Policy,
        IsCritical = data.IsCritical,
        State = data.State,
        RegisteredAtUtc = data.RegisteredAtUtc,
        LastObservationAtUtc = data.LastObservationAtUtc,
        Diagnostic = data.Diagnostic,
        Outcomes = data.Outcomes,
        Collection = data.Collection,
        Numeric = data.Numeric,
        Measurements = data.Measurements.OrderBy(measurement => measurement.Key, StringComparer.Ordinal)
    };

    private static HealthMonitorSnapshotData Copy(HealthMonitorEntry entry) => new()
    {
        MonitorId = entry.Id,
        Name = entry.Name,
        Policy = entry.Policy,
        IsCritical = entry.IsCritical,
        State = entry.State,
        RegisteredAtUtc = entry.RegisteredAt,
        LastObservationAtUtc = entry.LastObservation,
        Diagnostic = entry.Diagnostic,
        Outcomes = entry.Policy is HealthPolicyKind.ConsecutiveFailures or HealthPolicyKind.FirstSuccessAvailability ? new(new HealthOutcomeSnapshotData
        {
            Successful = entry.Successful,
            Failed = entry.Failed,
            Skipped = entry.Skipped,
            ExpectedCanceled = entry.ExpectedCanceled,
            ConsecutiveFailures = entry.ConsecutiveFailures,
            CountersSaturated = entry.Saturated,
            LastOutcome = entry.LastOutcome,
            LastSuccessAtUtc = entry.LastSuccess,
            LastFailureAtUtc = entry.LastFailure,
            LastFailureDiagnostic = entry.LastFailureDiagnostic,
            Execution = entry.Execution
        }) : null,
        Numeric = entry.Policy == HealthPolicyKind.NumericThreshold ? new(entry.Comparison, entry.Threshold, entry.Unit!, entry.LastValue, entry.LastValueAt) : null,
        Collection = entry.HasCollectionFacts ? new(entry.PendingOperations != 0, entry.LastCompleted, entry.CollectionError, entry.CollectionErrorAt) : null,
        Measurements = entry.Measurements.Values.Select(metric => metric.Value).ToArray()
    };

    private sealed class Effects
    {
        internal bool SignalStopping;
        internal readonly List<CollectionAttachment> Retire = [];
        internal readonly List<CollectionOperation> Cancel = [];
        internal readonly List<TaskCompletionSource> Complete = [];
        internal readonly List<CancellationTokenSource> Dispose = [];
    }

    private void Dispatch(Effects effects)
    {
        if (effects.SignalStopping)
        {
            try { stoppingCancellation.Cancel(); }
            catch (Exception) { /* An internal observer must not prevent operation cancellation or settlement. */ }
        }
        foreach (var attachment in effects.Retire) { CancelRetirement(attachment); }
        foreach (var operation in effects.Cancel) { CancelOperation(operation); }
        foreach (var source in effects.Complete) { source.TrySetResult(); }
        foreach (var source in effects.Dispose) { source.Dispose(); }
    }
}
