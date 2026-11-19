# Workflows State Machine contract — v1

This contract maps the preserved [specification](../../../planning/workflows/sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md)
to selected P01–P12 in [CIS-0014](../../../docs/changes/CIS-0014/implementation-plan.md).
The public C# surface is `CIS.Workflows` in `CIS.Core`; native composition helpers
are in `CIS.Workflows.Hosting`. Actual execution observations and limitations
belong to [verification](../../../docs/changes/CIS-0014/verification.md).
Source, a contract or a compiling example alone does not establish qualification.

## Definitions and activation

`IStateMachineFactory.CreateBuilder<TState,TTrigger,TContext>(name, context, options)`
binds one non-null application-owned reference context. Configure
`State(source).On(trigger, target)` for a direct transition, or
`On<THandler>(trigger, target, alias)` for one fresh compatible DI handler.
`Build(initialState)` snapshots mappings, names and options without acquiring a
scope or constructing application work. Later builder edits cannot change a machine.

States and triggers use ordinary C# enums with one of the eight integral underlying
types. Gaps, negative values and large unsigned values are valid. Flags and aliases
sharing an underlying value are rejected, as are undefined configured values and
duplicate source/trigger pairs. A terminal-only definition is valid, including an
empty trigger enum. Explicit self-transitions run normally with `StateChanged=false`.
There is no implicit mapping, priority, guard, queue, retry, timer or history service.

The built `IStateMachine<TState,TTrigger>` supports successive calls and retains
only committed state and the last completed admitted result. It does not clone,
persist, roll back or dispose its context. Applications coordinate separate machine
instances/processes and external effects themselves.

## Admission, inspection and publication

Each `Trigger(trigger, token)` has its own identifier, measured duration and result.
A pre-cancelled call returns `Cancelled` without taking the instance gate or changing
`LastTransition`. An overlapping/reentrant call returns `Busy` promptly, without
waiting or queuing; it also leaves that report unchanged. Both capture source/current
from one committed-state observation. They construct no handler or scope.

A call acquiring the instance gate is admitted, including a subsequent invalid or
unmapped trigger rejection. Undefined triggers return `StateMachine.InvalidTrigger`;
declared but unmapped triggers return `StateMachine.TriggerNotAllowed`.
These admitted rejections do update `LastTransition` and have no selected target.

`CanTrigger` and `PermittedTriggers` inspect immutable mapping membership only.
They perform no application validation, allocation of services or I/O and reserve
nothing. A busy machine can still report `CanTrigger=true`. Previously returned
permitted-trigger collections remain unchanged. Separate property reads can straddle
a completed transition; the immutable per-call result is authoritative.

The source remains visible through activation, forward work, compensation and owned
cleanup. After successful work and its final cancellation checkpoint, forward success
is selected and disposal begins. Only successful owned cleanup permits publication
of the target and result as one completed snapshot. A self-transition is successful
without changing the enum value. Final publication, gate release and completion of
the public task precede release of the host drain count. No thread lock spans awaits.

## Handler and scope ownership

Only the selected handler is activated, once per attempt, through the existing
`ActivatorUtilities` path. An existing singleton registration of that handler is
not resolved. Dependencies retain ordinary DI lifetimes. The same fresh handler
receives both forward and compensation methods; no attempt-scoped service is
retained for a later trigger.

The default creates one attempt-owned asynchronous scope. The public C#
`TriggerInScope(machine, trigger, scopedProvider, token)` extension dispatches
through optional `IStateMachineScopeExecution<TState,TTrigger>`. That public
capability supports consumer mocks and supplies the provider for this call only.
The caller must keep its scope alive until the call actually settles; neither
the reusable machine nor the result retains that request provider.

The runner owns the activated handler and disposes it first, preferring asynchronous
disposal over synchronous disposal. Dependencies remain live through that disposal;
the owned scope then disposes its own services. A borrowed scope is never closed.
Remaining runtime-owned cleanup is attempted after an ordinary disposal failure.
A constructor that throws before returning owns cleanup of its private resources.
No handler `Error` runs unless its `Execute` was entered.

## Operational results and failure precedence

`Trigger` returns a normally completed task for all five operational outcomes,
including `Failed` and `Cancelled`. Definition/argument errors, engine invariant
failures and fatal failures still throw. Ordinary exceptions are captured at
activation, handler and owned-disposal boundaries, not flattened from an arbitrary
outer engine catch into business rejections.

| Outcome | Effect |
| --- | --- |
| Succeeded | Selected target committed after successful owned cleanup. |
| Rejected | Undefined/unmapped trigger; source retained. |
| Busy | Another call owns the instance gate; this call changes no state/report. |
| Failed | Operational construction, method, contract or disposal failure; source retained. |
| Cancelled | Requested execution cancellation observed before cutoff; source retained after cleanup. |

`TransitionActionResult.Succeeded()` has no error details. `Failed(code, message)`
requires a nonempty stable code; absent/whitespace explanation uses a safe fallback
while preserving that business code. Null tasks/results are handler contract faults.
An entered failed/cancelled handler receives `Error` once, after its actual forward
method terminates. It gets the same context, immutable `TransitionInfo` and original
`StateTransitionFailure`. Compensation failure remains secondary. Earlier successful
transitions are never compensated by a later failure.

The first observed failure wins before a later cancellation checkpoint. A forward
OCE is execution cancellation only when its token is the exact requested execution
token at the catch. A foreign or not-yet-requested OCE is `Failed`, even if the
caller subsequently cancels. Constructor/disposal OCEs remain phase faults.
A successful handler followed by checkpoint cancellation keeps its forward report
`Succeeded` and receives compensation. Cancellation after the success cutoff does
not reopen compensation; disposal failure can still prevent state commitment.

Compensation has an independent 30-second cooperative `ErrorHandlingTimeout`,
captured at Build. Infinite is explicit; finite values must be positive within
the native 4,294,967,294ms timer range. Expiry requests cancellation without abandoning
handlers. `CleanupBudgetExpired` records that fact even if the handler subsequently
succeeds. Expiry reporting follows the actual cancellation request. Registered
cancellation callbacks and the timer are joined before handler/dependency disposal;
callback failures are separate lifecycle diagnostics. Disposal has no forced timeout.

## Detached reports and public mocks

`StateTransitionResult` exposes machine/call identity, trigger, source, nullable
intended target, committed current state, outcome, derived `Success`/`StateChanged`,
primary failure, nullable known duration, optional handler report and separate
handler/lifecycle diagnostics. Busy/pre-cancelled reports do not claim mapping
resolution or selected work. Construction failure can have a selected handler
report with both methods unentered; direct transitions have no handler report.

Phase statuses use the existing Workflows forward/error vocabulary. Successful
forward work remains successful after compensation or disposal failure. Unentered
methods have absent durations, never fabricated zero-length calls. Timing faults
do not authorize abandoning work or inventing measurements. C# diagnostics may
retain the original exception; no result retains mutable context, handler or provider.

Public guarded `StateTransitionResult.Create` and failure/report constructors let
outside-friend consumers build valid mock results. Successful results require a
committed target without failure diagnostics; unsuccessful results retain source.
Busy/rejected results cannot represent selected work. Diagnostic collections are
copied read-only, and failure/status consistency is checked. Result properties have
no public setters or record-copy bypass.

## Pipeline, host and telemetry composition

A handler can await a fresh real Pipeline, preferably through its explicit borrowed
scope when dependencies must be shared. Inspect the child's `Success`: normal return
can mean an expected failed pipeline. A failed child already attempted its unwind;
outer compensation must not repeat it. A successful child is not automatically
unwound when the outer attempt later fails. Its scope/steps have ended; additional
compensation uses application-owned identifiers/results, never disposed child steps.
Pipeline's single-use and throwing/cancellation contract remains unchanged.

Explicit `AddCisStateMachines()` reuses the existing workflow admission/count/host
barrier and actual-owner failed-Build capture. The host count covers whole valid
public calls, including Busy/pre-cancelled reporting. The metric active count covers
only instance-gate admission. Closed-host calls are lifetime misuse and throw before
normal observation; there is no synthetic stopping outcome or substituted host token.
Callers retain control of execution cancellation and must await accepted work.

Automatic logs default routine phases/success/rejection/cancellation to Debug;
`LogRoutineAsInformation` promotes successful events and `LogCalls=false` disables
automatic events without changing work or metrics. Reads are quiet, context is not
serialized, original faults are recorded at origin and terminal events use counts.

The reusable instruments are `cis.statemachine.trigger.duration`,
`cis.statemachine.active.attempts` and `cis.statemachine.handler.duration`. Capture
one complete binding per call; record runner-measured seconds and omit unavailable
measurements. Every normal call has an overall observation; only admitted calls
add active work, and only entered methods produce handler observations.

Build transactionally admits at most 16 machine aliases, 512 distinct
machine/source/trigger-label tuples and 64 machine/handler pairs. It retains bounded
string unions, not enum-type/instance history. The domain includes all declared
states/triggers plus invalid-trigger rejection. Undefined values use `invalid`;
a declared trigger named `invalid` uses `enum:invalid`. Unaliased handlers use
metric alias `handler`; CLR display names stay in reports/logs. The three normal
stream capacities are 2560/16/384 within existing global policy, with authoritative
central application/environment identity and unchanged Pipeline-only catalogs.
