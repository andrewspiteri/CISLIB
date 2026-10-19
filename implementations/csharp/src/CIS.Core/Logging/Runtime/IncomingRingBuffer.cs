using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// Producers own no slot before the final exchange. Only the consumer claims/removes waiting events.
internal sealed class IncomingRingBuffer
{
    private readonly IncomingEvent?[] slots;
    private readonly int[] budgets;
    private readonly AtomicLogCounter admitted = new(), consumed = new(), evicted = new(), abandoned = new();
    private readonly AtomicLogCounter[] evictedByLevel = Enumerable.Range(0, 6).Select(_ => new AtomicLogCounter()).ToArray();
    private long cursor = -1, publishers, waiting;
    private int closed;

    internal IncomingRingBuffer(int capacity, LoggingOptions options)
    {
        if (capacity is < LoggingOptions.MinimumQueueCapacity or > LoggingOptions.MaximumQueueCapacity)
        { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        ArgumentNullException.ThrowIfNull(options);
        options.ValidateIncomingSettings();
        slots = new IncomingEvent?[capacity];
        int?[] overrides = [options.IncomingTraceScanPercentage, options.IncomingDebugScanPercentage,
            options.IncomingInformationScanPercentage, options.IncomingWarningScanPercentage,
            options.IncomingErrorScanPercentage, options.IncomingCriticalScanPercentage];
        budgets = new int[6];
        int fallback = options.IncomingScanPercentage;
        for (int level = 0; level < budgets.Length; level++)
        { budgets[level] = Math.Max(1, (capacity * (overrides[level] ?? fallback) + 99) / 100); }
    }

    internal int Capacity => slots.Length;
    internal bool IsClosed => Volatile.Read(ref closed) != 0;
    internal long ActivePublishers => Interlocked.Read(ref publishers);
    internal int Budget(CoreLogLevel level) => budgets[(int)level];

    internal IncomingAdmission TryPublish(IncomingEvent? item, out IncomingPublication publication)
    {
        publication = default;
        if (item is null || item.Facts is null || item.Level is < CoreLogLevel.Trace or > CoreLogLevel.Critical)
        { return IncomingAdmission.Invalid; }
        if (!TryEnterPublisher()) { return IncomingAdmission.Stopped; }
        try
        {
            int index = SelectSlot(item, out int probes);
            publication = Exchange(index, item, probes);
            return IncomingAdmission.Accepted;
        }
        finally { ExitPublisher(); }
    }

    // Fixed steps are also exercised individually by controlled native races; no caller callback seam.
    internal bool TryEnterPublisher()
    {
        if (IsClosed) { return false; }
        Interlocked.Increment(ref publishers);
        if (!IsClosed) { return true; }
        ExitPublisher();
        return false;
    }

    internal void ExitPublisher() => Interlocked.Decrement(ref publishers);
    internal void Close() => Interlocked.Exchange(ref closed, 1);

    internal int SelectSlot(IncomingEvent item, out int probes)
    {
        int index = (int)(unchecked((ulong)Interlocked.Increment(ref cursor)) % (ulong)slots.Length);
        int selected = index;
        IncomingEvent? candidate = null;
        int limit = budgets[(int)item.Level];
        probes = 0;
        for (int offset = 0; offset < limit; offset++)
        {
            probes++;
            IncomingEvent? observed = Volatile.Read(ref slots[index]);
            if (observed is null) { return index; }
            if (candidate is null || observed.Level < candidate.Level ||
                observed.Level == candidate.Level && observed.CreationTimestamp < candidate.CreationTimestamp)
            { selected = index; candidate = observed; }
            if (++index == slots.Length) { index = 0; }
        }
        return selected;
    }

    internal IncomingPublication Exchange(int index, IncomingEvent item, int probes)
    {
        IncomingEvent? displaced = Interlocked.Exchange(ref slots[index], item);
        admitted.Increment();
        if (displaced is null) { Interlocked.Increment(ref waiting); }
        else
        {
            evicted.Increment();
            evictedByLevel[(int)displaced.Level].Increment();
        }
        return new(index, probes, displaced);
    }

    internal IncomingEvent? Read(int index) => Volatile.Read(ref slots[index]);

    internal bool TryClaim(int index, IncomingEvent expected)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref slots[index], null, expected), expected)) { return false; }
        consumed.Increment();
        Interlocked.Decrement(ref waiting);
        return true;
    }

    // Only the sole consumer/cleanup coordinator calls this, never a producer or a timer callback.
    internal IncomingEvent? AbandonSlot(int index)
    {
        IncomingEvent? removed = Interlocked.Exchange(ref slots[index], null);
        if (removed is not null) { abandoned.Increment(); Interlocked.Decrement(ref waiting); }
        return removed;
    }

    internal long EvictedAt(CoreLogLevel level) => evictedByLevel[(int)level].Value;

    internal bool TryAbandon(int index, IncomingEvent expected)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref slots[index], null, expected), expected)) { return false; }
        abandoned.Increment(); Interlocked.Decrement(ref waiting);
        return true;
    }

    internal IncomingRingSnapshot Capture() => new(admitted.Value, consumed.Value, evicted.Value, abandoned.Value,
        Math.Clamp(Interlocked.Read(ref waiting), 0, slots.Length), ActivePublishers,
        admitted.IsSaturated || consumed.IsSaturated || evicted.IsSaturated || abandoned.IsSaturated);
}

internal enum IncomingAdmission { Accepted, Stopped, Invalid }
internal readonly record struct IncomingPublication(int Slot, int Probes, IncomingEvent? Displaced);
// Independent atomic reads: a live snapshot is deliberately not a coherent lifetime cut.
internal readonly record struct IncomingRingSnapshot(long Admitted, long Consumed, long Evicted, long Abandoned,
    long Waiting, long ActivePublishers, bool IsSaturated);

internal sealed record IncomingEvent(long Sequence, CoreLogLevel Level, CapturedEventFacts Facts)
{
    internal long CreationTimestamp => Facts.TimestampUtc.UtcTicks;
}
