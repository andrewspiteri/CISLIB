# Heartbeat technical intent — CIS-0006

**Status:** Implemented development design under authorized CIS-0006; scoped qualification is recorded separately.

**Date:** 16 November 2026.
**Scope:** Recurring asynchronous work in the existing `CIS.Core` package and assembly.

The [actual outcome authorization](../../docs/changes/CIS-0006/authorization.json) records the user-approved implementation scope and shared finite allocation. The [verification record](../../docs/changes/CIS-0006/verification.md) identifies actual checks and remaining qualification. This document grants no additional execution capacity and is neither a publication nor an independent pass/completion decision.

## Source precedence and reconciliation

The [Heartbeat v0.2 specification](../healthcheck/sources/CIS_Heartbeat_Library_Specification_v0_2.md) supplies product requirements; its embedded handoff instructions do not authorize execution or approval. [Current decisions](../decisions.md), [repository guidance](../../AGENTS.md) and the [dependency policy](../../docs/architecture/csharp-dependency-policy.md) supersede conflicting historical shared-baseline text.

| Source conflict | Implemented reconciliation |
| --- | --- |
| `EnviormentsOptions.Current` and a shared initialization facade | Preserve actual `CIS.EnvironmentOptions` and its first-access behavior. Its readonly Heartbeat reference contains mutable module setup; the runtime copies validated effective settings. No Current, Initialize, reset or competing settings singleton. |
| Zero Core package references and no ASP.NET shared framework | Preserve the approved Microsoft dependency closure and `Microsoft.AspNetCore.App`. Scheduler mechanisms remain independent of host/DI types; adapters have distinct responsibilities in the same assembly. |
| Healthcheck described as a future design | Reuse its execution binder and extend detached facts additively. No second health subsystem. |
| Historical guide/task/release instructions | Preserve current .NET 10/MSTest conventions, actual authorization and existing owner accounting. Historical campaigns and completed owners remain closed. |

The source's A01–A05 remain explicit assumptions rather than newly asserted historical approvals:

| Assumption | Retained implementation interpretation |
| --- | --- |
| A01 | Completion-based fixed delay; optional InitialDelay defaults to zero. |
| A02 | A running condition finishes at shutdown; its callback requires a separate winning start gate. |
| A03 | Escaping subscriber disposal exceptions produce actual primary/secondary facts and retire the registration. |
| A04 | Boolean/skip/acknowledged-cancellation facts, real-fault precedence, nonrecursive awaited handling and actual-completion drains. |
| A05 | Effective MaxConcurrent range is 1–16, including explicit and environment values. |

## Public organization and construction

Handwritten source lives under `implementations/csharp/src/CIS.Core/Heartbeat`, in `CIS.Heartbeat`, `CIS.Heartbeat.Hosting` and `CIS.Heartbeat.Healthcheck`. One engine serves an application/container lifetime; owner facades never create independent schedulers.

| Implemented type | Responsibility |
| --- | --- |
| `IHeartbeatSubscriber` | `Task<bool> OnElapsedAsync(CancellationToken)` and `Task OnErrorAsync(Exception)`. Returned tasks include child work and subscriber cleanup using protected dependencies. |
| `IHeartbeatService` | `void Register(IHeartbeatSubscriber, HeartbeatRegistrationOptions, Func<CancellationToken, Task<bool>>? condition = null)` and `Task UnregisterAsync(IHeartbeatSubscriber, CancellationToken waitCancellation = default)`. |
| `HeartbeatRegistrationOptions` | Sealed named-init schedule: Interval defaults to 10 ms, InitialDelay to zero, Name is optional. Concrete monitoring configuration is a separate adapter argument. |
| `HeartbeatOptions` | Mutable application setup with MaxConcurrent; runtime configuration consumes a validated detached copy. |
| `HeartbeatRuntime` | Inert construction, owner creation, terminal StopAsync/disposal and detached InfrastructureFailure awaitable. Activation is internal to hosting. |
| `HeartbeatOwner` | Actual cooperative lifetime boundary; Service forwards to the shared engine. CloseAsync drains its registrations without disposing subscribers. |
| `HeartbeatScope` | Infrastructure wrapper around one actual AsyncServiceScope and HeartbeatOwner. Services resolves the real objects; Service registers them under that owner. |
| `HeartbeatHealthcheckOptions` | Selects one existing suitable monitor or typed policy; factory-created policies use MonitorFactory. Healthcheck interprets the policy. |

These are development contracts in the established assembly. Preserve their behavior and existing module contracts; publication qualification is separate. Public infrastructure methods do not add consumer start/stop/status methods to `IHeartbeatService`. Type-created subscribers, pause/priority, automatic retries, distributed jobs and a generic startup-provider framework remain outside this module.

## Configuration and safe timing

The computed default is `Math.Min(16, Math.Max(1, io))` using the completion-port maximum from `ThreadPool.GetMaxThreads(out _, out int io)`. Resolve a nonblank `CIS_HEARTBEAT_MAX_CONCURRENT` first, explicit module setup second, then that default. Read the named variable once during runtime configuration; parse invariant signed decimal integers and validate 1–16. Blank input is absent. Invalid effective input raises `CoreConfigurationException` with `Heartbeat.InvalidValue` or `Heartbeat.OutOfRange`; no usable runtime settings are published.

Construction and setup access start no scheduler, subscriber, timer or external resource. Later setup/environment changes do not resize an existing runtime. The readonly module property uses the actual [EnvironmentOptions](../../implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs) mechanism. Intentional architecture assertions include the new property without changing unrelated container behavior.

The documented maximum interval/initial delay is `TimeSpan.FromMilliseconds(uint.MaxValue - 1)`, approximately 49.7 days. Interval accepts one TimeSpan tick through that bound; InitialDelay accepts zero through it. Unsupported durations throw `ArgumentOutOfRangeException`, including duplicates. Validate a positive clock frequency and safely representable conversion before mutation. Widened arithmetic and upward rounding prevent deliberate early eligibility. Store an origin and elapsed delay; UTC never drives eligibility.

For pre-readiness registrations, first origin is activation; while running, it is successful first registration. Subsequent origin is full settlement with the captured interval. Missed ticks do not accumulate. One controllable TimeProvider and next-wake timer serve the engine; no task or periodic timer exists per idle registration.

Clock failures surface through detached infrastructure reporting, close global acceptance/start gates and preserve tracked cleanup/settlement. UTC correlation overflow does not strand otherwise valid monotonic admission. Settlement captures its UTC event instant once, avoiding inconsistent event/settlement timestamps after a one-shot clock fault.

## Identity, weak idle ownership and binding

`ConditionalWeakTable<IHeartbeatSubscriber, HeartbeatBinding>` supplies reference identity and target-dependent condition/cleanup bindings. Overridden Equals/GetHashCode never determine registration. The scheduler list stores weak target/owner references, copied scalar schedule/identity metadata and mechanical state. It does not retain subscriber-bound conditions, arbitrary factories, user closures or the registering ExecutionContext while idle.

The concrete Core reporting binding is retained independently so collection cannot discard the only way to retire its monitoring association. Its graph includes Core state and a Core monitor handle; it is not assumed scalar-only. The public adapter accepts only appropriate Core-created monitors/factories, and graph tests exercise surviving external monitors. Target-dependent setup is not retained by idle entries.

Actual application/scope owners keep subscribers and their owner boundaries alive while recurrence is wanted. Accepted operations temporarily retain the target, conditional binding and counted owner hold strongly. Settlement clears those temporary references. Conditional values preserve bound/capturing conditions while the key lives; independently weak-referencing the condition would incorrectly lose live behavior.

Argument validation precedes mutation. Global lifecycle validation and duplicate detection occur under the coordination gate. Valid duplicates retain every original option, condition, owner, monitor and timing origin, including individual drain. Shutdown/disposal rejection still takes precedence. A provisional first registration coordinates concurrent duplicates; binding runs outside the scheduler gate and commits only if its owner/application remains eligible. Binding failure or a competing cutoff rolls back and tracks retirement cleanup.

## Owner protocol and atomic admission

The engine's coordination gate serializes owner closure and admission. An open owner, live target and conditional binding are acquired together with the counted lifetime hold, instance reservation and accepted-operation tracking. Failure leaves no queued execution or hold. A closing owner rejects new acceptance while honoring already acquired holds.

Ordinary unregister/owner closure drains accepted queued work as well as running work. Global shutdown overrides that selection and suppresses unstarted phases. Unavailable weak targets/owners retire without replacement or invocation. An escaping ObjectDisposedException closes recurrence immediately; that exception alone does not prove the subscriber cannot safely handle a dependency failure under its existing hold.

Manually owned objects use runtime.CreateOwner(), retain that owner and await CloseAsync before disposing protected objects/dependencies. Scoped objects resolve from HeartbeatScope.Services and register through the same wrapper's Service. Plain DI scope teardown is too late to establish the barrier. No scope is inferred from an arbitrary object, no subscriber is replaced and no root/unrelated scope is used for scoped resolution. Shorter object lifetimes require coordinated unregistration/ownership before destructive disposal.

A managed reference, GC pin or ObjectName is not disposal protection. The actual owner must honor this protocol; independent destructive disposal remains outside its guarantee. Reporting retirement is claimed once, runs outside scheduler locks and is joined before registration removal/global barrier completion.

## Scheduling, dispatch and user phases

One gate coordinates mechanical state, with FIFO accepted pending work and stable registration ordering for ties. Each registration has at most one accepted operation; storage is bounded by registration metadata plus MaxConcurrent dispatched work. Reserve a slot before executor posting and retain it across all awaits. Reuse each released slot without a batch barrier; newer recurrence cannot repeatedly overtake older pending work.

The production executor posts asynchronously without carrying request ExecutionContext. A synchronous user prologue cannot occupy the scheduling control path. A wrapper awaits publication of its dispatch fact before attempting a user phase, even if an executor starts it before TryPost returns. Posted wrappers remain tracked through settlement.

Condition and callback have distinct coordinated start gates. Ordinary closure preserves accepted work's rights; global stopping or known unusability prevents new phases. User code executes outside the shared lock. False condition skips; callback false is a negative result. Neither invokes OnError. Unexpected escaping exceptions and null user tasks reach one error boundary; eligible OnError is awaited once and a secondary handler fault cannot recurse or erase the original failure.

The initial production execution token is uncanceled. Stop/unregister waits do not cancel it. Expected cancellation requires a canceled known execution token acknowledged by the thrown cancellation exception; unexplained cancellation is failure, and a requested token does not replace a normally returned boolean.

Withdrawn unstarted cleanup begins on a separately tracked infrastructure continuation rather than the scheduler pump. A blocking cleanup prologue therefore cannot block unrelated registration retirement. This does not make cleanup unowned: shutdown still joins it.

## Settlement, unregister and terminal shutdown

Wait for condition/callback, required error handling and failure-producing owned cleanup before selecting one final local result. A real cleanup failure overrides success/skip/expected cancellation while preserving actual raw results and cancellation context. Entirely unstarted suppression has lifecycle disposition and no fabricated callback Outcome, including when withdrawal cleanup fails.

The current adapter synchronously detaches/reports Outcome while the execution owns its target, instance reservation, slot and owner hold. Await owner-dependent FlushAsync before RunAsync ends. Actual task completion then permits coordinated reference/hold/slot release and recurrence bookkeeping. Publish the detached Settled fact outside the scheduler gate; ReportingSettlement prevents another pump from retiring that binding or admitting recurrence before the publication ends. No owner-dependent access is reacquired afterward.

Unregister establishes its cutoff before applying caller wait cancellation. Joined callers share the underlying drain; unknown/completed unregister is a successful no-op, even with a canceled token. Identity remains reserved until actual removal, so early re-registration is a duplicate. Successful sequential reuse creates a fresh registration/monitor lifetime. Neither unregister nor owner closure disposes subscribers.

Global shutdown closes registration, acceptance and condition/callback starts, wakes scheduling, suppresses pending/unstarted work and joins every posted wrapper. Running phases, recovery, cleanup and handoffs finish. Created-to-Stopping is supported without readiness; stop/dispose join one terminal drain and never reopen the engine. Caller cancellation changes only that wait, not the real barrier or disposal permission. An infrastructure error preventing proof of resource settlement must remain visible and cannot be labeled a successful graceful drain.

The execution-scoped guard rejects a direct drain containing its own operation. Arbitrary application/capacity-dependent cycles remain caller responsibilities; no general cycle detector is claimed. See the existing [HealthCollectionExecution precedent](../../implementations/csharp/src/CIS.Core/Healthcheck/Internal/Collection/HealthCollectionExecution.cs).

## Supported host and scope coordination

`AddCisHeartbeat` installs the engine and a narrow Generic Host lifecycle adapter. Startup validates that its hosted service is last and ServicesStopConcurrently is false. It arms synchronous ApplicationStarted/ApplicationStopping callbacks without awaiting ApplicationStarted from hosted startup or creating async-void work. Required initialization for registered work must finish by ApplicationStarted. No later-readiness bridge or generic startup-provider framework is implemented here.

ApplicationStopping establishes the cutoff. StoppingAsync and StopAsync join the actual barrier independently of an expired host-budget token, before ordered dependency teardown. The application must keep callback dependencies usable in stopping handlers regardless of whether a signal or explicit host stop raises that notification first. The adapter cannot repair arbitrary early disposal by another handler.

The application/root owner stops Heartbeat before disposing the provider, including startup-error paths. A canceled caller wait cannot authorize root disposal. HeartbeatScope.DisposeAsync closes/drains its actual owner before beginning underlying AsyncServiceScope disposal; repeated calls join one disposal task. A stuck subscriber may keep orderly shutdown incomplete.

## Monitoring and verification boundaries

The real [HealthExecutionBinder](../../implementations/csharp/src/CIS.Core/Healthcheck/Execution/HealthExecutionBinder.cs) owns monitor binding and policy interpretation. HeartbeatHealthcheckOptions selects one existing suitable monitor or typed consecutive-failure/availability policy. Typed policies supply MonitorFactory; availability also declares SuccessPublishesUsableData. Duplicates never bind again. No Heartbeat failure counters, health snapshots, GetStatus, watchdog or deadline policy is added.

The additive receiver supplies Dispatched and Suppressed facts plus optional dispatch, suppression, raw boolean and expected-cancellation context. Existing enum values, phase/outcome vocabulary and schema version remain intact. Real compatibility evidence remains distinct from an additive design statement. See [integration details](healthcheck-integration.md) and [the current fact contract](../healthcheck/execution-facts.md).

Tests use controlled time/executor/task/owner boundaries and complete weak-reference graphs, including real surviving external monitors and detached exception observations. [Acceptance mapping](acceptance.md) preserves every source case; [actual verification](../../docs/changes/CIS-0006/verification.md) states which checks ran and their limits. Native host/package/business/compatibility and release claims require their actual observations. Remaining execution uses the same approved finite allocation; completed owners and their costs, attempts, approvals and evidence remain unchanged.
