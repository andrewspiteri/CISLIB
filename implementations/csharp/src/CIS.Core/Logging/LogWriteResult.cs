namespace CIS.Logging;

/// <summary>An owned immutable observation of capture and independent output admission.</summary>
public sealed class LogWriteResult
{
    internal LogWriteResult(LogWriteOutcome outcome, IEnumerable<LogOutputWriteResult> outputs, string? errorCode = null, bool isTruncated = false)
    {
        Outcome = outcome;
        Outputs = Array.AsReadOnly(outputs.ToArray());
        ErrorCode = errorCode;
        IsTruncated = isTruncated;
    }

    /// <summary>The aggregate outcome.</summary>
    public LogWriteOutcome Outcome { get; }
    /// <summary>Owned observations in console-then-file order; disabled outputs are omitted.</summary>
    public IReadOnlyList<LogOutputWriteResult> Outputs { get; }
    /// <summary>A fixed safe capture error code for InvalidEvent; otherwise null.</summary>
    public string? ErrorCode { get; }
    /// <summary>Whether a valid captured event lost payload, including when admission later rejected it.</summary>
    public bool IsTruncated { get; }
}
