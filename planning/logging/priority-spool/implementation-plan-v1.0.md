# Implementation plan for the rotating logging frontend

**Project:** CISLIB / `CIS.Core`  
**Feature:** `LOG-PRIORITY-SPOOL`  
**Version:** 1.0  
**Date:** 19 October 2026  
**Requirements:** [BRD v1.2](C:/Users/andrs/Downloads/CISLIB_Logging_Priority_Spool_Feature_BRD_v1.2.md)  
**Design:** [D05 v0.2](C:/Users/andrs/Downloads/CISLIB_Logging_Priority_Spool_D05_Technical_Design_v0.2.md)  
**Status:** Plan prepared. Every batch below is **Not started**. No production code, test implementation, build, native test or benchmark was started for this plan.

This is a plan to follow after implementation is authorized. It does not resume the older paused logging roadmap, authorize publication or external review, or start the separate template API work. Explicit user decisions take precedence over imported documents and older plans.

## 1. Scope and fixed decisions

Implement the frontend that accepts an already-created, owned immutable event, queues or evicts waiting events, and hands claimed events to the separate consumer/backend thread. Preserve existing upstream capture and caller-side legacy factories/formatters. Filtering, formatting, duplicate suppression, routing and writing remain backend responsibilities.

The implementation must retain these decisions:

| Area | Required behavior |
| --- | --- |
| Storage | Fixed array of C occurrence references; no physical sorting or compaction. Capacity defaults to 1024, valid range 1–65536. |
| Search | One rotating starting position; at most K distinct probes, including the starting slot. Prefer the first observed free slot, otherwise the lowest severity and oldest event creation key among inspected slots. |
| Budget | `K = max(1, ceil(C * p / 100))`. Global fallback 30%; default overrides Trace 20%, Debug 30%, Information 40%, Warning 50%, Error 60%, Critical 70%. Unset overrides inherit the fallback; valid percentages are 1–100. Capture settings at startup. |
| Publication | One final `Interlocked.Exchange` publishes an eligible event. Account for the actual returned victim and severity. No retry/restart or capacity rejection. |
| P01 | Best-effort across the whole ring and during races. Higher-priority events may be evicted when the inspected window or a concurrent replacement requires it. Capacity and drain-rate tuning do not replace race correctness. |
| Age and identity | Creation key belongs to the event and never changes on publication/reindexing. Each publication has distinct immutable occurrence identity. The cursor is only a search hint. |
| Consumer | Lazy polling; capture up to C exact slot/reference pairs, sort privately by creation key, conditionally claim each exact occurrence, release references and rebuild. Skip stale entries. |
| Controls | Fresh captured-batch frontend flush; close/enrollment/final-drain stop. Explicit failure, timeout and cancellation results. Consumption means ownership handoff, not successful processing or writing. |
| Producer cost | Fixed bookkeeping plus at most K reads/comparisons and one publication exchange. No logging-owned locks/waits, notification calls, per-event tasks, helping, shared sorted index or retry-until-success. |

Idle polling has a positive tunable interval, separate from file flushing. Its numeric default is selected and measured during implementation. Drain/reindex immediately while work remains; producers never wake the consumer.

## 2. Batch order and tracking

Follow `F0 → F1 → F2 → F3 → F4 → F5`. Add meaningful native tests alongside each implemented behavior; do not postpone all testing to F5. Keep each batch a coherent source/test/documentation change. A helper or document alone does not complete a batch.

| ID | Outcome | Dependency | Status |
| --- | --- | --- | --- |
| F0 | Settle integration contracts and record the current baseline | Implementation authorization | Not started |
| F1 | Build owned occurrences and bounded atomic ring publication | F0 | Not started |
| F2 | Integrate polling, indexing and frontend/backend handoff | F1 | Not started |
| F3 | Implement accounting, shared batch flush and runtime mapping | F2 | Not started |
| F4 | Complete close/drain, expiry and safe late cleanup | F3 | Not started |
| F5 | Qualify the combined change and finish documentation | F4 | Not started |

These refine BRD delivery batches A–C. BRD batch D remains a separate, requested release qualification; F5 does not authorize a release campaign or publication.

### F0 — Contracts and baseline

- [ ] Recheck repository instructions, checkout identity and existing edits. Preserve unrelated changes and record the source used for subsequent evidence.
- [ ] Record the existing frontend/public contracts and relevant native baseline results after authorization. Distinguish pre-existing failures from regressions; do not change SDK pins or unrelated modules to obtain a green result.
- [ ] Specify the creation-key representation and rollover-safe comparator before coding it. Preserve the existing positive `IncomingSequence` contract unless a concrete, reviewed contract amendment is necessary. A signed wrapping comparison, cursor age or admission-stop threshold is insufficient.
- [ ] Specify the mapping between frontend batch outcomes and existing runtime `FlushAsync`/stop results. Keep ownership handoff, backend processing, writer flushing and delivery success distinct. Do not silently replace a public delivery/prefix guarantee with the weaker frontend batch contract. Document the proposed compatible mapping and any narrowly required public change before implementing it.
- [ ] Define startup option/result names, explicit lifecycle failure/cancellation outcomes, and live versus frozen diagnostics. Define counter rollover/saturation reporting without producer retry loops, extra admission limits or invented coherent live snapshots.
- [ ] Choose an initial idle-poll interval for measurement, record its rationale, and identify existing startup/configuration validation paths. No mandatory new caller argument or hot-reconfiguration framework.

**Primary inputs:** `LoggingOptions`, `LogWriteResult`, `LogIncomingSnapshot`, `CoreLogDiagnostics`, `LogFlushResult`, `LogStopResult`, runtime contract documentation and the BRD/D05 decisions.

**Exit check:** A short integration contract records the remaining engineering choices, compatibility mapping and baseline evidence. An unresolved contradiction in a public guarantee is resolved explicitly before dependent code; it is not hidden by renaming a result. No new business-policy decision is required by the current design.

### F1 — Owned events and bounded publication

- [ ] Implement immutable occurrence identity and the event-owned creation key. Preserve owned capture facts; do not defer callbacks, formatting or mutable caller data into the ring.
- [ ] Replace locked queue/full-rejection admission with the fixed reference array, startup-computed budgets and atomic rotating cursor. Derive valid indices across wrap, including non-power-of-two capacities.
- [ ] Implement the bounded search and exactly one final publication exchange. Stop at an observed null; otherwise select the minimum `(severity, creation key)` in the inspected window. Never extend the scan or retry because a candidate changed.
- [ ] Count actual admissions and returned evictions, including the actual displaced severity. Prepare the exact-reference conditional take primitive for the consumer. Keep lifetime removal classification distinct from later batch classification.
- [ ] Establish producer enrollment: cheap Running check, atomic increment, state recheck, and decrement in `finally` after publication bookkeeping. Producers passing the recheck may complete after Closing; rejected entrants never touch slots.
- [ ] Add native cases for capacities 1/2/3 and repeated wrap, percentage precedence/rounding, stable P01 selection, all-higher-priority windows, competing exchanges, null-to-occupied races and truthful recoverable failure results.
- [ ] Exercise paused producer seams before enrollment, after recheck, before exchange and after exchange. Verify peers progress and each real occurrence has one removal owner. Use controlled barriers without adding arbitrary callbacks or locks to the shipping producer path.

**Primary source:** `Runtime/IncomingEventQueue.cs`, occurrence ownership/capture types, `LoggingOptions.cs`, and focused incoming queue tests. Use direct internal types; avoid new public placeholder APIs or a parallel simulated queue.

**Exit check:** Real queue tests establish at most K probes and one exchange for every valid enrolled publication, no saturation rejection, actual-victim accounting and rollover-safe indexing. Test instrumentation is absent from or harmless to production enqueue cost. Publication can be reviewed before runtime wiring.

### F2 — Consumer and public-path integration

- [ ] Preallocate the reusable consumer index at startup. Capture exact slot/reference pairs, sort by immutable creation key, claim with reference-conditional `CompareExchange`, clear processed/skipped references and rebuild after draining.
- [ ] Poll lazily while idle, continuing without idle delay while work remains. Never wait for a missing creation sequence. A delayed older event can enter a later batch.
- [ ] Wire `CoreLogRuntime` incoming admission and direct/provider/convenience paths to the new frontend. Remove ordinary frontend admission locks and hidden notifications from the transitive call path, including existing `Write`/`IsEnabled` gates where applicable; changing only the queue is insufficient.
- [ ] Preserve early disabled/stopped/recursive checks, stable capture, existing overloads, scopes, and caller-side at-most-once legacy callback behavior with contained exceptions. Do not restart a stopped worker.
- [ ] Hand successfully claimed occurrences to the existing backend. Keep interceptor → global filter → routing → duplicate policy order, custom writer registration, output ownership, JSONL content, recovery and destination isolation intact.
- [ ] Add native consumer replacement/claim races and public-path preservation tests. Separately stall the consumer and backend to establish bounded producer progress. Verify that an evicted or abandoned event never reserves a backend duplicate key.

**Primary source:** `CoreLogRuntime.cs`, `CoreLogRuntime.Incoming.cs`, `Runtime/IncomingEventQueue.cs`; inspect `Runtime/EventProcessing.cs` for preservation. Relevant tests include incoming runtime, processing policy and logger convenience suites.

**Exit check:** The real public paths use the ring and consumer. A stale index entry cannot consume a newer replacement; physical slots never compact. Backend capabilities and callback placement remain intact. The transitive ordinary frontend path has no logging-owned lock, wait, retry loop or producer notification dependency.

### F3 — Diagnostics and captured-batch flush

- [ ] Expose settled frontend accounting separately from backend outcomes: `Admitted = Consumed + Evicted + Abandoned + Waiting`. Report ActivePublishers separately. Label independently read live counters as potentially skewed.
- [ ] Implement one current consumer batch and one pending flush-request group. Freeze group membership before fresh capture at the next safe point; later requests join the next capture. Capture-pass membership is weakly consistent, not an instantaneous admission prefix.
- [ ] Maintain exact-reference target dispositions and `TargetCount = Consumed + Evicted + Abandoned + Pending`. Pending means unresolved target classification, not necessarily a physically waiting slot. Classifying a stale target must not double-count its lifetime eviction.
- [ ] Implement one original monotonic timeout budget from call entry, including scheduling and capture. Distinguish NoBatchCaptured/incomplete from a captured empty batch. Return immutable observations; timeout leaves normal consumption running.
- [ ] Detach canceled/expired waiters promptly, preserving other live waiters. Give each waiter only control metadata; share the single index and target bookkeeping. A pending group without live waiters needs no special capture or retained history.
- [ ] Implement the F0 runtime/backend flush mapping. Preserve writer-boundary and public delivery meaning; do not equate Consumed with Processed or written. Keep backend callbacks/I/O outside frontend coordination.
- [ ] Add deterministic mixed-disposition, capture-arrival, stale-entry, delayed-creation, pre-capture timeout, coalescing, cancellation and repeated-expired-request tests against the integrated runtime.

**Primary source:** `CoreLogRuntime.Incoming.cs`, `LogIncomingSnapshot.cs`, `CoreLogDiagnostics.cs`, `Runtime/RuntimeAccounting.cs`, `LogFlushResult.cs`, `Runtime/FlushBoundary.cs`, `Runtime/FlushDeadline.cs` and native flush suites.

**Exit check:** Fixed batch membership and exact frozen result equations hold under races. New traffic cannot extend a completed capture. Runtime/backend guarantees match the documented mapping. Consumer/control coordination never becomes a producer dependency and is not held over sorting, capacity-sized scans, awaits or backend work.

### F4 — Stop, expiry and late cleanup

- [ ] Implement the shared Running → Closing stop operation and original monotonic deadline. Reject new entrants through the enrollment recheck, allow enrolled producers to finish, drain while closing, then require ActivePublishers zero followed by a final fresh pass.
- [ ] Freeze explicit timeout results on expiry and request abandonment. Coordinate consumer claim/disposition with expiry so an event cannot be marked Abandoned and subsequently handed to the backend. An already-started claim/handoff may finish and remains unresolved until its actual outcome is known.
- [ ] Keep cleanup alive for an enrolled producer paused past timeout. Account for its later publication and abandon waiting work safely; complete final cleanup only after enrollment reaches zero and the final pass settles.
- [ ] Keep returned timeout observations distinct from eventual completion. Do not dispose the ring, reuse occurrence identities, or dispose backend resources beneath active publishers or I/O.
- [ ] Make concurrent stop callers share the operation. Caller cancellation detaches that waiter without reopening the frontend or canceling other callers. Contain recoverable lifecycle faults in explicit results rather than faulted/canceled tasks.
- [ ] Add controlled enrollment/recheck/exchange/bookkeeping races, blocked consumer/backend cases, late-publication expiry cases and resource-lifetime tests. Include flush/stop overlap and final settled accounting.

**Primary source:** lifecycle sections of `IncomingEventQueue` and `CoreLogRuntime.Incoming`, `LogStopResult`, stop diagnostics, incoming runtime and flush coordination/stress tests.

**Exit check:** No enrolled publisher is forgotten, no waiting occurrence has two dispositions, and no Abandoned-to-Consumed reversal occurs. Frontend completion requires safe cleanup; backend completion remains separately represented. Ordinary log calls never wait for stop.

### F5 — Combined qualification and documentation

- [ ] Run relevant focused native tests and integrated runtime/process cases, then a coherent Developer verification checkpoint. Audit the complete frontend call path again, including helper/provider paths and failure handling.
- [ ] Measure quiet, full and contended workloads across incoming severities, capacities, producer counts and drain rates. Record probe/exchange bounds, caller latency distribution, CPU, allocations and atomic operations. Separate upstream callback duration and backend time while retaining total caller latency.
- [ ] Exercise cursor/creation/counter boundaries and sustained replacement with an old index held. Establish retained-memory bounds: ring C references, index up to C old references, a consumer-owned occurrence, producer in-flight references and live waiter metadata. Do not claim only C payloads can remain alive.
- [ ] Update affected incoming/runtime contract documentation, options/results usage, examples, shared contract/case inventory and requirement mappings. Preserve published historical baselines; reconcile only superseded behavior that this feature actually replaces.
- [ ] Record every BRD acceptance case against actual evidence and source identity. Report failures and unexecuted gates honestly. Keep unrelated pre-existing corrective work and release obligations visible without enlarging this frontend implementation.
- [ ] Record the selected idle-poll default and measured tuning implications. Do not invent latency, allocation or coverage thresholds merely to declare a pass.

**Exit check:** All feature acceptance cases have real native or measured evidence, current documentation matches behavior, and no frontend/backend/default regression remains unexplained. A focused pass does not satisfy outstanding release families. If release qualification is separately requested, run the required unit, integration, mutation, architecture, compatibility and business-case obligations for that candidate before any release claim.

## 3. Acceptance coverage

AC identifiers below refer to BRD v1.2. These are planned responsibilities, not passing results. F5 reconciles the complete matrix; shared cases may be exercised in more than one batch.

| Batch | Primary acceptance coverage |
| --- | --- |
| F0 | AC01, AC10, AC26 — defaults, boundary semantics and explicit lifecycle/public mapping decisions. |
| F1 | AC02, AC03, AC04, AC05, AC06, AC07, AC09, AC10, AC12, AC23, AC29 — real publication, selection, bounds, races and counters. |
| F2 | AC01, AC08, AC09, AC11, AC13, AC14, AC15, AC16, AC20, AC21, AC22, AC24, AC27, AC28, AC30 — consumer and existing caller/backend integration. |
| F3 | AC17, AC18, AC26, AC31, AC32 — fixed batch targets, explicit outcomes, capture and shared controls. |
| F4 | AC07, AC19, AC20, AC26, AC33 — enrollment, close/drain, cancellation and late cleanup. |
| F5 | AC10, AC22, AC23, AC24, AC25, AC34 — boundary/cost/memory qualification, preservation and coherent verification. |

## 4. Verification workflow

Run commands from `C:\miscwork\portfolio\cislib` after implementation authorization and corresponding code changes. These commands were inspected for planning; none was executed as a test run for this document. Use the repository's pinned toolchain and current focused-test prerequisites rather than changing tool versions.

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Class CIS.Tests.UnitTests.IncomingEventQueueTests
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Class CIS.Tests.UnitTests.IncomingRuntimeTests
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Filter 'FullyQualifiedName~Incoming|FullyQualifiedName~Flush'
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Class CIS.Tests.UnitTests.ProcessingPolicyTests
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Class CIS.Tests.UnitTests.LoggerConvenienceTests
pwsh -NoProfile -File eng/test.ps1 -Project IntegrationTests -Filter 'FullyQualifiedName~Flush' -Configuration Release
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer
```

Select narrower real classes/methods while editing. Add selectors for implemented cases as needed; an empty selection is a failure. The focused runner incrementally builds the selected project and rejects empty, failed or skipped required results. Retain its TRX and summary artifacts under `.artifacts/csharp/focused/` with the source identity. A broad filter is not evidence that every feature case exists or ran.

At a separately requested release boundary, use the documented release profile and required lanes, including the Windows lane command below and the corresponding Linux lane on an appropriate host. Resolve current repository prerequisites at that time; running one lane is not complete release qualification.

```powershell
pwsh -NoProfile -File eng/verify.ps1 -Profile Release -Lane windows-x64
```

Use controlled barriers and explicit test seams for concurrency proof, not elapsed sleeps. Verify real C# source behavior rather than a separate model that mirrors the algorithm. No external review service is needed for this plan or feature verification.

## 5. Evidence and completion record

For each batch, record the exact source/worktree identity, changed behavior, native commands, selected/executed case counts, outcome, artifact paths and remaining failures. For measurements also record build/host, capacity, scan profile, producer count, idle interval, backend/drain conditions, duration and observation method. Keep frontend facts distinct from backend outcomes and from historical reports.

An evidence index can be added under `planning/logging/evidence/log-priority-spool/` during implementation; that directory is a proposed destination, not evidence created by this plan. Mark a batch complete only when its exit check is supported. Record any deviation and its reason against the BRD/D05 requirement before proceeding.

Feature completion requires the real integrated frontend, bounded producer work, accepted P01 behavior, immutable ownership, truthful accounting/control outcomes, preserved public/backend capabilities, acceptance evidence and matching documentation. Release completion remains separate. No commit, package publication or original-roadmap status change is authorized by this document.

## 6. Planning provenance

| Input | Identity at plan preparation |
| --- | --- |
| BRD v1.2 | SHA256 `1CB6B678D7EA252075CA6CB90A0125DF33557C31FB956CF9357475CE5FA36477` |
| D05 v0.2 | SHA256 `D69ACF362759C32E2F90CA9C7AD71C40A367D8A984299E5A670601BBA1B731F7` |
| Inspected checkout | `ca6b30ac47e5cb228a086f89a26717d663f5e632` — navigation baseline, not a qualified implementation candidate. |
| Existing worktree item | Untracked `implementations/csharp/examples/type-manager/Web/Properties/`; preserved. |

Source navigation covered the incoming queue, runtime entry/consumer glue, options/results/accounting, frontend/backend contracts, current native suites and repository test entry points. Current code still contains the older locked queue/admission and prefix-boundary behavior; this plan does not claim those changes already exist.

The required local graph check reported fresh validation, 11,868 nodes, 59,724 edges and zero reported build warnings/errors. Build identity: `sha256:1a752390082d0cf3287b13f5c8855f7c2deea8b9bc89bb7874ebfa03ca31109c`. Exact queue-class lookup and the bounded `RegisterIncomingBoundary` caller lookup were untruncated; callers were `FlushIncomingAsync` and `StopIncomingAsync`, with no proposed edges. Call observations identify `cis.csharp.semantic-facts/3`; a schema version and complete compiler coverage were not established. This is navigation evidence, not test or semantic-completeness evidence.

**Current progress:** F0–F5 are all Not started. Only this implementation plan has been prepared.
