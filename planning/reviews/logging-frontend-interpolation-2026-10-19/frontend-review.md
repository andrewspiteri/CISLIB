# CISLIB incoming-frontend review: uncommitted change (read-only)

I found 2 high, 1 medium and 3 low findings, plus test gaps. Approximate P01 under races is not reported, per your decision.

## Confirmed defects

### F1: High (depends on configuration). The idle consumer cannot be woken by stop, flush or abandon

**Locations:**
- `Runtime/IncomingEventQueue.cs:257`: `if (count == 0) { await Task.Delay(pollInterval) }`.
- `Runtime/IncomingEventQueue.cs:176`: the same delay in the fault cleanup loop.
- `Runtime/IncomingEventQueue.cs:55-74, 106-117, 119-123`: `RegisterBoundary`, `Complete` and `Abandon` change state but signal nothing.
- `LoggingOptions.cs:64, 182`: the poll interval accepts 1 to `int.MaxValue` ms.

**Failing scenario:**
1. Configure `IncomingPollIntervalMilliseconds = 10000`, a valid value. Keep `ShutdownTimeoutMilliseconds` at the default 5000.
2. Leave the runtime idle, then call `StopAsync`.
3. The consumer stays asleep until the delay ends. It cannot reach the final pass, so the stop is always `TimedOut`.
4. `queuedOutputs.Complete` runs only from `onDrained` after the drain (`IncomingEventQueue.cs:254`). Output workers, owned writers and the file handle therefore stay alive about 10 s. At the maximum value this becomes about 24.8 days.
5. `Completion` is delayed by the same amount.
6. `FlushAsync(1s)` on an idle runtime returns `TimedOut` with `NoBatchCaptured`.

**Related:** `Task.Delay` uses the system clock, not the runtime `TimeProvider`.

**Fix:**
- Add a control-wake TCS that `RegisterBoundary`, `Complete` and `Abandon` complete. These are control paths, not producer notifications, so this fits the design.
- Wait with `Task.WhenAny(Task.Delay(pollInterval, clock), wake)`.
- Optionally cap the poll interval in validation.

**Test:** set the poll interval to 60000 and the stop timeout to 100 ms on an idle runtime. Assert the stop is `Completed`, the writer is disposed and `Completion` settles well within the limit.

### F2: High (behavior confirmed; confirm against the BRD intent). Backend `QueueFull` bypasses the severity retention policy

**Locations:**
- `Runtime/IncomingEventQueue.cs:204-237`: the consumer claims every captured entry oldest-first. It never checks output capacity.
- `CoreLogRuntime.Incoming.cs:84-88`: the commit result is only counted.
- `Runtime/OutputEventQueue.cs:442-449`: `TryWrite` failure returns `QueueFull` immediately.

**Failing scenario:**
1. A slow writer fills its output channel, which also has `QueueCapacity` slots.
2. The consumer keeps emptying the ring into rejected admissions. These are counted as `RejectedByWriters` or `QueueFull`.
3. The ring rarely fills, so P01 eviction is almost never exercised. Losses become age-ordered and severity-blind.
4. Example: a Critical event created after a Trace burst is claimed after the Trace events and rejected by the full writer. Meanwhile the frontend would have kept it and evicted Trace.

This is deterministic, not the accepted exchange race.

**Fix (choose by intent):**
- Have the consumer stop claiming while an interested output queue is full, using a disposition/space signal bounded by the stop budget. Unclaimed occurrences stay in the ring under P01.
- Or document explicitly that P01 applies only to frontend saturation.

**Test:** use a blocking writer with capacity 2. Publish a Trace burst, then Critical events. Assert the Critical events are written and the losses are Trace evictions.

## Likely defect (depends on code not supplied)

### F3: Medium. A consumer-side interpolation format failure is misclassified

**Locations:** `CoreLogRuntime.Incoming.cs:70` runs `Interpolation.Render` on the consumer, and only `EventEnvelopeTooLargeException` is caught (`:93`). The legacy path maps `FormatException` to `InvalidEvent/InterpolationFormatFailed` (`CoreLogRuntime.cs:531`).

**Failing scenario:**
1. A built-in value is copied with an invalid specifier, such as `int` with `"Q"`, and the caller receives `Accepted`.
2. The consumer's invariant `Render` throws `FormatException`.
3. The exception escapes `prepare` and lands in `ProcessCoreAsync:231`. Only `incoming.Failed` is incremented.
4. `InvalidEventCount` and `workerInvalid` are not incremented, so the result looks like a worker failure.

This depends on whether `Render` can throw `FormatException`; I could not see it.

**Fix:**
- Preferably validate format specifiers on the caller.
- Otherwise, catch `FormatException` in `PrepareIncoming` when `Interpolation` is non-null, and return a commit that increments `invalidEventCount` and `workerInvalid`, like the envelope branch.

## Low

### F4: `stopTask` can stay pending forever on a nested setup failure

**Location:** `CoreLogRuntime.Incoming.cs:215-223`.

**Scenario:**
1. `stopTask` is published at `:206`.
2. Setup fails, and the catch block itself then throws (from `Abandon`, `Complete`, the `afterDrain` call to `queuedOutputs.Complete`, or `FrontendFailure`).
3. The exception escapes before `TrySetResult`. The first caller gets `StopFailed` from `:139-140`.
4. Every later `StopAsync(None)` or `DisposeAsync` returns the never-completed task (`:192`, `:249`) and hangs.

**Related:** `RunIncomingStopAsync:280` excludes OOM, so an OOM there also leaves the task pending.

**Fix:** complete `completion` with the failed result in a `finally` before or around the cleanup steps.

### F5: Idle consumer cost and lifetime

**Location:** `IncomingEventQueue.cs:201, 257`.

**Scenario:**
- At the 1 ms default, Windows timer granularity makes the consumer wake about 64 times per second.
- Each wake does a full O(C) slot scan. At C=65536 that is about 4M volatile reads per second per idle runtime.
- The recurring `Task.Delay` timer keeps an undisposed runtime and its outputs rooted indefinitely.

**Fix:**
- Combine with F1's wake signal.
- Skip `index.Capture` when the waiting gauge is zero and no control is pending. A lagging gauge only delays work by one poll, and the final pass must still capture.

### F6: Clock and output registration run under `admissionGate`

**Location:** `CoreLogRuntime.Incoming.cs:107-113`. These paths reach `RegisterBoundary` → `Resolve`, which calls `clock.GetTimestamp` and `captureOutputs` (`IncomingEventQueue.cs:68-72, 134`) while `admissionGate` is held:
- any flush after drain, or
- a stop after a worker fault.

**Impact:** this contradicts the invariant comment at `CoreLogRuntime.cs:156`. Public creation uses `TimeProvider.System`, so only internal custom clocks are affected.

**Fix:** register under the gate, then call `Resolve` after releasing it.

### Documentation inconsistency

`docs/.../incoming-pipeline.md:43` says "Templates/interpolation are separate API work". Interpolation is already wired into the incoming path (`CoreLogRuntime.Incoming.cs:26, 70`).

## Missing or weak regression tests

- **Full-pipeline race test:** multiple producers racing `FlushAsync` and `StopAsync`. It should assert `TargetCount = Consumed + Evicted + Abandoned + Pending` per batch and the lifetime equation at `IsExact`. Current concurrency tests cover only the bare ring.
- **Named scenarios:** none of these is tested:
  - long poll interval vs stop (F1),
  - backend-full priority loss (F2),
  - consumer `FormatException` (F3),
  - built-in console/file `Prepare` and `RenderTooLarge` through the incoming path.
- **Brittle test seams:**
  - `IncomingLifecycleTests.cs:56` overwrites the `readonly` field `clock` via reflection.
  - `:24` and `IncomingFrontendTests.cs:21` identify the incoming worker by launch order (`++launches == 2`). That breaks silently if built-in outputs become enabled or the binding order changes.

## Compatibility notes (by your decision, not defects)

These are behavior changes for existing callers:
- The public `StopAsync`, `FlushAsync` and `DisposeAsync` now return `Failed` or `Canceled` results instead of throwing or returning a canceled task.
- An accepted write now returns empty `Outputs`.

## Coverage limits

- **Not supplied:**
  - `EventProcessing`, `FlushDeadline`, `ElapsedBudget` and `OutputQueues`;
  - interpolation handlers and `Render`, `EventProjection` and `EventEncoder`;
  - `CoreLogger`, the attribute APIs and the console/file writers.
- **Assumed:** `FlushDeadline.ObserveCompletion` freezes the result at timer expiry, and the `CoreLogLevel` ordinals are Trace=0 through Critical=5.
- **Not verified:** nothing was executed. The 7016 passing tests are your claim and do not show release qualification.
- **Not deeply re-reviewed:** `OutputEventQueue` appears largely pre-existing, so I reviewed it only where the incoming path interacts with it.