using CIS.Logging.Exceptions;

namespace CIS.Logging.Events;

// Allocates a byte budget to owned payloads in the documented priority order.
internal static class EventByteProjection
{
    private static bool Descriptive(string key) => key is "os_description" or "runtime_description";

    internal static CapturedEventFacts Fit(CapturedEventFacts source, int maximum)
    {
        var resource = source.Resource.MapProperties.Where(pair => !Descriptive(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        CapturedEventFacts envelope = Copy(source, string.Empty, LogValue.Map(resource), LogValue.Map(new Dictionary<string, LogValue>()), null, null);
        int remaining = maximum - EventWireFormat.Size(envelope, maximum);
        if (remaining < 0) { throw new EventEnvelopeTooLargeException(); }
        string message = JsonTextBytes.Prefix(source.Message, remaining, out _);
        remaining -= JsonTextBytes.Size(message, maximum) - 2;
        LogValue attributes = Value(source.Attributes, remaining + 2, out int attributeSize)!;
        remaining -= attributeSize - 2;
        CapturedException? error = null;
        if (source.Exception is { } original)
        {
            int overhead = EventWireFormat.PropertySize("exception", 0);
            error = Exception(original, remaining - overhead, out int errorSize);
            if (error is not null) { remaining -= overhead + errorSize; }
        }
        string? template = null;
        if (source.MessageTemplate is { } originalTemplate)
        {
            int overhead = EventWireFormat.PropertySize("message_template", 2);
            if (remaining >= overhead)
            {
                string prefix = JsonTextBytes.Prefix(originalTemplate, remaining - overhead, out _);
                if (prefix.Length != 0 || originalTemplate.Length == 0)
                {
                    template = prefix; remaining -= overhead + JsonTextBytes.Size(prefix, maximum) - 2;
                }
            }
        }
        foreach ((string key, LogValue description) in source.Resource.MapProperties.Where(pair => Descriptive(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            int overhead = EventWireFormat.PropertySize(key, 0);
            LogValue? retained = Value(description, remaining - overhead, out int size);
            if (retained is not null) { resource[key] = retained; remaining -= overhead + size; }
        }
        return Copy(source, message, LogValue.Map(resource), attributes, error, template);
    }

    private static CapturedEventFacts Copy(CapturedEventFacts source, string message, LogValue resource,
        LogValue attributes, CapturedException? error, string? template) => new(source.TimestampUtc, source.TraceId, source.SpanId,
            source.Status, source.EventName, message, template, source.OperationId, source.Service, source.Environment,
            source.Version, source.Component, resource, attributes, error, isTruncated: true);

    private static LogValue? Value(LogValue source, int available, out int size)
    {
        size = 0;
        if (available < 1) { return null; }
        int fullSize = EventWireFormat.ValueSize(source, available);
        if (fullSize <= available) { size = fullSize; return source; }
        if (available < 2) { return null; }
        if (source.Kind == LogValueKind.String)
        {
            string prefix = JsonTextBytes.Prefix(source.Scalar.Text!, available - 2, out _);
            if (prefix.Length == 0 && source.Scalar.Text!.Length != 0) { return null; }
            size = JsonTextBytes.Size(prefix, available);
            return LogValue.String(prefix);
        }
        if (source.Kind == LogValueKind.Array)
        {
            size = 2;
            var children = new List<LogValue>();
            foreach (LogValue child in source.ArrayItems)
            {
                int comma = children.Count == 0 ? 0 : 1;
                LogValue? retained = Value(child, available - size - comma, out int childSize);
                if (retained is not null) { children.Add(retained); size += comma + childSize; }
            }
            return LogValue.OwnedArray(children, truncated: true);
        }
        if (source.Kind == LogValueKind.Map)
        {
            size = 2;
            var children = new Dictionary<string, LogValue>(StringComparer.Ordinal);
            foreach ((string key, LogValue child) in source.MapProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                int overhead = (children.Count == 0 ? 0 : 1) + JsonTextBytes.Size(key, available) + 1;
                LogValue? retained = Value(child, available - size - overhead, out int childSize);
                if (retained is not null) { children[key] = retained; size += overhead + childSize; }
            }
            return LogValue.OwnedMap(children, truncated: true);
        }
        // Explicit numeric strings, UUIDs, timestamps and other scalars are indivisible.
        return null;
    }

    private static CapturedException? Exception(CapturedException source, int available, out int size)
    {
        size = 0;
        if (available <= 0) { return null; }
        int fullSize = EventWireFormat.ExceptionSize(source, available);
        if (fullSize <= available) { size = fullSize; return source; }
        size = 1 + EventWireFormat.PropertySize("type", JsonTextBytes.Size(source.Type, available))
            + EventWireFormat.PropertySize("message", 2) + EventWireFormat.PropertySize("truncated", 4);
        if (size > available) { size = 0; return null; }
        string message = JsonTextBytes.Prefix(source.Message, available - size, out _);
        size += JsonTextBytes.Size(message, available) - 2;
        string? stack = null;
        if (source.StackTrace is { } originalStack)
        {
            int overhead = EventWireFormat.PropertySize("stack_trace", 2);
            if (available - size >= overhead)
            {
                stack = JsonTextBytes.Prefix(originalStack, available - size - overhead, out _);
                size += overhead + JsonTextBytes.Size(stack, available) - 2;
            }
        }
        var children = new List<CapturedException>();
        int arrayOverhead = EventWireFormat.PropertySize("inner_exceptions", 2);
        foreach (CapturedException child in source.InnerExceptions)
        {
            int overhead = children.Count == 0 ? arrayOverhead : 1;
            CapturedException? retained = Exception(child, available - size - overhead, out int childSize);
            if (retained is not null) { children.Add(retained); size += overhead + childSize; }
        }
        return new CapturedException(source.Type, message, stack, children, isTruncated: true);
    }
}
