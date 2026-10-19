# Feature BRD for the priority aware rotating logging frontend

**Project:** CISLIB / `CIS.Core`  
**Feature reference:** `LOG-PRIORITY-SPOOL`  
**Version:** 1.2  
**Date:** 19 October 2026  
**Status:** Confirmed frontend behavior recorded for implementation review. Implementation and native qualification have not started for this feature.  
**Historical source baseline:** `main` at `4a263fd3f56405586e453909d771f7d2558e86cd`, committed 17 October 2026 at 19:57:50 UTC. [S01]  
**Technical design:** [D05 technical design v0.2](C:/Users/andrs/Downloads/CISLIB_Logging_Priority_Spool_D05_Technical_Design_v0.2.md)  
**Authorization:** Documentation preparation only. Do not start implementation from this revision.

## 1 Business outcome

The logging frontend accepts a supplied, already-created event, queues it in bounded rotating storage and returns promptly. It must not acquire logging-owned locks, wait for another producer or the consumer, or reject an otherwise valid incoming event because the buffer or its search window is full.

The consumer is a separate background thread. It polls the ring lazily, builds a creation-ordered index, claims the indexed occurrences and processes them independently. Filtering, message rendering, routing, duplicate suppression and writing belong to the backend and are outside the frontend implementation scope.

Producers use a configurable bounded search. They prefer a free slot observed within that search; otherwise they select a victim using P01 within the inspected window and publish with one atomic exchange. The default severity profile searches 20% for Trace, 30% for Debug, 40% for Information, 50% for Warning, 60% for Error and 70% for Critical. A global 30% fallback and optional severity overrides support tuning.

Priority selection is best-effort across the whole buffer and under races. A better victim or a free slot may exist outside the search window. Another operation may change the selected slot before the final exchange. These deviations are accepted; the actual displaced event must be accounted for. Buffer capacity, consumer drain rate and search percentages are the operating controls, rather than a global admission index or producer coordination protocol.

This remains a correction to the current frontend, with backend functionality and existing defaults preserved. No backend rewrite or removal of existing filters, interceptors, duplicate checking, custom writers, file recovery or ownership behavior is authorized.

## 2 Authority and confirmed decisions

### 2.1 Current requirements

The latest explicit user decisions take precedence over earlier drafts, current implementation behavior and historical task plans. Versions 1.0 and 1.1 are retained as history. This revision supersedes their global exact-P01, publication-age, strict admission-prefix flush and broader implementation-scope assumptions.

| Reference | Confirmed direction and current disposition |
| --- | --- |
| U01 | Simplification preserves functionality and defaults. |
| U02 | Incoming storage is a configurable bounded rotating buffer. |
| U03 | Saturation does not reject the incoming event. Replacement follows the bounded-window interpretation in U19/U20. |
| U04 | Recoverable logging failures must not escape into business execution. |
| U05 | No logging-owned locks on the calling side; a short lock is not a compromise. |
| U06 | Keep caller work bounded and small; backend work stays off the caller. |
| U07 | Use simple atomic publication and a rotating cursor. The cursor is separate from event identity and age. |
| U08 | Preserve filters, interceptors, duplicate checking and extensible writers as backend capabilities. |
| U09 | Preserve short public logging calls and explicit event identity. Automatic business-event identity remains unapproved. |
| U10 | Qualify the real C# implementation with native tests, coherent work batches and complete release obligations. |
| U11 | Retain P01 ranking and its all-higher-priority fallback within the inspected window. Global exact selection is superseded by U19/U20. |
| U12 | Explicit frontend lifecycle operations return failure, timeout and cancellation results instead of exposing recoverable throws or faulted/canceled tasks. Preserve the earlier broader lifecycle decision outside this frontend work. |
| U13 | Legacy lazy factories and Microsoft formatters remain on the caller, at most once when needed, with recoverable exceptions contained. They run before frontend enqueue and are outside its bounded queue-work measurement. |
| U14 | The prior API direction is templates plus supplied values first, with structured interpolation deferred. That API work is separate from this frontend correction. |
| U15 | Cheap level/lifecycle rejection skips capture. Background processing filters act after capture; they do not belong in the frontend queue. |
| U16 | Confirmed frontend-only scope: queue, bounded replacement, handoff and associated frontend controls/accounting. Backend processing is a separate consumer responsibility. |
| U17 | The consumer polls lazily, builds and drains a sorted index, then rebuilds it. Cached entries identify the exact event occurrence, and claims must not consume a replacement under an old index entry. |
| U18 | Age is sourced from the event itself: an immutable creation timestamp or creation auto-incrementing long. The selected default direction is CreationSequence assigned once at event creation, with timestamp retained as an occurrence fact. |
| U19 | Use simple atomics and allow P01 deviations during concurrent changes. Do not restore a global admission index, announcement registry or producer helping mechanism to enforce exact selection. |
| U20 | Use a tunable percentage search budget, rounded up with a minimum of one slot. Global fallback is 30%; the initial severity profile is 20/30/40/50/60/70%. P01 applies within the inspected window, including deviations without a race when a better candidate is outside it. |
| U21 | Frontend flush covers one fresh consumer-captured batch. It is not an exact instantaneous prefix of admissions. Its target freezes after capture and does not grow afterward. |
| U22 | Stop closes new admission, allows already-entered producers to finish, and drains the frontend under one original deadline. Expiry requests abandonment of waiting work; active work remains unresolved until safe completion. |
| U23 | Update documentation before implementation; the current request does not start code or test implementation. |

### 2.2 Revision history and source limits

| Version | Meaning |
| --- | --- |
| 1.0 | Original feature definition with visible unresolved decisions. |
| 1.1 | Recorded initial P01, explicit lifecycle results, caller-side callbacks, template-first direction and LOG-05 clarification. |
| 1.2 | Narrows implementation to the frontend and records bounded-window P01, event creation order, consumer indexing, batch flush and close-then-drain stop. |

The source register preserves the original pinned repository evidence. At that baseline, the input queue and runtime use gates and full-buffer rejection; current backend policy and writer capabilities already exist. This revision records new behavior rather than claiming those contracts or implementation files have changed. No build, native test, stress run or benchmark qualifies this feature yet.

## 3 Scope and preservation boundaries

In scope are frontend input ownership, rotating storage, bounded search, atomic publication/replacement, consumer claiming/indexing, frontend accounting, batch flush and stop coordination. Integration removes caller-side queue/admission locks without moving backend work onto the producer.

Preserve `CIS.Core`, current namespaces, approved dependency closure, defaults, supported capacities, output formats, correlation/scopes, backend policies, writer registration and ownership, file recovery/retention, independent destinations and release verification. Backend regressions are checked as preservation evidence, not invitations to redesign those components. [S03, S08, S09, S12]

Separate work includes structured-template overload implementation, interpolation, new duplicate policies, rsyslog transport, hot writer/plugin loading, persistent disk spooling, Type Manager corrections and application migrations. Earlier user decisions about these boundaries remain recorded; they are not frontend delivery requirements.

## 4 Frontend execution contract

```text
BEFORE FRONTEND ENQUEUE
    Existing public-call checks and owned event creation
    Existing caller-side legacy factory/formatter when needed
    Assign immutable creation key once

FRONTEND PRODUCER
    Cheap running-state check
    Register active producer and recheck state
    Allocate rotating starting position
    Inspect at most K distinct slots
    One atomic exchange into the selected slot
    Record actual admission/eviction and unregister
    Return

BACKGROUND CONSUMER
    Poll and capture at most C slot/reference entries
    Sort by event creation key
    Conditionally claim each exact indexed occurrence
    Hand claimed events to existing backend processing
    Release index references and rebuild
```

### LOG-01 No caller locks or waits

Frontend producers acquire no monitor, mutex, semaphore, spinlock, owner token or equivalent busy-owner protocol. They do not wait for capacity, another producer, consumer progress, callbacks, output availability or I/O. No retry-until-success loop, per-event task, timer or synchronous fallback output is allowed. Atomic operations do not imply permission to build a lock.

Control-side request coordination may wait or serialize flush/stop callers where needed, but enqueue never acquires that coordination or executes its callbacks. Consumer polling removes producer notification from the ordinary path.

### LOG-02 Bounded producer work

For capacity `C` and effective incoming-severity percentage `p`, inspect at most `K = max(1, ceil(C * p / 100))` distinct slots. Valid percentages are 1–100, so K never exceeds C. The starting slot is included; wrap does not increase the budget. A free observation may end the search early. Contention does not restart the search.

The normal path has a fixed number of state/counter/cursor operations, at most K slot reads/comparisons, one final event-reference exchange and bounded bookkeeping. Sorting, reindexing, filtering, formatting and writing are consumer/backend work. Prepare reusable ring/index infrastructure at startup. Measure full-window costs at supported capacities; a percentage bound is not a capacity-independent latency claim.

### LOG-03 Owned event and creation age

Enqueue receives an already-created event whose occurrence facts and supported payload are owned and stable. Reuse of a caller's mutable event object must not allow later mutation to alter an admitted occurrence. Each publication has a distinct occurrence identity even when business payloads repeat.

The ordering key is assigned once at event creation and stored in the event. Reindexing never assigns age, slot reuse never transfers age, and a cursor is not an event key. Prefer the creation sequence direction selected in U18; finalize its rollover-safe representation/comparison before coding that comparator. Do not introduce arbitrary reflection, serializer execution or ToString calls into enqueue. Existing capture/truncation and legacy callback contracts remain upstream preservation boundaries.

## 5 Failure and lifecycle boundaries

### LOG-04 Safe failures

Recoverable frontend validation, publication bookkeeping and control-operation faults must return safe, truthful outcomes. Do not label an unpublished event Accepted, a dropped/evicted event delivered, or a failed control operation successful. Ordinary invalid input should not use throw-and-catch as normal control flow. No recursive logging or expensive exception formatting is performed on the producer.

Caller expressions evaluated before the API and fatal runtime/process failure are outside this guarantee. Existing public wrappers must retain safe callback/provider/scope behavior; this correction does not move those operations into the queue.

### LOG-05 Early rejection and closure

Cheap level/lifecycle rejection before event creation skips capture and legacy callbacks. Backend filtering operates on captured events and does not imply capture was avoided.

Frontend enqueue first observes its running state, increments its active-producer counter, and rechecks that state before touching ring storage. A failed recheck unregisters and returns Stopped. A producer that passed the recheck may finish its one bounded publication after Closing begins; stop includes such producers. This replaces the older assumption that every publication physically occurring after the closure flag must be rejected.

The counter is decremented in finally after publication and accounting. Stop does not clear/release the ring while such a producer can still publish. None of these steps waits on the producer path.

## 6 Rotating storage and replacement

### LOG-06 Capacity and configuration

Capacity bounds waiting slots, remains positive and fixed for the runtime, and retains `LoggingOptions.QueueCapacity` default 1024 and range 1–65536. No resizing or new mandatory startup argument is selected. [S04]

Capture configuration at startup. A per-severity search percentage overrides the global 30% fallback; the initial profile supplies Trace 20%, Debug 30%, Information 40%, Warning 50%, Error 60% and Critical 70%. Unset/cleared overrides inherit the global value. Invalid percentages produce explicit configuration failure. Hot reconfiguration is outside scope.

| Incoming severity | Initial percentage | Maximum checks for C = 10 |
| --- | --- | --- |
| Trace | 20% | 2 |
| Debug | 30% | 3 |
| Information | 40% | 4 |
| Warning | 50% | 5 |
| Error | 60% | 6 |
| Critical | 70% | 7 |

`None` is disabled, not an event priority. A positive tunable consumer idle polling interval is required; its numeric default remains an implementation measurement choice. Drain/reindex immediately while work remains. The polling interval is separate from the existing file flush interval.

### LOG-07 Admission always completes while eligible

For a valid owned event whose producer passed the running-state recheck, publish with one atomic exchange into the selected slot. An observed free slot is preferred. If no free slot is found within K checks, replace the best inspected candidate. Never reject because the ring/search window is full or because the selected occupant changed.

The exchange is publication's linearization point. Its returned reference identifies the actual displaced event: null means no eviction; a non-null occurrence means one real eviction. Count that actual occurrence, including its actual severity, rather than the event observed during scanning. The incoming occurrence may itself be consumed or evicted before its producer returns.

### LOG-08 P01 within the search window

Severity order is `Trace < Debug < Information < Warning < Error < Critical`. Among inspected occupied slots, choose the lowest severity and then the oldest event creation key at that severity. This is equivalent to the approved lower/equal/all-higher P01 cases on the inspected set.

If all inspected events outrank the incoming event, admission still replaces the oldest event at the lowest inspected severity. A Trace may therefore replace an Error. A free or lower-priority slot outside the window does not extend the search. Races may change the actual displaced severity/age after selection; this accepted best-effort behavior requires no retry.

Rotation spreads starting positions across the ring. It does not guarantee that the physical cursor points to the globally oldest event. Exact global victim selection, a shared sorted producer index, producer announcement/registration slots and a helping scheduler are superseded design directions.

### LOG-09 Consumer ownership and reuse

Producers replace whole immutable occurrence references. The sole consumer claims an indexed occurrence only if the slot still contains that exact occurrence, using conditional atomic removal. A cached index must not unconditionally remove a newer replacement under an older occurrence's position.

Claim and replacement can each remove a given occurrence only once. If the consumer wins, it owns a stable event independently of later slot reuse. If replacement wins, the consumer skips that stale index entry and the actual removed occurrence is evicted. Do not republish or pool occurrence identities while cached index references can survive.

### LOG-10 Consumer index and ordering

The consumer lazily captures up to C `(slot, exact occurrence)` entries, reads each occurrence's creation key, sorts its private index, drains it, releases the references, and rebuilds. It must not physically reorder/compact slots being written by producers. An index is a bounded observation pass, not an atomic snapshot of the entire ring.

Ordering is by creation key within the captured surviving set. New arrivals and replacements may wait for the next index. A producer paused before enqueue does not reserve a sequence position that the consumer must await. Global output order across delayed publications is not promised by this frontend.

## 7 Frontend accounting and control

### LOG-11 Admission and disposition accounting

Separate frontend admission from backend processing and writer delivery. Frontend terminal dispositions are Consumed (ownership handed to the consumer), Evicted (removed by producer exchange) and Abandoned (discarded by shutdown cleanup). Waiting occurrences remain unresolved. A stopped/invalid call that never publishes is not admitted.

At a settled, exact observation: `Admitted = Consumed + Evicted + Abandoned + Waiting`. Counts may additionally be grouped by actual event severity. Active publishers are separate because some have not published yet. Live independent counters may be temporarily skewed while operations are in flight; do not present them as a coherent frozen equation or backend delivery proof.

Frozen flush counts describe its captured target only: `TargetCount = Consumed + Evicted + Abandoned + Pending`. Pending here means target occurrences not yet settled by that batch, not necessarily still physically waiting. Classifying a stale batch entry does not increment the lifetime eviction counter again; that loss was counted by the actual producer exchange. Lifetime eviction diagnostics include losses that occurred before a particular batch was captured. Saturation/inexactness must be explicit and must not stop admission.

### LOG-12 Flush one captured consumer batch

An explicit frontend flush requests a fresh index capture at the consumer's next safe point. Once construction ends, the captured set is fixed. Events arriving during construction may be included; later events do not extend it. This is the user-accepted replacement for strict admission-prefix flush, not an exact instantaneous cut at API invocation or a maximum creation sequence.

The consumer conditionally claims target occurrences, and records replacements/removals as loss rather than processing success. It may finish the remaining batch as normal work after a caller times out. Before capture, the result must distinguish NoBatchCaptured from an empty captured batch; do not invent zero target/pending counts or backend output boundaries.

Flush completes when its captured occurrences have been handed off or their removal dispositions have been settled. A completed batch with covered eviction/abandonment reports loss. Events already evicted before capture are visible in lifetime diagnostics, not reconstructed into this target. Frontend completion says nothing about backend filtering, successful writing or durability.

Use one original timeout budget from the call, including scheduling and capture. Caller cancellation detaches its waiter with an explicit outcome; shared work needed by other callers continues. Requests pending before a capture may share that batch; requests arriving after the capture group is frozen use the next batch. Retire no-waiter pending controls and completed results rather than accumulating an orphan history. Do not retain a target/payload copy per waiter.

Existing public backend/runtime flush contracts are separate. Integration must label/map the frontend result without silently redefining an API that also promises output flushing. Public result/member names are implementation-review details.

### LOG-13 Close then drain on stop

Stop changes Running to Closing, prevents new producer enrollment through the recheck protocol, and allows already-entered publishers to finish. The consumer drains while stop waits for active publishers to reach zero. A final pass after that zero observation establishes no remaining waiting events. A caller paused before incrementing cannot publish later because its recheck sees closure.

Use one original stop deadline, including producer completion, drain and frontend cleanup. At expiry, request abandonment of remaining waiting work and return an explicit timeout observation. Active publishers and consumer/backend-owned work remain unresolved; do not count them as already abandoned or finished. Abandonment occurs through safe cleanup when the consumer/control coordinator can detach the remaining references.

An already-entered producer may publish after the timeout result. Cleanup must remain alive until it unregisters, and those late waiting events are abandoned rather than starting new backend processing. The consumer observes expiry before starting subsequent claims; an already-started claim/handoff remains active and may finish. Never label an occurrence Abandoned and later hand that occurrence to the backend.

Do not release the ring beneath an active publisher or dispose backend resources beneath unresolved processing/I/O. Frontend cleanup completion and the immutable timeout result are distinct. Repeated stop calls share the underlying stop operation; canceling one waiter does not reopen intake or cancel another caller's stop.

## 8 Backend preservation

### LOG-14 Policies remain backend work

Keep the existing interceptor → global filter → destination routing → duplicate-check order on owned facts in background processing. A failing redactor cannot be bypassed to forward sensitive original data. No backend callback enters the frontend scan/exchange path. Preserve existing field protection, routing precedence and failure observations. [S08]

### LOG-15 Duplicate suppression remains unchanged

Retain the opt-in application-key/window semantics, bounded cache and existing defaults. An occurrence evicted or abandoned before backend processing cannot reserve a duplicate key. Input eviction and duplicate-cache key eviction remain separate observations. No automatic suppression or library-invented business identity is introduced. [S08]

### LOG-16 Writers remain extensible and isolated

Preserve startup registration, the common writer binding, immutable JSONL writer payloads, independent destination progress, ownership, output overflow, file recovery and retention. A new writer still requires no Core destination-name branch. No rsyslog transport or writer redesign is included. Backend rejection/faults remain observable independently of successful frontend admission. [S09]

## 9 Existing public API boundaries

### LOG-17 Preserve short calls and earlier API decisions

Keep existing convenience methods, message-only `log.message`, explicit event names and Microsoft event identifiers. Frontend correction must not rebind existing overloads or remove caller-owned capture behavior.

The earlier template-first decision and safe missing/malformed-value behavior remain recorded for separate API work; interpolation, automatic field-name derivation, template hashes and typed event keys are not added to this frontend feature. Legacy factories/formatters stay upstream on the caller, at most once when needed, with exceptions contained and their own execution time reported separately. None is stored as a deferred closure for the consumer to invoke.

## 10 Acceptance scenarios

These are planned native C# cases against real frontend/runtime paths, not executed evidence or separate microtasks. Backend cases below qualify preservation only.

| ID | Scenario | Required observation |
| --- | --- | --- |
| AC01 | Default startup and explicit capacity | Existing defaults work without new mandatory input. |
| AC02 | Capacity 1, 2, 3 and repeated non-power-of-two wrap | Waiting storage stays bounded; no negative index or saturation rejection. |
| AC03 | Inspected set contains one Trace and higher severities; incoming Error | Trace selected in stable conditions; actual exchange result controls eviction accounting under a race. |
| AC04 | Equal-priority inspected candidates with different creation keys | Oldest creation key selected; replacement keeps its own key. |
| AC05 | Mixed inspected priorities/ages | Lowest severity then oldest creation key within the window; no global selection claim. |
| AC06 | All inspected events outrank incoming | Incoming publishes; oldest at lowest inspected severity is selected. |
| AC07 | Pause producer before enrollment, after recheck, before exchange and after exchange | Other producers/consumer continue; one bounded publication, no reserved-hole wait or ghost result. |
| AC08 | Pause consumer and separately block backend work | Producers complete bounded scans/exchanges while waiting slots rotate. |
| AC09 | Race producer replacement with exact-reference consumer claim | Occurrence is consumed or evicted once, never both; no torn mutable content. |
| AC10 | Seed cursor/creation/counter rollover boundaries | Safe indexing, defined ordering/comparison and visible diagnostic saturation; no admission stop due to wrap. |
| AC11 | Mutate upstream caller-owned data after log return | Existing owned-capture facts survive; frontend publishes stable occurrences. |
| AC12 | Invalid supplied event or injected recoverable frontend fault | Safe truthful result; no recoverable exception or invented admission. |
| AC13 | Existing scope/provider/error helper paths | Preservation checks show safe integration and no new caller queue lock or backend work. |
| AC14 | Throw/block in existing processing policies | Backend behavior preserved; frontend publication does not wait on policies. |
| AC15 | Duplicate suppression with input eviction/abandonment | Existing policy unchanged; never-processed occurrences do not reserve keys. |
| AC16 | External custom writer registration | Existing common registration, isolation and ownership preserved. |
| AC17 | Flush batch with consumed, replaced and pending targets | Fixed target equation; covered removal is loss; no missing-sequence wait. |
| AC18 | Timeout before consumer captures a batch | Explicit NoBatchCaptured/incomplete result, not an invented empty successful batch. |
| AC19 | Stop racing enrollment/publication/claim | New calls stop; enrolled calls may finish; final drain follows active-publisher zero. |
| AC20 | Early disabled, stopped and recursive public calls | Existing safe early behavior; no unnecessary capture/callback or worker restart. |
| AC21 | Existing message-only, named and lazy consumers | Public call patterns preserved; no new template/interpolation implementation required here. |
| AC22 | Audit all ordinary frontend paths | No logging-owned lock/wait, notification primitive with hidden blocking, or retry-until-success. |
| AC23 | Quiet/full/contended measurements by incoming severity | At most K probes and one exchange; caller latency, CPU, allocations and atomic counts recorded. |
| AC24 | Existing backend scopes, outputs, recovery and provider ownership | No capability/default regression. |
| AC25 | Focused checks and coherent integration/release boundaries | Real C# tests run; empty/failed/skipped required selections fail; full release obligations preserved. |
| AC26 | Frontend startup/flush/stop failure, timeout and cancellation | Explicit outcomes; no recoverable throw or faulted/canceled lifecycle task. |
| AC27 | Legacy factories/formatters and disabled calls | Existing caller-side at-most-once behavior preserved outside enqueue; no deferred closure or foreground backend work. |
| AC28 | Early thresholds versus backend suppression | Capture skipped only by upstream early decisions; backend filtering still receives captured events. |
| AC29 | Percentages, fallback, overrides, rounding and K boundary | 10-slot 30% budget is 3; small capacities inspect at least 1; starting slot included; no repeated/wrapped excess probes. |
| AC30 | Consumer index entry replaced before its turn | Conditional claim leaves the newer event for reindexing; physical slots are never compacted. |
| AC31 | Admissions during and after batch capture; delayed older creation key | Capture-pass membership documented; frozen target never grows; no wait for an absent creation sequence. |
| AC32 | Shared flush requests, one cancellation and repeated expired requests | Other waiters continue; newer requests use the next capture; controls and target references do not accumulate. |
| AC33 | Stop deadline while a producer is paused before its exchange | Timeout reports unresolved publisher; cleanup remains alive and abandons its late waiting publication safely. |
| AC34 | Held sorted index while producers repeatedly replace the ring | Explicit ring/index/in-flight memory bounds; no unbounded payload, reservation or eviction history. |

Use controlled barriers rather than sleeps as concurrency proof. Measure capacity, producer count, drain rate and scan percentages together. No arbitrary latency, allocation or coverage target is invented.

## 11 Delivery approach after authorization

| Batch | Outcome | Relevant verification |
| --- | --- | --- |
| A | Real bounded rotating frontend, immutable occurrence ownership, scan configuration, one-exchange publication and consumer indexing | Native probe-bound, replacement, claim, ordering and paused-thread cases. |
| B | Frontend accounting, captured-batch flush, producer enrollment and close/drain/abandon stop | Real control races, cancellation, timeout, late-publisher and bounded-memory cases. |
| C | Preserve existing public/backend integration, update usage/contracts and qualify combined source | Native consumer/backend regressions, caller-cost measurements and coherent Developer checkpoint. |
| D | Qualify a publishable candidate only when separately requested | Complete unit, integration, mutation, architecture, compatibility and business-case release obligations. |

Use established native focused verification during implementation and complete release checks at the appropriate boundary. Do not restart the older roadmap or implement the separate API/backend work as part of this frontend feature. This document starts none of these batches. [S02, S10]

## 12 Decision register and remaining engineering detail

| ID | Current disposition |
| --- | --- |
| D01 | Closed behavior: P01 on the inspected window, with accepted race deviations, free-slot preference inside that window and unconditional admission. |
| D02 | Closed behavior: explicit frontend lifecycle results, captured-batch flush and close/drain stop under one original deadline. Earlier broader lifecycle direction remains outside this frontend implementation. |
| D03 | Closed preservation boundary: legacy factories/formatters stay on the caller before enqueue. |
| D04 | Closed separate API direction: templates first; interpolation deferred. Existing short calls are preserved, not redesigned here. |
| D05 | Architecture selected: fixed ring, bounded rotating search, one producer exchange, private consumer index and conditional claim. The v0.1 global-index/helping proof obligation is superseded; implementation correctness and native qualification remain required. |

Routine engineering details still require specification/verification: numeric idle-poll default, public mapping of frontend results without changing backend flush meaning, rollover-safe creation-key representation/comparison, and live-versus-frozen diagnostic projections. They must not introduce a new producer limit, locks/waits, rejection at a retry limit or removed defaults. No new product-policy question blocks this documentation revision.

## 13 Definition of done

After implementation is authorized, the actual frontend must honor its scan bound, publish eligible events without capacity rejection, preserve immutable ownership under races, expose actual eviction/consumption, and implement the accepted batch flush and stop behavior with truthful failure results. Backend/public capabilities and defaults remain functional.

Completion requires real native evidence, a transitive frontend call audit, controlled races, measured caller costs and matching contracts/examples. A document update, passing model, ring cursor or average throughput result alone does not qualify the implementation. Release gates remain separate from feature completion.

## 14 Handoff guardrails

Read this revision and D05 v0.2 together before any later authorized implementation. Preserve older documents and source history. Do not reintroduce global exact-P01, publication-age ordering or strict instantaneous-prefix flush from superseded drafts.

Keep implementation direct: no shared sorted admission index, producer announcement registry, helping scheduler, owner flag, unbounded overflow queue, per-event task, producer retry loop or hidden notification lock. Do not enlarge this frontend feature into backend filtering/writer/template work. Control-side coordination must never become a producer dependency.

Report exact source and actual native evidence when implementation eventually starts. This update does not authorize implementation, repository publication or external review payloads. The unchanged historical source register follows.

## 15. Source register

All repository references below are pinned to the reviewed commit. Section 2.1 records the explicit user instructions from this conversation; those instructions are not inferred from repository code. Older review counts and qualification reports remain historical and must be read with their own source identity.

| Reference | Source | Use |
| --- | --- | --- |
| S01 | [Current main commit](https://github.com/andrewspiteri/CISLIB/commit/4a263fd3f56405586e453909d771f7d2558e86cd) | Pinned baseline and commit time; not test evidence. |
| S02 | [Repository instructions](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/AGENTS.md) | Current authority, native-test workflow and scope instructions. |
| S03 | [Recorded foundation decisions](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/planning/decisions.md) | Preserved package identity, allowed dependencies and recorded user amendments. |
| S04 | [Incoming pipeline contract](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/docs/modules/logging/incoming-pipeline.md) | Current two-stage execution, capacity, incoming results, accounting and lifecycle. |
| S05 | [IncomingEventQueue implementation](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/implementations/csharp/src/CIS.Core/Logging/Runtime/IncomingEventQueue.cs) | Direct evidence of the current Queue, locks, full rejection, sequencing and boundary implementation. |
| S06 | [CoreLogRuntime implementation](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/implementations/csharp/src/CIS.Core/Logging/CoreLogRuntime.cs) | Direct evidence of caller-side gates, capture, factories and current result/lifecycle paths. |
| S07 | [CoreLogger implementation](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/implementations/csharp/src/CIS.Core/Logging/CoreLogger.cs) | Direct evidence of ordinary public guard and factory-exception behavior. |
| S08 | [Processing policies and convenience methods](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/docs/modules/logging/processing-policies.md) | Implemented processing order, duplicate settings/semantics and convenience methods. |
| S09 | [Runtime, writer registration and ownership contract](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/docs/modules/logging/runtime-contract.md) | Current common writers, JSONL contract, ownership and retained lifecycle semantics. |
| S10 | [Ordinary builds and focused native tests](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/docs/architecture/csharp-focused-tests.md) | Focused native verification, build-host corrections and documented Python retirement; no new run claimed. |
| S11 | [17 October review continuation](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/planning/reviews/corrective-continuation-2026-10-17/README.md) | 23/24 older approved review bundles and outstanding review/release limitations. |
| S12 | [Repository overview and package boundaries](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/README.md) | Current module/package overview and unpublished implementation boundaries. |
| S13 | [Type Manager status; historical qualification context](https://github.com/andrewspiteri/CISLIB/blob/4a263fd3f56405586e453909d771f7d2558e86cd/planning/type-manager/implementation-status.md) | Separate Type Manager scope and qualification history; no blanket completion claim. |
