namespace CIS.Logging;

/// <summary>An owned immutable observation of runtime state and cumulative delivery accounting.</summary>
/// <remarks>This live observation does not register a flush boundary or acknowledge durability.</remarks>
public sealed class CoreLogDiagnostics
{
    internal CoreLogDiagnostics(CoreLogState state, bool admissionClosed, DateTimeOffset observedAtUtc,
        DateTimeOffset? shutdownStartedAtUtc, long invalidEventCount, long truncatedEventCount, IEnumerable<LogOutputDiagnostics> outputs, LogStopResult? lastStopResult = null)
    {
        State = state; AdmissionClosed = admissionClosed; ObservedAtUtc = observedAtUtc;
        ShutdownStartedAtUtc = shutdownStartedAtUtc; InvalidEventCount = invalidEventCount; TruncatedEventCount = truncatedEventCount;
        Outputs = Array.AsReadOnly(outputs.ToArray()); LastStopResult = lastStopResult;
    }
    /// <summary>The runtime admission/draining state, independent of individual output faults.</summary>
    public CoreLogState State { get; }
    /// <summary>Whether further runtime admissions are closed.</summary>
    public bool AdmissionClosed { get; }
    /// <summary>The system UTC time captured with this live observation; not an elapsed deadline.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>The system UTC time admission first closed for draining; null while still open.</summary>
    public DateTimeOffset? ShutdownStartedAtUtc { get; }
    /// <summary>Cumulative invalid captured events, saturating at Int64.MaxValue.</summary>
    public long InvalidEventCount { get; }
    /// <summary>Cumulative valid captures with omitted payload, saturating at Int64.MaxValue.</summary>
    public long TruncatedEventCount { get; }
    /// <summary>Owned console, file, then registered-writer observations; disabled outputs are omitted.</summary>
    public IReadOnlyList<LogOutputDiagnostics> Outputs { get; }
    /// <summary>The original cached stop observation, or null until the shared operation publishes one.</summary>
    public LogStopResult? LastStopResult { get; }
}
