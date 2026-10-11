using System.Collections;
using System.Globalization;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal sealed class LogValueObservedInputs
{
    internal int Enumerations { get; private set; }
    internal int Produced { get; private set; }
    internal bool Disposed { get; private set; }
    internal LogValue[]? Values { get; init; }
    internal string? InvalidLastKey { get; init; }
    internal bool DuplicateLastKey { get; init; }

    internal IEnumerable<LogValue> Sequence()
    {
        Enumerations++;
        try
        {
            for (int index = 0; Values is null || index < Values.Length; index++)
            {
                if (++Produced > 1024)
                {
                    throw new InvalidOperationException("Core requested an excessive input prefix.");
                }
                yield return Values is null ? LogValue.Integer(42) : Values[index];
            }
        }
        finally
        {
            Disposed = true;
        }
    }

    internal IReadOnlyDictionary<string, LogValue> Map() => new ObservedMap(this);

    private sealed class ObservedMap(LogValueObservedInputs owner) : IReadOnlyDictionary<string, LogValue>
    {
        public int Count => throw new InvalidOperationException("Do not query caller Count.");
        public IEnumerable<string> Keys => throw new InvalidOperationException("Do not query caller Keys.");
        public IEnumerable<LogValue> Values => throw new InvalidOperationException("Do not query caller Values.");
        public LogValue this[string key] => throw new InvalidOperationException("Do not query caller indexer.");
        public bool ContainsKey(string key) => throw new InvalidOperationException("Do not query caller ContainsKey.");
        public bool TryGetValue(string key, out LogValue value) => throw new InvalidOperationException("Do not query caller lookup.");

        public IEnumerator<KeyValuePair<string, LogValue>> GetEnumerator()
        {
            int index = 0;
            foreach (LogValue value in owner.Sequence())
            {
                string key = "k" + index.ToString("D4", CultureInfo.InvariantCulture);
                if (owner.Values is not null && index == owner.Values.Length - 1)
                {
                    key = owner.InvalidLastKey ?? key;
                    if (owner.DuplicateLastKey)
                    {
                        key = "k0000";
                    }
                }
                yield return new(key, value);
                index++;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
