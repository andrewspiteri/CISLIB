namespace CIS.Logging;

/// <summary>Whether a fresh frontend target set was completely captured.</summary>
public enum LogFrontendCaptureStatus
{
    /// <summary>No complete target set exists; zero target counts must not be inferred.</summary>
    NoBatchCaptured = 0,
    /// <summary>The fixed target set and its frozen disposition counts are available.</summary>
    Captured = 1
}

/// <summary>A frozen observation of one captured consumer batch, separate from backend delivery.</summary>
public sealed class LogFrontendBatchSnapshot
{
    internal LogFrontendBatchSnapshot(long targetCount, long consumed, long evicted, long abandoned, long pending)
    {
        TargetCount = targetCount; Consumed = consumed; Evicted = evicted;
        Abandoned = abandoned; Pending = pending;
    }
    /// <summary>Number of distinct occurrence references captured during the pass.</summary>
    public long TargetCount { get; }
    /// <summary>Targets whose ownership was handed to the consumer; writing is not implied.</summary>
    public long Consumed { get; }
    /// <summary>Targets replaced before the consumer could claim them.</summary>
    public long Evicted { get; }
    /// <summary>Targets actually removed by shutdown cleanup.</summary>
    public long Abandoned { get; }
    /// <summary>Targets whose disposition is unresolved, including stale entries not yet classified.</summary>
    public long Pending { get; }
    /// <summary>Whether every target has a terminal frontend disposition.</summary>
    public bool IsComplete => Pending == 0;
    /// <summary>Whether every target was consumed without eviction or abandonment.</summary>
    public bool IsLossless => IsComplete && Evicted == 0 && Abandoned == 0;
}
