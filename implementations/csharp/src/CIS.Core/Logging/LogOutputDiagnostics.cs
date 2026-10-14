using CIS.Logging.Runtime;

namespace CIS.Logging;

/// <summary>An owned immutable live observation of one enabled output.</summary>
public sealed class LogOutputDiagnostics
{
    internal LogOutputDiagnostics(string name, OutputAccounting counts, bool recoveredPartialSegment = false)
    {
        OutputName = name; State = counts.State; AcceptedSequence = counts.AcceptedSequence;
        Filtered = counts.Filtered; AdmissionAttempts = counts.AdmissionAttempts; Accepted = counts.Accepted;
        QueueFull = counts.QueueFull; Unavailable = counts.Unavailable; Written = counts.Written;
        Failed = counts.Failed; Abandoned = counts.Abandoned; Pending = counts.Pending;
        LastFaultCode = counts.LastFaultCode; LastFaultAtUtc = counts.LastFaultAtUtc;
        RecoveredPartialSegment = recoveredPartialSegment;
    }
    /// <summary>Whether this runtime preserved a partial file segment during successful startup. Retained after stop; false for console.</summary>
    public bool RecoveredPartialSegment { get; }
    /// <summary>The configured destination name.</summary>
    public string OutputName { get; }
    /// <summary>The output lifecycle state at this observation.</summary>
    public LogOutputState State { get; }
    /// <summary>The last successfully admitted ticket; zero when no entry was accepted.</summary>
    public long AcceptedSequence { get; }
    /// <summary>Cumulative filtered calls, saturating at Int64.MaxValue.</summary>
    public long Filtered { get; }
    /// <summary>Accepted plus interested rejections, saturating at Int64.MaxValue.</summary>
    public long AdmissionAttempts { get; }
    /// <summary>Cumulative successfully admitted records.</summary>
    public long Accepted { get; }
    /// <summary>Cumulative incoming queue-full rejections, saturating at Int64.MaxValue.</summary>
    public long QueueFull { get; }
    /// <summary>Cumulative interested faulted-output rejections, saturating at Int64.MaxValue.</summary>
    public long Unavailable { get; }
    /// <summary>Accepted records whose complete write was confirmed.</summary>
    public long Written { get; }
    /// <summary>Accepted records whose attempted write terminated unsuccessfully.</summary>
    public long Failed { get; }
    /// <summary>Accepted records discarded before an attempted write.</summary>
    public long Abandoned { get; }
    /// <summary>Accepted unresolved records, including in-flight work.</summary>
    public long Pending { get; }
    /// <summary>The latest fixed safe fault category, retained after stop; no exception payload. Cleanup can supersede a delivery category, and a shutdown-timer cleanup fault applies to every enabled output. Stop results and delivery counts retain their original meaning.</summary>
    public string? LastFaultCode { get; }
    /// <summary>The system UTC time of the last fault publication, retained after stop.</summary>
    public DateTimeOffset? LastFaultAtUtc { get; }
}
