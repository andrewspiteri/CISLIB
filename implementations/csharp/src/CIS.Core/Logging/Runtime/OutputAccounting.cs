namespace CIS.Logging.Runtime;

// Owned consistent observation of one output. Pending includes InFlight; no I/O or log routing.
internal readonly record struct OutputAccounting(long Filtered, long Accepted, long QueueFull, long Unavailable,
    long Written, long Failed, long Abandoned, int Pending, int InFlight, long AcceptedSequence,
    LogOutputState State = LogOutputState.Running, string? LastFaultCode = null, DateTimeOffset? LastFaultAtUtc = null)
{
    internal long Rejected => SaturatingSum(QueueFull, Unavailable);
    internal long AdmissionAttempts => SaturatingSum(Accepted, Rejected);
    private static long SaturatingSum(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
}
