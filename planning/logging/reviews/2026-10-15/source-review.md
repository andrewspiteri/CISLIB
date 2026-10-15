# CIS logging implementation review

**Review date:** 15 October 2026  
**Implementation:** `Logging(2).zip`  
**Specification:** Core Library Implementation Guide, specification 1.1, 8 October 2026  
**Backlog:** Core Library Implementation Plan, version 1.0, 9 October 2026  
**Review method:** Static source inspection, control-flow tracing, and explicitly identified boundary calculations. No C# build, original test suite, or release qualification was executed.

## 1. Overall assessment

This is a substantial implementation, not just a collection of contracts. It contains event capture, typed values, operation scopes, JSONL and human rendering, independently queued console/file outputs, file inventory and transitions, flush/shutdown coordination, diagnostics, and a Microsoft logging provider.

The foundation is worth keeping. The strongest parts are immutable value ownership, the separation of operation context from process identity, early filtering, accepted-only tickets, explicit delivery accounting, and a shared shutdown operation. These are real implementation decisions visible in the source—not claims that their tests have passed.

I would pause expansion briefly for a correctness and readability pass. The main concerns are file-recovery behavior under ordinary retention conditions, an unjournaled destructive rotation step, deadline decisions that depend on timer dispatch, inconsistent invalid-scope handling in the provider, and a growing concentration of lifecycle/composition logic in dense classes. There are also important disagreements between the attached baseline and the source's stated later decisions.

I would not discard the library or replace its foundations with a larger framework. The useful next step is targeted repair and simplification, followed by tests through the actual application entry points.

### Scope and limitations

The archive contains **61 C# source files, 4,606 physical C# lines, and two README files**. `CoreLogRuntime.cs` has 611 lines; `Runtime/OutputEventQueue.cs` has 444.

There are no project/solution files, build configuration, test projects, test results, package lockfiles, contract fixtures, or release artifacts in this archive. Referenced definitions such as `EnvironmentOptions`, `EnvironmentKind`, the `CIS.Configuration` types, and `CIS.Text.UnicodeScalarText` are not supplied. Their implementation cannot be inferred from the call sites.

The review environment also has no available `dotnet` executable. Accordingly, this report does **not** establish that the original project builds, that its package dependency graph is compliant, that a test has failed or passed, or that any coverage/mutation threshold is met. Tests proposed below are a regression backlog, not executed evidence. Absence from this ZIP does not establish absence from the repository.

### Evidence terminology

**Confirmed source behavior** means the implementation contains the cited branch or operation sequence. **Source-derived counterexample** means the stated result follows from that sequence under the stated conditions, but the C# scenario has not been executed here. **Contract decision** means the source and baseline disagree, or the required policy needs clarification in the maintained contract. **Verification gap** means the attachment does not contain the evidence needed to decide.

Priorities are review recommendations: **P1** before expanding or treating the affected behavior as complete; **P2** before freezing the public baseline; **P3** after correctness, with measurements where appropriate.

## 2. Reconcile the contract before implementing fixes

The implementation plan explicitly makes the guide authoritative for exact semantics. However, the source describes subsequent changes not present in those attachments. The separate 11 October environment discussion also superseded the old environment-initialization framework: the environment options class is a value store, with preparation/validation owned elsewhere. Do not restore that old framework merely to satisfy the older guide.

| Area | Attached guide/plan | Current source | Review disposition |
| --- | --- | --- | --- |
| Environment entry point | `EnviormentsOptions.Initialize` and immutable startup snapshot | `EnvironmentOptions` values consumed by parameterless `CoreLogRuntime.Create()` | Treat the later value-store direction as intentional; review logging's own preparation and capture. The environment implementation is not supplied. |
| Ordinary oversized payload | Reject oversized ordinary input globally; only bounded exception clipping is specified | Message/template, collection shape, and encoded payload truncation, with `IsTruncated` and `TruncatedEventCount` | Source README calls this user-selected. Preserve it pending an updated authoritative contract; do not silently replace it with rejection. |
| Record cap | Fixed 64 KiB JSONL cap in v1 | `MaxEntryBytes`, default 65,536, accepted range 16,384–16,777,216 | Define the new limit and its interaction with schema version, human output, file size, and memory limits. |
| Microsoft integration | Optional separate bridge assembly/package; generic core independent of Microsoft logging types | README says `CIS.Logging.MicrosoftLogger` is compiled into the same Core assembly | Verify the actual project and approved decision. Namespaces/folders do not establish a package boundary. |
| Level tokens | Seven full level names | Additional abbreviations, described as approved in README | Carry the approved aliases into the configuration fixtures and compatibility contract. |
| External scopes | Same copying, bounds, precedence, and rejection principles as core | Provider-created scopes copy at BeginScope; external scopes copy at event capture; invalid external scopes may be omitted | Document the timing distinction and settle invalid-state policy explicitly. |

The archive links to `docs/modules/logging/event-encoding.md`, `event-qualification.md`, and `exception-projection.md`, but does not include those documents. They may contain important qualifications; this report does not invent their contents.

**Required action:** maintain one current contract/addendum, with decision provenance and corresponding fixture changes. Until that exists, classify these as baseline discrepancies—not automatically unauthorized implementation defects. The same-assembly dependency approval is asserted by the source README; no independent approval or project manifest is included here.

## 3. Correctness and operational findings

### R01 — P1: Rotation deletes retained evidence before recording recoverable intent

**Evidence:** confirmed operation order; source-derived failure scenario.  
**Locations:** `Runtime/FileOutputOwner.cs:103–136`, particularly 115–126; `Runtime/FileGroupTransition.cs:14–36,42–47`; `Runtime/FileGroupInventory.cs:94–101`.

`Rotate` first calculates the next inventory. It then deletes the oldest retained members and only afterwards calls `FileGroupTransition.Begin`. `Begin` itself can fail during the active-file flush/hash, candidate creation/write, or publication of the transition file.

At the retention limit, the sequence is therefore:

1. The published inventory still lists the oldest closed segment.
2. That segment is deleted.
3. The process terminates, or transition creation fails before the intent becomes visible.
4. A new runtime sees no transition to reconcile.
5. Ordinary inventory validation encounters the missing listed segment and rejects initialization.

This is an avoidable availability failure of the existing rotation/recovery mechanism. It is not a claim that the active record was corrupted or that normal retention deletion itself is prohibited. A normal I/O failure can trigger it; abrupt power loss is not required.

**Recommended correction:** make retirement part of the recorded transition. Publish an owned intent identifying the exact planned retirements before performing them, and define reconciliation for not-started, partially retired, renamed, inventory-published, and fresh-active states. Keep the hard segment bound and exact ownership checks. Do not “fix” this by broadly ignoring missing inventory members or deleting directory matches.

**Regression:** run rotation at the retention limit with a failure immediately after each deletion and before/inside intent publication. Recreate the runtime and assert the documented safe recovery state, bounded retention, and preservation of unrelated files. Use controlled I/O seams plus real filesystem cases; do not require a production public fault-injection API.

**Traceability:** Guide §11; plan WP05-S06 and WP05-S07.

### R02 — P1: Partial-record recovery stops working when normal retention is full

**Evidence:** confirmed guard and source-derived steady-state scenario.  
**Locations:** `Runtime/FileGroupInventory.cs:127–143`, especially 135–138; `Runtime/FileOutputWriter.cs:23–43`; `Runtime/FileOutputOwner.cs:139–158`.

`AfterPartialRecovery` rejects recovery when `Segments.Count > maximumRetainedFiles`. The count includes the active file. For a limit of ten closed files, normal steady state is eleven inventory members: ten closed plus active. Any partial active record at that point makes startup fail with `FileRetentionLimitExceeded` rather than preserving the partial segment and resuming fresh output.

The initial check in `FileOutputWriter.Initialize` allows the normal ten-closed-plus-active state. The subsequent recovery calculation rejects it because it needs one extra closed/damaged segment. Consequently, recovery works while there is spare retention capacity, but fails after an ordinarily active rotation group reaches its configured limit.

The source explicitly chooses to preserve all pre-existing segments during startup. That is stronger than the guide's rule that **startup rollback** must not delete pre-existing evidence. These policies need to be distinguished. Unconditionally deleting old segments in a catch/rollback path would be wrong; performing a planned, owned retention transition during successful recovery is a different operation.

**Recommended correction:** provide an explicit committed recovery policy for a full group, coordinated with R01. Retire the appropriate oldest owned segment under that policy while preserving the damaged active segment, or explicitly document operator-required recovery and provide an actionable diagnostic. Do not advertise automatic recovery without its full-capacity qualification.

**Regression:** limits 1, 10, and 1,000, with exactly that many retained members and a partial active tail. Include failure to delete the required oldest segment. Verify damaged-segment classification, exact final count, fresh JSONL framing, and unaffected neighboring files.

**Traceability:** Guide §11; plan WP05-S07-AC01 through AC04.

### R03 — P1 verification priority: Deadline decisions depend on timer callback dispatch

**Evidence:** source-derived concurrency counterexample; requires C# regression confirmation.  
**Locations:** `Runtime/FlushDeadline.cs:29–58,60–66`; `CoreLogRuntime.cs:262–290,327–346`.

Elapsed time is updated in `Tick`, but `ObserveCompletion` neither updates elapsed time nor consults a recorded boundary-completion timestamp. It returns the callback's saved observation if one exists; otherwise it takes the current completion observation.

A deterministic scenario for a controllable clock is:

1. Start a flush/stop while a writer is held at a barrier.
2. Advance monotonic time beyond the timeout, while withholding timer callback dispatch.
3. Release the writer and allow its flush/completion path to run.
4. Complete the wait before the delayed timer callback runs.

The source permits `ObserveCompletion` to return current successful completion without establishing that it occurred inside the budget. The relevant distinction is not UTC clock movement; it is **elapsed deadline versus callback scheduling**.

This has not been reproduced on the CLR here. It is a concrete branch that should be tested before signing off the documented deadline semantics, not a claim that every timeout is broken. The use of monotonic time is a good foundation; a monotonic source alone does not settle the delayed-dispatch race.

**Recommended correction:** qualify the behavior with controlled scheduling, then make the timing verdict depend on monotonic deadline/completion observations rather than solely on whether the timeout callback ran first. Preserve the distinction between work completed before the deadline but observed later, and work that actually completed after the deadline. A naive “check the clock after await” can conflate those cases.

**Regression:** delayed timer dispatch, sub-millisecond positive timeout, completion just before/after the deadline, caller cancellation, a late in-flight write, and simultaneous stop joiners. Retain the original shared stop deadline.

**Traceability:** Guide §12 flush and stop semantics; plan WP05-S08 and WP05-S10.

### R04 — P2: Invalid external host scopes are converted into accepted truncated events

**Evidence:** confirmed source behavior and a contract-policy inconsistency.  
**Locations:** `MicrosoftLogger/ProviderScopeCapture.cs:9–25`; `Scopes/CapturedOperationContext.cs:8`; `MicrosoftLogger/ProviderStateCapture.cs:38–65,68–100`.

External scope capture catches `ArgumentException` from `Copy` and substitutes `CapturedOperationContext.Omitted`. The replacement has empty attributes, no operation ID, and truncation provenance. That allows the event to continue as a valid truncated capture.

For example, an external host scope containing a valid `operation_id` plus an unsupported object can lose the **entire** scope, including that operation ID. This does not take the normal global `InvalidEvent` path. The equivalent provider-created scope propagates its invalid-state exception instead.

This makes malformed or unsupported input behave differently depending on which scope route the host uses. It also conflates invalid input with the newer “oversized but valid payload” truncation policy.

**Recommended correction:** define one explicit unsupported/invalid-scope policy. Under the supplied baseline, invalid captured input should reject the event and increment invalid-event accounting. An approved tolerant external-scope policy needs separate documentation and observable diagnostics; it should not be indistinguishable from size truncation. Avoid blanket exception swallowing as the policy definition.

A plain string scope also takes different paths: provider-created `BeginScope("text")` is rejected by `Copy`, while an external string scope is omitted. Decide whether supported scalar scopes should map to a named attribute or be explicitly unsupported. Do not imply general Microsoft scope compatibility without that qualification.

**Regression:** identical invalid/unsupported/oversized states through direct core scopes, provider-created scopes, and `LoggerFactory` external scopes. Assert operation ID, retained attributes, write/diagnostic classification, and formatter invocation.

**Traceability:** Guide §§9–10; plan WP06-S01-AC04 and WP06-S02.

### R05 — P2: Scope saturation can remove the provider's numeric EventId

**Evidence:** confirmed merge policy; source-derived boundary example.  
**Locations:** `Events/EventProjection.cs:108–120`; `Scopes/CapturedScopeContext.cs:41–64`; `MicrosoftLogger/ProviderStateCapture.cs:27–33`; `MicrosoftLogger/CoreLoggerProvider.cs:8–9`.

The provider creates an `event_id` attribute. Final merging adds scope keys first and retains at most 1,023 distinct root keys. Once that dictionary is full, a new explicit event key is omitted unless it replaces a key already present.

A valid scope with 1,023 distinct scalar attributes therefore consumes the entire root key budget. A subsequent provider event with only its numeric `event_id` can lose that attribute during merging. The source provider contract says `EventId.Id` supplies the integer attribute, including zero and negative values.

This is a budget/precedence contract issue, not an unbounded-memory bug. Same-key overrides work; **retention priority for newly introduced event fields** is the issue. Under a truncation policy, the priority must be deliberate.

**Recommended correction:** reserve protocol-owned adapter fields and define retention priority after scope/event precedence is resolved. Do not let incidental insertion order decide whether promised adapter metadata survives. Carry the decision into the revised truncation contract.

**Regression:** a full scope without `event_id`, one with a conflicting `event_id`, large nested scoped values, and event-specific keys added at the boundary. Assert both values and truncation accounting.

**Traceability:** Guide §10 provider mapping; plan WP06-S01-AC02 and WP06-S02-AC02.

### R06 — P2: A human renderer failure can reject an otherwise valid JSONL event globally

**Evidence:** confirmed catch/admission structure; a source-derived extreme-envelope fixture.  
**Locations:** `CoreLogRuntime.cs:476–488`; `Rendering/HumanRenderer.cs:62–78`; `EventMetadata.cs:151–163`.

Both selected output representations are prepared before admission. The same catch converts `EventEnvelopeTooLargeException` from either canonical encoding or a human renderer into a global `InvalidEvent` result. Thus an output-specific protected human-envelope failure also prevents an otherwise valid JSONL record from reaching the other output.

A source-derived ASCII size calculation establishes one boundary fixture under the metadata class's accepted optional-text rules: at `MaxEntryBytes = 16,384`, a record with a 15,816-character host name and the other fixed fields listed in the accompanying review fixture occupies exactly 16,384 JSONL bytes. The verbose human renderer's required fields and reserved marker/message/LF consume 16,419 bytes. It therefore rejects the human envelope, and the runtime's catch rejects the event globally. This calculation does not execute the C# renderer; public reachability of such unusual metadata also depends on the omitted `EnvironmentOptions` implementation.

The general control-flow concern is independent of that extreme example: canonical invalidity and destination-specific renderability are collapsed into one global outcome.

**Recommended correction:** choose and document the per-output render-failure contract. Validate impossible fixed output envelopes during startup where feasible, and avoid suppressing a valid independent destination due to another destination's presentation failure. Preserve global rejection for invalid canonical event capture. Coordinate any changed result/truncation semantics before freezing the API; simply catching and ignoring the human exception is not sufficient.

**Regression:** JSONL fits while verbose human does not; reversing output assignments; one output already faulted; normal versus verbose detail; and exact-boundary identity/component sizes.

**Traceability:** Guide §§11–12 output isolation; plan WP05-S01, WP05-S02, and WP05-S03.

### R07 — P2: Public diagnostics discard the in-flight gauge already maintained internally

**Evidence:** confirmed projection omission.  
**Locations:** `Runtime/OutputAccounting.cs:4–6`; `Runtime/OutputEventQueue.cs:173–178`; `LogOutputDiagnostics.cs:8–15,41–42`.

Internal accounting contains separate `Pending` and `InFlight` values. `LogOutputDiagnostics` copies `Pending` but exposes no `InFlight` property. A consumer cannot distinguish queued work from the accepted record currently inside the writer.

The guide explicitly calls for pending/in-flight observation. This is a small omission in the public projection, not a reason to invent a new diagnostics framework.

**Recommended correction:** expose an immutable in-flight-write count from the same locked observation. Expose queued/buffered count as well if it belongs in the intended public contract, or derive it consistently as `Pending - InFlight`. Keep these as gauges, not cumulative counters, and distinguish an in-flight record from an outstanding buffer flush.

**Regression:** a blocked writer with one accepted in-flight record and two queued records; after release; after deadline abandonment; and after eventual completion. Assert the accounting identity at every observation.

**Traceability:** Guide §12 delivery accounting; plan WP05-S03.

### R08 — P2: Failed metadata candidates have no bounded cleanup/retention policy

**Evidence:** confirmed creation/preservation behavior; operational resource-policy gap.  
**Locations:** `Runtime/FileGroupInventory.cs:149–162`; `Runtime/FileGroupTransition.cs:23–36`.

Inventory and transition publication create uniquely named `.tmp` candidates. The inventory comment explicitly says failed private candidates are preserved. Neither component retires them or includes them in the managed segment limit.

Repeated failed runtime creation or replacement attempts can consequently leave an increasing number of owned metadata artifacts at the same group path. There is no automatic runtime retry loop here; the growth requires repeated attempts. Nevertheless, bounded event queues and bounded closed segments do not bound these failure artifacts.

**Recommended correction:** define exact ownership and bounded retention for failed private candidates. A safely identified temporary file created by the current failed attempt is different from a pre-existing active/retained segment or a published transition intent. Preserve evidence deliberately where needed, but do not make preservation an unlimited accumulation policy. Never clean up using a broad directory wildcard.

**Regression:** repeated failures after candidate creation and before publication, later successful recovery, pre-existing similarly named foreign files, and permission failures during cleanup.

**Traceability:** Guide §11 bounded file lifecycle and safe rollback; plan WP05-S05 through WP05-S07.

## 4. Readability and structure

### R09 — P1 maintainability: The two central classes are too dense to serve as the intended example

The guide's roughly 60-line method/300-line type thresholds are review triggers, not automatic pass/fail limits. Even allowing that distinction, `CoreLogRuntime` and `OutputEventQueue` need attention. They are 611 and 444 physical lines respectively, and many lines contain several statements or complete control-flow blocks.

Concrete examples include `CoreLogRuntime.cs:172–177,227–240,541–578`, `OutputEventQueue.cs:267–278,309–348,351–440`, and the compound inventory validators. The composition call at runtime lines 172–177 passes a long sequence of same-shaped delegates, including noncancellable and cancellable write/flush variants. `EventMetadata` has a 17-argument internal constructor, `CapturedEventFacts` has 16 constructor arguments, and `OutputQueues` has 14 parameters. Most are internal, so they can be simplified without creating a permanent public construction contract.

`CoreLogRuntime` currently combines settings capture, output acquisition, startup rollback, logger creation, scope access, early filtering, event capture/render preparation, admission aggregation, live diagnostics, flush observation, deadline orchestration, shared stop, and disposal. This is more responsibility than the public façade needs.

`OutputEventQueue` combines admission, ticketing, the worker loop, flush registrations, file timer ownership, cancellation-source lifetime, fault disposition, cleanup, and public-observation projection. Some coupling is necessary to maintain invariants; that does not justify dense transitions.

**Recommended order:**

1. Expand multiple statements and one-line control blocks. Use ordinary braces, explicit branches, and names. Do this before structural movement so review can compare behavior.
2. Consolidate production composition into one path. Migrate tests away from redundant historical kernel/composition seams without deleting their regression assertions.
3. Extract only coherent responsibilities: configured output acquisition/rollback, and the runtime's shared flush/stop coordination are sensible candidates. Keep admission and ticket accounting synchronized together.
4. Use named arguments immediately where adjacent same-typed values are easy to swap. For large repeated sets of facts, use an internal named construction shape rather than adding more positional parameters.
5. Write a short transition/invariant table for the queue: which state permits admission, what can be in flight, how controls resolve, and who releases ownership. Keep independent dimensions such as retained fault history and stopping state explicit; do not force them into an inadequate single flag.

Do **not** replace the density with generic pipeline frameworks, a proliferation of public interfaces, a class per tiny action, or partial files for one large class. The objective is a human-readable implementation with a small number of recognizable responsibilities.

Keep the public `CoreLogger` façade, result objects, and immutable value model small. Those are closer to the intended style.

**Traceability:** Guide §6; plan REQ-008 and the relevant work-package review rules.

## 5. Performance and bounded resource review

### R10 — P3: Reduce repeated hot-path conversion only after measuring it

There are clear extra passes in the current implementation:

- `EventProjection.Resource` rebuilds the invariant resource dictionary/value graph on each captured event (`Events/EventProjection.cs:123–152`).
- `EventEncoder` measures JSON and writes JSON; `HumanRenderer` then parses that JSON back into a DOM (`Events/EventEncoder.cs:15–32`; `Rendering/HumanRenderer.cs:15–16`). Human-only logging still takes the JSON encode/parse route.
- Two human outputs with identical detail independently prepare equivalent human representations (`CoreLogRuntime.cs:479–480`).
- `EventEncoder` uses an `ArrayBufferWriter` and then copies the written bytes into the owned event array. This is an explicit copy, not an unbounded queue leak.
- Rotation hashes the entire active segment and performs disk-flush operations (`Runtime/FileGroupTransition.cs:18–35`). That work is outside the admission lock, but it can increase file-worker latency and queue pressure.

These are source observations, not benchmark results. Do not attach invented throughput or allocation percentages to them.

The lowest-risk improvement is caching the immutable resource projection per runtime. Next, benchmark retaining captured facts for direct human rendering, while preserving the canonical size policy. Reuse a prepared representation when two outputs genuinely share format/detail/limits. Keep output-specific ownership and truncation results correct.

Do not remove size measurement, Unicode validation, or recovery integrity checks solely to reduce lines of code. Measure disabled logging, ordinary JSONL, ordinary human, two outputs, many attributes, exceptions, saturation, and rotation separately.

### R11 — P2 design review: Count bounds are not a practical total-memory budget

`QueueCapacity` permits 65,536 events and `MaxEntryBytes` permits 16,777,216 bytes. Their product is **1,099,511,627,776 bytes: 1 TiB of encoded records for one full output queue**, before additional representations and bookkeeping. That is a capacity calculation, not measured resident memory, and it does not imply that memory is allocated at startup.

At default values, the corresponding product is **64 MiB per full output queue**. Shared encoded events can reduce combined memory between outputs; human representations add buffers. Concurrent producers also perform capture before admission. None of this invalidates bounded queues, but it means the configured bounds should be evaluated together.

Before freezing these new limits, measure the intended workloads and decide whether a joint startup budget, a lower supported combination, or explicit capacity guidance is appropriate. Preserve approved behavior and avoid introducing a general resource-management framework.

## 6. What is implemented well

The following design choices should survive the corrective pass:

**Immutable ownership.** `LogValue` owns collection storage and safely shares immutable child values. Captured exceptions do not retain `Exception.Data`. Encoded buffers remain internal. A read-only wrapper is used on newly owned storage, not as a substitute for copying an arbitrary caller dictionary.

**Operation/process separation.** `OperationScopeStore` is per runtime and uses immutable parent frames. Process metadata is not used as mutable operation state. LIFO disposal checks are present, and the documented inability to reliably identify every prohibited inherited-flow disposal is more honest than claiming perfect enforcement.

**Early-exit behavior.** `CoreLogRuntime.Write` validates severity, handles stopped/filter/all-faulted exits before invoking the lazy factory, captures outside the admission gate, and evaluates the factory once. The stopped-plus-None precedence is present. Factory exceptions are not silently disguised as successful logging.

**Independent admission and truthful tickets.** Each output has a bounded Channel using incoming-event rejection rather than eviction. Successful tickets are assigned only after `TryWrite` succeeds. No total cross-output order is promised. Blocking writer I/O is outside the shared admission lock.

**Delivery accounting.** Written counts advance after successful writer completion, not dequeue. A terminal attempted failure and the abandoned tail are tracked separately. Faulted outputs stop accepting while the other output can continue. Flush results distinguish successful drain from previously observed loss.

**Flush controls.** Control registrations are separate from the saturated event queue and coalesce by accepted boundary. The worker prioritizes a ready flush boundary before later records. Caller-specific waits are not stored as a growing worker-side control list. The absence of supplied stress tests prevents certification, but the structure addresses the correct problem.

**Shared shutdown.** The first stop publishes a shared task, closes admission, and later callers join it. Caller cancellation does not become cancellation of the common operation. An already-cancelled first caller still initiates stop. Disposal joins that operation rather than opening another drain window.

**Context lifetime.** Worker startup suppresses execution-context flow, avoiding accidental retention of its creator's operation scope. Physical I/O is not disposed merely because a cancellation request was issued.

**File identity checks.** The inventory records format/encoding/group members, closed names derive from group identity, moves avoid overwriting collisions, and restart validation does not simply append based on a newline alone. These mechanisms are valuable; the retirement/recovery edge cases need repair, not abandonment of ownership checking.

**Provider ownership.** Provider disposal is explicitly non-owning and does not shut down a runtime used by direct core consumers. Event names, numeric IDs, original format, formatter output, and levels have explicit mapping code.

## 7. Completion assessment against the plan

This is an assessment of visible implementation, not a story-status update.

| Work area | Visible in attachment | What cannot be concluded |
| --- | --- | --- |
| WP-01 foundation | Namespaces and comments describing composition | Actual project references, SDK/tool pins, dependency allowlist, analyzers, package assets, test discovery |
| WP-02 contracts | Public logger/options/value/metadata/result/diagnostic/provider types | Agreement with the latest approved contract; compatibility fixtures; publication history |
| WP-03 environment | Logging preparation and metadata capture consume environment values | Variable loading, precedence, discovery, and the latest environment value-store implementation |
| WP-04 capture | Substantial value, exception, scope, correlation, and early-filter logic | Full boundary, mutation, concurrency, and contract-fixture qualification |
| WP-05 outputs/lifecycle | Substantial JSONL/human, console/file, queues, accounting, inventory, rotation, flush, and shutdown code | Windows/Linux ownership/restart evidence, timer races, saturation/cleanup stress, and resolution of this review's findings |
| WP-06 Microsoft bridge | Real provider/state/scope implementation exists | Actual package boundary, approved dependency graph, real LoggerFactory integration tests |
| WP-07 verification | No verification material supplied | Any test/coverage/mutation/architecture/compatibility gate |
| WP-08 examples | README material only | Runnable candidate-package examples and their observed outputs |
| WP-09 release | No release material supplied | Frozen contracts, exact tested package identity, historical baseline, clean replay, or release readiness |

The READMEs are stale: the root README repeatedly says runtime/output integration is subsequent work, and the provider README says provider implementation is pending, while corresponding code exists. This makes the ZIP less useful as agent context and can encourage duplicate or contradictory implementation work. Update them to describe current responsibilities and point to maintained status/evidence, without claiming tests have run.

The supplied plan begins as a planned backlog; it is not current completion evidence. Do not assign an overall completion percentage from file count or the mere presence of classes.

## 8. Recommended corrective sequence

**First: establish the current contract.** Record the approved environment simplification, payload truncation policy, cap, aliases, provider packaging, and scope timing/invalid-state policy. Update stale README/status text.

**Second: correct and qualify file transitions.** Address R01/R02 together with an explicit retirement/recovery transaction and failure tests at each step. Include R08's temporary-artifact policy. Avoid expanding file features until the current recovery path works at the normal retained-file limit.

**Third: qualify lifecycle and provider edge cases.** Add R03's delayed-dispatch timing tests; resolve R04/R05; verify per-output rendering behavior in R06; project the existing in-flight count for R07.

**Fourth: simplify the existing implementation.** Expand compressed code, consolidate redundant composition/test seams, and extract the two or three coherent responsibilities that genuinely reduce the reading burden. Preserve behavior tests during movement.

**Fifth: measure and complete verification.** Evaluate R10/R11, then run the repository's actual functional suites and package-level checks. Keep the six-family release requirement and exact-candidate evidence separate from these development review steps.

## 9. Regression test backlog — proposed, not executed

| Test ID | Scenario | Required observation | Related finding/plan |
| --- | --- | --- | --- |
| RV-01 | Retention deletion succeeds, intent publication fails | Deterministic safe restart; no unrelated deletion; explicit state if recovery cannot proceed | R01; WP05-S06/S07 |
| RV-02 | Interruption at every rotation transition step | Recoverable documented inventory/active/closed combination, preserved evidence, bounded files | R01; WP05-S07 |
| RV-03 | Partial active tail at a completely full retention group | Approved damaged-segment/retirement behavior, not an undocumented startup dead end | R02; WP05-S07 |
| RV-04 | Required retirement deletion fails during recovery | No excess segment creation; clear fault; other outputs independent | R01/R02; WP05-S06 |
| RV-05 | Advance monotonic time past timeout without dispatching timer | Timing verdict distinguishes actual late completion from timely completion observed later | R03; WP05-S08/S10 |
| RV-06 | Completion immediately either side of deadline | Immutable accurate result; no deadline restart for joiners | R03; WP05-S10 |
| RV-07 | Already-cancelled first stop caller | Admission closes; caller cancels; another caller gets shared result | WP05-S10 |
| RV-08 | Blocked write beyond shared stop deadline | Queued work abandoned, in-flight work pending, no disposal beneath active I/O | WP05-S10 |
| RV-09 | Thousands of cancelled/timed-out flush callers with blocked output | Queue/control registration retention remains bounded after caller detachment | WP05-S08 |
| RV-10 | Invalid/unsupported external scope versus provider-created scope | Approved consistent classification, operation-ID handling, and counters | R04; WP06-S02 |
| RV-11 | Full 1,023-key scope plus provider EventId | Documented protected adapter metadata and truncation priority | R05; WP06-S01/S02 |
| RV-12 | Canonical JSONL fits, human required envelope does not | Approved per-output behavior; valid other output not accidentally lost | R06; WP05-S01/S03 |
| RV-13 | One in-flight record and two queued records | Public pending/in-flight/buffered observations are consistent | R07; WP05-S03 |
| RV-14 | Repeated failed metadata candidate publication | Bounded owned failure artifacts; no foreign-file deletion | R08; WP05-S05/S07 |
| RV-15 | Console Warning, file Debug through actual LoggerFactory | Debug reaches file; formatter called once only when core is interested | WP06-S02 |
| RV-16 | Provider disposed while runtime shared | Direct core logger remains usable; provider stops forwarding | WP06-S02 |
| RV-17 | Caller mutates nested input after BeginScope/Write returns | Queued output retains captured facts | WP04-S01/S03/S04 |
| RV-18 | Saturated output and concurrent producers | No eviction, accepted-only tickets, stable per-output ordering/accounting | WP05-S02/S03 |
| RV-19 | Two real processes target one file group on Windows and Linux | Exclusive ownership, no interleaving or rotation corruption | WP05-S05 |
| RV-20 | File writer becomes idle after one record | Timer schedules a serialized flush without new traffic or accumulating work | WP05-S09 |
| RV-21 | Actual public Create with invalid settings/changed working directory | Public validation and preparation behavior, not only an internal test seam | WP03/WP05-S04/S05 |
| RV-22 | Current truncation policy across direct/core/provider/human/JSONL routes | Exact field retention, type stability, marker, counters, and loss results | Current-contract addendum; WP04/WP05/WP06 |

Every concurrency case needs barriers or controlled scheduling plus a finite test deadline. A sleep followed by an assertion does not prove the intended interleaving was exercised. Real filesystem/process tests should supplement internal seams, not be replaced by them.

## 10. File-by-file inspection index

The notes below identify responsibilities and the main review linkage. “No separate finding” means no additional issue was identified by this static pass; it is not a test pass or correctness proof.

| File (relative to `Logging/`) | Lines | Inspection note |
| --- | ---: | --- |
| `Configuration/LoggingFilePath.cs` | 64 | Lexical path normalization, including a special Windows tilde path. Keep targeted Windows tests; do not remove the special handling without verifying the no-filesystem-probing requirement. |
| `Configuration/SeverityTokenParser.cs` | 35 | Small readable parser. Approved-abbreviation assertion needs the current contract/fixtures; absence and invalidity remain distinct. |
| `CoreLogDiagnostics.cs` | 30 | Owned snapshot and cached stop result. Simplify compressed assignment lines; retain explicit runtime-versus-output state meaning. |
| `CoreLogInitializationException.cs` | 15 | Safe fixed output error categories; underlying details deliberately omitted. Qualify operator usefulness in failure tests. |
| `CoreLogLevel.cs` | 20 | Explicit stable severity values 0–6; no separate finding. |
| `CoreLogRuntime.cs` | 611 | R03/R06/R09/R10. Main concentration of composition, capture, admission, observation, and lifecycle responsibilities. |
| `CoreLogState.cs` | 12 | Runtime state explicitly independent of individual output faults; do not treat Running-with-faulted-output as an accidental bug. |
| `CoreLogger.cs` | 38 | Small useful public façade; method-level guards and lazy forwarding are appropriately limited. |
| `EventMetadata.cs` | 185 | Immutable allowlisted facts; environment source omitted. Long positional constructor; optional-text bounds relevant to R06. |
| `Events/CapturedEventFacts.cs` | 47 | Owned immutable capture; 16 constructor arguments merit a named internal construction shape. |
| `Events/EncodedEvent.cs` | 16 | Internal owned encoded bytes; current producer passes a newly owned array. No public mutable-buffer exposure. |
| `Events/EventByteProjection.cs` | 127 | Truncation allocation/retention order is a substantive contract. Update the authoritative policy and exact boundary fixtures. |
| `Events/EventEncoder.cs` | 36 | Measured JSONL encoding, complete LF, and consistency check. R10: separate measurement/allocation/copy passes; do not optimize without evidence. |
| `Events/EventProjection.cs` | 154 | Capture-time correlation and allowlisted resource construction. R05 merging priority; R10 repeated invariant resource creation. |
| `Events/EventWireFormat.cs` | 156 | Centralized field/value encoding and byte sizing; keep differential exact-byte tests for finite numbers, Unicode and all optional fields. |
| `Events/JsonTextBytes.cs` | 54 | Same encoder used for scalar byte measurement and output. Supporting Unicode input validation is partly in omitted CIS.Text code. |
| `Exceptions/CapturedException.cs` | 37 | Owned bounded tree/provenance; no retained Exception graph. Stale “future event pipeline” comment. |
| `Exceptions/ExceptionProjection.cs` | 60 | Node/depth/text limits and aggregate child ordering. Test custom accessor failures and truncation; accessors remain cooperative caller code. |
| `LogEventData.cs` | 28 | Small named input; adapter callbacks are internal. Explicitly document that concurrent input mutation is unsupported. |
| `LogFlushResult.cs` | 100 | Immutable boundary result; R03 is upstream deadline determination, not a request to reset cumulative counters. |
| `LogOutputDiagnostics.cs` | 47 | R07: in-flight accounting not projected. Fault history is deliberately retained after stop. |
| `LogOutputState.cs` | 14 | Stable output states; qualifying their transitions belongs with the queue tests. |
| `LogOutputWriteResult.cs` | 19 | Owned accepted-only ticket/result shape; no separate finding. |
| `LogStopResult.cs` | 30 | Cached immutable copy of initial stop observation; resource cleanup may continue, as documented. |
| `LogValue.cs` | 176 | Good typed ownership foundation. Truncation is newer than the guide; string/numeric constructor behavior needs current fixtures. |
| `LogValueKind.cs` | 28 | Explicit portable representation discriminators; no separate finding. |
| `LogWriteOutcome.cs` | 33 | Explicit write/output status values; admission is distinguished from delivery. |
| `LogWriteResult.cs` | 22 | Owned output observations and truncation marker. Coordinate result semantics before changing render timing. |
| `LoggingOptions.cs` | 163 | Simple public values plus preparation. Current cap differs from baseline; duplicated validation/composition and in-place path preparation warrant review. |
| `LoggingOutputEnums.cs` | 28 | Explicit format/stream/detail enum values; no separate finding. |
| `MicrosoftLogger/CoreLoggerProvider.cs` | 100 | Real non-owning adapter exists despite stale README. R04/R05 plus package-boundary decision. |
| `MicrosoftLogger/ProviderScopeCapture.cs` | 29 | R04: invalid external scope substitution and scalar-scope policy. Copy timing differs between provider and external scopes. |
| `MicrosoftLogger/ProviderStateCapture.cs` | 157 | Bounded supported scalar/collection conversion and cycle checks. R05 EventId retention; test root metadata at exhausted budgets. |
| `OperationContextData.cs` | 15 | Named operation input, no global operation state. No separate finding. |
| `Rendering/EventRenderer.cs` | 23 | Explicit format routing; R06 per-destination failure is handled globally by runtime. |
| `Rendering/HumanRenderer.cs` | 93 | Control-safe bounded display, with required envelope/payload policy. R06 and JSON round-trip overhead R10. |
| `Rendering/HumanText.cs` | 126 | Shared escaping/measurement for control and formatting characters. Test all separator/control cases and exact UTF-8 byte counts. |
| `Rendering/RenderedEvent.cs` | 12 | Small internal read-only representation view; JSON can reuse encoded bytes. |
| `Rendering/RenderedHumanEvent.cs` | 13 | Owned human bytes plus truncation provenance; no separate finding. |
| `Runtime/AdmittedEvent.cs` | 6 | Captured event, ticket, and optional prepared output bytes. Relevant to memory arithmetic R11. |
| `Runtime/ConsoleOutputWriter.cs` | 27 | Borrowed stream ownership and awaited write/flush; no stream disposal by this writer. |
| `Runtime/FileGroupInventory.cs` | 195 | R01/R02/R08. Exact group/member checks are useful; retirement/recovery and failed-candidate lifecycle need correction. |
| `Runtime/FileGroupTransition.cs` | 152 | R01/R08; transition state checks are meaningful but retirement precedes journal publication. Full-segment hash/flush needs latency measurement. |
| `Runtime/FileOutputOwner.cs` | 182 | R01/R02. Lease and non-overwriting moves retained. Real multi-process OS verification remains essential. |
| `Runtime/FileOutputWriteException.cs` | 11 | Narrow safe retention/render fault codes; no separate finding. |
| `Runtime/FileOutputWriter.cs` | 76 | Sequential owned file writer. R02 restart capacity; rotation and write cancellation need real integration tests. |
| `Runtime/FlushBoundary.cs` | 35 | Per-boundary shared controls and caller-specific waits. Coalescing structure is worth retaining. |
| `Runtime/FlushDeadline.cs` | 76 | R03: elapsed accounting only on Tick; completion/deadline ordering needs deterministic qualification. |
| `Runtime/ILogOutputAdmission.cs` | 14 | Internal trusted seam with no I/O/caller callback rule. Avoid expanding public extension surface just for tests. |
| `Runtime/JsonlRecordBoundary.cs` | 49 | Bounded tail inspection, BOM/UTF-8/JSON checks, and partial classification. It intentionally does not validate every historical record. |
| `Runtime/OutputAccounting.cs` | 11 | Owned counts include InFlight; saturating admission totals. R07 public projection omission. |
| `Runtime/OutputEventQueue.cs` | 444 | R09 density; fundamental accounting/flush/cooperative-cancellation structure is sound in intent. Requires concurrency stress evidence. |
| `Runtime/OutputQueues.cs` | 44 | One worker per enabled output; rollback joins unpublished workers. Fourteen-parameter construction is hard to audit. |
| `Runtime/RuntimeAccounting.cs` | 5 | Small immutable cross-output observation; no separate finding. |
| `Scopes/CapturedOperationContext.cs` | 44 | Owned operation facts and omitted-context singleton. R04 classification of omitted invalid scope. |
| `Scopes/CapturedScopeContext.cs` | 66 | Bounded immutable merge; R05 retained-key priority and event overlay limits. |
| `Scopes/OperationScopeFrame.cs` | 16 | Immutable parent chain; good fit for execution-context inheritance. |
| `Scopes/OperationScopeStore.cs` | 66 | Per-runtime AsyncLocal, LIFO checks, reentrant capture guard. Qualify asynchronous flow and documented ownership limitations. |
| `Values/LogValueProjection.cs` | 69 | Owned shape budget with immutable subtree reuse. Oversize projection policy must match revised contract, not original rejection fixtures. |
| `Values/LogValueScalar.cs` | 15 | Typed internal union without arbitrary object payloads; no separate finding. |
| `Values/LogValueText.cs` | 50 | Canonical numeric text checks and delegation to missing Unicode helper. Full Unicode correctness is not verifiable from this archive alone. |

## 11. Input identity and evidence boundary

- `Logging(2).zip` — SHA-256 `91224f919a093adf46984a137404cf448f772998a12bec56ebcff2ee1cc03d8a`.
- `core-library-implementation-guide-v1(1).md` — SHA-256 `01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675`.
- `core-library-implementation-plan-v1(1).md` — SHA-256 `41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5`.

All source locations refer to the unmodified files extracted from that ZIP. No production source was edited. The review-local JSON boundary fixture and source manifest are analysis aids, not original project artifacts, test results, or released contract fixtures.

**Bottom line:** keep the existing foundations; fix retirement/recovery, qualify timing and provider edge cases, reconcile the current contract, and make the central paths readable before adding more behavior. The review does not waive unfinished release obligations or claim that unavailable tests passed.
