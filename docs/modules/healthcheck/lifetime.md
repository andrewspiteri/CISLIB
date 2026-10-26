# Collection, binding, and ownership

The service registry keeps detached state and weak references. The component must retain its monitor handle for as long as its contribution should exist. A runner similarly does not strongly retain an idle owner-held registration: retain the `IHealthCollectionRegistration` alongside the component. An unretained handle may be collected and its monitor withdrawn; repeated snapshots are not a lifetime mechanism.

Dispose explicitly when an owner ends. `Withdraw()` closes admission and removes a monitor contribution immediately. `DisposeAsync()` also awaits actual tracked work and owner protection. `WaitForDisposalAsync()` only waits; it does not initiate withdrawal. Canceling a caller's wait does not cancel or replace shared disposal completion and does not certify settlement.

## Owner-safe collection

An `IHealthCollector` supplies `TryAcquire(out IHealthCollectionLease? lease)`. Acquisition must be nonblocking and bounded. Success returns a nonnull lease; refusal returns null. The owner's lease protocol must prevent its actual dependencies from being disposed until `CollectAsync` and lease cleanup settle. Keep the owner context that produced those dependencies. Resolving a fresh scope from a root provider is not protection for the original owner.

```csharp
await using var runner = new HealthCollectorRunner(health);
// collector is the actual owning component implementing IHealthCollector.
await using IHealthCollectionRegistration registration = runner.Register(
    refreshMonitor, collector, new HealthCollectorOptions
    {
        IsRequired = true,
        Interval = TimeSpan.FromMinutes(5)
    });

await runner.StartAsync();
HealthCollectionAttempt initial = await registration.CollectAsync();
// Inspect the attempt and actual snapshot; task completion is not health success.
// Only after the component and application are ready for recurring work:
registration.ActivatePeriodic();
```

Construction and registration are inert. Start the runner explicitly before manual collection or periodic activation. `Interval = null` selects manual only; there is no default polling period. Periodic intervals must be positive and representable by the selected clock/timer. The first periodic attempt is due at activation; later eligibility is actual completion plus interval. Missed eligibility is coalesced. At most one actual operation per registration and at most `MaxConcurrentCollections` globally can hold capacity.

Manual callers join an in-flight attempt. A pre-canceled wait creates no work; cancellation of one later wait affects only that waiter. A stalled callback or lease disposal retains its actual slot and owner lease. Neither a timeout wrapper nor expected cancellation proves that work ended or permits overlap. Healthcheck does not force-abort work.

Withdrawal before the coordinated invocation boundary suppresses user collection, including already-posted wrappers. After invocation, tracked completion includes callback work, result/error handoff, actual lease disposal and state settlement. Owner refusal retires collection and withdraws its associated monitor as owner-unavailable, without inventing an unhealthy observation. Acquisition contract violations or exceptions are integration errors; they are not admitted callback outcomes. Every acquired nonnull lease is disposed.

Required collection must return a result matching the monitor policy; an admitted exception or malformed required result becomes one policy evaluation failure. Optional diagnostic collection does not change policy health and retains earlier values on failure. Expected lifetime cancellation is neutral for the failure streak. A result superseded by a newer applicable observation is discarded instead of overwriting it.

An executing collector may request withdrawal. It must not await its own registration, monitor, runner or service disposal barrier. The implementation rejects established self-waits using its explicit execution token. It cannot discover every detached task/async-flow dependency a caller may construct.

## Execution facts

`HealthExecutionBinder.Bind` accepts a suitable existing Core monitor or creates an owned consecutive-failure/availability monitor through the factory overload. Validate the binding before activating an external schedule. Availability binding requires `SuccessPublishesUsableData = true`; choose it only when success actually publishes usable data. An absent monitoring policy means an external scheduler creates no binding; it does not imply a default threshold.

The existing-monitor overload never transfers ownership. Disposing that binding releases its association and leaves the monitor caller-owned. Factory-created monitors belong to the binding; retirement withdraws them and disposal awaits their actual tracked settlement. Finish all final owner-dependent handoffs before reporting `Retired` or disposing an owned binding.

Use a fresh nonempty `RegistrationId` per registration lifetime and positive, non-wrapping execution IDs. Report the actual Outcome once, with its original error and actual work-completion time. Report Settled only after work/cleanup really settle; settlement never creates another outcome. A successful error handler does not erase the failed callback outcome, and a callback false can fail without invoking an error handler. Durations are measured monotonic elapsed milliseconds, not wall-clock subtraction.

The binder keeps bounded current/previous execution context and an accepted outcome high-water mark. Identical retained terminal repeats are Duplicate, older history is Obsolete, retired bindings return Retired, and retained contradictions raise a bounded integration error. Isolate reporting errors from callback/error-handler execution; do not invoke business work again, call OnError again, or count a second callback failure because a health handoff failed. See the [validation matrix](../../../planning/healthcheck/execution-facts.md).

The current repository has no implemented Heartbeat scheduler. Real monitor/fact binding is implemented separately from the pending real Heartbeat activation, duplicate registration reuse, OnError, owner-retirement and final-handoff evidence. The CountryList example uses explicit application priming and the actual collector runner; it does not provide a substitute Heartbeat API.

## Application shutdown

1. Call `BeginStopping()` so readiness becomes false and new service/runner/binding admission closes.
2. Stop admitting external scheduled and collector work. Withdraw the owner-held registrations.
3. Await actual running callbacks, cleanup, final owner-dependent reporting handoffs and acquired leases. Keep Healthcheck and owner dependencies alive until they finish.
4. Retire/dispose bindings and monitors, then dispose the enclosing owner scopes and dependencies.
5. Close pipe/HTTP reporting admission and settle handlers; dispose adapters and the service/root dependencies.

A canceled shutdown wait leaves real work outstanding. Application supervisor policy owns what happens next. Repeated stop/dispose shares terminal completion; an instance is not restarted as a new application lifetime. Removing the last failing monitor during shutdown can make aggregate health Healthy while readiness remains false because `IsStopping` is terminal.
