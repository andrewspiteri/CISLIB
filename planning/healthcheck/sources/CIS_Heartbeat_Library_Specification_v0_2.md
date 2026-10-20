# CIS Heartbeat Library Specification

**Revision:** 0.2 — updated design review  
**Date:** 10 October 2026  
**Supersedes:** Heartbeat review draft 0.1  
**Shared baseline:** Core Library Implementation Guide, specification 1.1, 8 October 2026

Heartbeat is a small C# library for recurring asynchronous work. An application registers a class instance, a recurring interval and an optional condition. The class instance is the handle. The application and its DI integration own the service lifecycle; callbacks become eligible only after the application's required initialization. Work for the same instance never overlaps. A shared concurrency limit controls dispatch across instances.

During application shutdown, Heartbeat stops starting work and waits for work already running, including its asynchronous error handling and cleanup. The owning DI integration must complete that barrier before disposing dependencies used by the work. Idle registrations use weak ownership so registration does not give a transient or scoped object an application lifetime.

Heartbeat reports execution facts to the Healthcheck module. Healthcheck owns monitoring policies, failure allowances, health state, observations and supervisor integration. **Define Healthcheck before implementing the Heartbeat monitoring integration.** This revision specifies that boundary without inventing the future module's public API.

## Review status and reading guide

This is a specification update, not an implementation or a report of passing runtime tests. No further product decision blocks this revision. The decisions supplied during review are incorporated below; a small number of retained assumptions are visible in section 14. The illustrative API remains subject to the shared core conventions and the Healthcheck integration contract before first publication.

Requirements retain topic-based IDs from draft 0.1 where possible. **R** denotes an agreed requirement or a necessary consequence of it. **A** denotes a concrete assumption retained for this review, rather than a choice explicitly approved in a separate answer. MUST and MUST NOT define the intended contract within that status. Acceptance cases inherit the status of their referenced requirements.

The principal changes are application-owned lifecycle, successful duplicate registration, the selected concurrency control, running-work-only shutdown, owner-bound DI lifetimes, weak idle ownership, exception-only `OnError`, and Healthcheck ownership of monitoring. GmCore is an example for this new library, not a compatibility baseline.

| Sections | Contents |
| --- | --- |
| 1–3 | Scope, ownership terms and registration |
| 4–5 | Timing, conditions, asynchronous execution and capacity |
| 6–8 | Application lifecycle, shutdown and dependency lifetimes |
| 9–10 | Execution outcomes, error handling and Healthcheck integration |
| 11–12 | Illustrative API and inherited engineering requirements |
| 13–15 | Acceptance criteria, decision record and source references |

## 1 Purpose and responsibility boundaries

**HB-SCP-01 · R · Small recurring service.** Heartbeat MUST support asynchronous recurring callbacks, an optional condition, per-instance exclusivity, bounded dispatch, registration changes and coordinated application shutdown. Its normal consumer API MUST remain focused on registering and unregistering work.

**HB-SCP-02 · R · Application policy ownership.** Heartbeat MUST NOT infer or enforce maximum usable-result age, financial exposure, originating request time, end-to-end SLA, business expiry, business retries, rollback or compensation. Scheduling an invocation MUST NOT create or reset an application deadline.

A queued FX request may already have consumed four seconds of a five-second business budget. The subscriber or a separate application component interprets the original timestamp and the remaining budget, including its own queueing, dependency calls and retries. Heartbeat's interval and execution timings do not replace that accounting. A callback may process several requests with different budgets.

| Responsibility | Owner |
| --- | --- |
| Recurring eligibility, admission, dispatch capacity, instance exclusivity and drain tracking | Heartbeat |
| Callback logic, child tasks, business deadlines, result validity and recovery | Subscriber or application |
| Startup readiness and orderly shutdown/disposal coordination | Application hosting and DI integration |
| Ownership of a subscriber and the dependencies it uses | Its actual lifetime owner and DI scope |
| Monitor selection from a policy, failure allowances, counters, health interpretation and observations | Healthcheck |
| Watchdog policy and any future cancellation coordination | To be defined with Healthcheck and the application |
| Kubernetes probe mapping, endpoints and supervisor actions | Healthcheck integrations |

**HB-SCP-03 · R · Dependency boundaries.** The scheduling core MUST remain container- and host-neutral and obey the shared core dependency rules. Host, DI and concrete Healthcheck integrations MUST be separated from the scheduling mechanism. An integrated application MUST supply the owner and lifecycle coordination required by this specification; dependency independence does not remove those responsibilities.

**HB-SCP-04 · R · Initial scope.** The initial surface MUST NOT inherit unrequested GmCore features. Type-based subscriber creation, public priority or pause APIs, persistent jobs, distributed locks, internal automatic retries and a Heartbeat-owned watchdog are outside this revision's implementation scope. Future features must preserve the published ownership and compatibility contracts.

## 2 Terms and execution ownership

| Term | Meaning |
| --- | --- |
| Subscriber | The actual class instance whose callback is registered. |
| Registration | One recurring schedule, condition, diagnostic metadata and owner association for that instance. |
| Owner | The scope or other lifetime boundary responsible for the subscriber and its dependencies. |
| Eligible occurrence | A schedule becoming due; eligibility alone does not start user code. |
| Accepted execution | An occurrence committed to Heartbeat tracking, with an instance reservation, strong target reference and a valid owner lifetime hold. It may wait for dispatch capacity. |
| Dispatched execution | Accepted work with a reserved capacity slot that has been posted to an executor. Its condition or callback may not yet have started. |
| Started work | A condition or callback that has crossed its coordinated start gate. Merely posting a wrapper is not starting user work. |
| Execution | One condition evaluation, its callback when permitted, associated error handling and owned cleanup. “Attempt” in earlier review text refers to this same unit. |
| Settlement | All work owned by that execution has actually ended; its final bookkeeping is complete and no later access to the subscriber or its dependencies remains possible. |
| Cutoff | The coordinated boundary closing new acceptance or new user-code starts for the affected registration, owner or application. |
| Drain | Waiting for the work selected by the relevant cutoff rule to settle. Ordinary unregistration and application shutdown select different work. |
| Lifetime hold | An integration mechanism that makes the owner delay destructive disposal while accepted work can use its dependencies. It is more than a managed reference. |

**HB-OWN-01 · R · Returned tasks define owned work.** A condition, callback or error handler MUST return a task representing all work it starts that can use the subscriber or its dependencies. Its child operations and cleanup MUST be awaited or otherwise included in that task. Detached work cannot rely on Heartbeat's non-overlap or disposal guarantees. `async void` is not an accepted callback or error-handling contract. Task-returning methods provide an awaitable completion boundary; `async void` does not.[^async]

**HB-OWN-02 · R · Track before dispatch.** Acceptance, owner-hold acquisition, per-instance reservation and cutoff checks MUST form one coordinated admission operation. Heartbeat MUST establish tracking before any dispatch can execute user code. Failed admission MUST leave no partial registration execution, capacity reservation or owner hold.

**HB-OWN-03 · R · Observation is not settlement.** A timeout observation, cancellation request, failed health state or canceled caller wait MUST NOT release execution ownership. The actual tasks and owned cleanup must end first. This remains true when external supervision declares the work overdue.

**HB-OWN-04 · R · One final execution fact.** Each accepted execution MUST have a unique correlation identity within its registration and one final local outcome record. A suppressed, unstarted execution has a lifecycle disposition, not a fabricated callback result. Delivery reliability and historical storage of those facts belong to the monitoring integration, as defined in section 10.

## 3 Registration and instance reuse

### 3.1 Identity and duplicate registration

**HB-REG-01 · R · Instance handle.** Registration and unregistration MUST use actual object reference identity. Names, types, overridden `Equals` implementations and user-defined hash codes MUST NOT determine identity. The API MUST NOT require a returned wrapper, numeric identifier or string key as the registration handle.

**HB-REG-02 · R · Optional names.** A diagnostic name MAY be supplied. Names MUST be optional and need not be unique. Distinct objects of the same class or with the same name remain separate registrations. Internal correlation identifiers do not become alternative consumer handles.

**HB-REG-03 · R · Successful duplicate no-op.** There MUST be at most one registration for an instance in the application's shared scheduler. A valid duplicate `Register` MUST succeed without changing anything, including while that registration is individually draining. It MUST NOT replace the interval, condition, name, policy association, owner or timing origin; reset state; create a second monitor; or schedule more work. Normal return from the illustrative `void Register` means success. Concurrent first registrations MUST have one atomic winner.

A duplicate during draining does not undo unregistration and does not queue a future re-registration. Global lifecycle rejection still applies after application shutdown has closed registration.

### 3.2 Validation and changes

**HB-REG-04 · R · Validate before mutation.** A null subscriber or required options object MUST be rejected. `Interval` MUST be positive; `InitialDelay`, if retained, MUST be nonnegative. Options MUST be validated without partial registration. The duration implementation MUST safely support accepted values without arithmetic overflow or silently wrapping an eligibility time. The published API must document any maximum accepted duration and reject values beyond it with `ArgumentOutOfRangeException`.

Argument validation applies to duplicates as well. Within valid calls, a closed application lifecycle takes precedence over duplicate success. Validation MUST NOT evaluate the condition or invoke subscriber code.

**HB-REG-05 · R · Stable configuration.** Registration MUST capture an immutable copy of its effective options and bindings. Later caller-side changes MUST NOT mutate the active schedule. Changes require a completed unregistration followed by a new registration.

**HB-REG-06 · R · Registration states.** Registration MUST be allowed before the application is ready and while it is running. It MUST be rejected after the global shutdown cutoff. Registration MUST NOT execute a condition, callback or `OnError` inline. While running, asynchronously dispatched work can begin promptly, so the subscriber must be fully initialized when registered.

### 3.3 Ordinary unregistration

**HB-REG-07 · R · Drain accepted work.** Ordinary `UnregisterAsync` MUST atomically close that instance's new acceptance and await its already accepted queued or running execution, including error handling and owned cleanup. It MUST retain the instance reservation until settlement. Successful completion means Heartbeat cannot later invoke or use that registration. Unregistration MUST NOT dispose the subscriber or its container.

This is the ordinary rule while the application remains running. Application shutdown overrides it by suppressing unstarted work under section 7. Automatic retirement of an unusable target also prevents further invocations rather than invoking the target merely to empty a queue.

**HB-REG-08 · R · Idempotency and sequential reuse.** Unregistering an unknown or already removed instance MUST succeed with no work. Concurrent unregister requests MUST join the same drain. An instance may be reused only after that drain has actually completed. The operations are sequential even though waiting is asynchronous:

```csharp
await heartbeat.UnregisterAsync(poller);
heartbeat.Register(poller, revisedOptions);
```

A canceled wait does not prove unregistration is complete. Re-registering before completion remains a duplicate no-op. Reuse creates a new registration lifetime; it does not silently restore the previous schedule or force a Healthcheck history reset.

**HB-REG-09 · R · Wait cancellation.** An unregister request MUST establish its cutoff even if its wait token is already canceled. That token cancels only that caller's wait, not the underlying drain or execution. Other waiters must remain able to observe the shared completion. A completed unknown-instance no-op has no pending work to cancel.

The non-overlap guarantee covers invocations owned by one application's shared scheduler. An application must coordinate direct calls or registration of the same object with another independent scheduler if those paths could overlap.

## 4 Timing and conditions

### 4.1 Recurring interval and first eligibility

**HB-SCH-01 · A · Completion-based fixed delay.** This revision retains completion-based delay: the next eligibility is the previous execution's full settlement time plus `Interval`. Success, callback `false`, a false condition, cancellation and failure use the same delay. There is no accumulation of missed ticks or catch-up executions. This interval interpretation remains a visible review assumption from D01.

**HB-SCH-05 · R · Default interval.** The default recurring `Interval` MUST be **10 milliseconds**. It is configurable per registration and is distinct from the global `MaxConcurrent` setting. The default does not promise a 10 ms operating-system timer resolution or a 10 ms maximum dispatch delay.

For example, with a 10 ms interval and 30 ms of total execution work, idealized starts are 0, 40 and 80 ms if capacity is immediately available. Waiting for capacity delays the start further. A slow error handler or owned cleanup remains part of the execution before the next delay begins.

**HB-SCH-02 · A · Optional initial delay.** This revision retains `InitialDelay`, defaulting to zero. For registrations made before readiness, first eligibility is readiness plus `InitialDelay`. For a registration added while running, it is successful registration time plus `InitialDelay`. Duplicate registration does not restart that delay. Actual dispatch remains subject to capacity and lifecycle gates.

**HB-SCH-03 · R · Eligibility guarantee.** Heartbeat MUST NOT deliberately start an execution before its eligibility time. While running, valid eligible registrations MUST continue to be considered fairly. Resource pressure, long executions and process scheduling may delay actual starts; no hard real-time lateness guarantee is made.

**HB-SCH-04 · R · Elapsed-time clock.** Interval arithmetic and durations MUST use a monotonic elapsed-time source. UTC timestamps may accompany reports for correlation but MUST NOT drive interval calculations. The clock and dispatch seams MUST be controllable in tests. The inherited .NET baseline provides `TimeProvider` as a built-in implementation option.[^time]

### 4.2 Optional condition

**HB-CND-01 · R · Conditional invocation.** Without a condition, accepted work proceeds to its callback when lifecycle gates permit. With a condition, Heartbeat MUST await it and invoke the callback only if it returns `true` and the callback-start gate still permits invocation. A condition runs at most once per accepted execution.

**HB-CND-02 · R · Same exclusive execution.** The condition, callback, error handling and cleanup MUST share one instance reservation and one dispatched capacity slot. A second condition for the same instance MUST NOT overlap any phase of the first execution.

**HB-CND-03 · A · False condition.** The condition uses an awaitable `Task<bool>` contract. A false condition is `Skipped`; the callback is not invoked and the next eligibility follows the ordinary interval. The factual skip is reported to Healthcheck, which owns its policy interpretation. A false condition does not call `OnError`.

**HB-CND-04 · A · Condition exception.** An unhandled condition exception prevents the callback, produces a failed execution fact with the condition phase, and invokes eligible exception handling under section 9. An expected acknowledged cancellation is handled separately. Heartbeat MUST NOT maintain failure streaks or allowance arithmetic for these outcomes.

## 5 Asynchronous execution and shared capacity

### 5.1 Execution guarantees

**HB-EXE-01 · R · Asynchronous dispatch.** Conditions and callbacks MUST run through an asynchronous executor path, never inline in registration or lifecycle coordination. Heartbeat MUST await the full returned task, including an asynchronous continuation, rather than treating completion of an outer dispatch wrapper as completion of the work.

**HB-EXE-02 · R · No overlap for an instance.** Instance exclusivity MUST begin at acceptance and remain through queueing, condition evaluation, callback execution, awaited `OnError`, owned cleanup and final settlement. Cancellation, external timeout observations, unregister and shutdown MUST NOT release it early.

**HB-EXE-03 · R · Independent scheduler progress.** The scheduler MUST NOT run user code under a shared scheduler lock or on the scheduling control path. A callback's synchronous prologue or blocking code MUST NOT prevent unrelated registrations from being considered, subject to the configured capacity and runtime resources. User awaits must not hold shared scheduler locks.

**HB-EXE-04 · R · Failure isolation.** Exceptions from owned user work MUST be observed. An execution failure MUST NOT terminate scheduling for unrelated instances or skip necessary cleanup. Scheduler and integration faults MUST be surfaced through the appropriate infrastructure reporting path rather than silently appearing as successful operation.

**HB-EXE-05 · R · Atomic settlement.** Final outcome creation, scheduling state, release of ownership and recurrence decisions MUST be coordinated. An instance MUST NOT become available for a later execution while work from the previous one can still use it. A completion racing with a cutoff must not create another occurrence after that cutoff.

**HB-EXE-06 · R · No implicit policy actions.** Heartbeat MUST NOT retry within an execution, apply business backoff, choose rollback or stop scheduling because a failure allowance was exceeded. While the registration remains valid, a later execution follows its normal interval. Explicit unregistration, shutdown or unusable-target retirement still stops recurrence.

**HB-EXE-07 · R · One accepted execution per instance.** At most one execution may be accepted for an instance, including queued work. Elapsed intervals while that execution is queued or active MUST NOT create additional queued executions.

### 5.2 `MaxConcurrent` configuration

**HB-CAP-01 · R · Application-wide dispatch bound.** All registrations in the shared application scheduler MUST use one `MaxConcurrent` bound. The limit applies at startup and throughout normal operation, including when many registrations have equal intervals. It MUST be applied before work is posted to the executor, rather than posting unlimited tasks that wait for a semaphore inside their delegates.

**HB-CAP-02 · R · Selected default.** Resolve the default once using the completion-port output of `ThreadPool.GetMaxThreads`:

```csharp
ThreadPool.GetMaxThreads(out _, out int maxIoCompletionPorts);

int defaultMaxConcurrent =
    Math.Min(16, Math.Max(1, maxIoCompletionPorts));
```

This is the selected sizing rule. `Environment.ProcessorCount` and `GetAvailableThreads` MUST NOT replace it. The second output of `GetMaxThreads` describes the configured maximum for asynchronous I/O threads; it is not a measurement of currently free worker capacity. The formula is a Heartbeat policy choice, not a .NET guarantee that that many CPU-bound callbacks can execute efficiently.[^maxthreads]

**HB-CAP-03 · R · Environment option and override.** The setting name MUST be `MaxConcurrent`. It MUST be resolved through the shared **`EnviormentsOptions`** initialization and immutable snapshot, with the exact optional override **`CIS_HEARTBEAT_MAX_CONCURRENT`**. Resolution order is:

1. The nonblank `CIS_HEARTBEAT_MAX_CONCURRENT` process environment variable.
2. Explicit application setup for the Heartbeat environment option.
3. The computed default above.

The shared initialization rules apply: empty or whitespace-only environment input is absent; a present invalid higher-priority value fails validation instead of falling back. Parse integers using invariant culture. Validate and publish once before activation; do not reread configuration per callback or introduce live resizing in this version. Preserve the requested `EnviormentsOptions` spelling.[^core]

**HB-CAP-04 · A · Effective range.** This revision interprets “with a max of 16” as an inclusive effective range of **1–16**, including explicit setup and environment overrides. Out-of-range or malformed explicit values fail startup validation; only the calculated default is clamped. This override boundary is recorded as a review assumption in D08.

The conceptual resolved setting is `EnviormentsOptions.Current.Heartbeat.MaxConcurrent`, following the core's `Current` snapshot pattern. Adding that Heartbeat section is an integration change to the shared environment contract, not a claim that the member already exists. There is no independently initialized mutable Heartbeat settings singleton.

### 5.3 Accounting, queueing and fairness

**HB-CAP-05 · R · Slot lifetime.** A capacity slot MUST be acquired before executor posting and held until the dispatched execution settles. It remains occupied across condition and callback awaits, asynchronous error handling, owned cleanup and any owned reporting handoff that has not completed. A pending slot MUST NOT be released merely because the underlying thread is free during an I/O await. Work awaiting a slot remains accepted and tracked but is not dispatched.

**HB-CAP-06 · R · Fair incremental dispatch.** Eligible accepted work MUST receive fair dispatch consideration. The dispatcher MUST reuse slots individually as work settles, without waiting for an entire batch to finish. A fast registration must not continually overtake older eligible work. FIFO admission among ready executions, with deterministic ordering for ties, is a suitable initial implementation; the particular data structure is private.

**HB-CAP-07 · R · Bounded pending work.** Pending execution storage MUST be bounded by one accepted execution per registration, plus bounded scheduler metadata per registration and at most `MaxConcurrent` dispatched executions. Missed intervals MUST NOT allocate unbounded task or queue histories. The initial contract does not impose a separate maximum registration count or require one physical worker thread per registration.

For example, if 100 registrations become eligible at readiness and `MaxConcurrent` is 16, at most 16 unfinished execution wrappers may be dispatched. The rest remain in the scheduler's pending representation. A released slot can serve the next eligible registration immediately.

This controls outstanding Heartbeat execution, including repeated equal-delay bursts. It does not impose a starts-per-second rate limit, guarantee uniform spacing, reserve ThreadPool threads or control unrelated application work and child-task fan-out. No additional jitter or CPU-count cascade is selected for this version.

## 6 Application-owned lifecycle

**HB-LIF-01 · R · Inactive construction.** Construction and pre-readiness registration MUST be inactive. They MUST NOT accept recurring executions, evaluate conditions or call subscribers. Creating the DI singleton is not a readiness signal.

**HB-LIF-02 · R · Automatic activation at readiness.** Heartbeat's lifecycle MUST be bound to the application through hosting and DI integration. That integration MUST enable execution only after the initialization required by registered work is complete. The subscriber developer MUST NOT have to call a Heartbeat start method. Readiness here means ready to run this background work; an application may separately require an initial polling result before serving external traffic.

**HB-LIF-03 · R · Infrastructure activation.** Infrastructure activation completes when the scheduler is enabled and its bookkeeping is established. It MUST NOT require a first successful callback. Repeated activation while running is an internal no-op. No independent consumer restart operation is exposed.

**HB-LIF-04 · R · Coordinated lifecycle.** Activation, registration, owner closure and shutdown MUST coordinate their transitions. Once shutdown wins the cutoff race, no concurrent activation or registration can reopen the engine. The lifecycle integration owns stopping and resource disposal, including error paths before readiness.

**HB-LIF-05 · R · Application lifetime.** The engine is shared for one application/container lifetime. Its internal stopped state is terminal. A new application lifetime receives a new engine; the initial library does not implement restart generations, registration retention across restart or a developer-operated start/stop service.

**HB-LIF-06 · R · Shutdown before readiness.** If the application shuts down before activation, Heartbeat MUST retire registrations and release its resources without invoking their conditions or callbacks. Repeated infrastructure stop or disposal joins the same completion.

| Internal state | Meaning | Registration and lifecycle behavior |
| --- | --- | --- |
| Created | Constructed and waiting for application readiness | Registration allowed; no execution accepted. |
| Running | Readiness gate open and dispatch enabled | Registration allowed; duplicate registration is a no-op. |
| Stopping | Global admission and new-start gates closed | Registration rejected; unstarted work suppressed; running work settling. |
| Stopped | Scheduler and all owned work have settled | Registration rejected; repeated stop is complete. |
| Disposed | Stopped and scheduler resources released | Registration rejected; repeated infrastructure disposal is complete. |

These are internal coordination states, not a new public Heartbeat status API. Healthcheck owns externally observable state and its presentation.

**HB-LIF-07 · R · Explicit host ordering.** The integration MUST encode startup and shutdown ordering rather than relying on incidental DI registration or disposal order. For a .NET Generic Host adapter, `ApplicationStarted` is a suitable activation signal only if the application's required initialization is complete by then. The adapter can arm itself during hosted startup; it MUST NOT await `ApplicationStarted` inside a hosted `StartAsync` whose completion is needed for that event to occur. Lifecycle notifications must not leave untracked `async void` work.[^host]

The host has distinct stopping phases. A `StoppingAsync` integration can establish and await the Heartbeat barrier before dependent hosted-service stopping, but it cannot make dependencies safe if unrelated `ApplicationStopping` handlers tear them down earlier. The application adapter must coordinate those paths as well. Logging, monitoring and other dependencies needed by running callbacks must remain usable until their Heartbeat work has settled.[^host]

## 7 Application shutdown and drain

### 7.1 Cutoff and the selected work

**HB-STP-01 · R · Close admission and execution starts.** At application shutdown, Heartbeat MUST atomically close global acceptance and the gates for starting conditions and callbacks. Unaccepted due work, accepted queued work and posted-but-unstarted work MUST NOT begin user execution after losing that race. The scheduler must wake promptly from its own waits to process the cutoff.

A start gate is the coordinated decision immediately before a user phase is invoked. It defines ordering when startup of that phase races with shutdown: the phase either wins and is included as running work, or loses and is never invoked. Posting an executor wrapper does not win this gate.

**HB-STP-02 · R · Stop recurrence and suppress unstarted work.** Shutdown MUST prevent all subsequent recurrence. Accepted work that has not started MUST be withdrawn rather than dispatched for the sake of emptying a queue. Any associated wrapper, bookkeeping and owned cleanup must still settle safely. The suppression itself is a lifecycle fact, not a callback failure.

**HB-STP-03 · R · Let running work finish.** Shutdown MUST await currently running work through its actual completion, necessary continuations, error handling and owned cleanup. The shutdown signal stops scheduling; it MUST NOT automatically cancel the execution token of a running condition, callback or recovery handler. Separately owned application cancellation can still be acknowledged by that work.

**HB-STP-08 · A · Condition-to-callback boundary.** This revision uses the strict interpretation of “stop running any new work”: a condition that already started is awaited, but its callback is suppressed if that callback has not crossed its own start gate when shutdown closes it. A true condition does not override the shutdown cutoff. A false condition remains a factual skip; an exception from the running condition still follows error handling.

| Work at the global cutoff | Required treatment |
| --- | --- |
| Future eligibility or due work not accepted | Do not admit it. |
| Accepted work awaiting capacity | Withdraw it; do not invoke condition or callback. |
| Posted executor wrapper, user phase not started | Prevent invocation at the start gate and settle the wrapper safely. |
| Condition already running | Await it; suppress a not-yet-started callback even if the condition returns true. |
| Callback already running | Await its full returned task. |
| Error handling, rollback or cleanup belonging to running work | Allow it to finish and await it as part of that execution. |
| Remaining requests in an external business queue | Leave consumption or persistence policy to the application. |

Completing a running callback during shutdown never schedules its next recurrence. Necessary `OnError` and cleanup for that running execution may begin after the cutoff because they complete owned work; they are not a new recurring execution.

### 7.2 Completion barrier and failed waits

**HB-STP-04 · R · Join all owned work.** Infrastructure stop MUST complete successfully only when the scheduler has exited or become permanently quiescent, every accepted execution has either been safely suppressed or settled, and every posted wrapper is settled so no deferred path can later invoke user code. Joining a scheduling thread alone does not prove that asynchronous callbacks have completed. A failure in one execution must not abandon other work being drained.

**HB-STP-05 · R · Shared drain.** Concurrent infrastructure stop/disposal requests MUST join one drain. Canceling a wait cancels that waiter only. It MUST NOT reopen gates, cancel the shared drain, release live holds or publish Stopped. An already-canceled wait token does not prevent establishing the cutoff. Once the drain completes, subsequent waits can observe that completion.

**HB-STP-06 · R · No false completion.** A host shutdown budget, caller timeout or external watchdog threshold MUST NOT be reported as a completed barrier while work remains live. The owner must keep dependencies usable while that work can access them. A canceled stop wait is not permission to run container disposal in a `finally` block.

**HB-STP-07 · A · Terminal errors versus incomplete drain.** A failed callback, failed error handler or terminal cleanup fault remains a reported execution/integration fact and does not fault an otherwise proved drain solely because the work failed. Successful drain certifies that owned work ended, not that every operation succeeded. If an infrastructure error prevents proving that work ended, the drain remains unconfirmed, unresolved ownership is retained, and the infrastructure fault must be surfaced after other drainable work is awaited. Underlying container disposal errors retain the container's own error contract.

An indefinitely stuck task may keep orderly shutdown incomplete. .NET cancellation is cooperative, so a timeout or a canceled wait cannot force arbitrary callback code to stop.[^cancel] Process termination can end the process without orderly completion; the library must not describe that as a successful graceful drain.

A condition, callback, error handler or cleanup operation MUST NOT await a drain that includes itself, including its own `UnregisterAsync`. It also MUST NOT wait for another Heartbeat operation whose progress depends on capacity held by the waiting work. For example, with `MaxConcurrent = 1`, execution A cannot await unregistration of B when B is accepted and waiting for A's slot. The scheduler cannot generally discover arbitrary application dependency cycles. Notify an external owner and return; that owner coordinates waits outside the execution. Automatic retirement uses internal state transitions rather than recursively waiting on public unregistration.

### 7.3 Distinct stop cases

| Trigger | New acceptance | Accepted unstarted work | Running work | Subscriber/container disposal |
| --- | --- | --- | --- | --- |
| Ordinary `UnregisterAsync` | Closed for that registration | Drained under the ordinary execution contract | Awaited | Remains the owner's responsibility after the barrier. |
| Owner closes while the application is running | Closed for that owner's registrations | Drained under ordinary unregistration while owner holds remain valid | Awaited | Underlying owner disposal waits for these barriers. |
| Application shutdown | Closed globally | Suppressed | Awaited through error handling and cleanup | All affected owners wait for the global barrier. |
| Collected or known unusable target | Closed for that registration | No further invocation of the target | Any already-running work remains tracked | No disposal responsibility transfers to Heartbeat. |

Application shutdown takes precedence if these cases overlap. An owner's Closing state does not by itself invalidate holds already acquired for accepted work; actual dependency disposal must wait for their release.

## 8 Owner context, weak references and disposal

### 8.1 Execute with the actual owner's dependencies

**HB-DIS-01 · R · Pre-disposal barrier.** The hosting and DI integration MUST complete the applicable Heartbeat drain before disposing the enclosing registration owner, root container or shared dependency still accessible to Heartbeat-owned work. This ordering must begin before underlying destructive disposal starts. Eventual disposal of a Heartbeat singleton during container teardown is insufficient. A scope created solely for an execution follows the separate cleanup sequence in HB-DIS-03; its cleanup is part of the drain, not an enclosing-owner disposal that must wait for that same drain.

**HB-DIS-02 · R · Caller ownership.** Registration MUST NOT transfer ownership of the subscriber or its dependency scope to Heartbeat. The actual owner remains responsible for disposal. Unregistration removes Heartbeat's usage rights only after its barrier; it does not certify that other application users of the same scope have finished.

**HB-DIS-03 · R · Owner-context invocation.** Heartbeat MUST invoke the registered instance with its existing dependencies and owner context. If an integration needs additional dependency resolution, it MUST use that owner context. It MUST NOT silently resolve a replacement subscriber, resolve scoped dependencies from Heartbeat's application root, or give each invocation an unrelated scope.

An integration that explicitly creates an execution scope must document why it is needed and retain it until all its users, including any scope-dependent error handling or reporting preparation, have finished. It then disposes that execution scope as owned cleanup and awaits disposal before settlement; the enclosing registration owner remains held throughout. A reporter cannot use an already disposed execution scope to report that scope's cleanup result. It must detach beforehand or use a still-valid enclosing owner. If the container supports nested scopes, such a scope must have the registering object's owner as its appropriate parent. The built-in Microsoft DI container does not make scopes hierarchical; creating another scope does not inherit an existing scope's instances.[^di]

**HB-DIS-04 · R · Infrastructure-owned disposal.** The host/DI integration, rather than the normal subscriber-facing interface, MUST initiate or join Heartbeat shutdown and release scheduler resources after the barrier. The integration MUST support asynchronous completion where owned work requires it. It must not block an execution on its own drain.

**HB-DIS-05 · R · Owner-aware registration path.** The integration MUST associate a registration with the actual subscriber lifetime owner. It cannot infer the creating DI scope reliably from an arbitrary object reference. An owner-bound registration facade forwarding to the shared engine is an acceptable design. The facade or an internal owner association is not a new public registration handle and must not create a second scheduler. Duplicate registration does not change the original owner association.

For an object with a lifetime shorter than its enclosing DI scope, its shorter-lived owner must participate in the same close-and-drain protocol. “Transient” describes construction policy and does not by itself supply a deterministic expiry signal. The owner integration must define when the object stops accepting work.

**HB-DIS-06 · R · Scope is not ambient thread state.** The required context is the registered object's ownership and dependency context, not a promise to use its calling thread. ThreadPool execution is permitted. Heartbeat MUST NOT indiscriminately capture and retain the registering flow's complete `ExecutionContext`, request state, activity or logging scope for the lifetime of a registration. Logical execution-context flow and DI scope ownership are different mechanisms; any deliberately carried context must have a documented, bounded lifetime.[^context]

### 8.2 Weak while idle, held while accepted

**HB-DIS-07 · R · Weak idle ownership.** An idle Heartbeat registration MUST NOT keep an otherwise unowned subscriber or its owner scope alive for the application's lifetime. Use weak target ownership and equivalent protection across the entire registration reference graph. If the target is no longer available, retire the registration without calling it or constructing a replacement. The application or DI owner must retain the instance if recurrence is to continue.

This applies to indirect references as well: bound conditions, closures, owner providers, cached delegates, captured execution context, queued continuations, monitor bindings and diagnostic records must not create a permanent strong path back to an idle target or its owner. A strong-key dictionary of subscribers would also violate this requirement.

The configured condition and its binding must nevertheless remain available while the subscriber is externally alive. Merely weak-referencing the condition independently, so it can disappear while a live subscriber still needs it, is not sufficient. The implementation may use an appropriate weak-key binding or another mechanism that meets both requirements; the internal data structure is not frozen here.

**HB-DIS-08 · R · Atomic lifetime acquisition.** When work is accepted, Heartbeat and its owner adapter MUST atomically obtain a valid strong target and an owner lifetime hold, or reject admission because the target/owner is unavailable or closing. The hold begins before queueing, not only before callback invocation, so ordinary unregistration can honor already accepted queued work. It lasts through condition, callback, awaited error handling and owned cleanup. Release it only when no owned code can access the protected resources.

An owner closing concurrently with acceptance has one winner: a valid hold is acquired and included in the owner's barrier, or admission is rejected. There must not be an unprotected check-then-use gap.

**HB-DIS-09 · R · Disposal protection is cooperative.** A strong local obtained from `WeakReference<T>.TryGetTarget` keeps the target reachable during use, but it does not prevent another component from calling `Dispose`. Pinning a GC object is not a disposal lock. There is no universal `IsDisposed` contract on ordinary .NET objects. The integration's lifetime hold must therefore mean that the owner defers disposal, not merely that Heartbeat retains an `IServiceProvider` reference.[^weak][^tryget][^dispose]

The guarantee applies to owners that honor this protocol. Heartbeat cannot prevent arbitrary independent calls to destructive disposal. Such calls can still cause an in-flight operation to fail. Catching a disposal exception is recovery from that condition, not a substitute for the pre-disposal barrier.

**HB-DIS-10 · R · Release without lifetime promotion.** Settlement, completed unregistration, collection and safe shutdown suppression MUST release temporary strong target references and owner holds. Idle registrations must return to weak ownership. Required reporting must not leave an unbounded strong reference to the subscriber, its scope or its exception object graph.

A transient already retained by its DI container for disposal may remain alive independently of Heartbeat. In particular, resolving disposable transient or scoped dependencies from a root provider can retain them for the root lifetime. Weak Heartbeat ownership cannot undo that separate container decision.[^di]

### 8.3 Collected and disposed targets

**HB-DIS-11 · R · Automatic retirement.** If the target has been collected, its owner has ended, or the target is known unusable, Heartbeat MUST close that registration and prevent further invocation. An unhandled `ObjectDisposedException` escaping a condition or callback MUST also trigger automatic retirement. Under A03, this revision applies that retirement action to a disposal exception escaping the subscriber's error handler too, while preserving its secondary-error classification. Mark retirement immediately, retain the identity reservation while existing work settles, and remove the registration afterward. Automatic retirement MUST NOT await its own public unregistration from within the execution.

The revision's outcome interpretation is:

| Observation | Execution fact and action |
| --- | --- |
| Weak target unavailable before acceptance | Neutral lifecycle removal; no callback execution or fabricated failure. |
| Owner closes before acceptance | No new execution; ordinary owner-drain rules apply to work already accepted. |
| Owner or target known unusable before invocation | Suppress further target calls and retire; report the lifecycle reason. |
| `ObjectDisposedException` escapes a running condition or callback | One failed execution fact with the actual exception; automatic retirement after settlement. |
| `ObjectDisposedException` escapes the subscriber's error handler | Preserve the original failed outcome and secondary handler fault; retire after settlement under A03, without recursive error handling. |

The failed-execution classification and error-handler retirement boundary are retained assumption A03. A disposal exception may concern one of the subscriber's dependencies; its type or `ObjectName` does not establish that the subscriber object itself was disposed.[^ode] Eligible `OnError` may therefore still run while the subscriber and protected owner remain usable. If the target or owner is known unusable, do not call the error handler on that target; report why it could not be invoked.

An error-handler disposal exception remains a secondary handler fault even when it causes retirement. An unrelated monitoring or integration cleanup exception follows that integration's fault path and is not automatically treated as subscriber retirement. No exception is counted as another callback execution or taken as proof of which particular object was disposed. If unusability is independently established, the ordinary no-further-invocation rule still applies.

## 9 Results and `OnError`

### 9.1 Callback results and factual outcomes

**HB-OBS-01 · R · Report facts, not health state.** Heartbeat MUST report the lifecycle and execution facts needed by its selected Healthcheck monitor. It MUST retain only the internal mechanical state needed for scheduling, exclusivity, capacity and draining. Public snapshots, status queries, historical views, exception retention policies, aggregate counters and health state belong to Healthcheck.

**HB-OBS-02 · A · Boolean execution result.** The callback retains a `Task<bool>` result: `true` reports successful work and `false` reports unsuccessful work. Callback `false` is an ordinary reported result, not an unhandled exception. A false condition is a separate skipped execution. Final outcome also reflects observed exceptions and owned cleanup as described below.

| Observed event | Final fact, assuming no later owned-work fault | Call `OnError`? |
| --- | --- | --- |
| Condition returns `false` | `Skipped` | No. |
| Condition throws an unexpected exception | `Failed`, phase Condition | Yes, if the subscriber/owner is usable. |
| Callback returns `true` | `Succeeded` | No. |
| Callback returns `false` | `Failed`, ordinary negative result | **No; do not manufacture an exception.** |
| Callback throws an unexpected exception | `Failed`, phase Callback | Yes, if the subscriber/owner is usable. |
| Known cooperative cancellation is acknowledged | `Canceled`, with cancellation facts | No. |
| Unexplained `OperationCanceledException` | `Failed`, unexpected exception | Yes, if safe. |
| Unhandled disposal exception from condition/callback | `Failed` plus retirement reason | Only while the subscriber/owner is still usable. |
| Shutdown suppresses a not-yet-started callback | Lifecycle suppression; retain any condition facts | No, unless the condition itself failed unexpectedly. |
| Integration-owned cleanup fails | Actual cleanup fault; retain earlier execution result | No fresh subscriber call after dependencies have been released. |

**HB-OBS-03 · A · One final outcome and error precedence.** One execution MUST produce one final result without discarding observed earlier facts. A real condition, callback, handler or owned-cleanup failure takes precedence over success, skip or expected cancellation. Otherwise report acknowledged cancellation, then a false-condition skip, then lifecycle suppression if the callback was prevented from starting, then success for a callback that returned true. Suppression has no callback result. A callback `false` remains failed even if later error-free cleanup succeeds. A successful `OnError` does not convert an originating exception into callback success.

Shutdown suppression by itself is neutral. Any real error from work that did run is still reported with its phase. Cleanup needed to withdraw unstarted work can have an integration fault; it must not be described as a callback having run and failed. `TimedOut` and health classifications are not Heartbeat policy outcomes in this revision.

**HB-OBS-04 · A · Cancellation classification.** Expected cancellation requires a known canceled operation token or a documented cancellation reason associated with the execution. An arbitrary `OperationCanceledException` is not silently treated as normal. A token request alone does not override a normally completed boolean result; the operation must actually acknowledge cancellation. Canceling an unregister or host-stop wait does not change execution outcome. The supplied execution token MUST NOT be the wait token or an automatically canceled application-shutdown signal. With no separately defined execution-cancellation integration, it may remain uncanceled.

If an application uses additional tokens internally, it must handle their semantics or associate their cancellation facts through the defined integration. Heartbeat does not infer request expiry from an exception or substitute its own business deadline.

**HB-OBS-05 · R · No duplicate observation subsystem.** Heartbeat MUST NOT expose the draft 0.1 `GetStatus` methods or maintain its proposed public snapshots, failure streaks, success/failure totals, last-success policy or observation history. Correlation IDs and transient execution facts may be generated for reporting, but aggregation, reads and retention are Healthcheck responsibilities.

### 9.2 Awaited application error handling

**HB-ERR-01 · R · Unhandled exceptions only.** `OnError` exists for exceptions escaping the condition/callback execution boundary. It allows the subscriber to log, alert, repair or roll back according to application policy. Heartbeat MUST NOT invoke it merely because a callback returned `false`, a condition returned `false`, known cancellation was acknowledged, or a health policy was breached. Exceptions already handled within a callback do not separately trigger it.

**HB-ERR-02 · R · Error handling remains owned.** The error-handler contract MUST be awaitable so asynchronous logging or rollback can be tracked. It MUST remain inside the current instance reservation, dispatched capacity slot and owner lifetime hold. Shutdown and unregistration wait for it; recurrence waits for its settlement. Heartbeat chooses neither the rollback operation nor its business deadline.

**HB-ERR-03 · A · No recursion or result replacement.** Invoke the handler at most once for the originating unhandled condition/callback exception in an execution. If it throws or returns a faulted task, preserve that as a secondary handler fault and do not recursively invoke `OnError`. Preserve the original exception and negative execution outcome even if handling succeeds. A null task from a task-returning user method is a contract violation, not successful completion.

**HB-ERR-04 · A · Cleanup ordering.** Subscriber-owned cleanup must be included in the subscriber's returned task. If its failure escapes that task, it reaches the normal condition/callback error boundary. Error handling runs while the subscriber's required resources remain valid, before integration-owned scope release. An integration cleanup fault occurring after that safe boundary is reported through the integration; it MUST NOT trigger a new subscriber invocation against already released resources. All owned cleanup is still awaited.

Heartbeat can preserve only the errors it actually observes. If callback code masks one exception with another in its own cleanup, that code must preserve the original when its application needs both; Heartbeat must not claim to reconstruct hidden exceptions.

## 10 Healthcheck and watchdog integration

### 10.1 Policy ownership and monitor selection

**HB-HLT-01 · R · Healthcheck owns interpretation.** Healthcheck MUST own failure allowance, thresholds, counters, resets, health classification, observation snapshots, history and supervisor policy. Heartbeat MUST NOT include an `AllowedConsecutiveFailures` option, choose a numeric allowance, implement N-versus-N-plus-one arithmetic or decide that a successful callback resets health. It reports facts to the selected monitor.

**HB-HLT-02 · R · Bind the configured monitor.** Heartbeat's integration MUST obtain the appropriate monitor through Healthcheck's API for the configured policy. Healthcheck owns interpreting the policy and choosing/configuring the monitor. Heartbeat treats that policy as opaque and must not reproduce its logic. Duplicate registration must not rebind it; registration reuse is a new registration lifetime and Healthcheck decides any historical continuity.

The Healthcheck specification will define the policy reference type, binding API, default/absent-policy behavior, invalid-policy handling and notification types. A configured policy must not be silently dropped if monitor binding fails. The integration must honor Healthcheck's resolution-failure contract before activating that registration. These are Healthcheck design dependencies, not new failure policy decisions for Heartbeat.

### 10.2 Required fact categories

**HB-HLT-03 · R · Reporting boundary.** The integration contract MUST support the following fact categories without exposing subscriber objects as monitoring state. The exact public notification schema will be finalized with Healthcheck.

| Fact category | Needed information |
| --- | --- |
| Registration | Detached correlation identity, optional name, effective schedule, opaque monitor/policy association and registration lifetime. |
| Admission and dispatch | Eligibility, acceptance and dispatch timing sufficient to distinguish queueing from execution. |
| Running work | Condition/callback phase transitions, actual start timing and still-active ownership so a monitor can observe unfinished work. |
| Completion | One correlated final outcome, condition/callback result when present, work-completion timing, ownership-settlement timing when subsequently available, durations and observed error phases. |
| Error handling and cleanup | Whether handling was invoked, completed, faulted or unsafe to invoke; secondary cleanup/handler facts without duplicating the originating execution. |
| Lifecycle | Registration retirement, collection, owner closure, shutdown suppression and scheduler/integration faults. |

Heartbeat supplies execution facts; Healthcheck derives counters, last-success views, failure streaks, health snapshots and final observations. Business request timestamps, usable prices and exposure limits must be supplied by the application if a monitoring policy needs them. Heartbeat must not infer them from callback timing.

**HB-HLT-04 · R · Owner-safe, isolated reporting.** Monitor bindings and fact delivery MUST preserve weak idle ownership and the owner context rules. Long-lived reports must contain detached metadata and diagnostic error data, not live subscriber, scope, delegate, execution-context or arbitrary exception object graphs that prolong those lifetimes. Healthcheck owns retention limits and external transport.

A reporting failure is an integration fault, not another callback failure. It MUST NOT erase the original execution result, invoke subscriber `OnError` recursively, or let an exception escape through unrelated scheduling. Exactly one local final outcome does not promise durable or exactly-once external delivery. The Healthcheck contract must define buffering, loss/failure reporting, ordering and any delivery guarantees.

If a reporting call retains access to the execution's owner, it is owned work and must be awaited before release. A detached handoff can let monitoring continue independently only after it no longer requires the subscriber or its scope. The adapter must define this completion boundary; it must not fire and forget a delegate that captures the owner. Healthcheck must be available before its Heartbeat registrations activate and remain available through their final reporting handoffs.

Work completion and ownership settlement are distinct timestamps. Finish subscriber work, error handling and relevant cleanup first and form the execution outcome. Await any remaining owner-dependent reporting handoff while retaining the slot, instance reservation and enclosing-owner hold. Only then complete final bookkeeping, release those resources and establish settlement; that settlement time drives the next fixed delay. A report emitted before its own handoff finishes cannot claim the later settlement time. If a final settlement notification is needed, emit a distinct detached lifecycle fact afterward, without another owner-dependent wait or a second callback outcome. Monitoring transport after a completed detached handoff has its own Healthcheck lifetime.

### 10.3 Watchdog boundary

**HB-WDG-01 · R · Deferred to Healthcheck design.** Watchdog policy, observed duration, threshold, cancellation capability and delivery scope will be defined with Healthcheck. This revision does not add a Heartbeat timeout option, polling watchdog, `AttemptTimeout` or `TimedOut` health state. Heartbeat must provide the running/completion facts and ownership behavior needed by that future integration.

**HB-WDG-03 · R · Preserve actual ownership.** Any future timeout/cancellation integration MUST leave the original execution reserved, capacity-accounted and included in drains until it actually settles. It MUST NOT free the instance to run an overlapping replacement, release the owner or claim success merely because a timeout fired. `Task.WaitAsync` can bound a wait without proving that the underlying task ended.[^wait]

The watchdog measurement proposal in draft 0.1, **HB-WDG-02**, and its implementation proposal, **HB-WDG-04**, are retired from the Heartbeat scope. No threshold, timer origin or cancellation mode is implicitly approved by retaining these IDs in the revision history.

An application requiring a hard business deadline must define how its work cooperates with cancellation or uses stronger isolation. Its original request timestamp and remaining budget stay outside Heartbeat. Recovery performed in `OnError` belongs to the existing execution and is awaited. Separately scheduled rollback work has its own owner and must coordinate shared resources with any original work that is still alive.

## 11 Illustrative consumer API

These signatures express the reviewed behavior for the **new** library. The `Async` suffixes are proposed C# names for the callback and error hook; “OnError” elsewhere names the role. Final package, namespace and naming follow the shared core definition. This draft does not require compatibility members for the supplied GmCore example.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IHeartbeatSubscriber
{
    Task<bool> OnElapsedAsync(CancellationToken cancellationToken);

    Task OnErrorAsync(Exception exception);
}

public interface IHeartbeatService
{
    void Register(
        IHeartbeatSubscriber subscriber,
        HeartbeatRegistrationOptions options,
        Func<CancellationToken, Task<bool>>? condition = null);

    Task UnregisterAsync(
        IHeartbeatSubscriber subscriber,
        CancellationToken waitCancellation = default);
}

public sealed class HeartbeatRegistrationOptions
{
    public TimeSpan Interval { get; init; } =
        TimeSpan.FromMilliseconds(10);

    public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;

    public string? Name { get; init; }
}
```

`Register` returns normally for a successful new registration or duplicate no-op; the subscriber remains the handle. The options use a sealed setup class with named properties, following the core guide's approach to evolving contracts. The service copies their effective values. `InitialDelay` is included under retained assumption A01.

The consumer service has registration operations only. Host activation, shutdown, disposal, owner association and monitor binding are infrastructure responsibilities. Public health reads use Healthcheck's API. The required Healthcheck policy binding is omitted from this illustrative signature until Healthcheck defines its own configuration and types; it must be added through that integration before the initial public contract is frozen, without moving allowance policy into Heartbeat.

`OnErrorAsync` returns a task so application recovery can finish before ownership is released. A handler with nothing to do can return `Task.CompletedTask`. It receives the originating exception; the application owns any recovery cancellation or deadline. The callback's execution token has the separate semantics in HB-OBS-04.

### 11.1 Operation behavior

| Condition | Required or retained behavior |
| --- | --- |
| Null subscriber or required options | `ArgumentNullException`; no mutation. |
| Nonpositive interval, negative initial delay or unsupported duration | `ArgumentOutOfRangeException`; no partial registration. |
| Invalid environment concurrency setting | Shared `CoreConfigurationException` with a stable configuration issue; no initialized Heartbeat configuration is published. |
| Valid duplicate while registered or individually draining | Success/no-op; all original bindings remain unchanged. |
| Valid registration after global Stopping or Stopped | `InvalidOperationException`, including a duplicate. |
| Valid registration after engine disposal | `ObjectDisposedException`, including a duplicate. |
| New registration through an owner that is closing or ended | Reject without admission; use `InvalidOperationException` for Closing and `ObjectDisposedException` for an already disposed owner adapter. |
| Unknown or already completed unregistration | Successful no-op; no new execution. |
| Unregistration during global Stopping | Join the relevant existing settlement under the global suppression rule; do not restart queued work. |
| Unregistration after global drain/disposal | Completed no-op for the removed registration; no use of its former scope. |
| Pending unregister wait canceled | `OperationCanceledException` for that waiter; cutoff and shared drain remain. |
| Already-canceled unregister wait | Establish cutoff first; a pending wait observes cancellation. An already-complete no-op remains complete. |
| Callback returns `false` | Report the negative execution result; do not invoke `OnError`. |
| Subscriber unhandled exception | Await eligible `OnError`; preserve the failed result and any secondary handler error. |

Argument checks occur before mutation; global lifecycle checks and duplicate detection must then be atomic. Configuration issue codes, accepted duration bounds and exact public parameter names must be documented before publication and preserved afterward. The removed draft `GetStatus` method has no unknown-instance exception rule in this API.

### 11.2 Registration example

```csharp
// The DI integration supplied an owner-bound facade for this fully
// initialized poller. The poller's owner retains it for its intended life.
heartbeat.Register(
    subscriber: poller,
    options: new HeartbeatRegistrationOptions
    {
        Interval = TimeSpan.FromSeconds(1),
        Name = "FX polling"
    },
    condition: poller.CanPollAsync);
```

There is no subscriber-side start call. The hosting integration opens the readiness gate at the application boundary. It also performs shutdown and waits before the relevant DI owners dispose their dependencies.

```csharp
// A schedule change while the application remains running.
// Run from outside the execution being unregistered.
await heartbeat.UnregisterAsync(poller);

heartbeat.Register(
    poller,
    new HeartbeatRegistrationOptions
    {
        Interval = TimeSpan.FromSeconds(2),
        Name = "FX polling"
    },
    poller.CanPollAsync);
```

The caller must not replace the second registration with a concurrent operation and assume the original was reconfigured. If unregistration has not completed, that registration is still reserved and a valid duplicate remains a no-op.

## 12 Shared engineering and compatibility baseline

**HB-ENG-01 · R · Inherit dependency rules.** Apply the Core Library Implementation Guide, specification 1.1, rather than creating a second dependency policy. The shipping generic core has zero explicit `PackageReference` items and no third-party runtime dependency. It remains a normal .NET class library, without adding an ASP.NET shared-framework reference for convenience. Optional host, DI and concrete monitoring integrations follow separate dependency boundaries and the repository's actual dependency allowlist.[^core]

**HB-ENG-02 · R · Inherit framework and identity rules.** The guide's initial implementation target is `net10.0`, with Windows and Linux verification lanes. Consuming-application target validation, any required older target before first publication, pinned toolchains, project/package naming, assembly identity and namespace conventions are governed by that guide. Select final identities from repository conventions before the first published baseline; do not invent a competing Heartbeat naming or framework decision here.[^core]

**HB-ENG-03 · R · New library, permanent published compatibility.** GmCore is a design example, not a released consumer baseline for this library. There is no requirement to preserve its `Subscribe`, `Start`, type registration, `IDisposable`, old callback signatures or implementation behavior. Once this new library publishes its contract, including a published prerelease, the shared indefinite compatibility rule applies to source, binaries, configuration, package/type identity, retained target assets and documented behavior. Future major version numbers do not waive that rule.[^core]

**HB-ENG-04 · R · Shared environment initialization.** Preserve `EnviormentsOptions` exactly. Add Heartbeat's setup and resolved configuration through the core's initialize-once, validated, immutable snapshot model. Failed validation publishes nothing; production code has no reset or live mutable reinitialization. Execution counters, health state and per-callback mutable data must not be stored in that snapshot.[^core]

**HB-ENG-05 · R · Deterministic contract verification.** Tests MUST control elapsed time, executor admission, user-task completion and owner disposal. Race tests MUST exercise admission, start gates, settlement, unregistration and application cutoff with controlled synchronization. Arbitrary sleeps alone are insufficient evidence for non-overlap, fairness or safe disposal. Exercise complete weak-reference reachability, not merely the presence of a `WeakReference` field.

**HB-ENG-06 · R · Complete release evidence.** Every published version MUST meet the shared unit, integration, mutation, architecture, backward-compatibility and business-case verification gates and retain its complete verification bundle. The Heartbeat acceptance criteria below supplement those inherited gates. Record actual tools, platforms, dependencies, package/assembly hashes and historical baselines. This document does not claim those suites have run.[^core]

**HB-ENG-07 · R · Readable, measurable implementation.** Apply the guide's maintainability and analyzer rules. Separate timing, dispatch, registration ownership, lifecycle coordination and adapters into coherent responsibilities. Observe owned tasks, avoid user code under scheduler locks, use asynchronous waits and release obsolete references. Measure resource use under many equal-interval registrations and fractional-CPU deployments before making scale, latency or starvation claims; the chosen capacity formula itself supplies none.

The public owner-integration and reporting contracts must be reviewed with Healthcheck before first release. Their implementation may use internal adapters; no particular DI container, semaphore, timer, queue type, weak-key data structure or dedicated scheduling thread is prescribed by this specification.

## 13 Acceptance criteria

These are requirements for implementation verification, **not completed test results**. Existing AT-01–AT-33 topic references are retained and corrected to the reviewed contract; additional cases cover the decisions added during review. A case referencing an A requirement verifies this revision's stated assumption. Healthcheck integration cases become executable once its contract is defined.

### 13.1 Original cases reconciled with the review

| ID | Test and expected result | Contract |
| --- | --- | --- |
| AT-01 | Register before readiness. No execution is accepted and no condition/callback runs until the application integration opens the readiness gate. | HB-LIF-01; HB-LIF-02 |
| AT-02 | Shut down before readiness. Complete without invoking registered work; infrastructure disposal is repeatable. | HB-LIF-06; HB-DIS-04 |
| AT-03 | Hold an execution through callback, error handling or cleanup. Its next eligibility is full settlement plus the configured interval; no catch-up. | HB-SCH-01 |
| AT-04 | Exercise initial delay before readiness and registration while Running. Use the documented origin; duplicate registration leaves it unchanged. | HB-SCH-02; HB-REG-03 |
| AT-05 | Hold each asynchronous phase of one instance in turn. No second condition or callback overlaps any phase or its cleanup. | HB-CND-02; HB-EXE-02 |
| AT-06 | Register distinct instances with equal names and types. They remain independent and progress when capacity is available. | HB-REG-01; HB-REG-02; HB-EXE-03 |
| AT-07 | Use subscriber classes overriding equality/hash behavior. Actual object reference identity governs registration and unregistration. | HB-REG-01 |
| AT-08 | Repeat valid registration while active and while individually draining. Both calls succeed without replacement or additional execution. | HB-REG-03 |
| AT-09 | Return false from a condition. Report a skip, do not call the callback or `OnError`, and wait the normal interval. | HB-CND-01; HB-CND-03; HB-ERR-01 |
| AT-10 | Throw from a condition. Do not invoke its callback; await eligible `OnError`; report one final failed execution. | HB-CND-04; HB-ERR-02; HB-OBS-03 |
| AT-11 | Return false from a callback and true from a later one. Report distinct facts without synthetic exceptions or Heartbeat-owned failure/reset arithmetic. | HB-OBS-02; HB-OBS-05; HB-ERR-01 |
| AT-12 | Block a subscriber's synchronous prologue. It runs off the shared scheduling/control path and outside shared locks; unrelated eligible registrations remain considered. | HB-EXE-01; HB-EXE-03 |
| AT-13 | Fault one callback while other work is active. Observe the exception, finish owned handling/cleanup and keep unrelated scheduling/draining intact. | HB-EXE-04; HB-STP-04 |
| AT-14 | Begin shutdown with a blocked callback. The barrier remains incomplete and its dependencies remain usable until actual settlement. | HB-STP-03; HB-STP-04; HB-DIS-01 |
| AT-15 | Shut down with accepted work awaiting capacity. Suppress it, release its holds safely and never invoke its condition or callback. | HB-STP-01; HB-STP-02; HB-DIS-10 |
| AT-16 | Shut down while a condition is running. Await the condition but suppress a callback that has not won its start gate, including a later true result. | HB-STP-08 |
| AT-17 | Race eligibility/admission against shutdown. Work is either tracked under the correct drain/suppression rule or not accepted; no ownership is lost. | HB-OWN-02; HB-STP-01 |
| AT-18 | Race settlement with a cutoff. No new recurrence is created afterward; the released instance never exposes stale scheduling state. | HB-EXE-05; HB-STP-02 |
| AT-19 | Complete the callback while owned asynchronous cleanup remains active. Shutdown still waits and retains protected dependencies. | HB-OWN-01; HB-STP-04; HB-DIS-08 |
| AT-20 | Cancel one infrastructure stop waiter. The global cutoff and drain remain; another waiter observes the real completion. | HB-STP-05; HB-STP-06 |
| AT-21 | Supply an already-canceled wait token to infrastructure stop or an unregister with live work. Establish the applicable cutoff before canceling that wait. | HB-REG-09; HB-STP-05 |
| AT-22 | Repeat activation, stop and disposal concurrently through infrastructure. One application lifecycle and shared drain exist; no stop-to-start reopening occurs. | HB-LIF-03; HB-LIF-04; HB-LIF-05 |
| AT-23 | Ordinarily unregister accepted queued and running work while the application stays Running. Drain that work before completion and keep its owner usable. | HB-REG-07; HB-DIS-08 |
| AT-24 | Shut down the integrated host with dependencies used by active callbacks. Prove no relevant dependency stops or disposes before the Heartbeat barrier. | HB-LIF-07; HB-DIS-01 |
| AT-25 | Have work ignore independently requested cancellation or outlive an external timeout. Retain instance/capacity ownership and await actual completion. | HB-OWN-03; HB-EXE-02; HB-WDG-03 |
| AT-26 | Inspect core contracts and dependency boundaries. No Heartbeat allowance, watchdog timeout, health-state machine or inherited GmCore feature is introduced. | HB-SCP-03; HB-SCP-04; HB-HLT-01; HB-WDG-01 |
| AT-27 | Read Healthcheck observations repeatedly while Heartbeat is active. Those reads neither invoke subscriber work nor cause duplicate completion facts. | HB-OBS-01; HB-OBS-05; HB-HLT-03 |
| AT-28 | Change UTC time forward/backward while advancing the monotonic test clock. Eligibility and measured intervals remain correct. | HB-SCH-04 |
| AT-29 | Exercise nulls, invalid durations and registration after the global cutoff/disposal, including duplicates. Apply documented errors without leaked holds or partial mutation. | HB-REG-04; HB-REG-06; HB-LIF-04 |
| AT-30 | Execute application work carrying an originating timestamp and deadline. Heartbeat neither resets nor interprets that business budget or result age. | HB-SCP-02 |
| AT-31 | Inject terminal cleanup faults and, separately, an unconfirmed ownership failure. Only a proved barrier can complete; other drainable work is awaited in both cases. | HB-STP-07; HB-ERR-04 |
| AT-32 | Verify the package against the inherited core baseline and all applicable release families and historical contracts. GmCore is not treated as this library's baseline. | HB-ENG-01; HB-ENG-02; HB-ENG-03; HB-ENG-06 |
| AT-33 | Advance many intervals while an instance is queued or active. At most one execution is accepted for it and pending history does not grow per tick. | HB-EXE-07; HB-CAP-07 |

### 13.2 Configuration, capacity and registration changes

| ID | Test and expected result | Contract |
| --- | --- | --- |
| AT-34 | Use default registration options. The recurring interval is 10 ms; first eligibility follows the separate initial-delay/readiness rule. | HB-SCH-05; HB-SCH-02 |
| AT-35 | Control the `GetMaxThreads` completion-port result. Values at/below 1 produce default 1; values above 16 produce 16. CPU count/live availability do not select the default. | HB-CAP-02 |
| AT-36 | Exercise setup/environment/default precedence, blank input, malformed input, zero, negative values and values above 16. Resolve once; invalid effective input fails publication. | HB-CAP-03; HB-CAP-04; HB-ENG-04 |
| AT-37 | Change setup objects and process environment after initialization/registration. Published settings and registered schedule bindings remain unchanged. | HB-REG-05; HB-CAP-03; HB-ENG-04 |
| AT-38 | Make many registrations eligible together with capacity 16. Reserve slots before posting and observe no more than 16 unfinished dispatched executions. | HB-CAP-01; HB-CAP-05 |
| AT-39 | Suspend condition, callback, error handler and cleanup across awaits. Each dispatched execution keeps its slot until full settlement. | HB-CAP-05; HB-ERR-02 |
| AT-40 | Mix slow and fast work with equal intervals. Reuse each free slot without a batch barrier and give older eligible work fair progress. | HB-CAP-06; HB-SCH-03 |
| AT-41 | Duplicate-register with different interval, name, condition, policy and owner. Validate inputs but leave every original binding and timing origin unchanged. | HB-REG-03; HB-REG-04 |
| AT-42 | Race two initial registrations of one instance. Exactly one registration and monitor binding wins; both valid calls return without duplicate work. | HB-REG-03; HB-HLT-02 |
| AT-43 | Await unregistration and then re-register the same instance. The new options apply only to the new registration lifetime; overlap is impossible. | HB-REG-08; HB-EXE-02 |
| AT-44 | Cancel an unregister wait and immediately re-register. While the underlying drain remains active, registration is still a successful duplicate no-op. | HB-REG-03; HB-REG-09 |
| AT-45 | Unregister an unknown/removed instance, including with an already-canceled wait token. Complete the no-op without creating work or accessing a disposed owner. | HB-REG-08; HB-REG-09 |

### 13.3 Lifetimes, exceptions and monitoring integration

| ID | Test and expected result | Contract |
| --- | --- | --- |
| AT-46 | Register objects from two owner scopes. Each uses its own dependency instances; neither resolves a new subscriber or falls back to Heartbeat's root scope. | HB-DIS-03; HB-DIS-05 |
| AT-47 | Keep the engine alive while dropping all external references to an idle subscriber/owner. Collection is possible and later scheduling retires the registration without invocation. | HB-DIS-07; HB-DIS-10; HB-DIS-11 |
| AT-48 | Repeat collection checks with bound/capturing conditions, monitor bindings, owner providers and diagnostics. No indirect permanent root defeats weak idle ownership. | HB-DIS-06; HB-DIS-07; HB-HLT-04 |
| AT-49 | Keep a subscriber externally alive while the configured condition has no other external reference. The condition remains available and is not independently lost. | HB-DIS-07 |
| AT-50 | Hold accepted work waiting for capacity. Its strong target and owner hold exist from acceptance and survive through error handling and cleanup. | HB-OWN-02; HB-DIS-08 |
| AT-51 | Race owner closure with admission. Acquire a valid protected hold and include it in drain, or reject acceptance; never execute in a disposed scope. | HB-DIS-01; HB-DIS-08; HB-DIS-09 |
| AT-52 | Close one owner while other owners remain active. Drain only the affected registrations; unrelated owners continue. Subscriber disposal remains with the owner. | HB-REG-07; HB-DIS-02; HB-DIS-05 |
| AT-53 | Throw `ObjectDisposedException` from a condition/callback. Mark retirement immediately, report one failed execution under A03 and retain the reservation through settlement. | HB-DIS-11; HB-OBS-03 |
| AT-54 | Throw a dependency disposal exception while the subscriber and owner remain usable. Permit eligible `OnError`; do not infer subscriber identity from `ObjectName`. | HB-DIS-11; HB-ERR-01 |
| AT-55 | Establish that the target or owner is unusable before invocation/error handling. Suppress further target calls and report the retirement/handler-suppression facts. | HB-DIS-11; HB-HLT-03 |
| AT-56 | Throw from `OnError`. Preserve the primary failure and secondary handler fault without recursion. For an `ObjectDisposedException`, also retire the registration under A03 without adding another execution outcome. | HB-ERR-03; HB-DIS-11 |
| AT-57 | Keep asynchronous rollback in `OnError` active during unregister/shutdown. Wait for it and prevent the next execution; successful rollback does not erase the original failure. | HB-ERR-02; HB-OBS-03; HB-STP-03 |
| AT-58 | Fault integration-owned cleanup after its safe subscriber-invocation boundary. Report the fault and settle correctly without calling into released subscriber dependencies. | HB-ERR-04; HB-STP-07 |
| AT-59 | Retain monitoring facts after an execution/registration ends. Verify that detached facts do not keep subscribers, scopes or arbitrary exception object graphs alive. | HB-OBS-05; HB-HLT-04; HB-DIS-10 |
| AT-60 | Fail a monitoring notification. Preserve the callback's actual outcome, expose the integration fault and continue unrelated scheduling without recursive `OnError`. | HB-HLT-04; HB-EXE-04 |
| AT-61 | Select different configured health policies. Use Healthcheck's binding API; Heartbeat applies no allowance arithmetic or scheduling change from health state. | HB-HLT-01; HB-HLT-02; HB-EXE-06 |
| AT-62 | Delay an owner-using reporting handoff. Work completion precedes ownership settlement; fixed delay begins at the latter. A subsequent settlement fact is detached, cannot retain the subscriber and is not a second callback outcome. | HB-HLT-04; HB-OWN-04; HB-CAP-05; HB-SCH-01 |
| AT-63 | Reuse an instance after a completed unregister. Give the new registration its own correlation lifetime and let Healthcheck determine any history continuity. | HB-REG-08; HB-HLT-02 |
| AT-64 | Race ordinary unregister/owner drain with application shutdown. Apply the global suppression rule to unstarted work; never resurrect it for the earlier drain. | HB-REG-07; HB-STP-01; HB-STP-02 |
| AT-65 | Pause a posted wrapper before its user start gate, then shut down. It must lose the gate, settle safely and never touch released dependencies later. | HB-STP-01; HB-STP-04 |
| AT-66 | Check public/API architecture. The consumer service exposes registration operations, no manual lifecycle/status API, and does not own failure counters or a second initialization facade. | HB-SCP-01; HB-OBS-05; HB-LIF-05; HB-ENG-04 |
| AT-67 | Suppress work at shutdown with no user phase started. Report lifecycle suppression; if withdrawal cleanup fails, report that actual integration fault rather than a fabricated callback failure. | HB-STP-02; HB-OBS-03; HB-HLT-03 |
| AT-68 | Exercise real host startup and early shutdown. Activation cannot deadlock on an event that waits for its own startup method, and dependencies are ready before execution. | HB-LIF-02; HB-LIF-07 |
| AT-69 | A callback throws known cooperative cancellation; another throws unexplained cancellation. Report cancellation versus failure as specified, independently of wait-token cancellation. | HB-OBS-04; HB-ERR-01 |
| AT-70 | Audit tasks, waits and ownership in runnable examples. No detached scope-using child work or `async void` recovery. No execution awaits its own drain or creates a dependency cycle through the shared capacity limit. | HB-OWN-01; HB-STP-04; HB-ENG-05; HB-ENG-07 |

Business-case verification should include an FX poller with temporary dependency failures, many startup registrations at the default interval, transient/scoped subscriber teardown, and application-owned deadline/recovery logic. These cases verify the separation of responsibilities; they do not move business SLA or allowance rules into Heartbeat.

## 14 Decision record and retained assumptions

### 14.1 Review decisions D01–D11

| Decision | Status in revision 0.2 | Applied position |
| --- | --- | --- |
| D01 — Interval and first execution | Default and capacity resolved; timing assumptions retained | Default recurring interval is 10 ms. Bounded dispatch compensates for simultaneous eligibility. Completion-based delay and optional `InitialDelay = 0` remain the explicit A01 interpretation. |
| D02 — Registration and reuse | Resolved | Object reference is the handle; optional nonunique name. Valid duplicates succeed without change, including while individually draining. Ordinary unregistration drains accepted work; reuse follows a completed asynchronous wait. |
| D03 — Lifecycle | Resolved | Lifecycle belongs to the application and DI integration. No consumer start/stop/dispose control or independent restart. One engine per application/container lifetime. |
| D04 — Application drain | Resolved; condition boundary made explicit | Stop new starts, suppress queued/unstarted work and await running work through handling/cleanup. Shutdown does not automatically cancel it. A02 states the condition-to-callback gate interpretation. |
| D05 — Results and errors | `OnError` purpose resolved; remaining outcome rules retained | `OnError` handles unhandled exceptions and may perform awaited recovery. Callback false never invokes it. One final result, cancellation classification, cleanup precedence and terminal-drain fault treatment use A04. |
| D06 — Failure allowance | Resolved | Healthcheck owns policy, monitor selection, failure allowances and arithmetic. Heartbeat binds through that API and reports facts only. |
| D07 — Watchdog | Deferred to Healthcheck definition | No Heartbeat-owned timeout/watchdog API in this revision. Preserve actual execution ownership for any later observation/cancellation integration. No watchdog timing proposal is silently adopted. |
| D08 — Capacity and inherited features | Capacity resolved; explicit override cap recorded | Use `GetMaxThreads` completion-port output, minimum 1 and maximum 16 for the calculated default. Configure `MaxConcurrent` through `EnviormentsOptions` and `CIS_HEARTBEAT_MAX_CONCURRENT`. A05 applies the 16 cap to explicit values as well. No priority/pause feature is inherited. |
| D09 — Observation surface | Resolved | Healthcheck owns snapshots, queries, counts, health state, exception retention and history. Remove Heartbeat `GetStatus` and its former status-error rules. Heartbeat keeps mechanical state and reports facts. |
| D10 — Framework and packages | Inherited, not reopened | Apply Core Library Implementation Guide 1.1 for the initial .NET target, configuration, naming/identity process, dependencies, consumer constraints and release gates. |
| D11 — Compatibility baseline | Resolved | This is a new library; GmCore is an example. No GmCore API preservation or compatibility shim is required. The new library's first published contract begins its permanent compatibility obligations. |

The additional lifetime decisions are incorporated into section 8: execution belongs to the registered object's owner context; idle registration must not promote transient/scoped lifetimes; accepted work is held strongly with an owner-coordinated disposal barrier; collected/unusable targets are retired automatically. Catching a disposal exception complements that barrier and does not replace it.

### 14.2 Assumptions kept visible for this revision

| ID | Concrete assumption | Where it applies |
| --- | --- | --- |
| A01 | Use completion-based fixed delay and retain optional `InitialDelay`, default zero. The separately agreed recurring default remains 10 ms. | HB-SCH-01; HB-SCH-02 |
| A02 | At application shutdown, finish an already-running condition but suppress its callback if the callback has not crossed its own start gate. | HB-STP-08 |
| A03 | An unhandled disposal exception from running condition/callback work is one failed execution fact plus automatic retirement. A disposal exception from subscriber error handling also retires the registration but remains a secondary fault. Pre-execution collection or owner closure is neutral lifecycle handling. | HB-DIS-11; section 9 |
| A04 | Retain the explained boolean/skip/cancellation result model, real-error precedence, one final outcome, secondary nonrecursive handler/cleanup errors and successful drain only on a proved completion barrier. | HB-CND-03; HB-CND-04; HB-OBS-02/03/04; HB-ERR-03/04; HB-STP-07 |
| A05 | The effective concurrency range is 1–16 for explicit setup and environment overrides as well as the calculated default. Invalid explicit values fail validation. | HB-CAP-04 |

These are the concrete assumptions used consistently throughout this review draft. They are not represented as separately confirmed answers and do not require another version of the full question list to continue reviewing the document.

### 14.3 Work to define next

**Healthcheck takes precedence next.** Its specification must establish policy/monitor binding, fact notification and handoff semantics, observation/retention ownership, failure policy and any watchdog capability. Kubernetes or other supervisor adapters belong with that work.

After that definition, finish the Heartbeat adapter details against the shared core baseline: the owner close-and-hold mechanism, precise hosting order, monitoring handoff, environment-section addition, public names, supported duration bounds and configuration issue codes. These are integration and pre-publication design tasks, not a request to revisit the settled D02–D11 positions.

## 15 Traceability and technical basis

### 15.1 Revision traceability

Requirement IDs in draft 0.1 were review references, not released contracts. Revision 0.2 preserves their topics where practical and changes the superseded behavior explicitly:

| Previous draft content | Revision 0.2 treatment |
| --- | --- |
| HB-REG-03 rejected duplicates | Successful duplicate no-op. |
| HB-LIF requirements and section 11 exposed manual lifecycle | Application/DI-owned activation, shutdown and disposal; registration-only consumer interface. |
| HB-STP-01/02/03 and AT-15/16 drained queued application-shutdown work | Suppress unstarted work; await running work. Keep ordinary unregistration's accepted-work drain separately. |
| HB-EXE capacity paragraph selected no numeric limit | HB-CAP-01–07 specify the agreed calculation, configuration, accounting and fairness. |
| HB-OBS-03/05 and section 9 defined streaks and snapshots | Healthcheck owns aggregation and observations; Heartbeat reports facts. |
| HB-HLT-02 proposed allowance vocabulary/arithmetic | No allowance policy in Heartbeat. |
| HB-WDG-02/04 proposed Heartbeat watchdog behavior | Retired from this module's scope pending Healthcheck definition; HB-WDG-03 retains the ownership invariant. |
| Section 11 status exceptions and old example lifecycle calls | Removed with the superseded consumer status/lifecycle surface. |
| HB-ENG-02/03 reopened framework/GmCore compatibility choices | Shared core baseline inherited; new library identity confirmed. |

The supplied `Heartbeat.zip` and its GmCore C# source are design references. This revision does not certify that example, preserve its behavior, or claim it was built and tested as a project. The reviewed requirements govern the new library.

### 15.2 Sources and interpretation

The review conversation supplies the product decisions; the shared guide supplies the inherited engineering baseline. Microsoft references below establish platform behavior such as weak references, disposal, cancellation and host ordering. The capacity formula, concurrency cap, timing assumptions and policy boundaries are project design choices, not recommendations attributed to those platform sources.

[^core]: **Core Library Implementation Guide**, specification 1.1, 8 October 2026, `core-library-implementation-guide-v1.md`. In particular: sections 1 and 3–7 for framework, package/dependency, C# and `EnviormentsOptions` conventions; section 7 for resolution/validation; the compatibility provisions and sections 18–19 for permanent baselines and release evidence. This is the existing shared CIS definition, not a new Heartbeat framework decision.

[^maxthreads]: Microsoft Learn, [ThreadPool.GetMaxThreads](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.getmaxthreads?view=net-10.0), accessed 10 October 2026. Documents the worker and completion-port outputs and their maximum-thread meaning.

[^time]: Microsoft Learn, [TimeProvider overview](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview), accessed 10 October 2026. Provides the built-in abstraction for time, elapsed timestamps and timers.

[^async]: Microsoft Learn, [Async return types](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types), accessed 10 October 2026. Explains awaitable task completion and the limitations of `async void` for caller observation.

[^host]: Microsoft Learn, [.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host), accessed 10 October 2026. Defines the startup/shutdown event sequence used when assessing adapter ordering. The additional pre-disposal barrier in this specification is an integration requirement, not an automatic guarantee of arbitrary application teardown code.

[^di]: Microsoft Learn, [Dependency injection guidelines](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines), accessed 10 October 2026. Covers scope/nonhierarchy rules, disposal ownership and captured disposable transient services.

[^context]: Microsoft Learn, [ExecutionContext](https://learn.microsoft.com/en-us/dotnet/api/system.threading.executioncontext?view=net-10.0), accessed 10 October 2026. Describes logical execution-context state; it does not provide a DI disposal lease.

[^weak]: Microsoft Learn, [Weak references](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/weak-references), accessed 10 October 2026. Describes collection eligibility and obtaining a strong reference for use.

[^tryget]: Microsoft Learn, [WeakReference.TryGetTarget](https://learn.microsoft.com/en-us/dotnet/api/system.weakreference-1.trygettarget?view=net-10.0), accessed 10 October 2026. Documents resolving the weak target when it remains available.

[^dispose]: Microsoft Learn, [Implement a Dispose method](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose), accessed 10 October 2026. Disposal is a resource-lifetime operation distinct from object reachability; this specification adds a cooperative owner barrier around it.

[^ode]: Microsoft Learn, [ObjectDisposedException](https://learn.microsoft.com/en-us/dotnet/api/system.objectdisposedexception?view=net-10.0), accessed 10 October 2026. Describes the exception and its `ObjectName` property. Automatic retirement and its outcome classification are this library's stated contract/assumption.

[^cancel]: Microsoft Learn, [Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads), accessed 10 October 2026. Describes cooperative cancellation and acknowledgment.

[^wait]: Microsoft Learn, [Task.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0), accessed 10 October 2026. Defines a task representing the bounded wait; ownership of the underlying work must be handled separately.
