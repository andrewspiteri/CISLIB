---
name: cislib-workflows
description: Use, implement or review CIS.Workflows sequential pipelines in CISLIB, including ordered steps, Build and Execute, DI scope ownership, reverse Error handling, execution reports and automatic observations.
---

# CISLIB Workflows

Read [AGENTS.md](../../../AGENTS.md), the
[pipeline contract](../../../contracts/workflows/pipeline/README.md) and
[consumer guide](../../../docs/modules/workflows/pipeline.md). Check the actual
[public API](../../../implementations/csharp/src/CIS.Core/Workflows/Pipeline/PipelineContracts.cs)
before using names from older proposals. The compiling application example is
[CountryData's cached lookup](../../../implementations/csharp/examples/CountryData/CountryLookupPipeline.cs),
with request composition in its
[endpoint](../../../implementations/csharp/examples/CountryData/CountryEndpoints.cs).

## Define and execute

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

## Keep observations and verification aligned

Reuse automatic run/phase logs and the three module metric handles. Read the
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
to consume this sequential facility.
