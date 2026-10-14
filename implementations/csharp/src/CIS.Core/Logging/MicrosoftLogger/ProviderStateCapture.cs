using System.Collections;
using System.Globalization;
using CIS.Logging.Scopes;
using CIS.Logging.Values;

namespace CIS.Logging.MicrosoftLogger;

// A single bounded synchronous copy; no template parsing or arbitrary object serialization.
internal sealed class ProviderStateCapture
{
    private int remaining;
    private ProviderStateCapture(bool includeEventId) => remaining = LogValueProjection.MaximumNodes - (includeEventId ? 2 : 1);
    private readonly HashSet<object> ancestors = new(ReferenceEqualityComparer.Instance);

    internal static (string? Template, LogValue Attributes) Capture<TState>(TState state, int eventId)
    {
        var captured = CaptureRoot(state, eventId, includeEventId: true);
        return (captured.Template, captured.Attributes);
    }

    internal static CapturedOperationContext CaptureScope(IEnumerable<KeyValuePair<string, object?>> state)
    {
        var captured = CaptureRoot(state, 0, includeEventId: false);
        return CapturedOperationContext.FromAttributes(captured.Attributes, captured.OperationId);
    }

    private static (string? Template, LogValue Attributes, string? OperationId) CaptureRoot<TState>(TState state, int eventId, bool includeEventId)
    {
        var capture = new ProviderStateCapture(includeEventId);
        var attributes = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        if (includeEventId) { attributes["event_id"] = LogValue.Integer(eventId); }
        if (state is not IEnumerable<KeyValuePair<string, object?>> fields)
        { return (null, LogValue.OwnedMap(attributes, truncated: false), null); }
        string? template = null, operationId = null; bool truncated = false; int visited = 0;
        capture.Enter(fields);
        try
        {
            foreach ((string key, object? value) in fields)
            {
                if (++visited > LogValueProjection.MaximumNodes) { truncated = true; break; }
                if (key == "{OriginalFormat}")
                {
                    if (value is not null and not string) { throw new ArgumentException("OriginalFormat must be a string or null."); }
                    template = (string?)value; continue;
                }
                if (!includeEventId && key == "operation_id")
                {
                    if (value is string identifier)
                    {
                        operationId = CapturedOperationContext.NormalizeOperationId(identifier, "state");
                        attributes.Remove(key); continue;
                    }
                    operationId = null;
                }
                if (includeEventId && key == "event_id") { continue; }
                // Keep scanning bounded root metadata after the value budget is spent.
                if (capture.remaining == 0) { truncated = true; continue; }
                ValidateKey(key);
                LogValue? retained = capture.Value(value, depth: 2);
                if (retained is null) { truncated = true; continue; }
                attributes[key] = retained;
            }
        }
        finally { capture.ancestors.Remove(fields); }
        return (template, LogValue.OwnedMap(attributes, truncated), operationId);
    }

    private LogValue? Value(object? value, int depth)
    {
        if (remaining == 0 || depth > LogValueProjection.MaximumDepth) { return null; }
        if (value is LogValue existing && existing.NodeCount <= remaining && depth - 1 + existing.Depth <= LogValueProjection.MaximumDepth)
        { remaining -= existing.NodeCount; return existing; }
        remaining--;
        return value switch
        {
            null => LogValue.Null(),
            LogValue owned when owned.Kind == LogValueKind.Array => Array(owned.ArrayItems, depth, owned.IsTruncated),
            LogValue owned when owned.Kind == LogValueKind.Map => Map(owned.MapProperties.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)), depth, owned.IsTruncated),
            bool scalar => LogValue.Boolean(scalar),
            string scalar => LogValue.String(scalar),
            char scalar => LogValue.String(new string(scalar, 1)),
            sbyte scalar => LogValue.Integer(scalar),
            byte scalar => LogValue.Integer(scalar),
            short scalar => LogValue.Integer(scalar),
            ushort scalar => LogValue.Integer(scalar),
            int scalar => LogValue.Integer(scalar),
            uint scalar => LogValue.Integer(scalar),
            long scalar => LogValue.Integer(scalar),
            ulong scalar when scalar <= 9_007_199_254_740_991 => LogValue.Integer((long)scalar),
            float scalar => LogValue.Number(scalar),
            double scalar => LogValue.Number(scalar),
            decimal scalar => Decimal(scalar),
            Guid scalar => LogValue.Uuid(scalar),
            DateTimeOffset scalar => LogValue.Timestamp(scalar),
            DateTime scalar when scalar.Kind == DateTimeKind.Utc => LogValue.Timestamp(new DateTimeOffset(scalar)),
            IEnumerable<KeyValuePair<string, object?>> properties => Map(properties, depth),
            IDictionary properties => Map(Entries(properties), depth, owner: properties),
            IEnumerable items => Array(items, depth),
            _ => throw new ArgumentException("Use supported scalar, collection or explicit LogValue inputs.")
        };
    }

    private LogValue Array(IEnumerable items, int depth, bool truncated = false)
    {
        Enter(items);
        try
        {
            var owned = new List<LogValue>();
            foreach (object? item in items)
            {
                LogValue? retained = Value(item, depth + 1);
                if (retained is null) { truncated = true; break; }
                owned.Add(retained);
            }
            return LogValue.OwnedArray(owned, truncated);
        }
        finally { ancestors.Remove(items); }
    }

    private LogValue Map(IEnumerable<KeyValuePair<string, object?>> properties, int depth, bool truncated = false, object? owner = null)
    {
        owner ??= properties; Enter(owner);
        try
        {
            var owned = new Dictionary<string, LogValue>(StringComparer.Ordinal);
            foreach ((string key, object? value) in properties)
            {
                ValidateKey(key);
                LogValue? retained = Value(value, depth + 1);
                if (retained is null) { truncated = true; break; }
                owned[key] = retained;
            }
            return LogValue.OwnedMap(owned, truncated);
        }
        finally { ancestors.Remove(owner); }
    }

    private static IEnumerable<KeyValuePair<string, object?>> Entries(IDictionary properties)
    {
        foreach (DictionaryEntry entry in properties)
        { yield return new(entry.Key as string ?? throw new ArgumentException("Map keys must be strings."), entry.Value); }
    }

    private void Enter(object collection)
    {
        if (!ancestors.Add(collection)) { throw new ArgumentException("Cyclic state is unsupported."); }
    }
    private static void ValidateKey(string key)
    {
        if (key is null || !LogValueText.IsValidKey(key)) { throw new ArgumentException("Use valid ordinal attribute keys."); }
    }
    private static LogValue Decimal(decimal value)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        return LogValue.DecimalString(text.Contains('.', StringComparison.Ordinal) ? text : text + ".0");
    }
}
