namespace CIS.Metrics;

// Implemented on actual module owners, never on borrowing loggers/providers or a global latest-provider slot.
internal interface IMetricsSource
{
    void AttachMetrics(MetricsRuntime runtime);
}

// A live mutable module owns this one-time host claim. Immutable bootstrap report snapshots can be
// observed independently by each host without this claim or a retained runtime pointer.
// Host attachment itself is serialized during composition.
internal sealed class MetricsAttachment
{
    private MetricsRuntime? runtime;

    internal MetricsRuntime? Runtime => Volatile.Read(ref runtime);

    // Returns true exactly once so the owner creates its reusable module handles once.
    internal bool Attach(MetricsRuntime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        MetricsRuntime? existing = Interlocked.CompareExchange(ref runtime, owner, null);
        if (existing is not null && !ReferenceEquals(existing, owner))
        { throw new InvalidOperationException("A Metrics source cannot belong to more than one host runtime."); }
        return existing is null;
    }
}
