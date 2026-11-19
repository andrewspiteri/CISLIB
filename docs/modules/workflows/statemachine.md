---
title: Enum state machines
type: document
status: Draft
cis:
  stable_id: cislib.document.modules.workflows.statemachine
---

# Enum state machines

`CIS.Workflows` provides reusable, in-memory state machines alongside its
[single-use Pipelines](pipeline.md). A machine accepts successive explicit
triggers, permits one admitted attempt at a time and commits a destination only
after successful selected work and owned cleanup. It adds no guards, hooks,
trigger queue, timers, retry, persistence or distributed coordination. See the
[portable contract](../../../contracts/workflows/statemachine/README.md),
[public contracts](../../../implementations/csharp/src/CIS.Core/Workflows/StateMachine/StateMachineContracts.cs)
and [result construction](../../../implementations/csharp/src/CIS.Core/Workflows/StateMachine/StateTransitionResult.cs).

## Compose one host

Call `builder.Services.AddCisStateMachines()` from `CIS.Workflows.Hosting` in the
normal service callback. This registers `IStateMachineFactory` and automatic
observations. It does not install a Pipeline factory; select
`AddCisWorkflows()` separately when handlers compose children. Repeated calls
and either registration order share one existing provider and workflow lifetime
bridge. Pipeline-only applications retain their existing collection profile.

With CISApplication, use its normal callback and await Start/Stop/disposal. With
a native host, register ordinary business hosted services first, then workflow
registration before cooperating CIS REST/Heartbeat/Startup bridges. Keep hosted
start/stop sequential. Only the supported native web-host tail may follow those
bridges. Shutdown closes admission early, stops the listener before the workflow
drain, then stops ordinary business services. A cancelled shutdown wait cannot
authorize dependency disposal while accepted work remains. Always await actual
host Stop before disposing its provider. User-owned resources destroyed on a
stopping signal must first join their own work.

## CountryData lookup

The [request-local example](../../../implementations/csharp/examples/CountryData/CountryLookupStateMachine.cs)
uses three states and two triggers:

```csharp
var builder = machines.CreateBuilder<CountryLookupState, CountryLookupTrigger, CountryLookupFlowContext>(
    "CountryLookup", context);
builder.State(CountryLookupState.Pending)
    .On<ResolveCountryLookup>(CountryLookupTrigger.Resolve, CountryLookupState.Resolved, "resolve");
builder.State(CountryLookupState.Resolved)
    .On(CountryLookupTrigger.Complete, CountryLookupState.Completed);

var machine = builder.Build(CountryLookupState.Pending);
```

`context` is a fresh class instance containing the identifier and local output.
The [endpoint](../../../implementations/csharp/examples/CountryData/CountryEndpoints.cs)
supplies its current scope for just the Resolve call:

```csharp
var result = await machine.TriggerInScope(
    CountryLookupTrigger.Resolve, httpContext.RequestServices, httpContext.RequestAborted);
if (result.Success)
{
    result = await machine.Trigger(CountryLookupTrigger.Complete, httpContext.RequestAborted);
}
```

Resolve DI-activates one handler. That handler builds and awaits a fresh existing
country-lookup Pipeline in the same scope and explicitly inspects `child.Success`.
Expected child failure is mapped to a failed action result, keeping Pending.
Its original child report is retained as one detached summary, without child
context, live steps or provider. The outer `Error` clears only its own response
draft; it never calls the child's handlers again. A successful child followed by
outer cancellation has no external writes to reverse in this example. General
handlers must compensate their own effects using retained results, not disposed
child steps.

Complete is direct: it creates no handler or scope. Completed means that lookup
preparation finished, not that an HTTP response was delivered. The route retains
the existing 200 country JSON, 400 `country.invalid_identifier` and 404
`country.not_found`. Unexpected operational failures remain server errors;
request cancellation still propagates at the HTTP boundary. Lookup reads only
the cached snapshot. Initial loading, readiness, periodic refresh and bounded
refresh diagnostics are unchanged; there is no session registry or Busy control
in the application.

An interrupted later Complete retains its own source, Resolved. Earlier
successful transitions are never unwound. This is separate from a failed Resolve,
whose source remains Pending.

## Definitions, results and cancellation

States/triggers must be ordinary enums with distinct declared values. Gaps and
nonzero values are valid; Flags, underlying-value aliases, undefined configured
values and duplicate source/trigger pairs are rejected. Explicit self-transitions
and terminal-only definitions are valid. `Build` snapshots mappings/options
without handler activation. Separate machines have independent gates and state;
building again does not clone a mutable context.

`Trigger` returns an immutable result for operational outcomes:

| Outcome | Meaning |
| --- | --- |
| Succeeded | Work and cleanup succeeded; the target committed. |
| Rejected | Invalid enum trigger or no mapping from the observed source. |
| Busy | Another attempt owns the instance; no queue or delayed replay. |
| Failed | Activation, handler, result-contract or disposal failed; source retained. |
| Cancelled | Execution cancellation was observed before its cutoff; source retained. |

**Cancelled completes the Trigger task normally.** Check `Success`/`Outcome`,
not just whether the await threw. This differs from Pipeline's existing outward
exception/cancellation contract. Configuration/argument errors, engine defects
and closed-host lifetime misuse still throw. Handler programming faults become
Failed results with the original exception retained for diagnostics.

Inspect source, intended target, committed current state, primary `Error`,
`HandlerReport`, `ErrorHandlingFailures`, `LifecycleFailures` and measured
`Duration`. Handler reports retain separate forward/compensation statuses and
nullable durations using the existing Pipeline phase-status vocabulary. Unentered
methods have no invented duration. Child cleanup diagnostics stay in the
example's `LastLookup` summary rather than being counted twice as outer failures.
Results contain no mutable context, handler or provider.

`LastTransition` is initially null and becomes the last completed admitted
attempt, including rejection. Busy and pre-cancelled calls return their own
results without replacing it. Previous results never change. `CurrentState`,
`CanTrigger` and `PermittedTriggers` inspect committed mappings only, without I/O
or reservation; eligibility does not predict handler success or prevent Busy.

The handler is fresh for each selected attempt and remains the same instance
through `Execute`, optional `Error` and disposal. Default calls own an attempt
scope. `TriggerInScope(trigger, provider, token)` borrows a scope for that call
only. Keep it alive through settlement; never capture it in the reusable machine
or context. Dispose the handler before an owned scope, prefer async disposal,
and leave injected dependencies to DI. A direct/rejected trigger activates no
handler.

The execution token is checked before work and after successful work. The final
success decision precedes disposal; later cancellation does not start another
compensation. Disposal failure prevents target commitment without calling Error
after disposal begins. Entered failed/cancelled work receives one Error using an
independent cooperative budget, default 30 seconds, configured by
`StateMachineOptions.ErrorHandlingTimeout`; `Timeout.InfiniteTimeSpan` disables its timer.
Expiry requests cancellation but never abandons active work. Original failures
stay primary when compensation or disposal also fails. Source retention is not
a guarantee that external effects were reversed.

## Public substitutes and observations

Consumers can implement `IStateMachine<TState,TTrigger>` and construct immutable
results through public guarded `StateTransitionResult<TState,TTrigger>.Create(...)`, failure and
handler-report constructors. The C# adapter dispatches through public
`IStateMachineScopeExecution<TState,TTrigger>` for callers needing scoped
execution, including substitutes; it does not downcast to the Core engine or
resolve a hidden provider. The existing
[outside-friend consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CountryData/CountryDataPublicConsumerTests.cs)
checks successful, failed and normally completed cancelled substitute results.

Logs use `CIS.Workflows.StateMachine`. Routine success/phase/rejection and
cancellation events default to Debug; `LogRoutineAsInformation` promotes
successful routine events. Expected work failure is Warning, unexpected faults
retain their original diagnostics, and terminal summaries carry secondary counts.
Reads are quiet. `LogCalls=false` opts out per definition without changing work
or metrics. Context/payload serialization and a second correlation scheme are
not introduced. Child Pipelines keep their distinct observations.

The [selected Metrics catalog](../../../contracts/metrics/statemachine.md) owns
three reusable instruments: `cis.statemachine.trigger.duration`,
`cis.statemachine.active.attempts`, `cis.statemachine.handler.duration`.
Every public operational call has its own known elapsed observation, including
Busy/pre-cancelled calls. Active counts only instance-gate admission through
actual cleanup; unentered handlers produce no phase observation. Identity remains
centrally authoritative. Read the [Metrics skill](../../../.github/skills/cislib-metrics/SKILL.md)
before changing schemas or collection.

CIS-0014 selects bounds of 16 machine aliases, 512 unique machine/source/trigger
tuples and 64 machine/handler pairs, admitted transactionally at Build even with
collection disabled. These are selected profile bounds, not source defaults.
The tuple domain includes all declared states/triggers plus the fixed invalid
trigger category; no numeric invalid values become labels. The literal declared
trigger `invalid` uses `enum:invalid`. Unaliased handler metrics use `handler`.
Configured/enum labels fit 256 UTF-8 bytes. The three stream reservations are
2560/16/384 combinations, without raising global limits or silently accepting
ordinary overflow.

The existing package consumer checks real Core/application components, local
provider, exact deployed hashes and build/folder-publish output. Its test-owned
entry is component/package evidence, not unchanged production-Main evidence.
Controlled Busy/fresh-child and skill-use stories belong in existing tests,
without application test flags. Native and usage results are recorded by the
change owner; authored source alone is not a passing result or release claim.
