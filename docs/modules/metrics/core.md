---
title: Metrics Core API
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.core
---

# Metrics Core API

`CIS.Metrics` supplies the portable facade in the existing `CIS.Core` assembly.
Application/module code uses `IMetrics`, `MetricDefinition`, `MetricDimensions`,
`MetricObservation` and the CIS instrument interfaces. The optional integration
registers `IMetrics`; ordinary consumers neither construct the internal runtime
nor reference Meter, TagList, an SDK provider or exporter types. Existing modules
remain usable without that integration.

The [portable contract](../../../contracts/metrics/README.md) defines shared
meanings and [literal fixtures](../../../conformance/metrics/v1/primitive-cases.json)
pin required inputs/emissions/names. The actual Core types are in
[MetricContracts.cs](../../../implementations/csharp/src/CIS.Core/Metrics/MetricContracts.cs).
The recorded Windows SDK/exporter checks include the eight literal exported
families. See [CIS-0009 evidence](../../changes/CIS-0009/verification.md) for
executed scope, checkpoint currentness and remaining qualification; this guide
does not establish another platform or release pass.

## Reusable definitions

The constructor is:

```csharp
new MetricDefinition(
    name,
    unit: MetricUnit.Count,
    description: null,
    allowedDimensionKeys: null,
    histogramBounds: null);
```

It copies key/bound arrays and normalizes an omitted description to empty text.
Registration performs name/schema/unit/profile validation; construction alone
does not certify validity. Keys form an allowed ordinal set, not required values.
The unit enum is Count, Seconds, Bytes or Ratio. Counters/up/down additions take
`long`; gauges/histograms and direct timer recordings take `double`.

CreateCounter, CreateUpDownCounter, CreateGauge, CreateHistogram and CreateTimer
return reusable handles. Equivalent definitions reuse the handle. Kind, unit,
description, allowed key set and selected bounds must agree for one logical name.
Observable registration also requires an equivalent C# delegate. Register static
definitions once during service construction/startup, rather than per request.

Seconds distributions use the standard duration bounds when none are supplied.
Other histograms need explicit suitable bounds; do not apply seconds buckets to
bytes. Ordinary non-histogram instruments do not accept histogram bounds. Timers
require MetricUnit.Seconds.

## Recording and timing

`MetricDimensions` copies string tuples. `default` is empty; unknown, duplicate,
reserved or over-limit inputs are dropped at recording without converting
arbitrary objects. A valid zero quantity is distinct from an unavailable value.
Counter additions are nonnegative; up/down deltas may be negative; gauge values
are finite absolute values; histogram/timer values are finite and nonnegative.
Recording calls return no business-success acknowledgement.

For example, create these handles once for the owning service:

```csharp
using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

ICounter completed = metrics.CreateCounter(new MetricDefinition(
    "app.orders.completed", allowedDimensionKeys: ["outcome"]));
IUpDownCounter active = metrics.CreateUpDownCounter(new MetricDefinition(
    "app.orders.active", allowedDimensionKeys: ["operation"]));
ITimer duration = metrics.CreateTimer(new MetricDefinition(
    "app.orders.duration", MetricUnit.Seconds,
    allowedDimensionKeys: ["operation", "outcome"]));
```

Around the application's real work, preserve its original outcomes:

```csharp
var initial = new MetricDimensions(("operation", "submit"));
active.Add(+1, initial);
using ITimingScope elapsed = duration.Start(initial);
try
{
    await SubmitOrderAsync(cancellationToken);
    completed.Add(1, new MetricDimensions(("outcome", "success")));
    elapsed.Stop(new MetricDimensions(("outcome", "success")));
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    elapsed.Stop(new MetricDimensions(("outcome", "cancelled")));
    throw;
}
catch
{
    elapsed.Stop(new MetricDimensions(("outcome", "error")));
    throw;
}
finally { active.Add(-1, initial); }
```

The first Stop/Dispose fixes monotonic elapsed seconds and makes one recording
attempt. Further stops return that value and ignore completion arguments;
Dispose cannot record again. Invalid combined initial/completion keys finish the
scope while dropping the attempt. Dispose alone supplies no outcome. Known caller
cancellation does not establish timeout; only report distinctions the owner knows.

## Snapshots and ownership

`RegisterObservableGauge(definition, observe)` takes
`Func<IReadOnlyList<MetricObservation>>`. Read a bounded, thread-safe snapshot of
already-owned state. A `MetricObservation` contains `Value` and `Dimensions`.
Return an empty list when a fact is not known; an ordinary callback failure omits
its collection data with bounded diagnostics. The current Core default bounds one
callback result to 128 observations; oversized snapshots and invalid observations
are omitted. Valid sibling observations remain available.
Do not seed durations/histograms
with zero to make a series appear. Do no I/O or lifecycle work during collection.

The internal runtime borrows its host-owned native Meter, retains bounded
definitions/handles, and delegates aggregation to the platform/optional SDK.
Live mutable owners use explicit host attachment once; immutable terminal facts
can feed each host's own observations. There is no global latest-host
pointer and no per-request registration/unregistration facility. Collection closes
and admitted callbacks settle before their owners are disposed. A canceled HTTP
wait cannot substitute for that actual barrier.

Disabled/absent collection retains no replayable event history. Pre-container
discovery/loading is not repeated to populate later counters; already-retained
terminal state is observable when known. Capacity/invalid-measurement diagnostics
are bounded and do not change application return values, readiness or exceptions.

The full v1 limits, authoritative application/environment labels and exact
required export spellings are in the portable contract. Core only adapts the
native instruments; the optional package owns catalog views, cardinality,
identity projection and the reserved endpoint. Actual Core/exporter evidence and
remaining qualification are recorded under
[CIS-0009](../../changes/CIS-0009/verification.md).
