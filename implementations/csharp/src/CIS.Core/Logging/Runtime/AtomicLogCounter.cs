namespace CIS.Logging.Runtime;

// One increment, at most one sticky write. Saturation never creates a producer CAS retry loop.
internal sealed class AtomicLogCounter
{
    private long value;
    private int saturated;

    internal void Increment()
    {
        if (Interlocked.Increment(ref value) <= 0) { Volatile.Write(ref saturated, 1); }
    }

    internal long Value
    {
        get
        {
            long observed = Interlocked.Read(ref value);
            return Volatile.Read(ref saturated) != 0 || observed < 0 ? long.MaxValue : observed;
        }
    }

    internal bool IsSaturated
    {
        get
        {
            long observed = Interlocked.Read(ref value);
            return Volatile.Read(ref saturated) != 0 || observed < 0 || observed == long.MaxValue;
        }
    }
}
