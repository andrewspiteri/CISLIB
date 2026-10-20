using System.Numerics;

namespace CIS.Healthcheck.Internal;

// Detached policy/lifecycle state. The registry never strongly references the monitor handle.
internal sealed class HealthMonitorEntry
{
    internal readonly Guid Id = Guid.NewGuid();
    internal required BigInteger Ordinal { get; init; }
    internal required HealthPolicyKind Policy { get; init; }
    internal required bool IsCritical { get; init; }
    internal string? Name { get; init; }
    internal required DateTimeOffset RegisteredAt { get; init; }
    internal int FailureThreshold { get; init; }
    internal NumericComparison Comparison { get; init; }
    internal double Threshold { get; init; }
    internal string? Unit { get; init; }
    internal double? LastValue;
    internal DateTimeOffset? LastValueAt;
    internal WeakReference<IHealthMonitor> Handle = null!;
    internal MonitorState State;
    internal HealthDiagnostic? Diagnostic;
    internal DateTimeOffset? LastObservation;
    internal BigInteger HealthRevision;
    internal readonly Dictionary<string, HealthMetricEntry> Measurements = new(StringComparer.Ordinal);
    internal bool Withdrawn;
    internal long Generation;
    internal int PendingOperations;
    internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HealthCollectionTarget? Collection;
    internal DateTimeOffset? LastCompleted;
    internal HealthDiagnostic? CollectionError;
    internal DateTimeOffset? CollectionErrorAt;
    internal bool HasCollectionFacts;
    internal long Successful, Failed, Skipped, ExpectedCanceled, ConsecutiveFailures;
    internal bool Saturated, HasUsableData;
    internal HealthExecutionOutcome? LastOutcome;
    internal DateTimeOffset? LastSuccess, LastFailure;
    internal HealthDiagnostic? LastFailureDiagnostic;
    internal HealthExecutionSnapshot? Execution;
}

internal sealed record HealthMetricEntry(HealthMeasurement Value, BigInteger Revision);
