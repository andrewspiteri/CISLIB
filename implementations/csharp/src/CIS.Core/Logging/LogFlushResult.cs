namespace CIS.Logging;
/// <summary>Whether a captured delivery boundary settled within its caller's wait.</summary>
public enum LogCompletion
{
    /// <summary>Every covered record and writer flush settled before the original monotonic budget expired.</summary>
    Completed = 0,
    /// <summary>Covered work or a writer flush did not settle before the deadline, even if observed terminal later.</summary>
    TimedOut = 1
}

/// <summary>The writer-buffer flush disposition for one captured output boundary.</summary>
public enum LogWriterFlushStatus
{
    /// <summary>No writer flush was requested.</summary>
    NotRequested = 0,
    /// <summary>The requested writer flush has not settled.</summary>
    Pending = 1,
    /// <summary>The writer confirmed its buffer flush.</summary>
    Succeeded = 2,
    /// <summary>The writer flush failed or confirmation was unavailable.</summary>
    Failed = 3
}

/// <summary>Owned caller rejection counts at registration plus worker rejections through the covered incoming prefix.</summary>
public sealed class LogOutputRejectionSnapshot
{
    internal LogOutputRejectionSnapshot(long filtered, long queueFull, long unavailable)
    {
        Filtered = filtered;
        QueueFull = queueFull;
        Unavailable = unavailable;
    }

    /// <summary>Events excluded by this output's threshold; filtering is not loss.</summary>
    public long Filtered { get; }
    /// <summary>Interested admissions rejected because the output queue was full.</summary>
    public long QueueFull { get; }
    /// <summary>Interested admissions rejected because the output was unavailable.</summary>
    public long Unavailable { get; }
}

/// <summary>An owned immutable observation through one output's fixed accepted boundary.</summary>
public sealed class LogOutputDrainResult
{
    internal LogOutputDrainResult(string name, long boundary, long written, long failed, long abandoned, long pending, LogWriterFlushStatus writerFlushStatus, LogOutputRejectionSnapshot rejections)
    {
        OutputName = name;
        AcceptedSequenceBoundary = boundary;
        Written = written;
        Failed = failed;
        Abandoned = abandoned;
        Pending = pending;
        WriterFlushStatus = writerFlushStatus;
        Rejections = rejections;
    }

    /// <summary>The enabled built-in or registered destination.</summary>
    public string OutputName { get; }
    /// <summary>The last accepted sequence included in this observation.</summary>
    public long AcceptedSequenceBoundary { get; }
    /// <summary>Covered complete records confirmed by the writer.</summary>
    public long Written { get; }
    /// <summary>Covered records whose started writes failed.</summary>
    public long Failed { get; }
    /// <summary>Covered accepted records abandoned before their writes started.</summary>
    public long Abandoned { get; }
    /// <summary>Covered queued or unresolved in-flight records.</summary>
    public long Pending { get; }
    /// <summary>The covered writer-buffer flush disposition.</summary>
    public LogWriterFlushStatus WriterFlushStatus { get; }
    /// <summary>Whether the writer confirmed its buffer flush.</summary>
    public bool WriterFlushSucceeded => WriterFlushStatus == LogWriterFlushStatus.Succeeded;
    /// <summary>Caller rejection counts at registration plus covered worker rejections, frozen before later input dispatch.</summary>
    public LogOutputRejectionSnapshot Rejections { get; }
}

/// <summary>An owned immutable result of waiting for a registered flush boundary.</summary>
/// <remarks>Writer-buffer confirmation does not acknowledge disk durability or collector receipt.</remarks>
public sealed class LogFlushResult
{
    internal LogFlushResult(DateTimeOffset boundaryCapturedAtUtc, DateTimeOffset observedAtUtc, long invalidEventCount, long truncatedEventCount, IEnumerable<LogOutputDrainResult> outputs, bool deadlineExceeded = false, LogIncomingSnapshot? incoming = null, bool registrationLoss = false)
    {
        BoundaryCapturedAtUtc = boundaryCapturedAtUtc;
        ObservedAtUtc = observedAtUtc;
        InvalidEventCount = invalidEventCount;
        TruncatedEventCount = truncatedEventCount;
        Outputs = Array.AsReadOnly(outputs.ToArray());
        Incoming = incoming;
        Completion = !deadlineExceeded && (incoming is null || incoming.Pending == 0) && Outputs.All(output => output.Pending == 0 && output.WriterFlushStatus is LogWriterFlushStatus.Succeeded or LogWriterFlushStatus.Failed) ? LogCompletion.Completed : LogCompletion.TimedOut;
        Succeeded = Completion == LogCompletion.Completed && (incoming is null || incoming.Failed == 0 && incoming.Abandoned == 0 && incoming.RejectedByWriters == 0) && Outputs.All(output => output.Pending == 0 && output.Failed == 0 && output.Abandoned == 0 && output.WriterFlushSucceeded);
        LossObserved = registrationLoss || invalidEventCount > 0 || truncatedEventCount > 0 || incoming is { Failed: > 0 } or { Abandoned: > 0 } or { QueueFull: > 0 } or { RejectedByWriters: > 0 } || Outputs.Any(output => output.Failed > 0 || output.Abandoned > 0 || output.Rejections.QueueFull > 0 || output.Rejections.Unavailable > 0 || output.WriterFlushStatus == LogWriterFlushStatus.Failed);
    }

    /// <summary>Whether all covered dispositions settled within the wait.</summary>
    public LogCompletion Completion { get; }
    /// <summary>Whether completion was timely, every covered record was written and every writer flush succeeded.</summary>
    public bool Succeeded { get; }
    /// <summary>Whether initial capture/rejection facts or covered terminal failures confirm loss.</summary>
    public bool LossObserved { get; }
    /// <summary>The system UTC label captured when the fixed boundaries were registered.</summary>
    public DateTimeOffset BoundaryCapturedAtUtc { get; }
    /// <summary>The system UTC label of this final owned observation; not an elapsed deadline.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>The cumulative invalid-event count at registration plus later invalid processing through the covered incoming prefix.</summary>
    public long InvalidEventCount { get; }
    /// <summary>The cumulative truncated-event count at registration plus later truncation through the covered incoming prefix.</summary>
    public long TruncatedEventCount { get; }
    /// <summary>Owned console, file, then registered-writer observations; disabled outputs are omitted.</summary>
    public IReadOnlyList<LogOutputDrainResult> Outputs { get; }
    /// <summary>Fixed incoming prefix accounting, separate from downstream writer delivery.</summary>
    public LogIncomingSnapshot? Incoming { get; }
}
