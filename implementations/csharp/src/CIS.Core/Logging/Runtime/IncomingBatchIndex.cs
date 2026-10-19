namespace CIS.Logging.Runtime;

// Sole-consumer metadata: sorting never moves physical slots or changes an event's creation key.
internal sealed class IncomingBatchIndex(int capacity)
{
    private readonly Entry[] entries = new Entry[capacity];
    internal int Count { get; private set; }

    internal void Capture(IncomingRingBuffer ring)
    {
        Release();
        for (int slot = 0; slot < ring.Capacity; slot++)
        {
            if (ring.Read(slot) is { } item) { entries[Count++] = new(slot, item); }
        }
        Array.Sort(entries, 0, Count, EntryComparer.Instance);
    }

    internal Entry At(int index) => entries[index];
    internal void Clear(int index) => entries[index] = default;
    internal void Release()
    {
        Array.Clear(entries, 0, Count);
        Count = 0;
    }

    internal readonly record struct Entry(int Slot, IncomingEvent? Item);
    private sealed class EntryComparer : IComparer<Entry>
    {
        internal static readonly EntryComparer Instance = new();
        public int Compare(Entry first, Entry second)
        {
            int age = first.Item!.CreationTimestamp.CompareTo(second.Item!.CreationTimestamp);
            return age == 0 ? first.Slot.CompareTo(second.Slot) : age;
        }
    }
}
