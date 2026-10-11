namespace CIS.Logging.Values;

// One budget per root. Reused immutable subtrees still count once per containment occurrence.
internal sealed class LogValueProjection
{
    internal const int MaximumNodes = 1024;
    internal const int MaximumDepth = 8;
    private int remainingNodes = MaximumNodes - 1;

    internal LogValue Array(IEnumerable<LogValue> items, int depth, bool truncated = false)
    {
        var owned = new List<LogValue>();
        foreach (LogValue item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("Use LogValue.Null() for explicit null array elements.", nameof(items));
            }
            LogValue? retained = Project(item, depth + 1);
            if (retained is null)
            {
                truncated = true;
                break;
            }
            owned.Add(retained);
        }
        return LogValue.OwnedArray(owned, truncated);
    }

    internal LogValue Map(IReadOnlyDictionary<string, LogValue> properties, int depth, bool truncated = false)
    {
        var owned = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        foreach ((string key, LogValue value) in properties)
        {
            if (key is null || !LogValueText.IsValidKey(key) || value is null || owned.ContainsKey(key))
            {
                throw new ArgumentException("Use unique ordinal keys of 1–255 valid Unicode scalars without controls, and explicit LogValue values.", nameof(properties));
            }
            LogValue? retained = Project(value, depth + 1);
            if (retained is null)
            {
                truncated = true;
                break;
            }
            owned.Add(key, retained);
        }
        return LogValue.OwnedMap(owned, truncated);
    }

    private LogValue? Project(LogValue value, int depth)
    {
        if (remainingNodes == 0 || depth > MaximumDepth)
        {
            return null;
        }
        if (value.NodeCount <= remainingNodes && depth - 1 + value.Depth <= MaximumDepth)
        {
            remainingNodes -= value.NodeCount;
            return value;
        }
        remainingNodes--;
        return value.Kind switch
        {
            LogValueKind.Array => Array(value.ArrayItems, depth, value.IsTruncated),
            LogValueKind.Map => Map(value.MapProperties, depth, value.IsTruncated),
            _ => throw new InvalidOperationException("A supported scalar cannot exceed a positive shape budget.")
        };
    }
}
