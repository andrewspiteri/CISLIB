namespace CIS.Logging;

/// <summary>An owned immutable observation of capture and incoming admission.</summary>
public sealed class LogWriteResult
{
    internal LogWriteResult(LogWriteOutcome outcome, IEnumerable<LogOutputWriteResult> outputs, string? errorCode = null, bool isTruncated = false, long? incomingSequence = null)
    {
        Outcome = outcome;
        Outputs = Array.AsReadOnly(outputs.ToArray());
        ErrorCode = errorCode;
        IsTruncated = isTruncated;
        IncomingSequence = incomingSequence;
    }

    /// <summary>The aggregate outcome.</summary>
    public LogWriteOutcome Outcome { get; }
    /// <summary>Known early output decisions, in destination order. Empty for incoming acceptance because writer admission happens later.</summary>
    public IReadOnlyList<LogOutputWriteResult> Outputs { get; }
    /// <summary>A fixed safe capture or incoming-admission error code; otherwise null.</summary>
    public string? ErrorCode { get; }
    /// <summary>Whether caller-side capture omitted payload. Later encoding/rendering omissions are reported in diagnostics and flush/stop observations.</summary>
    public bool IsTruncated { get; }
    /// <summary>The positive incoming acceptance ticket, or null if not admitted. Acceptance does not confirm writer admission or delivery.</summary>
    public long? IncomingSequence { get; }
}
