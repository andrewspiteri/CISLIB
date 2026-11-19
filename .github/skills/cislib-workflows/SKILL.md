---
name: cislib-workflows
description: Use, implement or review CIS.Workflows pipelines and enum state machines in CISLIB, including sequential execution, state-dependent triggers, DI ownership, reports, cancellation and automatic observations.
---

# CISLIB Workflows

Choose the facility by its lifetime: a built Pipeline executes once; a built
State Machine accepts successive explicit triggers. Use the Pipeline guidance
below for ordered steps, or the State Machine section for per-state mappings.
Both use the existing host, Logging and Metrics rather than a separate engine.

Read [AGENTS.md](../../../AGENTS.md), the
[pipeline contract](../../../contracts/workflows/pipeline/README.md) and
[consumer guide](../../../docs/modules/workflows/pipeline.md). Check the actual
[public API](../../../implementations/csharp/src/CIS.Core/Workflows/Pipeline/PipelineContracts.cs)
before using names from older proposals. The compiling application example is
[CountryData's cached lookup](../../../implementations/csharp/examples/CountryData/CountryLookupPipeline.cs),
with request composition in its
[endpoint](../../../implementations/csharp/examples/CountryData/CountryEndpoints.cs).

## Pipeline: define and execute

Opt in using `AddCisWorkflows()` from `CIS.Workflows.Hosting` in the normal service
callback. Obtain the injected `IPipelineFactory`; use a stable configured name
and fresh non-null class context. Add concrete `IPipelineStep<TContext>` types in
order, then `Build()` and await `Execute(token)`. Build snapshots the definition
and options without activation. Each built instance accepts only one execution;
rebuilding does not clone the context.

Steps are freshly DI-activated for each position, including repeated types.
Register their dependencies normally, without singleton step resolution or a
parameterless-constructor constraint. All activation finishes before forward work.
The default `CreateBuilder` path owns one execution scope. The C# extension
`CreateBuilderInScope(name, context, scopedProvider, options)` explicitly borrows
the caller's scope. Keep that scope alive until execution settles; do not assume
a new scope shares request-scoped instances.

Return `PipelineStepResult.Failed(code, safeMessage)` for an expected failure.
`Error` runs on the failing entered step first, then earlier entered steps in
reverse order. It never runs on unentered steps. Handle partial work using the
same context and original failure; compensation does not restore context or
guarantee external rollback. Keep compensation separate from disposal. The
pipeline disposes its activated steps; DI owns their injected dependencies.

Expected failure returns normally after cleanup. Inspect `Status`, `Error`,
`FailedStep`, ordered `Steps`, `ErrorHandlingFailures` and `LifecycleFailures`.
Inspect the same report in a catch block for exceptional/cancelled completion.
Preserve separate forward/handler statuses and absent durations for unentered
methods. A Failed primary unrelated OCE uses `PipelineExecutionException`, retaining
the original inner/report exception; cleanup must not replace the primary cause.

Pass execution cancellation through work and await actual settlement. The
independent `ErrorHandlingTimeout` defaults to 30 seconds and supports explicit
infinity. Its expiry requests cancellation but still invokes all eligible
handlers and awaits them. Never detach uncooperative work or dispose its services.
Await host/facade Stop before provider disposal; caller-owned teardown must also
join the executions using those resources.

## State machines

Read the [state-machine contract](../../../contracts/workflows/statemachine/README.md),
[consumer guide](../../../docs/modules/workflows/statemachine.md) and
[public API](../../../implementations/csharp/src/CIS.Core/Workflows/StateMachine/StateMachineContracts.cs).
The compiling [CountryData machine](../../../implementations/csharp/examples/CountryData/CountryLookupStateMachine.cs)
composes a fresh lookup Pipeline; its Completed state means lookup preparation,
not confirmed HTTP delivery.

Opt in with `AddCisStateMachines()` from `CIS.Workflows.Hosting` and inject
`IStateMachineFactory`. Select `AddCisWorkflows()` separately for child Pipelines;
both registrations share one provider/lifetime bridge. Use a stable configured
name, ordinary state/trigger enums and a non-null class context. Configure
`CreateBuilder<TState,TTrigger,TContext>(name, context, options)`,
`State(source).On(trigger, target)` or `On<THandler>(trigger, target, alias)`, then
`Build(initialState)`. Flags, enum aliases, undefined configured values and
duplicate source/trigger pairs are rejected. Build activates no handlers.

Await `Trigger(trigger, token)` for each event. Overlapping/reentrant calls return
Busy without queuing. Every operational result, including Cancelled, completes
the Trigger task normally after actual cleanup. Check `Success` and `Outcome`;
do not apply Pipeline's outward cancellation/exception contract to Trigger.
Inspect source, intended target, committed current state, primary failure,
handler report and secondary/lifecycle diagnostics. Earlier results are immutable.
Busy/pre-cancelled calls do not replace `LastTransition`; admitted rejections do.
`CanTrigger`/`PermittedTriggers` inspect mappings, not handler success or admission.

Only the selected handler is freshly DI-activated for the attempt. It receives
the same context/transition through Execute and Error, then is disposed before
its owned scope. Source state stays visible through work and owned cleanup;
disposal failure retains it without another Error pass. Error compensates only
this entered attempt, never earlier successful transitions. The independent
30-second `ErrorHandlingTimeout` is cooperative, supports infinity and never
abandons uncooperative work. Retaining a state does not prove external rollback.

The C# `TriggerInScope(trigger, scopedProvider, token)` adapter borrows a provider
for one call. Keep that scope alive until settlement; never save a request
provider in the reusable machine or context. Consumer substitutes may implement
`IStateMachine` and public `IStateMachineScopeExecution`; guarded
`StateTransitionResult<TState,TTrigger>.Create(...)` and public diagnostic
constructors support mocks without Core internals or friend access.

Inside a handler, build a fresh child Pipeline for that attempt and await it.
Normal child return can be an expected failure: inspect `child.Success` and map
failure to `TransitionActionResult.Failed`. The child already attempted its own
unwind; never repeat that compensation or call disposed child steps. After child
success followed by outer failure/cancellation, compensate additional effects
through retained business results, or explicitly accept them. Borrow the current
attempt scope for a child that shares its transaction/dependencies. Retain only
detached child diagnostics, not a provider or unbounded history.

Routine State Machine events default to Debug; successful-event Information
promotion is `LogRoutineAsInformation`. Reads are quiet. `LogCalls=false` affects
that definition only; explicitly composed child Pipelines keep their own setting.
The [State Machine metric catalog](../../../contracts/metrics/statemachine.md)
counts every public call, with active attempts only after instance admission.
Budget the full declared state/trigger domain, including fixed invalid-trigger
labels and rejected combinations, within the selected 16-name/512-tuple/64-handler
shared profile. Invalid numeric trigger values never become distinct labels;
unaliased handler metrics use `handler`.

## Keep observations and verification aligned

Reuse each facility's automatic logs and three reusable metric handles. Read the
[Metrics skill](../cislib-metrics/SKILL.md) before changing instrumentation.
Use bounded configured aliases, never request IDs, URLs or context values as
labels. Omitted step aliases use `step` in metrics. Preserve central identity,
report-matching elapsed seconds and entry-captured bindings. `LogCalls=false` is
the explicit per-definition choice for a frequent callback with its own bounded
reporting policy; it does not disable metrics or change execution.

Use the existing native test projects and package infrastructure for changed
boundaries. The [public consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CountryData/CountryDataPublicConsumerTests.cs)
shows three DI-dependent steps, middle failure, reverse handlers and borrowed
scope ownership outside friend assemblies. Keep this guide, example and skill
aligned with actual code. Authored tests alone are not passing evidence. Do not
add a scheduler, retry engine, persistence, another tutorial app or skill harness
to consume these implemented workflow facilities.
