# Workflows Pipeline contract — v1

This contract binds the preserved [source specification](../../../planning/workflows/sources/CISLIB_Workflows_Pipeline_Implementation_Specification_v1.0.md)
to approved P01–P12 in the [delivery plan](../../../docs/changes/CIS-0013/implementation-plan.md).
The C# surface is `CIS.Workflows` in `CIS.Core`; composition adapters are in
`CIS.Workflows.Hosting`. Current execution evidence and its limitations belong
to [CIS-0013 verification](../../../docs/changes/CIS-0013/verification.md).
Authoring this contract does not establish native or release qualification.

## Definition and one accepted execution

`IPipelineFactory.CreateBuilder(name, context, options)` supplies a builder with
the existing host's dependencies. The context is a non-null reference object
owned by the application. `Add<TStep>(alias)` appends a concrete implementation of
`IPipelineStep<TContext>` without requiring a parameterless constructor. Duplicate
types represent distinct ordered registrations. Names and explicit aliases are
stable configuration, never request identifiers.
The v1 display-name bound is 256 UTF-8 bytes, including the default CLR display
name; supply a short explicit alias for an unusually long constructed type name.

`Build()` snapshots order, names and `PipelineOptions`, and validates finite
definition metadata. It acquires no scope and constructs no business object.
An empty definition, invalid type/name or unsupported cleanup duration is rejected.
Later builder/options changes cannot change a built instance. Builders do not
support concurrent mutation; neither builder reuse nor Build clones the context.

Each built `IPipeline<TContext>` accepts exactly one `Execute(token)` invocation.
Its operation is published before activation or a user prologue can reenter.
Concurrent, reentrant and later calls reject without changing the accepted run
or adding observations. A pre-cancelled accepted invocation consumes the instance,
becomes `Cancelled` and acquires no scope. A host whose admission is already closed
rejects before claiming the instance; it remains `NotStarted`, with no run telemetry.

There is no scheduler, retry, branching, persistence, transaction manager or global
execution history. Independent instances can execute concurrently with separate
activation, entered-step stacks and report state. Sharing a mutable context between
those instances is the application's responsibility.

## Activation and ownership

Execution first activates every registration in order, then invokes forward work
sequentially. C# uses `ActivatorUtilities.CreateInstance` for fresh instances; a
registration of the step itself as a singleton is not resolved. Constructor
dependencies retain their ordinary container lifetimes.

The default path creates one asynchronous execution scope. The explicit C#
`CreateBuilderInScope(name, context, scopedProvider, options)` adapter borrows the
supplied provider and never closes its scope. A new scope does not inherit request
scoped instances. The caller must keep a borrowed scope alive until execution
actually settles.

No `Execute` or `Error` runs when a later activation fails. Every successfully
returned step is still disposed, including constructed registrations never entered.
A throwing constructor owns cleanup of resources it acquired before returning;
the runner cannot dispose an instance it never received.

The pipeline owns activated step instances. It disposes them in reverse construction
order, preferring `IAsyncDisposable` over `IDisposable`, and attempts remaining
step disposals after a failure. Injected dependencies remain DI-owned. Steps stay
inside the live execution scope through compensation and step disposal; the owned
scope is then disposed once. The runtime does not replace DI's own disposal policy.
Caller context values and borrowed services are not disposed by the pipeline.

## Forward work and reverse handlers

Each entered step receives the same context reference. `PipelineStepResult.Succeeded()`
has no failure details; `Failed(code, message)` requires a nonempty stable code and
represents an expected business failure. A null task or null/invalid result is a
contract fault, not success.

Forward admission stops at the first unsuccessful result, unexpected exception or
observed execution cancellation. The actual current invocation must settle before
reverse work starts. Every entered registration receives exactly one `Error`,
including the failing registration first, then earlier registrations in reverse
order. Unentered registrations never receive a handler. Each handler gets the
same immutable original `PipelineFailure` and the same independent cleanup token.
Handler faults/cancellation are recorded separately and do not prevent later
handlers. Compensation does not restore context snapshots or guarantee reversal
of external effects. Full forward success invokes no handlers.

## Cancellation and completion

The caller's token is the execution policy; the module adds no forward timeout and
does not substitute a host stopping token. Checkpoints precede scope acquisition,
each activation and each forward invocation, and follow each successful invocation,
including the last. A checkpoint cancellation can leave completed method reports
`Succeeded`; no cancelled method or duration is fabricated.

Returned failures and thrown faults are frozen before a subsequent checkpoint.
Only a forward OCE carrying the exact requested execution token is accepted as
execution cancellation. Attribution is made at the catch, so later cancellation
cannot relabel a foreign or not-yet-requested OCE. Constructor and disposal
exceptions remain phase faults. After the final success checkpoint commits forward
success, cancellation arriving during disposal does not initiate compensation.

`ErrorHandlingTimeout` defaults to 30 seconds for the whole unwind. Its token is
independent of execution cancellation. `Timeout.InfiniteTimeSpan` is supported;
finite values must be positive and within the native timer's 4,294,967,294ms limit.
The budget starts at unwind, not at execution entry. Expiry requests cancellation
without abandoning work; remaining handlers are still invoked with that cancelled
token. Only a handler OCE carrying that requested cleanup token has `Cancelled`
handler status. An ignoring handler can succeed after expiry; the independent
`CleanupBudgetExpired` fact remains true. Disposal has no forceful timeout.

| Primary situation | Final status | Outward completion after owned cleanup |
| --- | --- | --- |
| All forward work and disposal succeed | Succeeded | Normal return. |
| Expected unsuccessful result | Failed | Normal return, even when secondary cleanup faults exist. |
| Unexpected forward/contract/construction fault | Failed | Original fault, after applicable unwind and disposal. |
| Observed execution cancellation | Cancelled | Cancelled task, after applicable unwind and disposal. |
| Disposal fails after forward success | Failed | First disposal fault; no new unwind. |
| Handler/disposal failure after an existing failure | Original Failed/Cancelled | Original completion behavior and separate secondary diagnostics. |

A failed primary OCE is the narrow C# exception: outward completion faults with
`PipelineExecutionException`, whose inner exception is the original OCE. The report
retains the original exception. Ordinary originating faults keep their identity
and stack. Handler exceptions never replace the primary completion policy.

## Reports and lifetime

Overall states are `NotStarted`, `Running`, `Unwinding`, `Succeeded`, `Failed` and
`Cancelled`. Activation, method work, compensation and owned disposal remain
nonterminal. Reports and final duration are published after actual owned cleanup;
the awaited operation provides the final consistent view.

Each configured registration has a zero-based position, stable display name,
independent forward/error statuses, original phase failures and nullable durations.
Never-entered methods have no duration. A successful forward method remains
successful when its later handler fails. `ExecutedSteps` includes the failed or
cancelled entered registration; `FailedStep` derives only from a failed forward
status. Construction/checkpoint/disposal failure can therefore have no failed step.

Collections are detached read-only snapshots, safe to inspect concurrently.
The pipeline retains context, reports and original diagnostic exceptions, while
releasing activation/service/provider references after cleanup. An application
must not put execution-scoped services into its retained context expecting them
to remain usable. Faulting timing infrastructure must not skip eligible handlers;
unavailable measurements are not replaced by invented zero-duration samples.

## Existing-host and observational integration

`AddCisWorkflows()` is explicit and idempotent on the existing service collection.
Hosting closes admission early, then joins actual accepted work after the native
listener stops and before ordinary business hosted teardown/provider disposal.
Failed-start/late-Build cleanup also retains the actual instantiated owner.
A local active count and zero-drain completion suffice; no run/task registry is
required. Applications destroying resources directly on stopping signals still
must await their own executions. Standalone provider disposal is not a substitute
for joining unawaited work.

Automatic logging uses existing filters/correlation/writers and emits no context
serialization. Captured per-definition `LogCalls=false` suppresses automatic events
for callers with a frequent-callback reporting policy. It changes no business,
health, report or Metrics outcome. Ordinary telemetry failures cannot alter work
or interrupt handlers; there is no per-run flush or second telemetry provider.

The three reusable instruments are `cis.pipeline.execution.duration`,
`cis.pipeline.active.executions` and `cis.pipeline.step.duration`. The runner
measures with its injected monotonic clock and records those same seconds to
reports and Metrics. The operation observation includes activation, unwind and
owned cleanup; phase observations include only actually entered Execute/Error.
One complete binding is captured at accepted entry and used through settlement.
Rejected invocations have no second active or terminal observation.

Metric labels use finite configured pipeline/step aliases, phase `execute`/`error`
and outcome `success`/`error`/`cancelled`, with central application/environment.
Unaliased registrations retain type display names in reports/logs but aggregate
under metric alias `step`. The selected profile admits 16 pipeline names and
64 pipeline/alias pairs transactionally at Build, even with collection disabled.
Its three reserved normal capacities are 48, 16 and 384 combinations. Existing
global limits and optional provider ownership remain authoritative. No execution
IDs, context values, exception text or arbitrary error codes become metric labels.
