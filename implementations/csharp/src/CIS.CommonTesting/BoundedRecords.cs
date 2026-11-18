namespace CIS.CommonTesting;

internal sealed class BoundedRecords<T>
{
    private readonly object gate = new();
    private readonly Queue<T> items = new();
    private readonly int capacity;
    private long dropped;
    private long total;

    internal BoundedRecords(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 65_536);
        this.capacity = capacity;
    }

    internal long Dropped
    {
        get
        {
            lock (gate)
            {
                return dropped;
            }
        }
    }

    internal long Total
    {
        get
        {
            lock (gate)
            {
                return total;
            }
        }
    }

    internal void Add(T item)
    {
        lock (gate)
        {
            if (items.Count == capacity)
            {
                items.Dequeue();
                dropped = Increment(dropped);
            }
            items.Enqueue(item);
            total = Increment(total);
        }
    }

    internal IReadOnlyList<T> Snapshot()
    {
        lock (gate)
        {
            return Array.AsReadOnly(items.ToArray());
        }
    }

    internal void Clear()
    {
        lock (gate)
        {
            items.Clear();
            dropped = 0;
            total = 0;
        }
    }

    internal static long Increment(long value) => value == long.MaxValue ? value : value + 1;
}
