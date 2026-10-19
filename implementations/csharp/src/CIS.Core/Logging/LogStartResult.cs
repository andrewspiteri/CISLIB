namespace CIS.Logging;

/// <summary>A safe startup result. A successful runtime is owned by the application.</summary>
public sealed class LogStartResult
{
    internal LogStartResult(bool succeeded, string? errorCode = null)
    { Succeeded = succeeded; ErrorCode = errorCode; }
    /// <summary>Whether startup produced a running runtime.</summary>
    public bool Succeeded { get; }
    /// <summary>A fixed safe failure category; no exception or payload is retained.</summary>
    public string? ErrorCode { get; }
}
