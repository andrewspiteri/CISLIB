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
        Suppressed = counts.Suppressed; DuplicateSuppressed = counts.DuplicateSuppressed; DuplicateKeyEvictions = counts.DuplicateKeyEvictions;
        Consumed = counts.Consumed; Evicted = counts.Evicted; Waiting = counts.Waiting;
        ActivePublishers = counts.ActivePublishers; IsSaturated = counts.IsSaturated; IsExact = counts.IsExact;
        ErrorCode = counts.Failure;
    }
    /// <summary>Lifetime admissions observed here; not a captured batch target or a sequence cutoff.</summary>
    public long Accepted { get; }
    /// <summary>Events processed through policy suppression or writer admission; individual writers may still reject or fail.</summary>
    public long Processed { get; }
    /// <summary>Events whose background preparation or dispatch failed.</summary>
    public long Failed { get; }
    /// <summary>Waiting frontend events actually discarded by shutdown cleanup.</summary>
    public long Abandoned { get; }
    /// <summary>Observed waiting plus active backend preparation; may include work outside a flush batch.</summary>
    public long Pending { get; }
    /// <summary>Legacy incoming capacity rejection count. Rotating frontend admission leaves this zero.</summary>
    public long QueueFull { get; }
    /// <summary>Processed events rejected by at least one interested writer; these events are already counted in Processed.</summary>
    public long RejectedByWriters { get; }
    /// <summary>Processed events intentionally excluded by global filtering, all routing decisions or duplicate policy. Not loss.</summary>
    public long Suppressed { get; }
    /// <summary>The subset of Suppressed excluded by the configured duplicate policy.</summary>
    public long DuplicateSuppressed { get; }
    /// <summary>Oldest duplicate identities evicted at the configured capacity; no event is discarded by eviction.</summary>
    public long DuplicateKeyEvictions { get; }
    /// <summary>Lifetime admissions; synonym for Accepted.</summary>
    public long Admitted => Accepted;
    /// <summary>Occurrences handed to the consumer, including backend work still active or failed.</summary>
    public long Consumed { get; }
    /// <summary>Occurrences displaced by actual producer exchanges.</summary>
    public long Evicted { get; }
    /// <summary>Observed physical waiting gauge; independently read live counters may be skewed.</summary>
    public long Waiting { get; }
    /// <summary>Observed registration/publication bookkeeping in flight, including a late attempt that must fail its closed-state recheck.</summary>
    public long ActivePublishers { get; }
    /// <summary>Whether a frontend lifetime counter reached its representable limit.</summary>
    public bool IsSaturated { get; }
    /// <summary>Whether closed, settled and unsaturated frontend lifetime counts form an exact observation.</summary>
    public bool IsExact { get; }
    /// <summary>A retained fixed safe frontend worker failure code, otherwise null.</summary>
    public string? ErrorCode { get; }
}
