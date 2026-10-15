using System.Buffers;
using System.Text.Json;
using CIS.Logging.Events;

namespace CIS.Logging.Runtime;

// One incoming worker owns this state. Callback scope flows to child tasks so callbacks cannot create
// unbounded recursive input, including through Task.Run or a second runtime.
internal sealed class EventProcessing
{
    private static readonly AsyncLocal<bool> Active = new();
    private readonly Func<LogProcessingEvent, LogEventChanges>[] interceptors;
    private readonly Func<LogProcessingEvent, bool>? filter;
    private readonly Func<string, LogProcessingEvent, bool>? route;
    private readonly LogDuplicateSuppression? duplicate;
    private readonly TimeProvider clock;
    private readonly int maximumBytes;
    private readonly Dictionary<string, long> keys = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, long At)> order = new();

    internal EventProcessing(LogProcessingOptions options, TimeProvider clock, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(options.Interceptors);
        interceptors = options.Interceptors.ToArray();
        if (interceptors.Any(callback => callback is null)) { throw new ArgumentException("Interceptors must be nonnull.", nameof(options)); }
        filter = options.Filter; route = options.Route; duplicate = options.DuplicateSuppression; this.clock = clock; this.maximumBytes = maximumBytes;
    }
    internal static bool IsCallbackActive => Active.Value;

    internal ProcessingDecision Prepare(CapturedEventFacts facts, CoreLogLevel level, IReadOnlyList<string> destinations)
    {
        bool previous = Active.Value; Active.Value = true;
        try
        {
            facts = Bound(facts);
            LogProcessingEvent view = View(facts, level);
            foreach (var interceptor in interceptors)
            {
                LogEventChanges changes = interceptor(view) ?? throw new InvalidOperationException("An interceptor returned null.");
                facts = Bound(Apply(facts, changes)); view = View(facts, level);
            }
            if (filter is not null && !filter(view)) { return new(facts, [], true, false, null); }
            string[] selected = destinations.Where(name => route is null || route(name, view)).ToArray();
            if (selected.Length == 0) { return new(facts, selected, true, false, null); }
            string? key = duplicate?.Key(view);
            if (key is { Length: > 1024 }) { throw new ArgumentException("Duplicate keys exceed the configured contract bound."); }
            long now = clock.GetTimestamp();
            Expire(now);
            return new(facts, selected, key is not null && keys.ContainsKey(key), key is not null && keys.ContainsKey(key), key);
        }
        finally { Active.Value = previous; }
    }

    // Called after actual writer admission, so rejected events do not poison the duplicate window.
    internal bool Remember(string? key)
    {
        if (key is null || duplicate is null) { return false; }
        long now = clock.GetTimestamp(); Expire(now);
        bool evicted = keys.Count == duplicate.Capacity;
        if (evicted) { keys.Remove(order.Dequeue().Key); }
        keys.Add(key, now); order.Enqueue((key, now));
        return evicted;
    }
    private void Expire(long now)
    {
        while (order.TryPeek(out var first) && clock.GetElapsedTime(first.At, now) >= duplicate!.Window)
        { order.Dequeue(); keys.Remove(first.Key); }
    }

    private static LogProcessingEvent View(CapturedEventFacts facts, CoreLogLevel level)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JsonTextBytes.Encoder })) { EventWireFormat.Write(writer, facts); writer.Flush(); }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return new(level, document.RootElement.Clone());
    }
    // Project the retained facts themselves. A later transformation must never resurrect omitted data
    // that an earlier redactor could not inspect, and callback views must not allocate unbounded JSON.
    private CapturedEventFacts Bound(CapturedEventFacts facts) => EventWireFormat.Size(facts, maximumBytes) <= maximumBytes
        ? facts : EventByteProjection.Fit(facts, maximumBytes);
    private static CapturedEventFacts Apply(CapturedEventFacts source, LogEventChanges changes)
    {
        string? message = changes.Message, template = changes.MessageTemplate;
        var attributes = changes.Attributes;
        bool clearTemplate = changes.ClearMessageTemplate, removeException = changes.RemoveException;
        bool messageClipped = false, templateClipped = false;
        string capturedMessage = message is null ? source.Message : EventProjection.CaptureText(message, out messageClipped);
        string? capturedTemplate = clearTemplate ? null : template is null ? source.MessageTemplate : EventProjection.CaptureText(template, out templateClipped);
        LogValue capturedAttributes = attributes is null ? source.Attributes : LogValue.Map(attributes);
        // Preserve the provider's protected integer identity even when all application attributes are redacted.
        if (source.HasProviderEventId && attributes is not null)
        {
            capturedAttributes = EventProjection.Merge(capturedAttributes,
                LogValue.Map(new Dictionary<string, LogValue> { ["event_id"] = source.Attributes.MapProperties["event_id"] }));
        }
        return new(source.TimestampUtc, source.TraceId, source.SpanId, source.Status, source.EventName,
            capturedMessage, capturedTemplate, source.OperationId, source.Service, source.Environment, source.Version,
            source.Component, source.Resource, capturedAttributes, removeException ? null : source.Exception,
            source.IsTruncated || messageClipped || templateClipped || capturedAttributes.IsTruncated,
            source.HasProviderEventId, attributes is null ? source.ExplicitAttributes : capturedAttributes);
    }
}

internal sealed record ProcessingDecision(CapturedEventFacts Facts, IReadOnlyList<string> Destinations, bool Suppressed, bool Duplicate, string? Key);
