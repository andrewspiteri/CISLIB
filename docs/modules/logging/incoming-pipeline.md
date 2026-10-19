# Rotating incoming logging frontend

Public `CoreLogRuntime.Create` and `TryCreate` overloads use the rotating frontend followed by the existing backend. The caller performs early threshold/state checks and owned capture, including an approved legacy factory/provider formatter at most once on the caller. The frontend receives an already-created immutable occurrence. Backend filtering, encoding/rendering, routing, duplicate suppression and writer work remain on the separate consumer.

The [19 October feature record](../../../planning/logging/priority-spool/README.md), [BRD](../../../planning/logging/priority-spool/brd-v1.2.md) and [design](../../../planning/logging/priority-spool/design-v0.2.md) supersede the former full-rejection and incoming-prefix rules. Earlier source plans and receipts remain historical evidence.

## Publication and configuration

The frontend owns C stable reference slots and a rotating atomic search cursor. An eligible producer inspects at most K distinct positions, including the starting slot, wrapping locally. It chooses the first observed null; otherwise it selects the lowest severity, then oldest event creation timestamp within that window. One final `Interlocked.Exchange` publishes the incoming occurrence and returns the actual displaced reference. That actual event and severity determine eviction accounting.

An event is admitted even if every inspected occurrence has higher priority. A free slot or better victim outside the window does not extend the search. A changed candidate can result in a different victim by exchange time; this accepted race deviation does not trigger a retry. P01 is a window policy with best-effort global behavior, not an exact whole-ring guarantee.

| Startup option | Default and meaning |
| --- | --- |
| `QueueCapacity` | 1024; valid range 1–65536. Bounds this frontend and each separate output buffer. |
| `IncomingScanPercentage` | 30%; fallback when a severity override is null. |
| `IncomingTraceScanPercentage` | 20% |
| `IncomingDebugScanPercentage` | 30% |
| `IncomingInformationScanPercentage` | 40% |
| `IncomingWarningScanPercentage` | 50% |
| `IncomingErrorScanPercentage` | 60% |
| `IncomingCriticalScanPercentage` | 70% |
| `IncomingPollIntervalMilliseconds` | 1 ms initially; any positive Int32 value. Idle polling only, independent of file flushing. |

Percentages must be 1–100. `K = max(1, ceil(C * p / 100))`; for ten slots, 30% means three probes. Settings are captured before workers start; later mutation does not reconfigure an existing runtime. Setting the fallback alone does not clear the six initial overrides. There are no new mandatory arguments or environment variables.

The producer path has fixed enrollment/bookkeeping plus at most K slot reads/comparisons and one publication exchange. It takes no logging-owned lock, waits for no owner, sends no worker notification, allocates no event task and never retries a changed slot. Scan cost still grows with capacity and percentage. Owned capture and supplied callback duration remain separate upstream costs.

The age key is the immutable owned event's `TimestampUtc.UtcTicks`. Reindexing and publication never change it. Equal timestamps tie by slot in the consumer index; wall-clock changes affect timestamp order. The cursor is not an age, reservation or delivery sequence. Each publication gets distinct reference identity, including repeated calls using the same event-data object.

## Consumer and retention

The background consumer lazily polls. It captures at most C exact slot/reference pairs into one reusable private index, sorts that index by event creation timestamp, and conditionally removes each expected occurrence with `CompareExchange`. A stale entry cannot consume a newer occupant at the old event's position. Physical slots never compact.

The consumer rebuilds after draining a batch and continues without idle delay while work remains. New arrivals, including delayed older-created events, may appear in a later batch; it never waits for missing sequence values. Successfully claimed ownership is recorded as Consumed before backend processing.

Flush, stop and abandonment controls wake an idle consumer independently of the configured poll interval. Ordinary producers still send no notification. After closure, cleanup checks enrolled publishers at a 1 ms interval so a long idle-poll setting cannot postpone late-publication cleanup. Poll scheduling uses the system timer; lifecycle deadlines retain the runtime's monotonic budget.

Retention includes C ring references, up to C older index references (including already-evicted payloads), a consumer-owned occurrence and O(P) producer in-flight references. Shared controls retain only one current batch, one pending group and O(F) live waiter metadata. They do not create per-waiter C-entry arrays, overflow storage or an eviction history. Slot capacity is not a promise that only C payloads can be alive.

## Admission, diagnostics and results

An accepted write returns `Outcome=Accepted`, a positive `IncomingSequence` correlation ticket and empty `Outputs`. Tickets may repeat at finite-range rollover; they are not age or a flush cutoff. Full frontend storage never produces `IncomingQueueFull`. Acceptance proves publication, not that the occurrence is still waiting when the caller returns, nor processing/writer success.

Early disabled/stopped/recursive paths avoid capture and callbacks. A recoverable legacy factory failure returns `InvalidEvent/FactoryFailed`; no closure is deferred to the consumer. Existing short, named and provider calls remain available. The separately implemented [C# interpolation API](interpolation.md) documents expression evaluation and owned argument capture before publication.

`Incoming` is an immutable observation of lifetime counts, not a flush target:

| Member | Meaning |
| --- | --- |
| Accepted / Admitted | Actual frontend publications |
| Consumed | Ownership handed to the consumer, including still-active or failed backend work |
| Evicted | Actual occurrences displaced by producer exchanges |
| Abandoned | Waiting occurrences actually detached by shutdown cleanup |
| Waiting | Observed physical waiting gauge |
| ActivePublishers | Independently observed registration/publication bookkeeping, including an attempt that must fail its closed-state recheck |
| Processed / Failed | Backend dispatch or preparation dispositions, separate from Consumed |
| Pending | Waiting plus active backend preparation; may include work outside a flush batch |
| QueueFull | Retained legacy incoming member; zero for this frontend |
| RejectedByWriters | Processed events rejected by at least one interested writer |
| Suppressed / DuplicateSuppressed / DuplicateKeyEvictions | Existing backend policy observations |
| IsExact / IsSaturated | Whether closed settled counts are exact, and whether a lifetime counter saturated |
| ErrorCode | Retained safe worker-failure category; a failed frontend closes admission |

At a settled exact unsaturated observation, `Admitted = Consumed + Evicted + Abandoned + Waiting`. Independent live atomic reads can be skewed; `IsExact` stays false until closure and settlement. Saturation is sticky and never stops admission. Output diagnostics preserve their own writer accounting and combine caller early counters separately.

## Flush and shutdown

Frontend flush requests a fresh index at the next safe consumer point. Pending callers may share a group frozen before construction. Events arriving during the capture pass may join; after capture the target never grows. A caller arriving after group freeze uses the next capture. An older-created event not yet published is not awaited.

`LogFlushResult.FrontendBatch` is null until capture completes. `FrontendCaptureStatus=NoBatchCaptured` therefore differs from a captured empty batch. A present batch reports the exact frozen equation `TargetCount = Consumed + Evicted + Abandoned + Pending`. Pending includes stale targets not yet classified. Classifying a replaced target increments batch eviction only; its lifetime eviction was already recorded by the producer.

Runtime `FlushAsync` additionally waits for backend dispatch and the resulting fixed output boundaries and writer-buffer flushes. It registers those before dispatching later batches. It does not treat Consumed as Written or promise an incoming prefix at invocation. Covered eviction/abandonment prevents success; lifetime loss before capture remains observable and can coexist with a lossless successful current batch. Writer flushing is not durability.

One original monotonic budget includes scheduling and capture. Timeout leaves ordinary consumption running and freezes the observed result. Cancellation returns `LogCompletion.Canceled` and detaches only that caller; shared live callers continue. Expired/canceled pending controls are retired rather than retained as history.

Stop closes enrollment using increment/recheck. Already-enrolled producers may finish; final completion requires their count to reach zero followed by a fresh empty pass. Its original deadline includes drain and frontend cleanup. On expiry, request abandonment and return TimedOut with actual unresolved publishers/preparation/I/O. A claimed occurrence is already Consumed and cannot be relabeled Abandoned. An enrolled late publisher may still publish after timeout; cleanup stays alive and abandons its waiting event safely.

The final control's ready signal establishes the completed frontend drain. An attempt paused before its registration increment can resume after that frontier, briefly increment the raw publisher gauge and then fail the closure recheck without touching storage. This does not reopen the drain or turn a successful stop into a timeout. The frozen observation retains the actual gauge; operation completion is determined separately from that diagnostic sample.

Stop callers share the operation and original deadline. A canceled first caller still initiates shutdown but receives an explicit canceled result. Later cleanup cannot rewrite `LastStopResult`. Resources remain alive beneath active processing/I/O; `Completion` represents actual worker/cleanup settlement and may still report underlying backend faults.

Public frontend flush/stop return explicit Failed results for recoverable setup/timer/control failures and callback-flow misuse. `ErrorCode` is a fixed safe category. `TryCreate` similarly returns `LogStartResult`; the established `Create` overloads remain the legacy throwing startup adapter. Internal output-only qualification retains its original output-stage exception/prefix mechanics and is not the public frontend contract.

## Usage

Configure the caller-owned static settings before startup. To use one fallback percentage for every severity, clear the initial overrides:

```csharp
using CIS;
using CIS.Logging;

LoggingOptions settings = EnvironmentOptions.Logging;
settings.IncomingScanPercentage = 30;
settings.IncomingTraceScanPercentage = null;
settings.IncomingDebugScanPercentage = null;
settings.IncomingInformationScanPercentage = null;
settings.IncomingWarningScanPercentage = null;
settings.IncomingErrorScanPercentage = null;
settings.IncomingCriticalScanPercentage = null;

LogStartResult started = CoreLogRuntime.TryCreate(out CoreLogRuntime? created);
if (!started.Succeeded)
{
    Console.Error.WriteLine(started.ErrorCode);
    return;
}
await using CoreLogRuntime runtime = created!;
runtime.CreateLogger("Orders").Info("Order captured");
LogFlushResult result = await runtime.FlushAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
// Inspect Completion, ErrorCode, FrontendCaptureStatus, FrontendBatch and Outputs.
LogStopResult stopped = await runtime.StopAsync(CancellationToken.None);
// Inspect stopped before disposal; disposal joins this same operation.
```

Capacity, drain rate, percentages and idle interval need application measurements. Local native measurements and acceptance coverage are tracked in the feature record; release qualification remains separate.
