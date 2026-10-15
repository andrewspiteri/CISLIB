using CIS.Logging.Runtime;

namespace CIS.Logging;

/// <summary>Owned incoming-stage accounting, separate from writer admission and delivery.</summary>
public sealed class LogIncomingSnapshot
{
    internal LogIncomingSnapshot(IncomingAccounting counts)
    {
        Accepted = counts.Accepted; Processed = counts.Processed; Failed = counts.Failed;
        Abandoned = counts.Abandoned; Pending = counts.Pending; QueueFull = counts.QueueFull;
        RejectedByWriters = counts.RejectedByWriters;
    }
    /// <summary>Accepted incoming events through this observation's sequence boundary.</summary>
    public long Accepted { get; }
    /// <summary>Events processed through writer admission; individual writers may still reject or fail.</summary>
    public long Processed { get; }
    /// <summary>Events whose background preparation or dispatch failed.</summary>
    public long Failed { get; }
    /// <summary>Accepted events discarded before writer admission when shutdown expired.</summary>
    public long Abandoned { get; }
    /// <summary>Accepted events awaiting processing, including active preparation.</summary>
    public long Pending { get; }
    /// <summary>New events rejected because the incoming buffer was full.</summary>
    public long QueueFull { get; }
    /// <summary>Processed events rejected by at least one interested writer; these events are already counted in Processed.</summary>
    public long RejectedByWriters { get; }
}
