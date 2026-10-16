# CISLIB review — pass 01-runtime

## Verdict: **FINDINGS**

I found two actionable issues and one open question. None of the findings is a crash or a data-corruption defect. The first breaks the immutable flush/stop accounting contract; the second is contract and catalog drift.

---

## Finding 1 (Medium, confirmed): incoming-mode flush/stop results use later counters instead of the counters at boundary registration

**Location:**
- `CoreLogRuntime.Incoming.cs`
  - `CapturePipelineResult`, which uses `downstream?.InvalidEventCount ?? boundary.Invalid` and `downstream?.TruncatedEventCount ?? boundary.Truncated`.
  - `WaitPipelineBoundary`.
- `IncomingEventQueue.RegisterBoundary` and `Resolve`.

**Behaviour:**
- The runtime captures `PipelineBoundary.Invalid` and `PipelineBoundary.Truncated` at registration, then discards them whenever a downstream boundary exists, which is almost always.
- The downstream `RuntimeFlushBoundary` is created by `captureOutputs` only when the incoming prefix resolves, which can be much later.
- Per-output `Rejections` snapshots (Filtered, QueueFull, Unavailable) are also taken at resolution time, not at registration.
- `RegisterBoundary` coalesces on `accepted`. A later caller therefore receives the first caller's `IncomingFlushBoundary`, including its `QueueFull` snapshot.

**Contract requirement:** `runtime-contract.json` (observations.initial and coordination.coalescing) and the `LogFlushResult.InvalidEventCount` documentation say the initial counts are captured at registration, must not be replaced by later cumulative values, and that coalesced callers keep their own counters.

**Concrete triggers:**
1. **Truncated capture that loses the race to Stop.** A caller is capturing a message longer than 8192 scalars while `StopAsync` registers its boundary and closes admission.
   - `Write` then increments `truncatedEventCount` under the gate and returns `Stopped`.
   - The stop result's `TruncatedEventCount` includes that event, and `LossObserved` becomes true, even though no accepted, covered event was truncated.
2. **Unrelated activity during a pending flush.** A flush is waiting behind an incoming backlog. Meanwhile:
   - another caller logs invalid data, which increments the invalid counter; or
   - another caller logs at a level where only a faulted output is interested. `FinishWithoutCapture` then calls `RecordUnavailable`.

   The flush reports this later activity as `LossObserved`.
3. **Coalesced flush missing its own rejections.**
   - Flush A registers at `accepted=5`, `queueFull=0`.
   - Three incoming rejections follow. They do not change `accepted`.
   - Flush B coalesces onto A's boundary and reports `QueueFull=0`, missing loss that occurred before its own registration.

**Smallest fix:**
- Keep the per-caller registration snapshot: invalid, truncated, and incoming `QueueFull`.
- Add only background loss attributable to the covered prefix. Track worker-side invalid and truncated counts separately (the `PrepareIncoming` envelope path and the commit truncation increment) and freeze them at prefix resolution. Existing tests that expect background truncation in the flush result, such as `InterceptorTruncationIsCountedOnce`, still pass under this approach.
- Store incoming `QueueFull` in `PipelineBoundary` rather than on the shared `IncomingFlushBoundary`.
- Alternatively, if resolution-time semantics are intended, amend the contract and the XML documentation explicitly.

---

## Finding 2 (Low, confirmed): published-candidate contract and catalogs are stale for incoming acceptance

**Location:**
- `contracts/logging/v1/runtime-contract.json`: `writeResults`, `arguments.Write`, and `writeOrder`.
- `runtime-surface.json` `errorCodes`.
- `RuntimeSurface.cs` `LogErrorCodes`.
- The `LogWriteOutcome.Accepted` XML documentation.

**Mismatches:**

| Source | States | Actual public behaviour |
| --- | --- | --- |
| Portable contract | "Each enabled output appears in configured order" | `Create()` returns `Accepted` with empty `Outputs` and an `IncomingSequence` |
| Portable contract | "ErrorCode null for other normal outcomes" | Incoming `Rejected` returns `ErrorCode = "IncomingQueueFull"` |
| `errorCodes` catalog and `LogErrorCodes` | Do not list `IncomingQueueFull` or `RecursiveProcessing` | Core emits both codes publicly |
| `LogWriteOutcome.Accepted` XML doc | "accepted into the incoming queue" | Internal output-stage paths, still used by many qualification seams, still mean "every interested output accepted" |

`runtime-contract.md` says `incoming-pipeline.md` supersedes the old descriptions, but the portable JSON claims to be the current development contract.

**Fix:**
- Update the JSON `writeResults` and `ErrorCode` rules.
- Add the two codes to the catalog and the reference surface.
- Word the `Accepted` documentation so it covers both stages.

---

## Question (uncertain)

`LoggingReviewProcessTests` removed several inline assertions:
- exit code 0;
- empty stderr;
- `.NET 10.0.12` runtime;
- OS;
- fixture and Core SHA-256 checks.

These were replaced by `FixtureProcessChecks.RunAsync(..., flatReport: true)`, with `DOTNET_SYSTEM_IO_DISABLEFILELOCKING` explicitly cleared. That helper was not in this pass, so I could not confirm it still enforces the same identity and runtime controls. Please confirm it does; otherwise this is a lost assertion.

---

## Areas checked with no actionable defect

- **Truncation counting** across caller capture, interceptors and background rendering: counted once per event.
- **Duplicate-suppression state:** single-worker ownership; `Remember` only after acceptance; bounded eviction; non-sliding expiry.
- **Recursion guard and callbacks outside admission locks.**
- **Abandonment race between expiry and late commit:** serialized under the gate.
- **Output queue closure:** occurs only after incoming drains.
- **Lock order:** deadline gate before admission gate is preserved, including the `Abandon` → `Resolve` → `RegisterFlushBoundary` → `onDrained` path.
- **Owned and borrowed writer cleanup and rollback:** owned writers are not disposed on failed startup.
- **Common binding ordering** and per-output render faults.

The burst test's change from `SuccessfulStop` to asserting writer-stage loss matches the documented `RejectedByWriters` semantics. It is a documented design trade-off, not a defect.

---

## Limits

- No code, build or test was executed. Passing tests and the summarized evidence were not treated as proof.
- Only the files assigned to this pass were reviewed deeply. Not inspected:
  - `FixtureProcessChecks`, `TestSupport` and the `DiscoveryHost` fixture sources;
  - the provider, file, timer and Type Manager internals beyond the supplied context;
  - the architecture tests that compare the surface catalog;
  - deleted Python readers belonging to other passes.
- Coverage, mutation, compatibility, Ubuntu Developer and release work are already acknowledged as pending and are not reported as defects.