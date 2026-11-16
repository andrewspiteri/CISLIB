# Heartbeat acceptance mapping and verification scope

**Change:** CIS-0006 · **Status:** Implemented acceptance mapping; actual qualification is scoped by the verification record · **Date:** 16 November 2026.

Every acceptance case in the [retained Heartbeat v0.2 source](../healthcheck/sources/CIS_Heartbeat_Library_Specification_v0_2.md) remains explicit below. The rows state required observable behavior and verification areas. An authored test, supporting class or passing case count does not establish every clause or combination in all 70 descriptions.

The [actual outcome authorization](../../docs/changes/CIS-0006/authorization.json) and canonical task allocation govern CIS-0006. [Current decisions](../decisions.md), [agent guidance](../../AGENTS.md) and existing native [test conventions](../../implementations/csharp/tests/README.md) remain. Use the existing MSTest families; no new runner, per-module test project or manual-test catalogue is introduced. Completed CIS-0001 through CIS-0005 ownership, budgets, attempts and evidence remain unchanged.

The [verification record](../../docs/changes/CIS-0006/verification.md) is the authority for actual terminal native observations, exact candidate, effective test selection, platform and remaining gaps. Its reports also retain failures, consumed work, attempts and truthful currentness limits. This mapping does not grant another execution allocation, publish a package or close a task.

## Verification seams and assertions

- Use the existing `ManualTimeProvider` for monotonic time and independently controlled UTC. Advance time to exact eligibility boundaries; a wall-clock adjustment must not create or postpone an eligible occurrence.
- Use an internal controllable executor to pause before posting, after a capacity reservation, and immediately before each user-phase gate. Production dispatch must not execute user code on the scheduler control path.
- Use asynchronous completion sources and `AsyncSignal` to hold conditions, callbacks, handlers, cleanup and owner-dependent reporting independently. Continue an operation only after the assertion identifying its current phase succeeds.
- Instrument owner holds and destructive disposal with explicit counters/events. Assert holds exist from acceptance, disposal occurs after settlement, and each hold releases once. A strong object reference alone does not satisfy this test.
- Use detached fact recording and fault injection at the reporting boundary. Distinguish work completion, settlement, retirement, integration faults and neutral suppression; do not count a settlement as a second outcome.
- For reachability, retain the engine and intended detached observations, create targets in non-inlined helper scopes, retain only `WeakReference` probes, and perform bounded collection attempts. Test the complete subscriber/condition/owner/binding/exception graph. Do not interpret an explicit `WeakReference` field as proof of weak ownership.
- A finite test deadline bounds a failed test's wait; it does not change the product drain contract. Avoid arbitrary sleeps as race evidence. Observe and clean up all test-owned tasks before a test ends.

`Unit` below means `CIS.Core.UnitTests`; `Integration` means `CIS.Core.IntegrationTests`; `Architecture`, `Compatibility` and `Business` mean the existing corresponding test families. Groups indicate logical suites, not additional work owners.

## Implemented verification entry points

The following classes connect the required evidence to real authored checks. Consult verification.md for actual execution/admission results; this table assigns no blanket Passed state and contains no inferred result count. Changing a test inventory does not erase earlier real observations or make their exact effective-input basis current automatically.

| Existing test family / class | Acceptance areas supported |
| --- | --- |
| Unit / [HeartbeatContractTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/Contracts/HeartbeatContractTests.cs) | Consumer registration-only surface, awaitable subscriber contract, documented defaults, duration endpoints and Unicode names. |
| Unit / [HeartbeatConfigurationTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/Contracts/HeartbeatConfigurationTests.cs) | Default calculation, invariant precedence/validation and copied effective settings: AT-35, AT-36, AT-37. |
| Unit / [HeartbeatSchedulingTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatSchedulingTests.cs) | Inert/idempotent readiness, initial/fixed delay, true reference identity, duplicate/concurrent registration, FIFO reuse, UTC changes and blocking-prologue isolation. |
| Unit / [HeartbeatDrainTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatDrainTests.cs) | Queued/posted/running distinctions, ordinary/global cutoffs, canceled waiters, sequential reuse, owner-local drain and self-wait rejection. |
| Unit / [HeartbeatOutcomeTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatOutcomeTests.cs) | False results, primary/secondary errors, awaited handling/cleanup/flush, acknowledged cancellation, disposal retirement and reporting isolation. |
| Unit / [HeartbeatWeakOwnershipTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatWeakOwnershipTests.cs) | Bound/capturing idle graphs, live-subscriber condition retention and strong accepted target/owner holds. |
| Unit / [HeartbeatInfrastructureBoundaryTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatInfrastructureBoundaryTests.cs) | Clock-fault rollback/settlement, UTC correlation overflow, exactly-once binding retirement and independent withdrawal cleanup. |
| Unit / [HeartbeatRemainingAcceptanceTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatRemainingAcceptanceTests.cs) | One hundred eligible registrations with sixteen slots, both controlled admission/cutoff orders and retained real Healthcheck snapshots permitting subscriber/exception collection: AT-38, AT-17, AT-59. |
| Unit / [HeartbeatHealthcheckIntegrationTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat/HeartbeatHealthcheckIntegrationTests.cs) | Real binding/result ownership, dispatch-publication gate, duplicates/reuse, phase suppression, cleanup precedence and surviving external-monitor graphs. |
| Unit / [HealthExecutionHeartbeatFactTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Execution/HealthExecutionHeartbeatFactTests.cs) | Additive receiving validation, bounded immutable context/deduplication, neutral suppression and raw result/cancellation precedence. |
| Unit / [HealthHeartbeatFactWireTests](../../implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthHeartbeatFactWireTests.cs) | Actual formatter/reader context, malformed fields and unchanged legacy omission. |
| Integration / [HeartbeatHostingTests](../../implementations/csharp/tests/CIS.Core.IntegrationTests/Heartbeat/HeartbeatHostingTests.cs) | Actual host initialization/stopping, canceled host budgets and asynchronous dependency/scope disposal; owner counters alone cannot prove these boundaries. |
| Integration / [HeartbeatPackageTests](../../implementations/csharp/tests/CIS.Core.IntegrationTests/Heartbeat/HeartbeatPackageTests.cs) | Actual development package, isolated feed/cache, build/folder publish, polling/recovery and safe real scope shutdown. |
| Business / [HeartbeatPollingStories](../../implementations/csharp/tests/CIS.Core.BusinessCases/Heartbeat/HeartbeatPollingStories.cs) | Original application request budget, usable-data publication and awaited recovery without scheduler deadline policy. |
| Compatibility / [HealthcheckHeartbeatCompatibilityTests](../../implementations/csharp/tests/CIS.Core.CompatibilityTests/Healthcheck/HealthcheckHeartbeatCompatibilityTests.cs) and affected existing suites | Append-only public enum values and frozen wire/API behavior; new parser fixtures alone are not frozen compatibility evidence. |
| Architecture / affected existing suite | Actual dependency, public-contract, namespace and EnvironmentOptions boundaries; see the exact native selection in verification.md. |

The detailed rows retain stronger clauses than a single representative check may prove. In particular, do not infer every settlement/owner-close/concurrent-lifecycle interleaving, every unconfirmed-ownership failure, every unsafe-handler scenario or all null-task/cancellation combinations from a class-level pass. Record actual proof and remaining scope at that granularity. AT-32's platform, coverage, mutation, performance and complete release obligations remain separate.

## Readiness, timing and instance identity

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-01 | Unit / readiness | Register while Created, advance time and drain the executor: no acceptance or user phase. Open readiness and observe eligibility from that origin. |
| AT-02 | Unit / lifecycle | Stop before readiness; no user phase runs, all registrations retire, and repeated stop/disposal joins completed work. |
| AT-03 | Unit / fixed delay | Hold each owned phase in turn; the next execution is ineligible before full settlement plus Interval and no missed tick is replayed. |
| AT-04 | Unit / initial delay | Verify readiness-relative and running-registration-relative origins at the exact boundary; duplicate calls retain the original origin. |
| AT-05 | Unit / exclusivity | Pause condition, callback, handler and cleanup separately; no second accepted execution or phase overlaps for that instance. |
| AT-06 | Unit / identity | Distinct objects sharing type/name each receive independent registration lifetimes and capacity consideration. |
| AT-07 | Unit / identity | Equal-valued subscribers and throwing/custom equality/hash implementations cannot merge identities or influence lookup. |
| AT-08 | Unit / duplicates | Duplicate calls while active and individually draining succeed; registration, schedule, owner and binding remain unchanged. |
| AT-28 | Unit / clock | Jump UTC in both directions independently of monotonic advancement; interval and eligibility assertions remain unchanged. |
| AT-29 | Unit / validation | Nulls, duration boundaries, closed/disposed lifecycle and duplicate calls yield documented exceptions before mutation or hold acquisition. |
| AT-33 | Unit / bounded state | Advance many ticks while queued and active; inspect controlled dispatch/admission counts to prove one accepted execution and bounded metadata per registration. |
| AT-34 | Unit / defaults | Default Interval is 10 ms; zero InitialDelay uses the separate readiness/registration origin. |

## Results, error handling and fault isolation

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-09 | Unit / condition | False condition produces a skip, no callback/handler, and ordinary settlement-based recurrence. |
| AT-10 | Unit / condition fault | Thrown condition bypasses callback, invokes/awaits eligible handler once, and produces one failed outcome retaining the condition error. |
| AT-11 | Unit + Integration / outcomes | False callback then true callback creates distinct factual outcomes; neither synthesizes exceptions or scheduler failure-policy arithmetic. |
| AT-12 | Unit / dispatch isolation | Block a synchronous subscriber prologue on one slot while another registration is admitted/dispatched; coordination remains usable. |
| AT-13 | Unit / fault isolation | Fault one operation while unrelated work runs; its handler/cleanup settle and unrelated scheduling and drain continue. |
| AT-31 | Unit / infrastructure failure | Terminal cleanup fault still permits a proved drain; separately inject unconfirmed ownership and assert no false completed barrier while other work drains. |
| AT-53 | Unit / retirement | Condition/callback disposal exceptions immediately close recurrence, preserve one failure and retain the instance until settlement. |
| AT-54 | Unit / handler safety | A dependency disposal exception with still-usable target/owner permits handling; `ObjectName` does not identify subscriber disposal. |
| AT-55 | Unit / unusable target | Mark target/owner unusable before invocation or handling; no further subscriber call occurs and lifecycle/NotInvoked facts explain it. |
| AT-56 | Unit / secondary error | Handler fault is secondary, nonrecursive and cannot erase the primary failure; a handler disposal exception also retires under A03. |
| AT-57 | Unit / awaited recovery | Hold async rollback in the handler during unregister and stop; both barriers and future recurrence wait for its actual completion. |
| AT-58 | Unit / cleanup fault | After callback true and, separately, condition false, fault owned cleanup: produce one final Failed outcome retaining the raw callback/condition result and cleanup diagnostic. Avoid another subscriber call and settle protected ownership correctly. |
| AT-69 | Unit / cancellation | Known acknowledged cancellation and unexplained cancellation exception produce their distinct facts; wait-token cancellation changes neither result. |

Null task returns are contract violations. Required evidence for AT-10, AT-13 and AT-56 must distinguish the actual user phase exercised; a handler-null case does not establish every condition/callback-null combination. Normally completed booleans remain their actual results even when an independent execution token was requested.

## Cutoffs, drain and host ordering

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-14 | Unit + Integration / shutdown | Hold an active callback; shutdown remains incomplete and dependency probes remain usable until actual settlement. |
| AT-15 | Unit / queued suppression | Occupy all slots, accept another registration, then stop; queued work never invokes user code and releases its hold safely. |
| AT-16 | Unit / phase gate | Stop while a true-returning condition is held; condition settles but callback loses its own start gate. |
| AT-17 | Unit / admission race | Control both orders of admission versus global cutoff; a valid tracked hold or rejected admission is observed, with no partial state. |
| AT-18 | Unit / settlement race | Control both orders of cutoff versus settlement; no recurrence appears after cutoff and no stale instance reservation escapes. |
| AT-19 | Unit + Integration / cleanup barrier | Complete callback while its task-owned cleanup is held; dependency disposal and shutdown remain blocked. |
| AT-20 | Unit / waiter cancellation | Cancel one stop waiter; cutoff/shared drain remain, a second waiter stays pending, then observes real settlement. |
| AT-21 | Unit / canceled-entry wait | Already-canceled stop/unregister wait still establishes cutoff before cancellation; release work and observe the shared barrier from another caller. |
| AT-22 | Unit / terminal lifecycle | Race repeated activate/stop/dispose calls; one terminal drain exists and no activation reopens stopped acceptance. |
| AT-23 | Unit / ordinary unregister | While Running, unregister accepted queued and active work; queued work still executes under its existing owner hold and removal waits for settlement. |
| AT-24 | Integration / host ownership | Real host and dependency events prove Heartbeat cutoff/drain precedes dependent stopping and underlying root/scope disposal. |
| AT-25 | Unit / actual settlement | Ignore independent cancellation and exceed an observer wait; slot, instance and owner remain held until the real task completes. |
| AT-64 | Unit / overlapping cutoffs | Race ordinary unregister/owner drain with global shutdown; global suppression wins for every unstarted phase without resurrection. |
| AT-65 | Unit / posted wrapper | Pause a posted wrapper before its start gate, stop, then release it; it settles without touching a subsequently released dependency. |
| AT-67 | Unit + Integration / suppression facts | Suppress an entirely unstarted execution, with and without withdrawal-cleanup fault; preserve neutral lifecycle suppression plus the actual integration diagnostic, with no callback outcome or fabricated raw result. |
| AT-68 | Integration / startup | Real host completes required initialization before first work; startup and early shutdown cannot deadlock waiting for ApplicationStarted inside hosted startup. |

Host tests must exercise the supported explicit coordination route. Registering a lone hosted service or disposing a facade during ordinary DI teardown cannot establish pre-disposal safety. An application-owned cancellation source must remain separate from stop/unregister wait tokens.

## Configuration, capacity and registration reuse

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-35 | Unit / default calculation | Inject completion-port maximum values at/below 1, within range and above 16; clamp computed default to 1–16 without CPU/free-worker substitution. |
| AT-36 | Unit / option resolution | Table-drive environment/setup/default precedence, blanks, malformed/invariant integers and boundaries; invalid effective value yields stable CoreConfigurationException issues. |
| AT-37 | Unit / captured setup | Mutate setup/environment after resolution and registration; effective capacity and schedule/bindings remain copied and unchanged. |
| AT-38 | Unit / capacity | Make many registrations simultaneously due with capacity 16; slots are reserved before posting and unfinished wrappers never exceed 16. |
| AT-39 | Unit / slot lifetime | Hold every awaited execution phase, cleanup and owner-dependent reporting; each keeps its reserved slot through settlement. |
| AT-40 | Unit / fairness | Mix slow/fast registrations; each released slot serves the oldest eligible accepted work without a batch barrier or repeated overtaking. |
| AT-41 | Unit + Integration / duplicate bindings | Supply different valid options/condition/policy/owner on a duplicate; validate inputs while retaining every original binding and timing value. |
| AT-42 | Unit + Integration / first-registration race | Synchronize concurrent first registrations; one identity/monitor binding wins atomically and both valid callers succeed. |
| AT-43 | Unit / sequential reuse | Await removal, then register revised options; a fresh registration lifetime applies without overlap. |
| AT-44 | Unit / canceled unregister wait | Cancel a pending unregister wait and immediately re-register; the draining identity remains a duplicate no-op until real removal. |
| AT-45 | Unit / unknown unregister | Unknown/removed target with canceled wait completes as a no-op without touching its former owner or creating work. |

## Ownership graph and monitoring

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-27 | Integration / observations | Repeated Healthcheck snapshot reads neither invoke work nor duplicate correlated final outcomes. |
| AT-46 | Integration / owner context | Register in two real scopes with distinct dependency identities; execution uses each actual instance/context without replacement or root resolution. |
| AT-47 | Unit + Integration / idle reachability | Keep engine and an external monitor alive, release idle subscriber/owner externally, collect and advance scheduling; retire/release the binding association exactly once without invocation, subscriber/owner roots or withdrawal of the caller-owned monitor. |
| AT-48 | Unit + Integration / indirect reachability | Repeat with bound/capturing conditions, owner providers, bindings and diagnostics; no retained idle path roots the target/owner graph. |
| AT-49 | Unit / condition liveness | Keep subscriber alive, release other condition references and collect; its bound condition still runs correctly. |
| AT-50 | Unit / acceptance hold | Queue accepted work behind a full slot; target/owner remain strongly protected from acceptance through handling, cleanup and handoff. |
| AT-51 | Unit + Integration / owner race | Control owner-close versus admission; valid hold joins owner barrier or admission fails before dependency use. |
| AT-52 | Integration / owner-local drain | Close one owner; only its registrations drain before underlying disposal, unrelated owners continue and Heartbeat does not dispose subscribers itself. |
| AT-59 | Unit + Integration / detached facts | Retain Healthcheck observations after retirement while probing subscriber, scope, closure and exception graphs; all execution-owned references release. |
| AT-60 | Unit / reporting failure | Inject notification failure; preserve callback result, surface integration fault, avoid handler recursion and let unrelated work continue. |
| AT-61 | Integration / policy selection | Bind supported distinct Healthcheck policies; monitor interprets results while scheduler uses no allowance/reset/health arithmetic. |
| AT-62 | Unit + Integration / handoff boundary | Hold owner-dependent handoff after work completion; settlement/slot release/next fixed delay remain pending, followed by one detached settlement fact. |
| AT-63 | Integration / monitor lifetime | Reuse after completed unregister; fresh correlation lifetime binds according to Healthcheck's ownership/history contract. |

The [implemented integration](healthcheck-integration.md) preserves dispatch timing, raw results and neutral suppression. Its receiver, real binding and wire classes supply concrete entry points. Keep binding-created versus caller-owned retirement, final reports during Healthcheck stopping and invalid policy rollback explicit; the verification record identifies actual exercised scope.

## Architecture, business cases and release scope

| Source case | Family / group | Required observable evidence |
| --- | --- | --- |
| AT-26 | Architecture / responsibility boundaries | Inspect actual public/dependency surface and implementation boundaries: scheduling remains host-neutral and contains no health arithmetic, watchdog or inherited GmCore feature. |
| AT-30 | Business / application deadline | Poll queued requests carrying original timestamps/budgets; subscriber owns expiry/recovery and scheduler does not reset or reinterpret business age. |
| AT-32 | Compatibility + release families / package | Preserve existing development consumers/configuration/wire defaults; applicable package/platform/coverage/mutation evidence is recorded only when genuinely executed under its own authority. |
| AT-66 | Architecture + Compatibility / public API | Consumer service exposes registration operations; lifecycle belongs to infrastructure and health reads to Healthcheck. Preserve shared configuration mechanism and package/assembly identity. |
| AT-70 | Business + Architecture / runnable ownership | Audit and exercise awaited child work/recovery and external teardown; no async-void/detached scope users or self/capacity-dependent drain cycles appear in examples. |

Retain source assumptions explicitly: A01 completion-based delay/InitialDelay; A02 strict callback gate after a running condition; A03 disposal-exception retirement; A04 outcome/error/cancellation precedence and proved drain; A05 explicit capacity range 1–16. Testing these interpretations does not relabel them as separate human approvals.

The source's old `EnviormentsOptions.Current` initialization and zero-package/no-ASP.NET instructions are superseded by current repository decisions. Configuration tests use actual `CIS.EnvironmentOptions` conventions and module-owned resolved copies; dependency tests preserve the approved Microsoft packages/framework. Update intentional API/architecture assertions only for the accepted addition, preserving unrelated contracts.

Business evidence includes original request budgets and awaited recovery; real host/package evidence establishes its actual scope and teardown behavior. Simultaneous-registration checks establish their stated bound and ordering, not deployment-scale or latency claims. Shared support remains limited to reusable synchronization and lifetime behavior.

Further authorized focused execution retains `TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`, builds affected projects and exercises meaningful affected families. Record actual selection, candidate/platform/toolchain and terminal observations in verification.md; preserve earlier failures and costs. Unit receiver/wire checks, affected Healthcheck regression and real compatibility observations keep their distinct evidence boundaries.

Initial delivery evidence does not itself satisfy complete Windows 11 x64/Ubuntu 24.04 x64 package qualification, release performance/stress, 95% line/90% branch coverage, 90% conservative mutation, publication or permanent historical compatibility. Those accepted obligations remain visible and require applicable actual evidence before release. No manual-test catalogue obligation is introduced by this library plan.
