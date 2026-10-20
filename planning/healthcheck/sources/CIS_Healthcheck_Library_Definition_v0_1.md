# CIS Healthcheck Library Definition

**Version:** 0.1  
**Status:** Draft for review  
**Date:** 11 October 2026  
**Baseline:** Core Library Implementation Guide, specification 1.1; CIS Heartbeat Library Specification, version 0.2

## Review status and authority

Healthcheck is an application-owned singleton that records the health of registered services through transient monitor instances. Each service chooses its monitor's criticality. A failing active critical monitor makes application health Unhealthy. Disposing a monitor removes its contribution from current health.

This definition incorporates the Healthcheck discussion and the final H01–H04 decisions. It is a definition of the new CIS library, not an implementation, a claim of passing runtime tests, or a compatibility commitment to GMCore.

The shared core guide governs framework targets, package identity, dependency restrictions, configuration, coding conventions, compatibility, and release evidence.[^core] The Heartbeat specification governs recurring execution and its ownership guarantees.[^heartbeat] Where that earlier document deferred watchdog responsibility to the Healthcheck discussion, the later H03 decision resolves it: **watchdog execution control is outside Healthcheck and has no authority over health state.**

Requirements marked **R** carry a user decision or an inherited core obligation. Requirements marked **D** make an explicit design choice for this first draft. Both describe the proposed implementation; D does not imply that the user separately approved every API name, default, or wire detail. Section 16 gathers the material draft choices for review.

### Decisions carried into this definition

| Decision | Required interpretation |
| --- | --- |
| H01 — Criticality | The monitored service declares whether its monitor is critical. Healthcheck applies that declaration. It does not centrally classify services or invent a separate restart category. |
| H02 — Disposal | A disposed monitor is removed from current health and subsequent snapshots. An earlier immutable snapshot can still contain its earlier state. |
| H03 — Watchdog | A separate execution-control facility may request cancellation and control the caller's timeout result. It cannot flag, clear, or classify health. |
| H04 — Reporting transports | Provide HTTP and local pipe reporting. Kubernetes can use an HTTP probe or an exec probe invoking a matching pipe client. |
| Failure threshold example | A threshold of three consecutive failures flags on the third failure. Success resets the streak and clears that policy's flag. |
| Cached country list | Prime required data explicitly after container construction. Only successful priming is followed by recurring Heartbeat registration. Later refresh failure can be noncritical while retained data remains usable. |
| Mandatory startup failure | Stop the startup sequence, keep normal requests gated, report the failure, and await supervisor restart. Heartbeat does not retry failed priming. |
| Future startup coordination | The application will invoke intended IStartable instances in order, at most once per instance, after container construction. All required startup work must complete before normal requests are accepted. |
| Documentation | Maintain the deliverable in Markdown. |

## 1. Purpose and module boundaries

**HC-SCP-01 · R · Record and aggregate health.** Healthcheck MUST maintain current monitor state, apply each configured monitoring policy, and produce consistent application snapshots. It MUST support direct condition/counter reports and asynchronous collection.

**HC-SCP-02 · R · Keep responsibilities with their owners.** The following boundaries apply:

| Responsibility | Owner |
| --- | --- |
| Whether a service or condition is critical | Monitored service/application |
| Health policies, counters, recovery rules, recorded measurements, snapshots | Healthcheck |
| Recurring callback timing, condition skips, non-overlap, execution drain | Heartbeat |
| Business success, usable data, business deadlines, exposure limits, rollback | Application/component |
| Ordered initialization, application readiness gate, shutdown ordering | Application lifecycle coordinator and host integration |
| Requesting cancellation, waiting for a cancellation grace period, exposing a timeout/abort result | Separate future watchdog/execution-control module |
| Snapshot serialization and HTTP/pipe response handling | Healthcheck transport integrations |
| Restarting a container or removing it from traffic | Supervisor and deployment configuration |

Healthcheck MUST NOT call application rollback code, terminate tasks, restart services, replay business operations, or make Heartbeat scheduling depend on a health flag. A runtime readiness failure must not automatically stop the background work that could restore health.

**HC-SCP-03 · R · Preserve dependency direction.** The generic monitoring core MUST obey the core guide's zero-explicit-PackageReference and normal .NET class-library constraints. HTTP, hosting, and DI convenience APIs belong in appropriate integrations. Healthcheck MUST work without Heartbeat; Heartbeat's monitoring integration depends on the Healthcheck contract, not the reverse.

Use module and folder names as shorthand until repository conventions determine the published assembly, package, and namespace identities. Do not create a second environment singleton or require ASP.NET merely to record health.

## 2. Service, factory, and monitor ownership

**HC-OWN-01 · R · Application singleton.** There MUST be one Healthcheck service per application lifetime. The application host owns its activation, shutdown, and disposal. Ordinary consumers obtain monitors and report observations; they do not independently restart the application singleton.

**HC-OWN-02 · R · Transient monitors.** Factory calls MUST return distinct monitor class instances. A monitor represents a logical condition or activity and is reused across its observations. Two factory calls with identical options or display names MUST have independent mutable state.

The monitor instance is the caller's handle. A display name is optional. The registry assigns a unique opaque MonitorId; a name, type, or callback method is not registration identity. A disposed instance is terminal; a replacement receives a new identity and new state.

**HC-OWN-03 · R · No lifetime promotion.** Registration with the singleton MUST NOT turn a transient or scoped owner into a singleton. Long-lived registry state may contain detached values and weak references to monitor/collector objects. It MUST NOT retain the owner indirectly through a bound delegate, service provider, scope, execution context, arbitrary metric value, or exception object.

The owning component, or its lifetime integration, keeps the monitor alive for the required lifetime. Creating a monitor and immediately losing the only owner-held reference is not a reliable registration pattern. A monitor has no durable identity merely because it was once reported.

**HC-OWN-04 · R · Correct execution context.** A collector that uses scoped dependencies MUST execute against its originating owner context. Healthcheck MUST NOT resolve those dependencies from its application singleton's scope or create an unrelated substitute scope.

Before invoking owner-dependent code, the integration acquires both a live reference and an owner-lifetime hold. The hold is released only after actual collection, callback cleanup, and owner-dependent result handoff finish. A strong reference protects against garbage collection; it does not by itself prevent explicit disposal.

**HC-OWN-05 · D · Atomic registration.** Validate and copy options before publishing a registration. Invalid policy or configuration leaves no partial monitor. A successful factory call returns a fully registered instance. Initial registration performs no dependency I/O and starts no collector.

### Criticality declaration

**HC-CRT-01 · R · Service-selected criticality.** The service supplies IsCritical for each monitor. Healthcheck MUST NOT infer it from a service name, DI lifetime, exception type, failure count, or transport path.

**HC-CRT-02 · D · Stable declaration for an instance.** The first draft treats criticality as fixed at monitor creation. A component that needs separate critical availability and noncritical refresh reporting uses separate monitors. A factory convenience helper may create those two monitors together, but MUST NOT silently reclassify the same failure.

This does not require an application to choose the same criticality for every monitor belonging to one service.

## 3. Recorded state and aggregation

**HC-STA-01 · D · Monitor states.** A monitor's evaluated state is Pending, Healthy, or Unhealthy:

| State | Meaning |
| --- | --- |
| Pending | The policy has not yet received the observation needed to evaluate its condition. No failure has been asserted merely by registration. |
| Healthy | The policy's condition is currently satisfied, including any selected failure tolerance. |
| Unhealthy | The policy has flagged its condition. |

The last execution outcome is a separate fact. For a threshold-three policy, the second failed execution can leave the monitor Healthy because the selected threshold has not been reached.

**HC-STA-02 · R · Critical aggregate.** Aggregate HealthStatus MUST be Unhealthy if at least one active monitor is both critical and Unhealthy. Otherwise HealthStatus is Healthy. Noncritical failures remain visible in detail. Pending is visible but does not automatically become a failure.

No active monitors therefore means no recorded critical failure. It does not prove that application initialization completed. Readiness is a separate application fact, described in sections 9 and 10.

**HC-STA-03 · D · Atomic observation commits.** One accepted observation MUST update that monitor's policy state, applicable counters, diagnostic values, and the aggregate in one committed state change. Concurrent reports are ordered by their accepted commit order. The service MUST NOT claim to infer the real-world causal order of unrelated producers.

One report is one observation. Queries, serialization, and periodic exports MUST NOT replay it or advance a failure counter. Two explicit calls reporting the same failure are two observations unless they use the execution binding's duplicate-detection contract in section 8.

**HC-STA-04 · D · Recovery is local to the policy.** A successful observation clears only what its selected policy defines. It cannot clear another monitor, an independent condition, or the coordinator's recorded startup failure.

## 4. Initial monitor catalog

**HC-POL-01 · R · Factory policy ownership.** The factory MUST create the selected monitoring policy and own its evaluation. Integrations report facts; they do not duplicate threshold arithmetic or decide recovery.

The following catalog is the proposed bounded first release. It is not a general rules engine.

| Factory capability | Initial and failure behavior | Recovery |
| --- | --- | --- |
| Explicit condition | Owner supplies an initial clear/flagged value and can subsequently raise a condition with a reason. | Owner explicitly clears the condition. |
| Consecutive failures | Pending until first relevant result; threshold N flags on the Nth consecutive failed result. N must be at least one. | A successful result resets the streak to zero and clears this policy's flag. |
| Numeric threshold | Pending until a valid measurement. Owner supplies comparison, threshold, and unit. The comparison determines whether the measurement breaches the condition. | A subsequent measurement that does not breach clears the flag. |
| First-success availability | Pending before an initial result. A failed initial load flags; publishing usable data satisfies availability. Subsequent refresh failure does not erase success. | A valid first/replacement publication satisfies availability. Explicit invalidation can flag it again. |
| Asynchronous evaluation | An owner-supplied collector produces a condition, measurement, or execution result for one of the policies above. | The underlying policy determines recovery. |

**HC-POL-02 · R · Consecutive-failure semantics.** For FailureThreshold = 3:

| Accepted outcome sequence | ConsecutiveFailures | Evaluated state |
| --- | ---: | --- |
| No result | 0 | Pending |
| Failure | 1 | Healthy |
| Failure, Failure | 2 | Healthy |
| Failure, Failure, Failure | 3 | Unhealthy |
| Failure, Failure, Failure, Success | 0 | Healthy |
| Failure, Failure, Success, Failure | 1 | Healthy |

“Fail after three” means a threshold of three in this contract. An allowance of three failures before breach would be a different policy; it MUST NOT be substituted silently.

**HC-POL-03 · D · Neutral outcomes.** Skip and expected cancellation do not increment or reset a consecutive-failure streak. They do not fabricate a successful observation. A callback returning false is a failed execution; an unexpected exception can also be reported as failed, with different diagnostic information.

**HC-POL-04 · D · Numeric comparison.** Require an explicit comparison such as greater-than or greater-than-or-equal. Equality follows that chosen comparison. The first draft adds no automatic hysteresis, moving window, expiry, unit conversion, or freshness rule. Reject invalid/non-finite measurements without publishing a fictitious value.

For an application that monitors the age of usable FX data or a business exposure limit, the component supplies the relevant condition or age measurement and its originating timestamp. Healthcheck evaluates the selected condition/threshold. Heartbeat does not infer that timestamp, choose the acceptable age, or reinterpret a scheduling interval as the business budget.

**HC-POL-05 · R · First-success availability.** Availability records the owner's assertion that usable data has been published. A failed refresh MUST NOT clear that assertion while the retained data is still usable. The monitor does not inspect a cache, decide correctness, or impose a maximum age.

Explicit invalidation flags an existing availability requirement until usable data is published again. A new empty-cache lifetime starts without inherited success. Replacing a refresh worker does not, by itself, invalidate a still-live cache's availability monitor.

**HC-POL-06 · D · Distinguish required observations and optional metrics.** A failed required health evaluation is processed once according to the following explicit policy mapping:

| Policy receiving required evaluation | Exception, invalid collected value, or unexpected cancellation |
| --- | --- |
| Consecutive failures | Submit one failed observation under its threshold. |
| Explicit condition | Flag evaluation failure; a subsequent valid condition evaluation establishes the condition again. |
| Numeric threshold | Flag evaluation failure and retain the last valid measurement with its original observation time; a subsequent valid measurement determines the state. |
| First-success availability | Flag before usable data has been established; preserve established availability unless the owner explicitly invalidates it. |

An invalid direct numeric-report argument is rejected without changing state; the collector integration converts its own invalid required result into the evaluation failure defined above. A valid evaluation can still report an unsatisfied condition, so “valid” does not automatically mean Healthy.

Failure to collect an optional diagnostic measurement records a collection error but does not independently change health. Merely labelling a measurement “diagnostic” cannot conceal a condition that the service actually declared required. Failed evaluations obey the same obsolete-result rejection rule as successful collection; an old error cannot overwrite a newer direct success.

There is no watchdog monitor, deadline-abort policy, or automatic timeout-to-health conversion in this catalog.

### Illustrative factory usage

The following shows intent, not frozen API names or compilable implementation:

```csharp
var refreshMonitor = monitorFactory.CreateConsecutiveFailures(
    new ConsecutiveFailureMonitorOptions
    {
        Name = "Country list refresh",
        IsCritical = false,
        FailureThreshold = 3
    });

// Keep this instance for the refresh worker's intended lifetime.
refreshMonitor.ReportFailure();
refreshMonitor.ReportFailure();
refreshMonitor.ReportSuccess(); // Streak becomes zero.
```

Options use named properties, validated inputs, and immutable copied configuration. Public surface, ownership, cancellation, and error behavior must be finalized together before first publication.

## 5. Disposal and in-flight collection

**HC-DSP-01 · R · Remove the contribution.** Beginning disposal MUST atomically withdraw the monitor from the active registry and recalculate the aggregate. Snapshots captured after withdrawal MUST exclude it. Retiring the last failing critical monitor may make aggregate health Healthy; removal is not a synthetic success observation.

Healthcheck MUST NOT keep a failed-critical substitute, missing-service flag, or hidden persistent requirement after disposal. A longer-lived requirement must have its own still-live monitor and owner.

**HC-DSP-02 · D · Separate withdrawal from settlement.** Disposal has two observable boundaries:

1. Withdrawal closes further reports and collector admission and removes current health contribution.
2. Disposal completion proves that all admitted wrappers and owner-dependent work have actually settled and released their holds.

Collection admission acquires capacity and the required owner hold before executor posting. Coordinate the transition into collector code with withdrawal. Suppress code that has not crossed that invocation boundary, including a posted wrapper that has not entered the collector.

Disposal completion waits for admitted wrappers, acquired holds, running collection, cleanup, and owner-dependent handoffs, including wrappers that finish without invoking the collector. Requesting cancellation or stopping a caller's wait is not proof of settlement.

**HC-DSP-03 · D · Awaitable disposal barrier.** Monitor disposal MUST have an awaitable completion contract. Repeated calls share the same completion barrier. A canceled wait leaves withdrawal and the shared drain in progress. Enclosing scope/dependency disposal MUST respect actual completion.

The proposed common monitor handle supports IAsyncDisposable. A synchronous convenience for a purely push monitor may complete immediately, but the public API must not suggest that arbitrary asynchronous collectors can be drained by an unobserved synchronous fire-and-forget operation.

An executing collector MUST NOT await a disposal barrier that includes itself. It may request withdrawal and allow its outer owner/coordinator to await settlement.

**HC-DSP-04 · D · Reject late publication.** Completion of a retired collector is observed for cleanup, but its health result is discarded. It MUST NOT recreate the registration or update a replacement with the same display name.

Explicit calls through an already-withdrawn public monitor fail with ObjectDisposedException. Internal late-result handling uses the discard path rather than generating a new health failure.

**HC-DSP-05 · D · Weak-reference loss.** If an idle monitor can no longer be acquired, remove its registration under the same current-health rule. Reconcile weak liveness before publishing/capturing a snapshot so an already-unavailable target does not remain a known ghost contribution. A snapshot represents the liveness check at its boundary, not a promise about later garbage collection.

Weak-reference loss does not become a new critical health condition. Applications that require continued reporting must retain the monitor through their actual component lifetime.

## 6. Asynchronous collection

**HC-COL-01 · R · Collect outside reporting requests.** Support asynchronous owner-supplied collection. HTTP and pipe requests MUST capture current recorded state without invoking collectors, dependency calls, business startup, or Heartbeat callbacks.

Direct push reports update state without waiting for collection. A collection operation evaluates existing registrations; it does not repeatedly resolve transient services or create new monitors on every cycle.

**HC-COL-02 · D · Explicit activation.** Factory creation registers state only. A host/application integration activates collection after the relevant owner is initialized. Periodic collection is opt-in; each collector requires an explicit positive interval. No Healthcheck collector starts during DI construction or merely because a type was resolved.

Application-wide periodic collection is normally activated after startup completes. A component needing a mandatory initial evaluation performs and awaits it in its explicit startup operation before readiness. It must not create a readiness/collection circular dependency.

**HC-COL-03 · D · Bounded execution.** Permit at most one active collection per registration. Repeated requests while it is active join that collection's result; they do not create duplicate samples or an unbounded queue. Across registrations, apply the configured MaxConcurrentCollections limit.

Use a fair rotating selection among due collectors. Keep at most one pending due marker per active registration. Missed periods are coalesced. The next periodic eligibility is a completion-based fixed delay. A large registration set must not create one running Task per due collector just to wait for capacity.

The first-draft global default is four concurrent collections. This is a configurable project choice, not a CPU-count calculation or the Heartbeat capacity formula. Healthcheck has no inherited 10 ms collection interval.

**HC-COL-04 · D · Keep user code outside state coordination.** Invoke callbacks, await I/O, serialize reports, and obtain owner holds outside the short registry/publication critical section. Collection retains its execution slot and owner protection through actual completion and cleanup.

Caller cancellation cancels that caller's wait on shared collection. Lifetime cancellation is separately signalled when the collector retires or the application stops. Neither releases execution capacity early.

**HC-COL-05 · D · Prevent stale writes.** A collection carries its registration identity, generation, and starting health-observation revision. A health-bearing result can publish only while that registration is active and no newer direct health observation has committed since it began.

If a newer direct result won the race, discard the obsolete health-bearing result. Internal bookkeeping such as “collection started” does not itself invalidate the collection. Optional measurement updates have their own observation revisions so a health report cannot accidentally corrupt unrelated measurement ordering.

**HC-COL-06 · D · Isolate collection errors.** Observe collector exceptions and apply the required-versus-optional rule in HC-POL-06. Keep other monitors and reporters operational. Expected retirement/shutdown cancellation is neutral. Unexpected cancellation is processed according to the collection's declared policy, rather than universally assumed successful or neutral.

Failed optional collection preserves the previous value and its actual observation time, alongside the new collection error. It MUST NOT insert zero, refresh the old timestamp, or invent recovery.

Healthcheck does not forcibly terminate noncooperative collectors. A stalled collector remains tracked and occupies its actual slot until it ends; the separate watchdog limitation in section 13 also applies.

## 7. Snapshot, counters, and diagnostic contract

**HC-SNP-01 · R · One consistent snapshot.** Capture one immutable version containing the active monitor set, criticality, evaluated states, observations, counters, application lifecycle facts, and aggregate. The HTTP status and selected payload for a request MUST be derived from that same captured version. The pipe adapter uses the same rule.

A practical implementation may publish immutable state or copy all detached state under a short lock. Either is acceptable if it provides the same boundary. The contract does not require that all real dependencies were measured simultaneously.

**HC-SNP-02 · D · Distinguish capture and observation times.** Capture time says when the request obtained its snapshot. Observation timestamps say when facts were reported or measurements actually completed. A capture MUST NOT make old observations appear fresh.

Use UTC for interchange timestamps and a monotonic time source for elapsed durations. Use the shared core's stable application/build/instance metadata. Do not treat application timestamps as data freshness guarantees.

**HC-SNP-03 · D · Minimum fields.** The in-memory and detailed wire contracts expose:

| Scope | Required information |
| --- | --- |
| Snapshot | SchemaVersion, revision, CapturedAtUtc, application identity, HealthStatus, application startup/stopping facts, derived readiness, counts of active/critical/failing/pending monitors |
| Monitor | MonitorId, optional display name, policy kind, IsCritical, evaluated state, safe reason/code, registration time, last observation time |
| Execution policy | Successful, failed, skipped, and expected-canceled outcome totals where applicable; consecutive failures; last outcome; last success/failure times |
| Collection | Whether collection is in progress, last completed collection time, last collection error and its time |
| Measurement | Stable key, typed finite value or unavailable state, declared unit, observation time, optional diagnostic classification |

Fields that do not apply are absent or explicitly null according to the frozen schema. The absence of a measurement is not a zero value. Overall health is not derived by a transport independently counting a truncated monitor list.

**HC-SNP-04 · D · Counter semantics.** Counters accumulate for the monitor's current lifetime. Failure counters advance once per accepted failure; skip and expected-cancellation counters remain separate. Snapshots and duplicate execution notifications do not increment them.

Use nonnegative 64-bit lifetime outcome counters with saturation and an explicit saturation indicator rather than wraparound. Encode them as decimal strings in JSON, avoiding precision loss in consumers that use binary floating-point numbers for JSON numeric values.

Observation/version identities MUST remain distinct after a newer commit; do not use a saturating counter for obsolete-result detection. The first draft uses an arbitrary-precision increasing published revision, encoded as a decimal string, with independently versioned per-monitor observation state. Neither wraps or reuses a prior version within the application lifetime.

Aggregate monitorCounts are bounded integers derived from the current registry and may be JSON numbers because MaxMonitors bounds them. They are not lifetime outcome counters. Durations and measurement representations have explicit units.

**HC-SNP-05 · D · Bounded detached diagnostics.** Retain current state, lifetime counters, and the latest applicable success/failure/collection diagnostics. The first release does not require an unbounded event history or external event bus.

Copy bounded, safe diagnostic values. Do not retain Exception instances, arbitrary Exception.Data graphs, service instances, or mutable caller dictionaries in snapshots. A fault description can include a stable code, exception type name, and bounded message. Full stacks and arbitrary application payloads are not automatically included in public health responses.

The first draft limits display names to 128 characters, diagnostic messages and string metric values to 1,024 characters, metric keys to 128 characters, units to 32 characters, and metric entries to 32 per monitor. Metric values are copied scalar strings, booleans, or finite numbers; arbitrary object graphs are unsupported. Options that exceed structural limits fail validation; oversize diagnostic text is truncated with an explicit indicator, and invalid metric inputs are rejected. Additional registration/report/transport bounds appear in section 14.

**HC-SNP-06 · D · No query side effects.** Reading a snapshot does not reset counters, clear flags, activate collectors, refresh retention, or rerun a failed observation. Readers may serialize a previously captured snapshot after a monitor has been disposed; the snapshot remains unchanged.

## 8. Heartbeat integration

**HC-HB-01 · R · Bind by policy.** The Heartbeat integration MUST ask Healthcheck to bind the service-selected policy and criticality before activating that schedule. Healthcheck validates the policy and creates or associates the monitor. Heartbeat treats its descriptor as opaque.

The first draft permits either an owner-supplied monitor or a policy descriptor that creates an owner-associated monitor. A descriptor selects a built-in policy and its typed options; invalid/unknown descriptors fail binding with a configuration issue before the registration activates. A failed configured binding MUST NOT silently create an unmonitored running registration.

Validate that the input facts can actually evaluate the chosen policy. Ordinary success/failure binding supports consecutive failures. First-success availability requires an explicit owner declaration that successful callback completion means usable data has been published. Numeric and explicit-condition monitors require direct reports or an appropriate owner-supplied reporting adapter; Heartbeat MUST NOT invent a numeric value or infer an arbitrary condition from `Task<bool>`.

**HC-HB-02 · D · Binding lifetime and absent policy.** A missing policy explicitly means no monitoring binding for that schedule. A binding-created monitor belongs to that registration's owner-associated lifetime. Dispose it when that registration ends through completed unregistration, automatic retirement, owner closure, or application shutdown, after final required accepted handoffs. Its removal follows H02.

An externally supplied monitor remains caller-owned on all of these paths; unbinding Heartbeat does not dispose it.

Duplicate Heartbeat registration MUST NOT create another monitor or reset counters. Reuse after completed unregister creates a new execution-registration identity. Counter continuity occurs only through an explicitly retained external monitor, not through a matching name.

The owner/lifetime integration must retain binding-created monitors without rooting transient owners from the application singleton. A weak reference with no actual lifetime owner is insufficient.

**HC-HB-03 · R · Report execution facts.** Support detached notifications for registration, execution admission/start, condition skip, successful/failed/canceled outcome, and actual settlement/retirement as required by the Heartbeat contract. Include registration and execution correlation identities, relevant phase/timing facts, and copied error information.

Heartbeat decides what actually happened to the execution. Healthcheck decides its effect under the selected policy:

| Heartbeat fact | Healthcheck interpretation |
| --- | --- |
| Optional execution condition returned false | Skip; neutral to the default failure streak |
| Callback returned true | Successful outcome |
| Callback returned false | Failed outcome; it is not an unhandled exception |
| Condition or callback produced an unexpected exception | Failed outcome with phase/error facts supplied by Heartbeat |
| Expected cancellation | Separate neutral outcome under the default policy |
| Settlement after the outcome handoff | Lifecycle/timing update; not a second callback result |

Heartbeat's OnError remains an unexpected-exception handler. Healthcheck MUST NOT request OnError merely because a result is false, and a successful OnError does not erase the original failed execution.

The Heartbeat v0.2 ObjectDisposedException assumption is not silently changed here: the execution integration reports the original outcome and retirement facts under that contract. If the associated health monitor has itself already been withdrawn, its late publication is discarded under H02; disposal does not retain an active failure.

**HC-HB-04 · D · Immediate in-process handoff.** The first implementation uses a short, non-I/O in-process commit into Healthcheck, with no background delivery queue between the execution fact and its current-state update. Accepted means recorded in process memory, not durable or externally delivered.

A binding accepts at most one terminal outcome for each monotonically ordered execution identity. Retain bounded current/last-execution bookkeeping and an accepted high-water mark:

- Within the retained execution window, an identical terminal repeat is a no-op; a contradictory terminal payload is an integration fault.
- Terminal facts older than that window/high-water mark are ignored as obsolete, without changing state or counters. Do not claim to compare arbitrarily old payloads against unretained history.
- A settlement/lifecycle fact may update only its matching retained execution record. A normal late fact outside the retained window may be discarded.
- A late lifecycle fact cannot move current execution backward or create another outcome.

Heartbeat's no-overlap rule orders actual execution, but a detached settlement notification can arrive after a later execution begins. That delivery race is not itself a fault. No unlimited deduplication set is required.

**HC-HB-05 · R · Isolate reporting faults and preserve ownership.** A reporting rejection/fault does not become another callback failure or recursively invoke OnError. Preserve the original execution fact and expose a bounded integration diagnostic. A retired binding can ignore an expected late notification.

Any handoff still using the owner is awaited before Heartbeat releases that owner's hold or the execution slot. Later transport serialization operates on detached state only. Actual settlement can be a separate detached notification after release; it cannot be falsely included in an earlier outcome timestamp.

Healthcheck remains available until Heartbeat has finished its final accepted reporting handoffs. Do not introduce an HTTP request, pipe write, logging flush, or slow exporter into that handoff.

## 9. Application startup and readiness integration

**HC-APP-01 · R · No initialization during container construction.** Constructing or resolving the container MUST NOT prime the country list, start dependency polling, or run required serialization initialization. The future application startup coordinator explicitly invokes initialization after construction, in the intended dependency order.

Health reporting infrastructure must be available before business initialization can fail. A host integration must arrange that ordering deliberately; it must not assume that an ordinary hosted-service StartAsync runs after the HTTP server is already listening.[^hosting]

**HC-APP-02 · R · Complete startup before accepting requests.** The coordinator declares startup complete only after every required startup operation has actually completed. An empty startup list still requires the host's explicit completion boundary. Detached required work cannot count as completed initialization.

The coordinator can await the returned StartAsync task; the component is responsible for keeping that task incomplete until all its required work finishes. Healthcheck and the coordinator cannot generally discover undisclosed detached work after a component incorrectly returns success.

The application owns the business-request gate. Healthcheck records supplied StartupCompleted and IsStopping facts; it does not resolve IStartable, choose initialization order, or open the application's request pipeline itself.

**HC-APP-03 · D · Derived readiness.** Include the application facts in the same snapshot as health. Derive Ready as:

```text
StartupCompleted AND NOT IsStopping AND HealthStatus == Healthy
```

StartupCompleted begins false and becomes true only on successful completion of that application startup lifecycle. IsStopping prevents readiness during shutdown even if retiring monitors removes all failures. HealthStatus continues to mean the aggregate of active critical conditions.

Pending initialization is not itself a flagged failure. The distinction lets probes describe “not ready yet” without silently inventing a failed health observation.

### Country-list example

**HC-APP-04 · R · Prime, then subscribe.** The required flow is:

1. Construct the container and establish health-reporting infrastructure.
2. Invoke the country-list component's explicit StartAsync after its prerequisites.
3. Fetch, validate, and publish a usable initial country list.
4. Mark its critical availability monitor satisfied.
5. Register the recurring refresh with Heartbeat, with the selected noncritical refresh policy.
6. Complete that component's startup.
7. After the entire application startup sequence succeeds, publish StartupCompleted and allow normal requests subject to current critical health.

Heartbeat registrations accepted during startup remain execution-gated until the application's Heartbeat activation boundary. They must not refresh against a partially initialized application. Later runtime Unhealthy state does not automatically pause Heartbeat.

**HC-APP-05 · R · Failed priming stops startup.** If mandatory priming fails, do not register that recurring refresh, do not continue to dependent startup operations, and do not silently retry the startup sequence. The startup operation reports failure. The application remains alive with health reporting available and waits for the configured supervisor action.

The coordinator retains its own live critical startup-failure monitor for this application lifetime. That condition is not cleared by an unrelated successful monitor or a later health request. If a child monitor is disposed, it disappears as H02 requires; the still-live coordinator's independently reported failure explains why startup remains failed.

If priming succeeds but a required Heartbeat binding/registration fails, startup has still not completed successfully. A later required component's startup failure likewise keeps the application unready.

Expected cancellation because the application is shutting down stops startup and keeps readiness false. It is not converted into a mandatory-initialization failure solely because shutdown requested cancellation.

**HC-APP-06 · R · Retained-data behavior.** Once usable country data is published, ordinary refresh failures do not invalidate it. A separate noncritical refresh monitor can remain Unhealthy indefinitely without making the aggregate Unhealthy, provided no critical monitor is failing. No maximum age, retry count, or exposure limit is inferred for this data.

The component is responsible for retaining the old usable list during failed replacement, and for reporting invalidation if it ceases to be usable. Healthcheck cannot repair an application that discards the valid cache before a replacement is ready.

### Future IStartable note

**HC-APP-07 · R · Integration boundary, not a new Healthcheck orchestrator.** The future application lifecycle definition must carry the following requirements:

- Resolve the intended IStartable instances in their proper owner contexts after container construction.
- Invoke and await StartAsync sequentially in a defined order.
- Track invocation by object instance, including aliases that resolve the same object.
- Mark the instance invoked before entering its method. At most once means at most one invocation, even if the method faults or is canceled.
- Prevent repeated/concurrent coordinator runs from replaying an invocation or resolving new transient replacements to retry it.
- Retain no owner longer than the application/component lifetime actually requires.
- Treat completion as including required data and serialization-contract priming.
- Publish application startup success only after the full required sequence succeeds.

Healthcheck supplies observation and reporting APIs for this integration. Implementing that coordinator is a separate module task.

## 10. HTTP reporting and the reserved branch

**HC-HTTP-01 · R · Reserve /_health.** Provide middleware/integration that reserves /_health and all descendant path segments. Matching requests MUST be handled within the health branch and MUST NOT reach business controllers, ordinary application middleware, or a catch-all route.

Place the branch before application logic whose execution is to be excluded. Middleware cannot reverse side effects from earlier middleware; integration ordering must be verifiable.[^middleware] Match path segments so /_healthcare is an unrelated route.

**HC-HTTP-02 · R · Selected status codes.** A successful aggregate-health report uses 200 OK for Healthy and 500 Internal Server Error for Unhealthy. Do not replace the selected 500 mapping with a framework default such as 503.

**HC-HTTP-03 · D · Initial routes.** The following route choices are explicit proposals for this definition:

| Route | View and successful response |
| --- | --- |
| /_health | Compact aggregate view; 200 when HealthStatus is Healthy, otherwise 500 |
| /_health/ready | Compact readiness view; 200 when Ready is true, otherwise 500 |
| /_health/details | Detailed snapshot; same aggregate status mapping as /_health, including the recorded readiness facts |

The readiness route makes the earlier load-balancer example precise: **a load balancer deciding whether to send application traffic uses /_health/ready.** During initialization, /_health can report 200 because no critical condition has failed yet, while /_health/ready reports 500 because startup is incomplete.

| Recorded application situation | Aggregate view | Readiness view |
| --- | ---: | ---: |
| Priming pending/in progress, no critical failure | 200 | 500 |
| Mandatory priming failed; live coordinator has flagged failure | 500 | 500 |
| Full startup succeeded, no critical failure | 200 | 200 |
| Only a noncritical refresh monitor is failing after startup | 200 | 200 |
| An active critical runtime monitor is failing | 500 | 500 |
| Shutdown begun, no critical monitor currently failing | 200 | 500 |

The adapter does not add a central liveness or restart classification. Deployment configuration chooses which view to use for a Kubernetes probe.

**HC-HTTP-04 · D · Branch behavior.** Support GET and HEAD on enabled routes. HEAD uses the same snapshot/status rules without a body. Return 404 for unknown paths within the reserved branch and 405 with an appropriate Allow header for unsupported methods on known routes. Do not fall through. Match branch/known routes case-insensitively and accept a single trailing slash as the same route; document this as part of the routing contract.

The detailed route is disabled by default and then returns 404 within the branch. When enabled, its access policy belongs to the health adapter or hosting configuration; it must not depend on falling through to business-route authorization.

**HC-HTTP-05 · D · Response coherence and freshness.** Capture once per request. Serialize detached snapshot values only, outside registry locks. Set an explicit JSON content type and no-store/no-cache response policy; do not use a cached HTTP response as a new observation.

Health requests do not wait for collection or run application dependency checks. A disconnect cancels serialization/output for that request, not ongoing collection or shared health state.

Unknown routes and transport/serialization errors are request failures, not recorded monitor failures. Serialize/validate a complete response within the configured bound before committing HTTP headers or body. An oversize or serialization failure returns HTTP 500 with a small recognizable reporting-error payload that fits the minimum configured bound. Do not silently omit unhealthy monitors or claim a partial body is a complete snapshot.

HEAD applies the same validation and status decision for its captured version, then omits the body. A reporting error does not mutate HealthStatus. Request cancellation may prevent any response from being delivered; it cannot turn an incomplete response into a valid health result.

### Illustrative detailed payload

This example shows successful startup and usable country data alongside a failing noncritical refresh. Names and JSON field spelling are proposed schema choices.

```json
{
  "schemaVersion": 1,
  "revision": "42",
  "capturedAtUtc": "2026-10-11T10:00:00Z",
  "application": {
    "name": "CountryService",
    "instanceId": "example-instance",
    "startupCompleted": true,
    "isStopping": false
  },
  "healthStatus": "Healthy",
  "ready": true,
  "monitorCounts": {
    "active": 2,
    "critical": 1,
    "failing": 1,
    "criticalFailing": 0,
    "pending": 0
  },
  "monitors": [
    {
      "monitorId": "example-availability",
      "name": "Country list availability",
      "policy": "FirstSuccessAvailability",
      "isCritical": true,
      "state": "Healthy",
      "lastSuccessAtUtc": "2026-10-11T09:00:00Z"
    },
    {
      "monitorId": "example-refresh",
      "name": "Country list refresh",
      "policy": "ConsecutiveFailures",
      "isCritical": false,
      "state": "Unhealthy",
      "lastOutcome": "Failed",
      "consecutiveFailures": "3",
      "failedOutcomes": "3",
      "lastFailureAtUtc": "2026-10-11T09:59:30Z"
    }
  ]
}
```

The released schema must give every field a stable meaning, null/omission rule, type, and unit. Optional added fields must preserve existing readers. The illustrative payload omits some identity and diagnostic fields for readability.

## 11. Pipe reporting and Kubernetes

**HC-PIPE-01 · R · Support pipe reporting.** Provide a local pipe server and matching probe client as an alternative to HTTP. Both adapters consume the same snapshot model and apply the same selected view to a captured version.

Kubernetes supports an exec probe that invokes a program and judges its exit status; it does not have a native pipe probe field. The pipe integration therefore uses exec to run the client. Exit zero indicates success; a nonzero exit indicates failure. Native HTTP probes can use the selected HTTP routes.[^kubernetes]

**HC-PIPE-02 · D · Local IPC choice.** The first draft selects matched .NET named-pipe server/client APIs. On Linux these APIs use Unix domain sockets.[^named-pipes] This is not a promise of compatibility with an existing POSIX FIFO reader. No FIFO-specific transport is required by this draft.

The application explicitly configures the local pipe name and installs the matching client in its deployment image. The server and probe run in a filesystem/permission context that can reach the same endpoint. Remote network pipe access is outside the initial contract.

**HC-PIPE-03 · D · Framed request/response.** Use one request and one response per connection. Each message is a four-byte unsigned little-endian byte length followed by that many UTF-8 JSON bytes, without a byte-order mark. Enforce the length before allocating a payload buffer.

A request contains protocolVersion = 1 and a view of health, readiness, or details. The wire spelling follows the camelCase JSON convention. The response envelope has these required fields:

| Field | Contract |
| --- | --- |
| protocolVersion | Integer 1 for this protocol |
| schemaVersion | Integer 1 for the snapshot schema defined for the first release |
| view | The requested valid view; null is allowed only for an error replying to an unparseable/invalid request |
| kind | Exactly snapshot or error |
| snapshot | Required only for kind = snapshot: complete values for the selected view, including revision, capturedAtUtc, healthStatus, and ready |
| error | Required only for kind = error: a stable code of at most 64 characters and an optional message of at most 128 characters |

For a snapshot response, schemaVersion applies to its snapshot; if repeated inside the snapshot, the versions must match. A details snapshot includes the complete active monitor list. Compact views may omit that list but cannot omit the selected result fields. Exactly one of snapshot and error is present.

Reject unknown protocol/schema versions, invalid views, missing or contradictory required fields, duplicate JSON property names, and malformed UTF-8. An accepted snapshot view must match the request. For example, ready = true with healthStatus = Unhealthy is contradictory. Unknown optional fields remain ignorable for additive compatibility.

The 512-byte request limit and MaxReportBytes response limit count JSON payload bytes, excluding the four-byte prefix. Handle prefixes and bodies arriving across multiple reads; premature EOF is an error. Finish bounded serialization before writing the frame prefix. A response must not be advertised as a successful complete frame and then intentionally truncated to fit.

Details obey the same enablement and completeness rules as HTTP. Fixed error envelopes must fit the minimum report bound. Protocol errors are not new health observations.

**HC-PIPE-04 · D · Client result.** The matching command-line client selects the requested view and interprets the returned result:

| Client exit | Meaning |
| --- | --- |
| 0 | Valid complete response whose selected view is Healthy/Ready |
| 1 | Valid complete response whose selected view is Unhealthy/NotReady |
| 2 | Connection, timeout, protocol, schema, or reporting error |

Only a valid successful result can produce zero. A missing endpoint, early EOF, malformed JSON, oversize frame, mismatched version, or timeout must not produce success. Merely printing an Unhealthy response and exiting zero is incorrect.

**HC-PIPE-05 · D · Bounded clients.** Bound simultaneous clients and each connection's read/write budget. A slow reader must not hold the health registry lock, block observation commits, or create an unlimited queue of old snapshots. Capture a snapshot when serving the valid request; do not maintain a backlog of periodically generated reports.

The first-draft defaults are eight concurrent clients and a 1,000 ms total client request budget, including connect, request, and response. The server also enforces a bounded per-connection budget. Cancel/disconnect work locally when its client ends.

### Deployment examples

These are illustrative integration fragments, not a complete Kubernetes deployment or installed executable.

```yaml
readinessProbe:
  httpGet:
    path: /_health/ready
    port: 8080
  timeoutSeconds: 2
livenessProbe:
  httpGet:
    path: /_health
    port: 8080
  timeoutSeconds: 2
```

An equivalent readiness probe through the proposed matching client:

```yaml
readinessProbe:
  exec:
    command:
      - /app/cis-health-probe
      - --pipe
      - cis-health
      - --view
      - readiness
      - --timeout-ms
      - "1000"
  timeoutSeconds: 2
livenessProbe:
  exec:
    command:
      - /app/cis-health-probe
      - --pipe
      - cis-health
      - --view
      - health
      - --timeout-ms
      - "1000"
  timeoutSeconds: 2
```

To implement the agreed wait-for-restart behavior after failed priming, the deployment MUST configure a restart-capable probe, such as the aggregate liveness views illustrated above, as well as readiness.

The deployment supplies startup allowances appropriate for actual initialization. A Kubernetes startup probe can protect initialization before liveness/readiness probes begin. Configure its failure threshold and timing deliberately; Healthcheck does not own that startup budget. Readiness failure affects traffic eligibility, whereas a configured liveness/startup failure can cause restart after the supervisor's threshold.[^kubernetes]

Selecting the aggregate view for liveness means critical failures are exposed to that liveness probe. This is the deployment's choice. Healthcheck does not add RestartRequired or secretly suppress a critical failure for one transport.

## 12. Application shutdown

**HC-LIFE-01 · R · Preserve dependencies through remaining work.** Shutdown MUST prevent new collection, make readiness false, and finish accepted owner-dependent work before disposing the dependencies it uses. Healthcheck remains available for final Heartbeat reporting handoffs.

Publishing IsStopping atomically closes new monitor registration, monitoring bindings, collector activation, and collection admission. Existing active monitors may still accept final observations until withdrawn. IsStopping is terminal for that application lifetime and cannot be cleared.

The application integration establishes this order:

1. Publish IsStopping; reject new monitor registration, binding creation, and collector activation/admission; request cooperative collector lifetime cancellation.
2. Stop admitting new Heartbeat work under its own shutdown contract; suppress its queued unstarted work.
3. Let running Heartbeat work, Healthcheck collectors, cleanup, and owner-dependent reporting settle. Direct outcome reports remain accepted for still-active monitors during this phase.
4. Retire monitors and dispose component scopes after their execution barriers are complete. Keep the application root and scopes that own Healthcheck/reporting adapters alive.
5. Close new reporting-request admission, enforce transport budgets, and request cancellation of remaining handlers as necessary. Await actual handler completion before disposing adapters and Healthcheck; a requested cancellation is insufficient. Dispose their enclosing scopes/root afterward.

Normal monitor disposal removes its contribution immediately, including during shutdown. Readiness remains false because IsStopping is part of its snapshot predicate.

**HC-LIFE-02 · D · Shared terminal completion.** Repeated host stop/dispose operations share terminal completion. A canceled caller wait does not reopen admission or certify that running work ended. A stopped application service instance is not restarted for a new application lifecycle.

Transport closure must not make an in-flight collector safe to dispose. A noncooperative task can outlive a cancellation request; this must remain visible to lifecycle coordination, even though its withdrawn monitor is no longer part of current health.

## 13. Watchdog boundary and .NET execution limits

**HC-WDG-01 · R · No health authority.** Watchdog behavior is outside this library. The watchdog MUST NOT directly flag or clear a monitor, choose criticality, increment failure policy counters, or decide application health. The application can independently report an outcome through its chosen ordinary monitor.

The future watchdog definition may specify:

1. Request cooperative cancellation at its execution deadline.
2. Allow a configured cancellation grace period for the underlying work to stop.
3. If it still has not stopped, fault the watchdog's exposed wait with the selected timeout/abort error.
4. Continue tracking the real execution until it actually finishes.

**HC-WDG-02 · R · Do not promise impossible task termination.** Cancellation in .NET is cooperative.[^cancellation] Faulting a wrapper wait does not inject an exception into arbitrary task code or prove that code stopped. Thread.Abort is unsupported on modern .NET; a hard termination requirement for noncooperative code needs stronger isolation, normally a separate process.[^thread-abort][^destroying-threads]

TaskAbortException is not adopted as a built-in .NET type or a new Healthcheck exception. Any future custom public error contract belongs to the watchdog definition.

For Heartbeat integration, a watchdog timeout cannot release the callback's no-overlap reservation or owner hold while the actual operation still runs. Starting rollback or replacement work that can race the original operation remains an application/execution-control concern.

These execution limits do not delay Healthcheck implementation because no watchdog capability is required by its monitor or reporting contracts.

## 14. Configuration and inherited engineering rules

**HC-CFG-01 · R · Shared immutable environment configuration.** Follow the existing EnviormentsOptions spelling and the core's initialize-once model. Add Healthcheck setup and a resolved immutable section through that existing mechanism; the conceptual shape is EnviormentsOptions.Current.Healthcheck, not a claim that such a member has already shipped.

Resolve a nonblank environment override before an explicit setup value, then a documented default. Empty/whitespace-only environment input is absent. An invalid higher-priority value fails validation instead of falling back. Copy options before publishing and do not reread environment variables for each report or collection.

Use invariant parsing, explicit units/bounds, and CoreConfigurationException with immutable issues, stable codes, and configuration keys. Failed validation publishes no partial snapshot. Counters, flags, live registrations, and application startup progress are runtime state and MUST NOT be stored in the environment snapshot.

**HC-CFG-02 · D · Proposed module options.** The following names/defaults are first-draft proposals, distinct from the already-agreed CIS_HEARTBEAT_MAX_CONCURRENT setting:

| Setting | Proposed environment override | Default and validation |
| --- | --- | --- |
| MaxConcurrentCollections | CIS_HEALTHCHECK_MAX_CONCURRENT_COLLECTIONS | 4; integer 1–64 |
| MaxMonitors | CIS_HEALTHCHECK_MAX_MONITORS | 1,024; integer 1–65,536 |
| DetailsEnabled | CIS_HEALTHCHECK_DETAILS_ENABLED | false; explicit boolean |
| PipeEnabled | CIS_HEALTHCHECK_PIPE_ENABLED | false; explicit boolean |
| PipeName | CIS_HEALTHCHECK_PIPE_NAME | Required when pipe is enabled; validate a portable local endpoint name |
| PipeMaxClients | CIS_HEALTHCHECK_PIPE_MAX_CLIENTS | 8; integer 1–64 |
| PipeRequestTimeoutMs | CIS_HEALTHCHECK_PIPE_TIMEOUT_MS | 1,000; integer 50–60,000 ms |
| MaxReportBytes | CIS_HEALTHCHECK_MAX_REPORT_BYTES | 4,194,304; integer 1,024–16,777,216 bytes |

Periodic collection intervals are explicit per-collector options with no automatic interval. Policies, thresholds, and criticality are chosen by the service; they are not global environment settings that silently overwrite every service's policy.

When MaxMonitors is reached, fail a new factory registration atomically with a documented capacity error; do not evict existing monitors or lose a critical contribution. Pipe enablement requires its integration to be installed. HTTP installation and its reserved-branch ordering are explicit host configuration.

**HC-ENG-01 · R · Inherited baseline.** Use the core guide's initial net10.0 target and Windows/Linux verification requirements. Confirm target assets and freeze package/assembly/namespace identity before first publication, including prerelease. Preserve the shared core's dependency direction, readable C#, analyzer/formatting gates, and public documentation rules.

**HC-ENG-02 · R · Permanent compatibility.** Once published, maintain source, binary, configuration, schema, target-asset, identity, and documented-behavior compatibility under the existing core policy. That includes threshold arithmetic, disposal semantics, status codes, route matching, environment keys, snapshot field meanings, and pipe framing/version behavior.

**HC-ENG-03 · R · Complete verification evidence.** Apply all six required verification families: unit, integration, mutation, architecture, backward compatibility, and business cases. Apply the shared core's coverage, mutation, dependency, and replayable release-evidence gates. Extend critical-invariant mutation review to aggregation, threshold/reset boundaries, disposal, snapshot coherence, duplicate reporting, and owner settlement.

Use deterministic clocks and controllable collectors for timing/race scenarios. Verify actual HTTP and pipe integration on claimed platforms. Test exported packages and published wire fixtures; a successful internal mock test does not establish deployment behavior.

## 15. Acceptance criteria

These are requirements for implementation verification. No row is represented as a test already executed.

| ID | Scenario and required result | Contract |
| --- | --- | --- |
| HC-AT-01 | Two factory calls with identical options produce independent monitor state and distinct identities. | HC-OWN-02 |
| HC-AT-02 | A monitor's optional name or class type never merges its state with another instance. | HC-OWN-02 |
| HC-AT-03 | An active critical Unhealthy monitor makes the aggregate Unhealthy. | HC-CRT-01; HC-STA-02 |
| HC-AT-04 | A noncritical failure remains detailed while the aggregate is Healthy in the absence of critical failures. | HC-STA-02 |
| HC-AT-05 | Pending initial state is visible without inventing a failure; incomplete startup still prevents readiness. | HC-STA-01; HC-APP-03 |
| HC-AT-06 | Threshold three stays below breach for failures one/two and flags on failure three. | HC-POL-02 |
| HC-AT-07 | Failure, failure, success, failure produces streaks 1, 2, 0, 1 and the selected recovery. | HC-POL-02 |
| HC-AT-08 | Skip/expected cancellation neither increments nor resets the failure streak. | HC-POL-03 |
| HC-AT-09 | Numeric equality follows the selected comparison; invalid values publish no fictitious measurement. | HC-POL-04 |
| HC-AT-10 | Repeated refresh failure after usable data was published does not invalidate availability. | HC-POL-05 |
| HC-AT-11 | Explicit invalidation flags availability; a new empty cache starts without inherited success. | HC-POL-05 |
| HC-AT-12 | Success clears only the relevant policy, leaving an independent flagged condition intact. | HC-STA-04 |
| HC-AT-13 | Dispose the last failing critical monitor; subsequent snapshots omit it and recompute aggregate health. | HC-DSP-01 |
| HC-AT-14 | Snapshot capture racing disposal sees a coherent complete version on either side of withdrawal. | HC-DSP-01; HC-SNP-01 |
| HC-AT-15 | Withdrawal before collector admission suppresses invocation. | HC-DSP-02 |
| HC-AT-16 | Withdrawal during posted/running collection removes health contribution immediately, suppresses unentered user code, and waits for all admitted wrappers/holds to settle. | HC-DSP-02; HC-DSP-03 |
| HC-AT-17 | A late result cannot recreate a disposed monitor or update a new monitor with its old name. | HC-DSP-04 |
| HC-AT-18 | A weakly lost idle monitor disappears without an inferred missing-critical-service failure. | HC-DSP-05 |
| HC-AT-19 | Registry, callbacks, diagnostics, and bindings do not root a transient owner or unrelated scope. | HC-OWN-03; HC-HB-02 |
| HC-AT-20 | Collector dependencies come from their originating owner; disposal waits for its lifetime hold. | HC-OWN-04 |
| HC-AT-21 | Canceling a collection/disposal wait does not release actual capacity or dispose dependencies. | HC-DSP-03; HC-COL-04 |
| HC-AT-22 | Repeated concurrent collection requests join one running collection and apply one result. | HC-COL-03 |
| HC-AT-23 | At capacity, due collectors remain bounded and receive fair admission without one waiting Task per registration. | HC-COL-03 |
| HC-AT-24 | A newer direct observation prevents an older asynchronous result from overwriting it. | HC-COL-05 |
| HC-AT-25 | A failed optional metric collection preserves the previous value/time and does not independently change health. | HC-POL-06; HC-COL-06 |
| HC-AT-26 | Required evaluation failure, invalid collected data, and recovery follow each policy's explicit mapping; obsolete errors cannot overwrite a newer direct report. | HC-POL-06; HC-COL-05; HC-COL-06 |
| HC-AT-27 | Capturing/serializing a snapshot invokes no dependency check and changes no counters or flags. | HC-COL-01; HC-SNP-06 |
| HC-AT-28 | Concurrent observation commits cannot produce disagreement among policy state, counters, aggregate, HTTP status, and that response body. | HC-STA-03; HC-SNP-01 |
| HC-AT-29 | Capture time advances without rewriting historical observation times. | HC-SNP-02 |
| HC-AT-30 | Lifetime counter saturation cannot wrap; wire fixtures preserve large values, and newer observation revisions remain distinct after counter saturation. | HC-SNP-04 |
| HC-AT-31 | Mutating caller data after reporting cannot change a snapshot; retained diagnostics contain no live exception graph. | HC-SNP-05 |
| HC-AT-32 | Invalid/unsupported Heartbeat policy-input combinations fail binding before activation; absent policy has the documented behavior. | HC-HB-01; HC-HB-02 |
| HC-AT-33 | Duplicate Heartbeat registration/outcome notifications do not create a second monitor or advance counters twice; retained contradictions fail and obsolete facts cannot change state. | HC-HB-02; HC-HB-04 |
| HC-AT-34 | Heartbeat false result reports failure without requesting OnError; handler success does not erase an exception outcome. | HC-HB-03 |
| HC-AT-35 | Settlement updates its retained execution only; late settlement cannot move current execution backward or count another outcome. | HC-HB-03; HC-HB-04; HC-HB-05 |
| HC-AT-36 | Reporting faults remain integration diagnostics and do not create recursive callback failures or prematurely release owner protection. | HC-HB-05 |
| HC-AT-37 | Container construction/resolution performs no priming or periodic collection. | HC-APP-01; HC-COL-02 |
| HC-AT-38 | Successful country priming precedes recurring registration; execution waits for the application activation boundary. | HC-APP-04 |
| HC-AT-39 | Priming failure stops startup, creates no recurring refresh registration, and remains observable through the coordinator's live monitor. | HC-APP-05 |
| HC-AT-40 | A disposed child monitor disappears even while the live coordinator independently records startup failure. | HC-DSP-01; HC-APP-05 |
| HC-AT-41 | Required registration failure or later component startup failure prevents overall readiness despite successful country loading. | HC-APP-05 |
| HC-AT-42 | A conforming component keeps its StartAsync task incomplete until required priming finishes; while it is incomplete, the coordinator does not advance or publish startup completion. | HC-APP-02 |
| HC-AT-43 | The future coordinator invokes each intended instance at most once, including failures, cancellation, and alias registrations. | HC-APP-07 |
| HC-AT-44 | All HTTP phase cases in section 10 produce the defined 200/500 results from one snapshot. | HC-HTTP-02; HC-HTTP-03 |
| HC-AT-45 | /_health descendants never reach business logic; /_healthcare remains unrelated; unknown/unsupported requests stay in the branch. | HC-HTTP-01; HC-HTTP-04 |
| HC-AT-46 | GET/HEAD agree on status, disabled details returns 404, and report caching cannot create apparent fresh health. | HC-HTTP-04; HC-HTTP-05 |
| HC-AT-47 | Oversize details fail before HTTP headers or pipe success framing; GET/HEAD decide the same error status, without omitting critical state or mutating health. | HC-HTTP-05; HC-PIPE-03 |
| HC-AT-48 | HTTP and pipe select equivalent results for the same snapshot/view. | HC-PIPE-01 |
| HC-AT-49 | Pipe client handles split reads and exits zero only for a complete valid selected view; unhealthy, malformed, contradictory, missing, and timed-out responses fail the probe. | HC-PIPE-03; HC-PIPE-04 |
| HC-AT-50 | Slow/many pipe clients respect capacity and time bounds without blocking health commits or accumulating report history. | HC-PIPE-05 |
| HC-AT-51 | Linux integration uses the supported matched pipe client and real endpoint/permission context; no native Kubernetes pipe field is assumed. | HC-PIPE-01; HC-PIPE-02 |
| HC-AT-52 | Shutdown makes readiness false, rejects new registrations/activation, accepts final producer reports, and drains owner work and transport handlers before disposing their enclosing scopes/root. | HC-LIFE-01 |
| HC-AT-53 | Repeated/canceled shutdown waits neither reopen the service nor certify nonexistent settlement. | HC-LIFE-02 |
| HC-AT-54 | Watchdog cancellation or wrapper timeout alone produces no Healthcheck state mutation and no early Heartbeat overlap permission. | HC-WDG-01; HC-WDG-02 |
| HC-AT-55 | Environment precedence, blank/invalid inputs, bounds, immutable publication, and failed initialization follow the core rules. | HC-CFG-01; HC-CFG-02 |
| HC-AT-56 | Monitor capacity rejection leaves existing contributions unchanged and creates no partially registered monitor. | HC-OWN-05; HC-CFG-02 |
| HC-AT-57 | Package/architecture checks prove generic core independence from Heartbeat, ASP.NET, DI-container types, and unapproved runtime packages. | HC-SCP-03; HC-ENG-01 |
| HC-AT-58 | All six verification families and historical contract replay provide the required release evidence on claimed platforms. | HC-ENG-02; HC-ENG-03 |
| HC-AT-59 | Binding-created monitors retire on unregister, automatic retirement, owner closure, and shutdown; external monitors remain caller-owned. | HC-HB-02 |

Acceptance cases for IStartable validate the eventual application integration; they do not require building that separate coordinator inside Healthcheck.

## 16. Explicit first-draft choices

The user decisions in the decision table are incorporated. The following choices make the document concrete and remain reviewable before public API/schema freeze:

| Choice | Position used in this draft |
| --- | --- |
| Initial unevaluated state | Pending, with no automatic failure flag; application readiness remains gated separately |
| Criticality updates | Fixed per instance; model availability and refresh with separate monitors |
| Factory catalog | Explicit condition, consecutive failure, numeric threshold, first-success availability, and asynchronous evaluation |
| Disposal API | Awaitable settlement with immediate withdrawal; common IAsyncDisposable handle |
| Weak-reference loss | Remove the unavailable monitor without a substitute critical condition |
| Collection | Explicit interval, completion-based delay, one active collection per monitor, joining/coalescing, default global concurrency four |
| Observation ordering | Atomic commits; reject obsolete asynchronous results after a newer direct observation |
| HTTP views | Aggregate /_health, readiness /_health/ready, optional /_health/details; chosen 200/500 mapping preserved |
| Detailed reporting | Disabled by default; adapter/host controls access; bounded complete payload or explicit reporting error |
| Pipe technology | Matched local .NET named-pipe server/client; exec probe integration on Kubernetes |
| Pipe protocol | Versioned length-prefixed UTF-8 JSON, one request/response per connection, explicit view selection and client exit mapping |
| Observation retention | Current state, lifetime counters, latest bounded diagnostics; no required unbounded historical transport |
| Heartbeat monitoring | Optional absent policy; configured policy must bind before activation; immediate in-process fact commits |
| Counter representation | Saturating 64-bit lifetime counters and non-reusing increasing revisions as decimal strings; bounded aggregate counts as JSON numbers |
| Configuration | Proposed typed options, environment names, and limits in section 14 |

These choices are not claims that platform APIs prescribe the architecture. Any change before publication should update the affected behavior, examples, and acceptance cases together.

## 17. Follow-on work and references

Implement Healthcheck's policy, lifetime, snapshot, and reporting contracts before completing the Heartbeat monitoring adapter. Use the agreed core library engineering baseline and develop runnable business examples after the core behavior exists.

The separate application lifecycle definition must implement the IStartable notes and the required startup/shutdown ordering. The separate watchdog definition must decide its public timeout/abort contract and isolation assumptions.

Heartbeat v0.2 remains the scheduling baseline. Its statements deferring watchdog policy to this discussion should be reconciled in the next requested Heartbeat revision: watchdog execution control is separate; Healthcheck owns health interpretation only from explicitly reported ordinary observations. Its no-overlap and owner-settlement guarantees remain essential.

### Technical sources

Project decisions come from the CIS discussion and the named core/Heartbeat baselines. The primary sources below support the limited platform facts cited in this document; they do not dictate the project-specific defaults.

[^core]: **Core Library Implementation Guide**, specification 1.1, 8 October 2026, `core-library-implementation-guide-v1.md`. Sections 3–8 govern dependencies, framework/configuration and coding conventions; the compatibility and verification sections govern publication and all six verification families.

[^heartbeat]: **CIS Heartbeat Library Specification**, version 0.2, `CIS_Heartbeat_Library_Specification_v0_2.md`. In particular, application-owned lifecycle, owner context/leases, running-work shutdown, outcome reporting, and Healthcheck integration.

[^hosting]: Microsoft, [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0). Hosted-service startup ordering is relevant when arranging an available health listener before application priming.

[^middleware]: Microsoft, [ASP.NET Core middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0). Middleware order, branching, and short-circuiting support the reserved health-branch integration.

[^kubernetes]: Kubernetes, [Liveness, Readiness, and Startup Probes](https://kubernetes.io/docs/concepts/workloads/pods/probes/). Probe mechanisms, exec exit status, startup gating, and supervisor effects. HTTP 200/500 in this definition is the user's chosen mapping.

[^named-pipes]: Microsoft, [Use named pipes for network interprocess communication](https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication). The relevant cross-platform fact is that the Linux implementation of .NET named-pipe APIs uses Unix domain sockets.

[^cancellation]: Microsoft, [Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads). Cancellation requests are cooperative and distinct from actual task completion.

[^thread-abort]: Microsoft, [Thread.Abort is obsolete](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/thread-abort-obsolete). Modern .NET does not support aborting a thread through this API.

[^destroying-threads]: Microsoft, [Destroying threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/destroying-threads). Cooperative cancellation and a separate-process boundary for forcible termination on modern .NET.
