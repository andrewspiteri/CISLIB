using System.Numerics;

namespace CIS.Healthcheck;

// Named inputs for the state coordinator's detached snapshot copy. These never escape the assembly.
internal sealed class HealthSnapshotData
{
    internal int SchemaVersion { get; init; } = 1;
    internal BigInteger Revision { get; init; }
    internal DateTimeOffset CapturedAtUtc { get; init; }
    internal required HealthApplicationSnapshot Application { get; init; }
    internal HealthStatus HealthStatus { get; init; }
    internal bool Ready { get; init; }
    internal required HealthMonitorCounts MonitorCounts { get; init; }
    internal IEnumerable<HealthMonitorSnapshot> Monitors { get; init; } = [];
}

internal sealed class HealthMonitorSnapshotData
{
    internal required Guid MonitorId { get; init; }
    internal string? Name { get; init; }
    internal HealthPolicyKind Policy { get; init; }
    internal bool IsCritical { get; init; }
    internal MonitorState State { get; init; }
    internal DateTimeOffset RegisteredAtUtc { get; init; }
    internal DateTimeOffset? LastObservationAtUtc { get; init; }
    internal HealthDiagnostic? Diagnostic { get; init; }
    internal HealthOutcomeSnapshot? Outcomes { get; init; }
    internal HealthCollectionSnapshot? Collection { get; init; }
    internal HealthNumericSnapshot? Numeric { get; init; }
    internal IEnumerable<HealthMeasurement> Measurements { get; init; } = [];
}

internal sealed class HealthOutcomeSnapshotData
{
    internal long Successful { get; init; }
    internal long Failed { get; init; }
    internal long Skipped { get; init; }
    internal long ExpectedCanceled { get; init; }
    internal long ConsecutiveFailures { get; init; }
    internal bool CountersSaturated { get; init; }
    internal HealthExecutionOutcome? LastOutcome { get; init; }
    internal DateTimeOffset? LastSuccessAtUtc { get; init; }
    internal DateTimeOffset? LastFailureAtUtc { get; init; }
    internal HealthDiagnostic? LastFailureDiagnostic { get; init; }
    internal HealthExecutionSnapshot? Execution { get; init; }
}
