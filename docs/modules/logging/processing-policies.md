# Logging processing policies

Configure `LogProcessingOptions` at runtime creation. Callback lists and registrations are captured at startup. Existing `Create()` and `Create(writers)` calls have no processing callbacks and no duplicate suppression.

```csharp
var policies = new LogProcessingOptions
{
    Interceptors = [record => new LogEventChanges
    {
        Message = "redacted", ClearMessageTemplate = true, RemoveException = true
    }],
    Filter = record => record.EventName != "diagnostic.noise",
    Route = (destination, record) => destination != "file" || record.Level >= CoreLogLevel.Warning,
    DuplicateSuppression = new LogDuplicateSuppression(
        record => record.Record.TryGetProperty("truncated", out _) ? null : record.Record.GetProperty("attributes").TryGetProperty("occurrence_id", out var id)
            ? record.EventName + ":" + record.Level + ":" + id.GetString() : null,
        window: TimeSpan.FromSeconds(5), capacity: 1024)
};
await using var runtime = CoreLogRuntime.Create(policies);
var logger = runtime.CreateLogger("Orders");
logger.Info("Starting orders"); // event_name = log.message
logger.Info("order.received", "Order received");
logger.WarnLazy(() => new LogEventData
{
    EventName = "order.retry", Message = "Retry requested",
    Attributes = new Dictionary<string, LogValue> { ["attempt"] = LogValue.Integer(2) }
});
```

The example key is an application choice, not a default. Include customer, severity, exception or other occurrence facts when they distinguish events. Template identity alone does not establish a duplicate. Null keys bypass suppression. The example bypasses truncated records because omitted occurrence facts might otherwise produce a false match.

## Ordering and ownership

Severity checks and caller-owned capture precede bounded incoming admission. The incoming worker runs ordered interceptors, the global filter, destination routing and duplicate checking. Encoding, rendering and writer admission follow. Routing applies after destination thresholds and cannot enable a destination or lower its threshold. Microsoft logger events use this same pipeline.

`LogProcessingEvent.Record` is an immutable owned JSON view projected to the configured entry-byte limit before each callback. Each interceptor sees preceding changes. Omitted data is removed from the retained facts and cannot reappear after a later transformation; the truncated marker exposes that omission. `LogEventChanges` replaces message/template or the complete typed attribute map, clears the template or removes the exception tree. Replacement attributes include scope attributes; the provider's integer `event_id` remains protected. Changes are copied immediately under normal Unicode, shape and size limits. Identity, severity, occurrence time and correlation remain captured. Replacing the message alone does not redact its template, attributes or exception; applications select all necessary fields.

Callbacks run serially outside admission locks. Applications own callback closure state and any external synchronization. Exceptions, null interceptor results, invalid changed values and overlong duplicate keys fail the event closed: no writer receives it, Incoming.Failed increases, and covered flush/stop fails. Diagnostics expose no raw callback exception message. Later input continues. Registered interceptors are never skipped because a later predicate would exclude the event.

Logging from a callback execution flow is rejected with `RecursiveProcessing` before a factory runs; IsEnabled returns false. The restriction follows child execution contexts and applies across runtimes. Public frontend flush/stop return Failed/RecursiveProcessing without initiating a control operation; diagnostics remain available. The original stop deadline still expires while a callback blocks. Its already-claimed event remains Consumed with unresolved backend work; it is never relabeled Abandoned. That active operation may finish later, while waiting frontend events are abandoned by safe cleanup and expired backend work cannot start a later write. Completion waits for actual processing and I/O. See the [current frontend contract](incoming-pipeline.md).

## Duplicate window and accounting

Suppression is opt-in, using an ordinal application-supplied string key of at most 1024 UTF-16 units and a positive application-supplied window. Capacity defaults to 1024 keys and accepts 1–65536. Time is monotonic. The window starts when a nonsuppressed event reaches at least one writer; suppressed occurrences do not extend it. Rejection by every writer does not reserve the key. At capacity, the oldest key is evicted without discarding an event. Suppression is neither durable nor a delivery/retry guarantee.

`Accepted = Processed + Failed + Abandoned + Pending` remains valid. Processed includes intentional suppression. Suppressed is its subset excluded by filtering, all routes or duplicate policy; DuplicateSuppressed is the duplicate subset. DuplicateKeyEvictions reports cache turnover. These decisions are not loss and do not themselves fail a flush. Destination exclusions increment that output's Filtered count. Downstream rejection and failure remain unsuccessful-delivery observations.

## Convenience calls

Trace, Debug, Info, Warn, Error and Critical accept a message alone, an explicit event name plus message, or LogEventData. Message-only calls use `log.message`, matching the provider fallback. `TraceLazy` through `CriticalLazy` accept a LogEventData factory, preserving WriteLazy's at-most-once and disabled/stopped skipping rules. Ordinary interpolation happens before a call; use lazy calls for expensive work. All variants share the capture and incoming pipeline.

This is the implemented unpublished API. Final coverage, mutation, compatibility and both-platform release qualification remain separate.
