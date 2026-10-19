# D05 technical design for the rotating logging frontend

**Project:** CISLIB / `CIS.Core`  
**Feature:** `LOG-PRIORITY-SPOOL`  
**Version:** 0.2  
**Date:** 19 October 2026  
**Requirements:** [Frontend BRD v1.2](C:/Users/andrs/Downloads/CISLIB_Logging_Priority_Spool_Feature_BRD_v1.2.md)  
**Status:** Agreed architecture and control behavior recorded for implementation review. Native implementation, measurements and qualification remain unexecuted.  
**Authorization:** Documentation only. No production or test implementation, commit, package change or publication starts with this revision.

## 1 Selected design

Use a fixed array of immutable occurrence references, an atomic rotating cursor, a finite search budget and one final producer exchange. Producers inspect at most a severity-dependent percentage of slots. They stop at the first observed free slot or select the lowest severity and oldest event creation key within the inspected window.

The consumer is a separate background thread. It polls lazily, captures a private index of exact slot/occurrence pairs, sorts that index using each event's creation key, conditionally claims indexed occurrences and hands them to the existing backend. It rebuilds the index after the batch is drained. Filtering, formatting, duplicate checks, routing and writer work are backend responsibilities.

P01 is best-effort across the whole ring and under concurrent changes. A free slot or better victim outside the bounded window does not extend the search. A slot can change after observation; the producer still completes one exchange and accounts for the occurrence actually displaced. Unconditional eligible admission and bounded caller work take precedence over globally exact victim selection.

Frontend flush covers one fresh captured consumer batch. Stop closes new producer entry, waits for already-entered publishers, and drains under one original deadline. Expiry requests abandonment; active operations and safe late cleanup remain visible as unresolved.

This replaces v0.1's six shared severity lists, global FIFO admission structure, announcement registry, helping scheduler and strict-prefix flush design. Those mechanisms are not required to satisfy the revised policy. D05's architecture selection is settled; implementation correctness and qualification are not claimed by this document.

## 2 Scope and existing contracts

Frontend input is an owned, already-created occurrence. Existing public calls may still perform their cheap enabled checks, bounded capture and approved legacy factory/provider formatter invocation before entering enqueue. The queue itself performs no callback, arbitrary object conversion, JSON work, output routing or writer admission.

Preserve current package identity, dependencies, existing configuration defaults, backend policy order and duplicate semantics, custom writer registration, destination isolation, file recovery and ownership. Preserve the prior template-first API direction as separate API work; do not implement templates or interpolation as part of this correction.

Existing `IncomingSequence`, runtime flush results and diagnostics currently describe older acceptance/prefix contracts. Integration must explicitly map or distinguish frontend observations rather than silently repurpose an existing backend delivery guarantee. Internal semantic names in this design do not freeze new public signatures.

## 3 Data and ownership

| Item | Representation and responsibility |
| --- | --- |
| Ring | `C` stable slots, each holding null or an immutable occurrence reference. Slots are not physically sorted or compacted. |
| Occurrence | Owned facts, immutable creation key and distinct reference identity for this publication. Payload facts may share already-owned immutable data. |
| Cursor | Atomic counter used only to choose a starting index; never an age, acceptance ticket or slot reservation. |
| Effective budget | Six startup-captured integer K values derived from capacity and configured percentages. |
| Active publishers | Atomic count covering each producer's running-state recheck through final bookkeeping. No participant registration table. |
| Lifecycle | Running, Closing, expiry/abandonment requested, and final frontend cleanup completion. Only controls wait. |
| Consumer index | One reusable array of at most C slot/reference entries, with sort keys read from each event. Release processed/skipped references promptly. |
| Flush requests | One current batch and one pending capture group, plus metadata for live waiters. Shared waiters do not get independent payload snapshots. |
| Diagnostics | Actual admission/removal counts, separated from backend outcomes and from unresolved producer operations. |

Do not reinsert the same occurrence reference after removal or pool an identity while an index can retain it. If an upstream consumer reuses an event-data object for multiple log calls, its captured publication occurrences still need distinct identity and creation keys. A reference comparison must distinguish two occurrences with equal business data.

The event creation key is assigned once at event creation. CreationSequence is the default direction; timestamp remains an occurrence fact. The precise rollover-safe representation/comparator is an engineering item before coding that comparison. Neither sorting nor reindexing updates the key.

## 4 Search configuration

Preserve capacity default 1024 and range 1–65536. Capture all frontend settings before producers start. Search percentage precedence is per-severity override, otherwise global fallback. The global fallback is 30%; the initial default profile supplies the following overrides. Clearing/unsetting an override returns that severity to the fallback.

| Incoming severity | Initial percentage | K at C = 10 |
| --- | --- | --- |
| Trace | 20% | 2 |
| Debug | 30% | 3 |
| Information | 40% | 4 |
| Warning | 50% | 5 |
| Error | 60% | 6 |
| Critical | 70% | 7 |

Validate percentages as 1–100 and calculate `K = max(1, ceil(C * p / 100))`. Integer arithmetic must produce that ceiling. The starting slot counts toward K. Inspect at most K distinct positions, wrapping once as necessary; never restart because a slot changed. `None` follows existing disabled behavior before enqueue.

A positive tunable idle polling interval is separate from file flushing. Its numeric default is a measurement choice, not a newly user-approved value. While work remains, the consumer continues to drain/reindex without inserting an idle delay. No new mandatory startup argument, live resize or hot configuration framework is introduced.

## 5 Producer operation

The following is an operation specification, not C# implementation code.

1. Return a safe invalid/stopped outcome for invalid input or a frontend already observed closed. Event creation/callback work is upstream.
2. Atomically increment ActivePublishers, then recheck the frontend state. If Closing has begun, unregister and return Stopped without touching slots.
3. For a producer that passed the recheck, advance the rotating cursor once and derive a nonnegative starting index. This operation owns no slot yet.
4. Inspect up to the precomputed K slots using safe atomic/volatile reference reads. Stop on an observed null. Otherwise keep the lowest severity candidate, breaking equal severity by the event's creation key.
5. Atomically exchange the supplied occurrence into the selected slot exactly once. Do not compare/retry against the scanned occupant. Even a changed occupant does not reject the new occurrence.
6. Record one admission. If the exchange returned non-null, record one eviction using that actual occurrence and its actual severity. A null return means the slot was free at publication, irrespective of earlier observations.
7. Decrement ActivePublishers in finally after bookkeeping. Return the immutable admission result. No accepted result is returned before the exchange commits.

The final exchange is publication's linearization point and supplies the real displaced reference. It gives a definite per-slot ownership transition without coordinating a separate producer index. [Interlocked.Exchange](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.exchange?view=net-10.0).

P01 on any nonempty inspected set is equivalent to the minimum `(severity, creation key)`: a lower severity wins; absent lower severities, equal wins; if all are higher, the lowest available higher severity still wins. Incoming severity determines the budget, not permission to reject.

For a 10-slot Debug call, K is 3. If those three occupied slots contain Error E1, Trace T2 and Warning W3, choose T2. If a concurrent producer has replaced T2 with Critical C4 by the final exchange, this call may evict C4. That accepted race deviation is counted as a Critical eviction. An uninspected free slot or older Trace does not extend the budget.

## 6 Producer work and race bounds

For an enrolled, valid enqueue, the queue work consists of a fixed number of lifecycle/counter/cursor operations, at most K slot reads and comparisons, one event-reference exchange, and fixed bookkeeping. There is no slot-owner wait, CAS retry loop, ring sorting, history reconciliation, task allocation per event, callback or I/O in this path.

The scan cost grows with configured capacity and percentage. At default capacity 1024, a Critical full-window search can read up to 717 slots; it is bounded, not zero-cost. Early free-slot discovery shortens the normal case. Benchmark quiet, full and contended workloads to tune capacity, drain rate and percentages together; do not hide a producer scan inside a helper or claim capacity-independent latency.

| Race | Required behavior |
| --- | --- |
| Producer pauses before registering | Others continue. If it registers after closure, its recheck returns Stopped before slot access. |
| Producer pauses after the running recheck | It is enrolled; others continue. Stop accounts for its unresolved operation and preserves cleanup until it unregisters. |
| Two producers choose the same slot | Exchanges serialize on that slot. One may immediately evict the other's newly published occurrence. Both real admissions remain accounted for. |
| Consumer claims before producer exchange | Consumer owns the old reference; producer sees null or another actual occupant from its exchange. It cannot evict the consumer-owned object. |
| Producer exchange wins first | The old indexed reference is gone; conditional consumer claim fails and leaves the new occupant for a later index. |
| Producer observes null but another producer fills it first | The final exchange still publishes; its non-null return is a real eviction and accepted race deviation. |
| Event is consumed/evicted before its producer returns | The producer still returns its truthful admission result; delivery is not implied. |

The cursor is a rotating hint, not proof of the oldest global slot. Whole-buffer exact P01, free-slot detection beyond K, global FIFO output across delayed publication and per-caller real-time scheduling guarantees are not promised.

## 7 Consumer polling and indexing

At an idle poll or immediately after draining a batch, the consumer reads up to C slots into its reusable index. Each entry records the slot index and exact immutable occurrence reference. Sort entries by the occurrence creation key. Do not move the physical slot contents.

For each entry, conditionally replace the slot with null only if that exact occurrence is still present. `CompareExchange` provides this reference-conditional atomic operation. An unconditional Exchange under a cached index could consume a newer replacement at an older event's sort position. [Interlocked.CompareExchange](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange?view=net-10.0).

A successful claim transfers ownership to the consumer. Record the frontend Consumed disposition before invoking backend work, release the index entry and process the owned event independently. On failure, skip/release the stale entry; do not claim a different occupant through it. The sole consumer is the only normal claimant. Producer exchange and shutdown cleanup are the other removal mechanisms and must remain distinguishable.

After the index is drained, rebuild. A partial poll is not an atomic global snapshot. New occurrences, including a delayed event with an older creation key, may appear in a later batch. The consumer never waits for absent creation sequence values. Creation-key order applies to the surviving events in the captured set.

No ordinary producer notifies the worker through TaskCompletionSource, semaphore, timer or task scheduling. Polling/timers and request continuations are consumer/control-side work. Backend callbacks remain outside any frontend control bookkeeping gate.

## 8 Flush capture and membership

Frontend flush is a request for a fresh consumer batch at the next safe point. Record the caller's monotonic start immediately; scheduling delay uses the original budget. Do not infer the target from the highest creation sequence, the rotating cursor or a cumulative acceptance counter.

Before constructing the requested index, freeze the group of pending requests that will share it. Release any remaining stale index metadata before reusing the single index array; this does not discard surviving ring events. Capture at most C current references, sort them, and mark construction complete. That set is the fixed target.

Requests already in the frozen group share the same target and batch bookkeeping. Requests arriving after group freeze wait for the next fresh capture, even if the current construction has not finished. An event arriving while the current pass reads the ring may be included; an event arriving after target capture is complete cannot extend it. A replaced slot is never substituted into an already captured target.

This explicitly supersedes an instantaneous admission-prefix contract. For example, a flush request can be made at t0, then an event can arrive at t1 before the consumer captures at t2 and be included. Once capture ends, later traffic cannot prolong the target. Backend/runtime flush contracts remain separate and are not silently changed to mean this operation.

Conversely, an event created before t0 but not yet enqueued can arrive after capture and belong to a later batch. An empty captured batch can therefore complete while a producer has not yet published. Flush does not wait for such missing creation keys; stop has the separate active-producer enrollment/drain rule.

## 9 Flush settlement and observations

Each target occurrence ends as Consumed, Evicted or Abandoned; until the consumer settles its target disposition, it is Pending for this batch. With one consumer and distinct occurrence identities, a stale reference removed by a producer is an eviction. Abandonment is recorded by shutdown cleanup, not guessed from absence. An already consumer-owned occurrence cannot later be evicted or abandoned as waiting work.

Maintain `TargetCount = Consumed + Evicted + Abandoned + Pending`. Pending is unresolved target work, which may already have left the physical ring but not yet been classified by the consumer. Lifetime ring diagnostics and batch counters are separate; evictions before capture are not reconstructed into this target. Classifying a stale batch entry updates only that batch, not the lifetime eviction count already recorded by the producer exchange.

Control-only coordination may protect request grouping and coherent immutable result snapshots. The consumer may briefly serialize a claim/disposition metadata update with timeout/stop observations, but ordinary producers never acquire that coordination. Hold no such gate across sorting, a capacity-sized capture/cleanup pass, awaiting, a backend callback or I/O. A producer never helps perform control work.

Result snapshots distinguish capture-not-completed from a captured empty batch. If the deadline/cancellation fires before capture completes, report an explicit incomplete/canceled outcome and no invented target counts. If a batch exists, freeze its settled/pending counts at observation. Later progress does not mutate a returned result.

A batch is coordination-complete when all targets settle. It is lossless only if no target was evicted or abandoned; Consumed means ownership handoff, not backend processing or writer success. Lifetime loss can coexist with a lossless current batch. Do not reuse a backend delivery-success flag without an explicit mapping.

## 10 Flush timeout cancellation and retirement

One original monotonic budget covers scheduling, capture and target settlement. A caller timeout leaves normal consumption running. Cancellation detaches that waiter with an explicit result, without canceling another live waiter's batch or stopping normal logging.

Maintain at most one current batch and one pending request group. Remove expired/canceled waiters promptly. A pending group with no live waiter needs no new special capture; normal polling continues. A current batch can finish as the consumer's ordinary batch, but no abandoned waiter or completed result remains rooted as a history. This replaces v0.1's nondroppable exact-prefix control assumptions.

Each live waiter retains only control metadata, budget and completion observation. It does not retain its own C-entry target array or captured payload copy. No flush control occupies an event slot. No higher creation number is awaited to fill a missing sequence gap.

## 11 Stop enrollment and final drain

The first stop caller atomically changes Running to Closing and establishes the shared stop operation and original deadline. Subsequent stop callers join that operation, keeping independent wait-cancellation outcomes. Neither cancellation nor timeout reopens the frontend.

The producer's increment-then-recheck protocol is essential. A producer that passed the first read but pauses before incrementing can resume after stop observed zero; its second read then rejects without accessing slots. A producer that passed the second read was already counted and may finish its bounded publication. This is a fixed amount of producer bookkeeping, not a lock or a participant registry.

While Closing, let the consumer drain and wait on the control side for ActivePublishers to reach zero. After zero, make a final fresh pass. No eligible producer can subsequently publish: those already enrolled have finished, and late registrants fail their recheck. Frontend completion requires no unresolved publisher and no remaining ring event, plus finished frontend cleanup.

An active backend-owned event is already consumed from the frontend. Its processing/writing completion remains a backend lifecycle responsibility. Do not make frontend success imply that output has been written or durable.

## 12 Stop expiry and late cleanup

At the original deadline, request abandonment and freeze an explicit timeout result. An event already claimed or in a claim/handoff operation is active, not waiting. A producer paused inside publication is an unresolved publisher, not an admitted or abandoned event invented in advance.

The consumer observes expiry before beginning subsequent claims and removes remaining waiting references as Abandoned rather than handing them to backend work. Coordinate the expiry/claim metadata boundary on the consumer/control side so an occurrence cannot be classified Abandoned and then delivered. An already-started claim or handoff may finish; it remains active/unresolved in a timeout observation until its actual outcome is known. A deadline is not preemption of arbitrary executing code.

If a producer was enrolled before Closing and resumes after timeout, it may still perform its one exchange. Keep safe cleanup alive until it records its actual admission/eviction and unregisters. Late events left waiting are abandoned by cleanup. A final pass after publisher zero catches such arrivals. Do not free the array, pool identities or report final cleanup complete while that producer can still access the ring.

If the consumer is blocked in backend work, timeout returns with unresolved work and cleanup resumes only when safe. Waiting items are not declared physically removed before cleanup actually detaches them. Do not dispose backend resources under active I/O. The original timeout result remains immutable; later frontend/backend completion is reported separately.

## 13 Counters rollover and bounded memory

Actual event transitions drive counters: producer exchange returns the eviction reference; successful conditional claim yields Consumed; cleanup removal yields Abandoned. A selected-but-not-actually-displaced candidate does not increment eviction. ActivePublishers includes pre-publication operations and is reported separately from admitted event counts.

At a closed, settled frontend observation: `Admitted = Consumed + Evicted + Abandoned + Waiting`. Independent live counters may temporarily skew because a consumer can claim an event before its producer updates Admitted. Label them accordingly; do not fabricate a coherent lifetime boundary by clamping values or reading unrelated counters once. Batch snapshots use their own fixed target and coherent control-side bookkeeping.

Use wrap-safe cursor indexing: interpret the cursor's bit pattern without negative modulo, derive an index in `[0, C)`, and wrap the local scan index explicitly. Cursor rollover never rejects admission. Diagnostic saturation must be visible, separate from event identity, and never stop publication.

CreationSequence rollover is a remaining comparator/representation detail, not solved by using the cursor or a maximum sequence for flush. Specify the supported ordering domain and rollover behavior before implementing that comparison; a raw signed comparison across wrap is not acceptable. Any need to change the existing positive public IncomingSequence contract must be reviewed explicitly. This revision approves no additional public epoch/token API and does not claim that a wider integer eliminates every lifetime limit.

| Resource | Intended live bound |
| --- | --- |
| Waiting references/payloads | At most C ring occurrences. |
| Current index | At most C references; replaced occurrences can remain reachable until their index entries are released. Include these retained payloads in memory measurements. |
| Consumer/backend active occurrence | One current consumer item, with existing independent backend/output ownership separately accounted for. |
| Producer locals | O(P) bounded occurrence references for overlapping calls, including their actual returned victims. No library-owned overflow list. |
| Flush target storage | Shares the single consumer index/current batch; no C-entry copy per waiter or canceled request. |
| Request metadata | O(F) for live waiters, one current batch and one pending group; no orphan history. |
| Producer scan | At most K distinct reads/comparisons, one event-reference exchange and fixed bookkeeping. |

Thus C slots do not imply that only C payload objects are reachable: a held index can retain up to another C old occurrences, plus active work and producer locals. The bound must be documented honestly and measured. No reservation ledger, retired-event history, thread registry or per-event task list is allowed.

## 14 Verification after implementation authorization

Qualify real C# frontend/runtime paths with the established native focused-test workflow. These are planned cases, not test implementation or passing evidence.

| Area | Required cases and observations |
| --- | --- |
| Search | Validate percentage precedence, ceiling rounding, capacities 1/2/3/non-power-of-two, K includes start, early free stop, no extra probe on contention, and no incoming capacity rejection. |
| Selection | Literal stable-window P01 examples, equal-priority creation order, all-higher fallback, an outside-window free/better candidate, and an actual victim changed by a racing producer. |
| Ownership | Pause either producer around its exchange; race exact-reference consumer claim; establish one removal reason per occurrence and stable consumer data after slot reuse. |
| Consumer | Sort only event creation keys, skip stale entries, leave replacements for rebuild, handle timestamp ties if that representation is used, and never wait for missing sequence values. |
| Flush | Fresh capture group, new events during/after collection, fixed target, empty versus not-captured distinction, mixed consumed/evicted/pending counts, partial timeout and immutable returned observations. |
| Shared controls | Concurrent flush waiters, one canceled waiter, newer requests assigned to the next capture, repeated no-waiter requests while consumer is blocked, and bounded target/control memory. |
| Stop | Pause before/after enrollment and recheck, before/after exchange and bookkeeping; close while producers run; final drain only after zero; safe stopped calls. |
| Expiry | Paused enrolled producer publishes after timeout, late publication abandoned by cleanup, blocked consumer, active backend ownership, no disposal beneath active work, and no Abandoned-to-Consumed reversal. |
| Counters | Actual severity of returned victims, admitted versus active publisher distinction, exact final/batch equations, labeled live skew, saturation and rollover boundaries. |
| Preservation | Existing defaults, public call patterns, upstream callback gates, policy order, duplicate reservation, custom writers, independent outputs, file recovery and ownership. |
| Cost | Instrument maximum probe/exchange counts; measure caller latency distribution, CPU and allocations at quiet/full/contended loads across capacity, drain rate and incoming severity. |

Use controlled barriers and explicit seams rather than sleep-based race claims. Measure frontend work separately from upstream supplied callback duration and backend time, retaining full caller latency as well. Preserve complete integration/release obligations without running a release campaign for each small edit.

## 15 Integration and remaining engineering choices

Do not add a shared admission index or helping protocol to recover guarantees the user deliberately relaxed. Keep queue classes direct and avoid unrelated backend/API rewrites. Source contracts, examples and tests that assume global exact P01, publication age, full-only replacement or strict-prefix frontend flush must be reconciled during the later authorized implementation.

The following details remain implementation-review items rather than new business-policy questions: numeric idle-poll default, concrete startup option/result member names, mapping frontend controls without weakening backend flush, creation-key rollover representation/comparator, and coherent live-versus-frozen diagnostic exposure. They require explicit documentation and relevant native verification before claiming the implementation complete.

Earlier versions and their source evidence are retained. The next section preserves the v0.1 historical source inspection; it is not a fresh checkout qualification or evidence that source contracts already implement v0.2. No implementation has started with this revision.

## 16 Historical source inspection

The BRD baseline is `4a263fd3f56405586e453909d771f7d2558e86cd`. The inspected local checkout was at `2ac647d695e473a2ba8e5b77402eacd61be08133` with unrelated existing worktree changes. A bounded diff of the eight primary inputs below against the BRD baseline reported no differences. Existing worktree edits were left untouched.

| Primary local input | SHA256 at inspection |
| --- | --- |
| `docs/modules/logging/incoming-pipeline.md` | `A4E9864929D6C093442A9178B9F164889C68C54C2174774EA783081FB251F654` |
| `docs/modules/logging/runtime-contract.md` | `5380E31E4FB2580A6CD0EB3F4D7FF9E4B74AE1479BCCB0AC80064B8503A50284` |
| `implementations/csharp/src/CIS.Core/Logging/Runtime/IncomingEventQueue.cs` | `81A0AF32346042082935B5F97AF16793DCB1513CE13DE25D235318377BBA9486` |
| `implementations/csharp/src/CIS.Core/Logging/CoreLogRuntime.Incoming.cs` | `7FC6F70B8C634730140F01C08A68547714967ACC566BE67E56690E55F4ED8F15` |
| `implementations/csharp/src/CIS.Core/Logging/CoreLogRuntime.cs` | `FF388720E86BA3E93D3058791063D5B6BA48FE6EDDF28BA6829BDA0DB9D0BF49` |
| `implementations/csharp/src/CIS.Core/Logging/Runtime/FlushBoundary.cs` | `777D2EC8771582B4A2BD31546D836D9B79E2127F01C4E31F7FEE587EE403C337` |
| `implementations/csharp/src/CIS.Core/Logging/LogWriteResult.cs` | `31ED1495E6D8038C563C84AAB1BAF7852CA64DFCC37D45EA74D288C4266FCB3F` |
| `implementations/csharp/src/CIS.Core/Logging/LoggingOptions.cs` | `0B2EB552068B851C79A8827F909B831CBBD79BD5DCAD5975A1623B0B9941575E` |

Additional inspected material includes `AGENTS.md`, current logging plan amendments and foundation decisions, `FlushDeadline`, incoming queue/runtime native tests, and the current v1.1 BRD. Source-derived requirements and the proposed design are separate from runtime qualification: no CISLIB build, C# test, benchmark or concurrency model was executed for this design.

The required local graph refresh completed with 11,868 nodes, 59,724 edges, zero reported build warnings/errors and fresh validation. Build identity was `sha256:1a752390082d0cf3287b13f5c8855f7c2deea8b9bc89bb7874ebfa03ca31109c`. The initial broad `IncomingEventQueue` lookup was truncated at eight matches; the exact class query was untruncated. The exact `RegisterIncomingBoundary` caller query found `FlushIncomingAsync` and `StopIncomingAsync`, with no truncation or proposed edges. Those call observations identify extractor `cis.csharp.semantic-facts/3`; the bounded output did not supply a graph schema version or establish full compiler capability coverage. Graph evidence is navigation, not a complete semantic coverage claim; canonical source supplies the behavioral observations. No external reviewer received repository payloads.
