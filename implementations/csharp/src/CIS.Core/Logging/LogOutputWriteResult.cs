namespace CIS.Logging;

/// <summary>An immutable observation of one output admission, not delivery.</summary>
public sealed class LogOutputWriteResult
{
    internal LogOutputWriteResult(string outputName, LogOutputWriteStatus status, long? acceptedSequence)
    {
        OutputName = outputName;
        Status = status;
        AcceptedSequence = acceptedSequence;
    }

    /// <summary>The enabled output name, console or file.</summary>
    public string OutputName { get; }
    /// <summary>The observed admission status.</summary>
    public LogOutputWriteStatus Status { get; }
    /// <summary>The output's own positive ticket on successful admission; otherwise null.</summary>
    public long? AcceptedSequence { get; }
}
