namespace CIS.Logging;

/// <summary>The original owned immutable observation of one shared bounded stop operation.</summary>
/// <remarks>Later live diagnostics may show pending I/O completing; this result does not change or claim disk durability.</remarks>
public sealed class LogStopResult
{
    internal LogStopResult(LogFlushResult observation)
    {
        Completion = observation.Completion; Succeeded = observation.Succeeded; LossObserved = observation.LossObserved;
        BoundaryCapturedAtUtc = observation.BoundaryCapturedAtUtc; ObservedAtUtc = observation.ObservedAtUtc;
        InvalidEventCount = observation.InvalidEventCount; TruncatedEventCount = observation.TruncatedEventCount;
        Outputs = Array.AsReadOnly(observation.Outputs.ToArray());
        Incoming = observation.Incoming;
        ErrorCode = observation.ErrorCode;
    }
    /// <summary>Whether covered record dispositions and writer flushes settled; resource cleanup may still be pending.</summary>
    public LogCompletion Completion { get; }
    /// <summary>Whether all covered records were written and writer flushes succeeded.</summary>
    public bool Succeeded { get; }
    /// <summary>Whether initial capture/rejection facts or covered failures confirm loss.</summary>
    public bool LossObserved { get; }
    /// <summary>The system UTC label of original boundary capture, not an elapsed deadline.</summary>
    public DateTimeOffset BoundaryCapturedAtUtc { get; }
    /// <summary>The system UTC label of the cached final observation.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>The runtime invalid-event count frozen with the original boundary.</summary>
    public long InvalidEventCount { get; }
    /// <summary>The runtime truncation count frozen with the original boundary.</summary>
    public long TruncatedEventCount { get; }
    /// <summary>Owned console, file, then registered-writer fixed-boundary observations; disabled outputs are omitted.</summary>
    public IReadOnlyList<LogOutputDrainResult> Outputs { get; }
    /// <summary>The original lifetime frontend observation, including unresolved publishers and backend work.</summary>
    public LogIncomingSnapshot? Incoming { get; }
    /// <summary>A fixed safe frontend lifecycle failure/cancellation code, otherwise null.</summary>
    public string? ErrorCode { get; }
}
