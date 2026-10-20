# Healthcheck Technical Intent

**ID:** HC-TI · **Revision:** 0.2 · **Date:** 20 October 2026  
**Status:** HC00 working baseline selected by the user; unpublished.  
**Implementation baseline:** `5f36bbcb6fb26c1818c868bfb06ad701895bef1b` (clean at HC00 start).

The user's current direction approves the proposed configuration/contracts and parallel pilot, and permits ASP.NET in the existing Core library. HTTP integration therefore belongs to `CIS.Core/Healthcheck/AspNetCore`, namespace `CIS.Healthcheck.AspNetCore`, with an explicit `Microsoft.AspNetCore.App` framework reference. This supersedes every separate optional ASP.NET adapter/package proposal below. Core consumers now require that shared framework. Core retains its two approved runtime NuGets with package pruning disabled for this project; this avoids an unrelated dependency migration. No runtime Healthcheck behavior is certified by HC00 reference compilation. See [decisions](decisions.md), [contract catalog](contract-catalog.md), and [baseline receipt](baseline.json).

## 1. Objective and authority

Implement the existing Healthcheck behavior with clear shared contracts so production code, native tests, transport adapters, and documentation can be prepared concurrently without independently deciding the same behavior.

The acceptance target is the preserved Healthcheck v0.1 definition, not a reduced demonstration invented for the pilot. H01–H04, the country-list/startup decisions, and applicable current repository standards remain authoritative. Existing D choices remain reviewable draft choices; this document chooses concrete implementations of them without relabeling them as user decisions.

Authority order is: current explicit user decisions; current applicable repository amendments; the source definition's requirements; reviewed Technical Intent and contract catalog; task assignments. Historical specifications remain unchanged for provenance. A conflict between authorities is recorded at HC00, not silently resolved by a worker.

The [Contract Catalog](contract-catalog.md) owns exact shared shapes and default values. The [Task Plan](task-plan.md) owns write assignments and dependencies. This document owns the implementation design. Do not maintain parallel competing copies of these decisions in task prose.

### 1.1 Baseline reconciliation

| Source statement or historical assumption | Treatment in this pilot |
| --- | --- |
| Generic Healthcheck has no explicit PackageReference. | Preserve independence and introduce no new runtime dependency. Do not remove the Microsoft packages already approved in Core; the repository explicitly superseded the older blanket rule. |
| Module/package names were undecided. | Use `CIS.Core` and namespace `CIS.Healthcheck`. Module folders are not separate core packages. |
| `EnviormentsOptions.Current.Healthcheck` and immutable startup publication framework. | Superseded shape. Reuse current `EnvironmentOptions` identity facts. Use ordinary module-owned options and one explicit environment-resolution call; no `Current`, reset, readiness, or startup framework. The new entry point is a draft TI choice. |
| HTTP integration outside the generic monitoring core. | Superseded by the current user direction permitting ASP.NET in Core. Keep HTTP implementation in its module integration folder; use one Core package and an explicit shared framework reference. |
| Watchdog responsibility was deferred by Heartbeat v0.2. | H03 resolves it: watchdog is separate and has no health authority. Preserve actual execution ownership after cancellation or wrapper timeout. |
| Original task progression implemented tests and examples after code. | Change authoring dependencies, not acceptance. Tests/docs/examples may be written from frozen contracts; real execution still depends on real implementation. |
| Older environment decisions in conversation history. | Current repo documents later readonly/static-constructor choices. Do not revert to earlier mechanics while implementing an unrelated module. |

## 2. Scope and exclusions

Deliver an application-owned health registry and monitor factory; condition, consecutive-failure, numeric-threshold, and first-success-availability monitors; optional owner-safe asynchronous collection; immutable snapshots; shared bounded report serialization; reserved HTTP reporting; local pipe server and matching probe client; and the execution-fact binding needed by Heartbeat.

Keep the following outside Healthcheck: a general startup coordinator, a new DI container, generic configuration/type discovery, business retries or rollback, forced task termination, deployment restart decisions, automatic cache freshness rules, arbitrary policy expressions, distributed registration, durable event delivery, unbounded histories, and a CIS orchestration platform.

A runtime health failure must not automatically stop the background work that can restore health. A separate watchdog may affect the caller's wait or request cooperative cancellation, but Healthcheck changes only from ordinary explicitly reported observations. The .NET cancellation limitation is a platform fact, not an optional design preference.[P3]

## 3. Physical organization and dependency direction

Use existing layout:

```text
contracts/healthcheck/v1/                         language-neutral contracts
conformance/healthcheck/                          shared behavioral/wire cases
implementations/csharp/src/CIS.Core/Healthcheck/   public C# types and implementation
implementations/csharp/tests/CIS.Core.*Tests/      existing native test families
implementations/csharp/tests/CIS.Core.BusinessCases/
docs/modules/healthcheck/                         consumer documentation
planning/healthcheck/                             plan, ownership, decisions, evidence
```

HTTP integration lives at `implementations/csharp/src/CIS.Core/Healthcheck/AspNetCore/`, namespace `CIS.Healthcheck.AspNetCore`, inside the user-selected single `CIS.Core` package. It uses the approved `Microsoft.AspNetCore.App` framework reference. Policy/state code remains independent of HTTP types by implementation boundary; assembly independence from ASP.NET is no longer required.

The BCL-only pipe runtime and shared report formatter stay under Core's Healthcheck folder. A non-core executable at `implementations/csharp/tools/CisHealthProbe/` hosts the matching client. Keep application lifecycle glue in examples/host integrations, not in the registry.

Use existing UnitTests, IntegrationTests, ArchitectureTests, CompatibilityTests, BusinessCases, and TestSupport projects. Do not introduce a separate production package or test project per policy or workstream. HC00 adds only two nonshipping compiler projects. Actual HTTP fixture, probe and example projects are bounded shared-file transactions in HC04, HC05 and HC11 respectively, when their implementation is authored.

SDK-style projects normally include C# files by glob; confirm the evaluated project before relying on that behavior. Adding an ordinary source file should not require every worker to edit the project. No global using file, shared assembly attribute, build target, or central registry is an unowned exception to file ownership.[P1]

### 3.1 Concrete components

| Component | Owns | Does not own |
| --- | --- | --- |
| `HealthcheckService` | Application facts, monitor factory, state coordinator, coherent snapshots, terminal service disposal. | HTTP hosting, business initialization, arbitrary execution policy. |
| Internal state coordinator and monitor classes | State transitions, counters, metrics, weak registration, observation revisions, withdrawal/admission boundaries. | User callback execution, I/O, serialization. |
| `HealthCollectorRunner` | Optional bounded dispatch, due selection, joining, collector leases, actual settlement. | Health classification or a replacement Heartbeat scheduler for application work. |
| `HealthExecutionBinder` | Detached execution facts, bounded duplicate handling, owned/external monitor lifetime. | Callback execution, retry arithmetic, OnError invocation. |
| `HealthReportFormatter` | One snapshot into bounded UTF-8 JSON or a recognizable reporting error. | Reading live monitors or changing state. |
| HTTP adapter | Reserved branch, method/route handling, report output, access policy boundary. | Business controllers or health policy evaluation. |
| Pipe server and client | Framing, budgets, local endpoint, protocol validation, exit result. | Native Kubernetes integration beyond the matching exec client. |

Use ordinary classes, explicit methods, short constructors, and readable control flow. Do not split a large class into many partial files to manufacture parallel ownership. One writer owns shared mutable-state logic. Supporting classes should have real responsibilities, not one-file-per-task scaffolding.

## 4. Application and monitor lifetimes

The host creates one `HealthcheckService` for the application. The library need not enforce a process-global singleton: isolated test/application hosts may coexist. Construction validates/copies options and captures application metadata. It does not prime business data, run collectors, start listeners, or activate recurring work.

Factory methods return distinct class instances even for identical options/names. Each has a fresh opaque Guid ID; a name is display metadata. Criticality is an explicit required service choice and is fixed for that instance. No central classification is inferred from type, lifetime, exception, or transport.

The registry stores detached state plus weak references. The component retains its monitor for as long as the condition exists. A collector registration retains its collector and monitor only through its owner-held registration handle; the singleton must not root that handle or its owner graph while idle. Optional diagnostics are copied scalars, not arbitrary objects or Exceptions.

A scope that can be explicitly disposed needs a real lifetime protocol. Weak references only address garbage collection. For collection, the owning integration supplies a lease that prevents its dependencies being disposed until work and cleanup settle. Healthcheck must not resolve dependencies from a root service provider or recreate an unrelated scope. Do not capture an arbitrary `ExecutionContext` as a substitute for owner lifetime.

### 4.1 Withdrawal and disposal

`Withdraw()` is immediate and idempotent. Under the state gate it marks the handle terminal, removes its current contribution, closes collection admission, advances its generation, and identifies work whose cancellation must be signaled. Perform cancellation callbacks outside the gate.

`DisposeAsync()` first withdraws and then awaits the shared disposal barrier. `WaitForDisposalAsync(token)` only waits for that barrier; it does not initiate withdrawal. A canceled wait does not cancel or complete the barrier. Pure push monitors settle immediately. Repeated disposal calls observe the same terminal operation.

Already-withdrawn direct public report calls throw `ObjectDisposedException`. Internal late results are discarded; they do not resurrect the monitor or create a failure. Previously captured snapshots remain unchanged. A replacement with the same name is unrelated.

When collecting, reserve/track a wrapper before acquiring an owner lease or posting work. Withdrawal suppresses wrappers that have not crossed the coordinated invocation boundary, even if posted. Dispose completion includes wrappers that were suppressed, acquired holds, callback cleanup, and owner-dependent result handoff. A collector may request withdrawal but must not await a barrier containing itself. Detect self-wait where the library's execution token can establish it; document remaining caller misuse rather than claiming arbitrary async-flow detection.

Before capturing a snapshot, reconcile dead weak targets and remove their contributions. Losing a monitor is removal, not an invented missing-critical-service failure. A long-lived requirement needs a separate retained monitor.

## 5. State transitions and policy behavior

One short private state gate serializes registry mutations, policy updates, counter updates, application facts, and snapshot copying. Use one gate initially, not a read/write-lock hierarchy or an actor framework. Optimize only from measurement. No callbacks, lease acquisition, cancellation callbacks, awaits, serialization, log writes, or other I/O run under it. Complete task sources outside the gate with asynchronous continuations.

Every accepted observation updates all applicable state together and advances a non-reusing revision. Use `BigInteger` revisions internally; serialize their nonnegative decimal form. Use nonnegative saturating `long` lifetime outcome counts with a saturation indicator. A saturated count must never be used for stale-result detection.

Concurrent direct observations are ordered by accepted commit order, not inferred real-world causality. Repeating a direct report is another observation. Only execution binding has the explicit duplicate contract.

Policies follow the catalog. In particular, FailureThreshold 3 flags on failure **3**, not 4. Success resets that streak and only that policy's flag. Skips and expected cancellation neither advance nor reset it. Below-threshold failed outcomes remain visible even while evaluated health is Healthy.

Explicit conditions have an explicit initial flag. Numeric comparisons are selected by the owner, including strict/non-strict equality behavior. Invalid direct numeric input throws without changing state. Required collector failure maps to the policy once; optional metric failure changes only its collection diagnostic. First-success availability persists through failed refresh and changes to unavailable only on initial failure or explicit invalidation.

Aggregate health is Unhealthy exactly when at least one active critical monitor is Unhealthy. Pending and noncritical failures remain visible without manufacturing a critical failure. An empty registry is Healthy but does not certify initialization.

`Ready = StartupCompleted && !IsStopping && HealthStatus == Healthy` is evaluated in the same snapshot. IsStopping is terminal. Marking startup complete is explicit and idempotent; after stopping it is rejected without reopening the service. Startup failure is retained by an actual coordinator-owned live critical monitor, not a hidden Healthcheck requirement surviving disposal.

## 6. Asynchronous collection design

Collection is optional; push-only Healthcheck works without a runner or Heartbeat. A host creates at most one `HealthCollectorRunner` for a service. Constructor and registration are inert. `StartAsync()` enables its dispatcher; the host can do this before business initialization for explicit awaited initial evaluations. It does not automatically activate each periodic registration.

A registration explicitly calls `ActivatePeriodic()` only after its owner is initialized and the application permits recurring work. Manual `CollectAsync()` is available after runner start and is how an owner can await an initial evaluation without waiting for global readiness. An interval of null means manual only; an interval supplied for periodic use must be positive. The first periodic attempt becomes due at activation; later eligibility is completion plus interval. No 10 ms heartbeat default is inherited.

Keep at most one pending marker and one actual collection per monitor registration. Multiple requests while a collection is active share its actual operation. Each caller may cancel its own wait. Global default capacity is four. Select due registrations with a rotating cursor; coalesce missed periods. Do not create one waiting Task per registered monitor to acquire capacity.

The runner reserves a slot and asks the state coordinator for a tracked reservation. It then obtains the owner lease outside state coordination. Immediately before entering owner code, it checks the reservation's invocation boundary with the coordinator. Retirement/withdrawal before that boundary suppresses invocation. From this boundary onward, the slot and lease remain held until callback, cleanup, and required handoff actually finish.

A failure to acquire the owner lease retires collection and withdraws that monitor as the selected owner-closed disposition. It does not turn an owner closure into another health failure. Acquisition exceptions are integration faults; required-versus-optional evaluation rules apply only to an admitted evaluation, not an invented callback invocation. The lease implementation must not perform unbounded work in its acquisition method.

Results carry the registration identity/generation and starting health/metric observation revisions. A health result commits only if the monitor is active and no newer direct health observation has won. Optional metrics compare per-key revisions; they must not overwrite newer direct metric reports. A stale success or stale failure is equally discarded. Lifecycle bookkeeping must not itself invalidate a health observation.

Expected retirement/shutdown cancellation is neutral. A collector returning unexpected cancellation or a malformed required result produces one required evaluation failure. Optional collection errors retain the prior measurement and its observation time. Stalled work remains tracked and consumes its actual slot; there is no forced timeout-to-success or release.[P3]

### 6.1 The internal seam is agreed before parallel coding

The state owner implements `IHealthCollectionState`; the runner consumes it. The catalog specifies the reservation, invocation, commit, and settlement operations. The companion IHealthExecutionState seam in C05 provides atomic binding admission and outcome/lifecycle commits to the binding owner. State code never calls runner or binder code while holding its gate. Runner coordination never waits for owner code while holding its own scheduling gate, and it must release that gate before entering the state coordinator.

This split lets the runner and policy/state implementation proceed concurrently. It deliberately leaves policy, aggregate, and revision mutation with a single owner; another agent must not add a second dictionary or counter interpretation in the runner.

## 7. Snapshots and reporting

A snapshot is an immutable detached value graph with a schema version, published revision, capture timestamp, application facts, aggregate, readiness, counts, and complete ordered monitor details. Copy under the state gate, then sort/serialize detached data outside it. Monitor ordering is registration ordinal, not locale-dependent display-name order. Metric ordering is ordinal by key.

Capture time is fresh; observation times remain the times of actual facts. Snapshot capture never runs a collector or replays a failure. UTF-8 JSON formatting is shared by HTTP and pipe. The wire catalog fixes enums, null/omission rules, decimal counters/revisions, UTC times, units, bounded diagnostics, and errors.

The formatter receives exactly one snapshot and explicit view/limit options. It pre-serializes a complete bounded result before transport commitment. Overflow produces a small recognizable error, not truncated supposedly complete details. Reporting failure does not mutate health. HEAD makes the same serialization/limit decision as GET, then sends no body.

Bound payload bytes independently from object count. Bound writer growth while encoding; do not first allocate an unbounded JSON string and check its size afterward. Existing MaxMonitors and per-monitor metric/text bounds limit snapshot copying; MaxReportBytes limits each encoded payload, not aggregate host memory across arbitrary concurrent HTTP requests. Host-level HTTP connection/concurrency budgets remain the host's responsibility; this draft does not invent a global API rate limit.

## 8. HTTP behavior

Use an early reserved middleware branch for `/_health` and descendant path segments. It must not call business middleware or catch-all routes, including on 404/405, disabled details, serialization errors, or unauthorized details. `/_healthcare` remains unrelated. Middleware cannot undo side effects of earlier components; installation order is part of integration testing.[P4]

Freeze the catalog's GET/HEAD routes, case-insensitive matching, and one optional trailing slash. Health/details return 200 for Healthy and 500 for Unhealthy. Readiness returns 200 only for Ready. Do not substitute framework defaults such as 503. Unknown reserved paths produce 404; unsupported methods on enabled known routes produce 405 with `Allow: GET, HEAD`.

Details are off by default. Enabling details requires an explicit adapter-owned access predicate/policy; it may deliberately permit all only on an appropriately restricted host. Omission while enabled is invalid configuration rather than accidental public detail exposure. A failed predicate returns 403 without falling through. This additional concrete access behavior is a reviewable TI choice, not a new claim about previously approved error codes.

Responses use `application/json; charset=utf-8`, `Cache-Control: no-store, no-cache`, and `Pragma: no-cache`. No rate limiting or retry loop is introduced by Healthcheck. Each request reads state once. Request disconnect cancels its output work, not other reports, collectors, or shared state. Accept no client-supplied monitor updates on these read-only routes.

The adapter installs into an existing host pipeline; it does not build a web server or implement generic application startup. The example starts reporting infrastructure before awaiting mandatory priming and gates ordinary requests explicitly. Do not assume a hosted service's StartAsync automatically runs after Kestrel is listening.

## 9. Pipe protocol and probe client

Use matched asynchronous .NET named-pipe APIs, local-machine clients, and a configured portable name. These APIs use Unix domain sockets on Linux; this is not a POSIX FIFO contract.[P2] The .NET 10 deployment must verify actual Unix permissions/umask and reachable endpoint context. Do not assume later .NET behavior applies to the pinned runtime.

Each connection exchanges one length-prefixed UTF-8 JSON request and one response. Read exactly the four-byte little-endian prefix and then the stated body across any number of reads. Validate length before buffer allocation. Requests are at most 512 payload bytes; response payloads obey MaxReportBytes. Reject malformed UTF-8, duplicate JSON properties, invalid versions/views, inconsistent fields, premature EOF, and mismatched selected view. Unknown additive fields are ignored subject to framing and validation bounds.

At most eight served clients are active by default. Do not create unbounded accepted tasks waiting for a semaphore. An OS accept backlog is not a managed work queue; its behavior/limits need actual platform verification. Each served connection and client operation has a default total budget of 1,000 ms. Finish serialization before writing a successful frame prefix. Slow clients never hold state locks.

The matching probe returns 0 for a valid healthy/ready selected result, 1 for a valid unhealthy/not-ready result, and 2 for every transport/protocol/reporting problem. Unsupported CLI options and invalid values exit 2. Stdout may contain the selected JSON; safe errors go to stderr. No automatic retry is performed inside a probe invocation; the supervisor controls probe repetition.

Kubernetes uses HTTP or an exec probe invoking the client. There is no native pipe field. Deployment owns probe periods, failure thresholds, startup allowance, and restart policy. Selecting aggregate health for liveness exposes critical failures to that policy; Healthcheck must not secretly reclassify them.[P5]

## 10. Heartbeat execution-fact binding

Healthcheck owns policy interpretation. Heartbeat owns scheduling, callback/condition outcomes, no overlap, OnError, and actual settlement. Freeze a detached fact contract now; do not require the Healthcheck core to reference a Heartbeat implementation.

An absent policy means the Heartbeat adapter creates no binding. Configured binding must succeed before the schedule activates. The binder accepts a caller-owned suitable monitor or creates a monitor from one of the specified policies. Condition/numeric policies cannot be inferred from Task<bool>. Availability requires an explicit declaration that success means usable data was published.

The integration keeps a created monitor alive through its real owner-associated registration lifetime without rooting a transient owner from a singleton. Binding-created monitors are retired after final required handoffs on unregister/automatic retirement/owner closure/shutdown. External monitors are never disposed by unbinding.

Registration identity and monotonically increasing positive execution IDs correlate facts. Each binding stores a bounded current/previous execution record and high-water mark. Identical retained terminal repeats do nothing; contradictory retained terminal payloads are integration faults. Older facts outside retained history are ignored as obsolete, not compared against nonexistent infinite history. Settlement can update a matching retained record without becoming a second outcome or moving current execution backward.

The handoff is synchronous in-process state commitment with no I/O delivery queue, durability, or external exactly-once promise. A reporting fault remains a bounded integration diagnostic; it does not erase the actual callback outcome, invoke OnError, or increment callback failure twice. Owner-dependent handoff finishes before owner protection is released; later settlement is a detached fact.

Implement and test this binding against actual monitors now. Test its real Heartbeat adapter against the actual Heartbeat implementation when present. Test doubles qualify the fact contract, not real scheduling/owner-lifetime integration. External evidence remains visibly pending otherwise.

## 11. Startup, readiness, and shutdown composition

The country-list example is the integrated business proof:

1. Create the container and health/reporting objects without priming or collection.
2. Start health reporting so failed priming remains observable.
3. Explicitly prime and publish usable country data after prerequisites.
4. Report availability success through a critical availability monitor.
5. Bind/register recurring refresh with the noncritical threshold-three monitor, but do not activate recurring application work prematurely.
6. Publish full application startup completion only after all required startup work succeeds; then activate recurring work and permit ordinary traffic under readiness.

An initial failure stops dependent startup, creates no recurring refresh registration, and is recorded by a coordinator-owned critical monitor retained for the application lifetime. Do not implement startup retry inside Healthcheck. A disposed child monitor still disappears. Only noncritical later refresh failures leave aggregate and readiness Healthy/Ready while the cache remains usable.

The example owns its explicit startup sequence. It must not grow a generic IStartable framework. The future coordinator's instance-identity, at-most-once, ordering, and cancellation acceptance obligations remain in the traceability map as external integration cases.

Shutdown ordering is deliberate: publish IsStopping and close new registration/binding/collector admission; stop admitting Heartbeat and collection work; await actual running work, cleanup, and final owner-dependent handoffs; retire monitors and dispose their scopes; close reporting admission and settle handlers; then dispose adapters and Healthcheck/root dependencies. Existing active monitors can accept final direct reports after BeginStopping. Readiness stays false even when withdrawing all failing monitors makes aggregate Healthy.

No canceled caller wait certifies completion. A noncooperative task is still outstanding. Repeated stop/dispose shares terminal completion; no instance is restarted for a new application lifetime.

## 12. Configuration and errors

Exact settings and bounds are in the catalog. Keep options in one small module-owned class plus a straightforward resolver. `HealthcheckOptions.Resolve(setup)` captures relevant nonblank environment overrides once, overlays them on supplied options/defaults, validates, and returns detached values. Service/adapter constructors validate and copy supplied options so later caller mutation cannot alter active behavior. Direct constructor use does not secretly reread environment variables.

Environment variables precede explicit setup, then defaults; whitespace-only is absent; invalid selected values fail rather than falling back. Parsing is invariant and units are explicit. Criticality, thresholds, and per-collector intervals are service choices, not global environment policies. Use the existing configuration exception/immutable issue pattern after verifying exact repository signatures. Do not create a competing startup/configuration framework or store runtime flags in EnvironmentOptions.

Errors distinguish invalid arguments, invalid configuration, stopping state, disposed handles, capacity, collection faults, integration faults, and transport failures. A legitimate Unhealthy snapshot is not a malformed report. Safe reasons contain no automatic stack traces, credentials, or arbitrary Exception.Data. Monitor capacity rejects atomically without evicting a failing monitor.

## 13. Verification and definition of completion

All source HC-AT-01 through HC-AT-59 are mapped in the traceability document. Maintain unit, integration, mutation, architecture, backward compatibility, and business-case obligations. Do not invent a historical release if publication history is empty; qualify initial frozen fixtures and mark historical replay not yet applicable with evidence.

Native C# tests exercise actual implementation. Independent test authors use the contract's expected results, not private production helpers to calculate the answer. A compile-only reference surface may unblock authoring, but must be nonshipping and cannot produce runtime pass evidence. Test doubles isolate dependencies; combined proofs must use the real component at the claimed boundary.

Use deterministic TimeProvider-based clocks and controllable leases/collectors, not arbitrary sleep-based race assertions. Exercise disposal-before-invocation, direct-success-versus-old-error, duplicate outcomes, full/corrupt frames, saturation, malformed metrics, and slow clients. Mutation must challenge the important invariants, not simply count mutants.

Use existing `eng/test.ps1` for focused development and `eng/verify.ps1` at coherent checkpoints. Reread current gate prerequisites/pins at dispatch. Do not run the entire comprehensive suite after each small edit, weaken release gates, or create Python substitutes for native C# tests/utilities.[R5]

A task has separate authoring, implemented, locally verified, integrated, and accepted states. Empty/skipped/stale test results are not success. An external integration case stays pending until run against its real component. The pilot can reach useful module milestones without claiming all cross-module or release obligations have passed.

## 14. Parallelization design and pilot limits

The serial part is the reviewed behavioral/API/schema baseline and the small shared build foundation. The mutable registry/policy core has one implementation owner. The runner, binding, serializer, HTTP adapter, pipe implementation, test families, and documentation have different write sets and depend on the catalog.

Readers use pinned worktrees/baseline revisions. Shared build/solution/NuGet changes use the coordinator's transactional queue; do not lock the whole repository for an agent's feature. A contract change has semantic impact even if no file overlaps: revise it once and invalidate only affected task assumptions/evidence.

This is a hypothesis about reducing avoidable rework and waiting, not proof that more agents are always faster. Measure total design-to-accepted time, integration queue time, shared-file queue time, contract changes after dispatch, rework, verification cost, and human intervention. A single Healthcheck pilot gives operational evidence but cannot by itself establish a causal speedup over a different feature.

## References

Project facts: the [source register](sources/source-register.md) records preserved attachments and current repository inputs. The included definitions supply requirement IDs; current explicit user direction and repository amendments supply the superseding packaging/environment decisions.

[P1] Microsoft, [.NET project SDKs — default includes/excludes](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview#default-includes-and-excludes).  
[P2] Microsoft, [Named-pipe interprocess communication](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication).  
[P3] Microsoft, [Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads).  
[P4] Microsoft, [ASP.NET Core middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0).  
[P5] Kubernetes, [Liveness, readiness, and startup probes](https://kubernetes.io/docs/concepts/workloads/pods/probes/).

These sources support platform mechanics, not the proposed CIS API names, defaults, packaging identity, or an unmeasured performance improvement.
