# Shared local jobs runtime

`JobRuntime` is the one local owner used by `ContinuousJobRunner` and
`ScheduledJobDispatcher`. It lives in Core and creates no provider, security
evaluator, calendar engine or second DI container. Its constructor captures the
explicit `IJobCatalog`, `IJobExecutionScopeFactory`, optional immutable initial
configuration, fixed admitted-run source/evaluator and optional `TimeProvider`.
It performs passive configuration validation; `StartAsync` validates enabled
scheduling capabilities and available IANA zones.

```csharp
await using var runtime = new JobRuntime(catalog, workloadScopes, configuration,
    runSource: admittedSource, scheduleEvaluator: optionalEvaluator);
await runtime.StartAsync(startupCancellation);

var continuous = new ContinuousJobRunner(runtime);
var scheduled = new ScheduledJobDispatcher(runtime);
```

Constructing these objects starts no job. Select explicit continuous resources or
dispatch trusted admitted runs. New definitions and the host are disabled by
default. A healthy eligible standby does not need to hold a grant to be ready.
The application retains ownership of the catalog's services, scope factory,
coordination runtime/provider and database source.

## One actual admission owner

The runtime keeps three distinct reservations:

- A continuous duty lifetime owns its stable scope/job/resource locally during
  standby, ownership, idle, drain and any uncooperative callback. Scheduled work
  owns that same key through actual callback and scope settlement. No other local
  mode or occurrence can bypass it after cancellation or lease loss.
- Business capacity is reserved before acquisition/iteration preparation. Actual
  entered callbacks are counted separately and retain their slot through scope
  cleanup. A scheduled dispatch obtains immediate capacity before acquiring a
  lease, so a local queue never retains hundreds of lease-holding occurrences.
- Pending scheduled work is bounded before authentication/admission validation.
  It transfers out of pending when the callback enters, or releases after refusal
  and actual pre-admission cleanup. The source keeps authoritative durable work.

Configured default limits are 32 admitted continuous duty lifetimes, eight business
reservations and 128 pending scheduled dispatches. Continuous standby lifetimes
also consume the conservative local duty bound. The separate coordination runtime
counts its managed handles/unresolved acquisitions. Idle releases business capacity
while retaining its duty and grant.

Every new callback rechecks current enablement, mode/binding epoch, ownership and
current global/category capacity after asynchronous scope preparation. Category
usage derives from live reservations and the current accepted category mapping.
Reassignment cannot evade a hard ceiling. Reducing a ceiling preserves existing
callbacks but prevents new entry until usage fits. Already entered callbacks keep
their immutable execution settings.

Continuous capacity waiters use bounded FIFO opportunities, skipping currently
ineligible categories. A MoreWork iteration releases its actual scope/slot and
yields before joining the next opportunity. There is no catalog polling loop or
dedicated thread/timer for every registered definition.

## Existing trusted scopes and permissions

The application explicitly supplies `IJobExecutionScopeFactory`. An implementation
can own the published `SecurityHostContextScope`, using already-validated native
workload authentication. It must not project a serialized `CallerContext` or
inherit ambient HTTP authority.

For each independent scope, the runtime resolves the actual target using scoped
`ICoordinationTargetResolver`, requires the exact environment/scope/job/resource,
and checks all registered `JobDefinition.ExecuteCapabilities` through the existing
scoped `ICapabilityAuthorizer`. Acquisition then uses public
`ICoordinationService`, which still performs its own mapped capability checks.
The singleton retains no scoped authorizer or handler.

The application resolver must bind one coordination key consistently to its actual
protected resource. A returned grant must also match the requested key and the
canonical local owner application/instance. A mismatched grant is rejected; the
runtime never releases a grant for an unrequested different key as cleanup.
An accepted grant stays attached even if an optional diagnostic clock observation
fails; unavailable observation time is not fabricated.

Public service substitution supports consumer tests. `ManualCoordinationLease`
and `ScriptedCoordinationService` provide explicit test facts and no native
authorization, exclusivity, automatic renewal or SQL guarantee.

## Complete configuration revisions

`Configuration` exposes the last accepted immutable `JobConfigurationSnapshot`.
`TryApplyConfiguration` validates the complete candidate before adopting a strictly
newer revision atomically. Invalid updates leave it unchanged and populate bounded
`RejectedConfiguration`; closed/stale updates return false. Use the existing
Configuration coordinated-reader/change adapter to capture retained settings.

Continuous settings change between iteration reservations. Disabling a job or
category stops new admissions and requests finite drain. Changing its execution
mode or local scheduled-activation binding drains the old duty before another
local callback may enter. Source/evaluator instances are fixed for this runtime;
replacement requires actual stop/drain and a new runtime composition.

Already admitted scheduled occurrences retain their original immutable settings,
operation identity and source validation. After a supported local reactivation,
an old admitted occurrence may still execute through the same compatible source.
Its timeout is the admitted timeout, including null; current category defaults
cannot rewrite it. The trusted scheduler resolves defaults into the admitted
snapshot when appropriate. Current local disablement and hard capacity still gate
new entry. Schedule/category edits never mint a new operation or ownership key.

## Shutdown and passive observations

`BeginStopping` closes new admissions, marks duties draining and requests bounded
graceful renewal through their leases. Their admission cancellation signals do not
cancel an already entered callback immediately. At the managed lease's finite
deadline, ownership loss requests cooperative stopping and renewal ends. The
callback can continue physically; its task, scope, key and execution slot remain
owned until actual settlement. Original-transaction guarding enforces correctness.

`StopAsync` joins actual duty, invocation and pending work, including cancellation
callbacks. Its optional token cancels only that caller's wait. It never certifies
completion at a host timeout. `DisposeAsync` joins the same boundary. Do not dispose
the factory, coordination provider or database source while this barrier remains
pending. Startup cancellation is operation-specific; a successfully started duty
does not inherit the first caller's token as its later business lifetime.

`CaptureStatus` distinguishes reservations, actual entered invocations by mode,
pending dispatches and the latest cleanup diagnostic. `CaptureSnapshot` returns
bounded pages of live targets plus at most one latest completed observation per
registered definition. It retains faults across a large catalog without an event
history or an unbounded resource-ID cache. Observed owner/expiry data authorizes
nothing. Neither method queries a provider, executes business work or computes
calendar next-run times.

Known business completion remains separate from lifecycle/cleanup diagnostics.
Committed iteration/occurrence results survive later cancellation, lease loss or
ordinary scope-disposal failure. Unknown remains Indeterminate. A cleanup fault
can stop a continuous duty without rewriting the established iteration outcome.
Internal instrumentation counts real callback entry through actual scope cleanup,
uses finite success/failed/cancelled/ownership_lost/indeterminate outcomes, and
observes no completed duration for a callback that has not settled.
