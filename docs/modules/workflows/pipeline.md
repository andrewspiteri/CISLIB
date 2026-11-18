---
title: Sequential workflow pipelines
type: document
status: Draft
cis:
  stable_id: cislib.document.modules.workflows.pipeline
---

# Sequential workflow pipelines

`CIS.Workflows` in `CIS.Core` executes ordered application steps and awaits
reverse error handling and owned disposal before publishing its terminal report.
It provides no retry, persistence, parallel stages or automatic transaction
rollback. Read the [portable contract](../../../contracts/workflows/pipeline/README.md)
and [public C# types](../../../implementations/csharp/src/CIS.Core/Workflows/Pipeline/PipelineContracts.cs).

Opt in inside the normal service callback with
`builder.Services.AddCisWorkflows()`, importing `CIS.Workflows.Hosting`.
This idempotent registration supplies `IPipelineFactory` from the existing
provider. Register application dependencies normally. Each `Add<TStep>()`
DI-activates a fresh concrete step for its position; it does not resolve a step
singleton or require a parameterless constructor/separate step registration.

Registration includes the awaited host lifecycle bridge. Console/library
consumers can use `Host.CreateApplicationBuilder()` with its normal host services,
then await host start, stop and asynchronous disposal. Keep scope validation and
build validation enabled on that actual provider. The explicit caller-scope
adapter borrows a scope from the same provider; it does not create another host.

The [CountryData lookup](../../../implementations/csharp/examples/CountryData/CountryLookupPipeline.cs)
uses a fresh context and the caller's actual request scope:

```csharp
var context = new CountryLookupContext(identifier);
var pipeline = pipelines.CreateBuilderInScope("CountryLookup", context, scopedServices)
    .Add<NormalizeCountryIdentifier>("normalize")
    .Add<FindCachedCountry>("find")
    .Add<PrepareCountryResponse>("prepare_response")
    .Build();

await pipeline.Execute(cancellationToken);
```

Here `pipelines` is injected, `context` is a fresh class instance, and
`scopedServices` is an explicit caller-owned scoped provider. The
[HTTP endpoint](../../../implementations/csharp/examples/CountryData/CountryEndpoints.cs)
passes `HttpContext.RequestServices` and `RequestAborted`. Successful aliases
retain their country JSON; whitespace yields 400 `country.invalid_identifier`
and unknown identifiers yield 404 `country.not_found`. Lookup never fetches
provider data. Readiness, initial loading, refresh and bounded refresh logging
keep their existing behavior.

For a missing country, `normalize` succeeds and `find` returns failure. The runner
awaits `find.Error`, then `normalize.Error`; neither method on `prepare_response`
is entered. Handlers clear only execution-local draft fields, without changing
the catalog or claiming to reverse external effects. A handler with no work can
return `Task.CompletedTask`.

## Own scopes and completion

`pipelines.CreateBuilder("CountryLookup", context)` creates one owned async DI
scope at execution entry. `CreateBuilderInScope` borrows the supplied provider
and its existing scoped dependencies. Keep that scope alive until execution
settles. Both paths own fresh step instances, dispose them in reverse construction
order, prefer async disposal and retain dependencies through unwind/disposal.
DI owns injected dependencies; steps must not dispose them. Borrowed scope
ownership never transfers to the pipeline.

`Build()` activates nothing and snapshots registrations, aliases and options.
Duplicate types remain separate registrations. Later builder changes cannot
alter a built instance; rebuilding does not clone its context. Use a fresh context
and built instance per run. Repeated/concurrent `Execute` on one instance is
rejected without changing the accepted execution.

Inspect `Status`, `Success`, original `Error`, `FailedStep` and ordered `Steps`.
Each registration has separate forward/error statuses, errors and nullable
durations. `ExecutedSteps` includes every entered forward method. Unentered
methods have no invented duration. `ErrorHandlingFailures` and `LifecycleFailures`
retain secondary diagnostics. Reports are detached snapshots without live steps
or providers. Do not retain execution-owned disposable services in the context.

| Primary outcome | After unwind and owned cleanup |
| --- | --- |
| Success | Return normally with `Succeeded`. |
| Reported step failure | Return normally with `Failed`. |
| Unexpected construction/forward fault | Propagate its original exception with `Failed`. |
| Observed execution cancellation | Propagate cancellation with `Cancelled`. |
| Disposal fault after forward success | Propagate the first disposal fault with `Failed`; no second unwind. |
| Secondary handler/disposal fault | Preserve the existing primary outcome and retain diagnostics. |

Inspect the same pipeline in a catch block. A Failed primary unrelated
`OperationCanceledException` is wrapped by `PipelineExecutionException`; its
original remains the inner cause and report exception. Execution checkpoints run
before activation, between steps and after the last success. Running work must
actually settle before unwind or disposal starts.

All eligible `Error` methods receive an independent cleanup token. The default
`PipelineOptions.ErrorHandlingTimeout` is a cooperative 30-second whole-unwind
budget; `Timeout.InfiniteTimeSpan` disables its timer. Expiry sets
`CleanupBudgetExpired` and requests cancellation. When automatic logs are enabled,
the warning is emitted at expiry even while a handler continues running. The runner still invokes every
remaining handler and awaiting uncooperative work. Caller cancellation does not
pre-cancel cleanup. There is no forceful timeout.

Await normal host/facade shutdown: it closes admission and joins accepted runs
before provider-owned dependencies are disposed. Standalone callers must join
their executions and retain borrowed resources. An application owner destroying
its own resources on a stopping signal must first join its own work.

## Automatic observations

Logs use category `CIS.Workflows.Pipeline`, stable events, execution correlation,
separate phase outcomes and one terminal summary. Contexts/payloads are not
serialized. Central filters apply. `PipelineOptions.LogCalls` defaults to true,
captured at Build. Set false for a frequent callback whose owner already provides
bounded diagnostics; reports, metrics and error handling remain intact.
CountryData uses the pipeline on cached lookup, not periodic refresh.

| Logical instrument | Unit and dimensions |
| --- | --- |
| `cis.pipeline.execution.duration` | Seconds; `pipeline`, `outcome` |
| `cis.pipeline.active.executions` | Count; `pipeline` |
| `cis.pipeline.step.duration` | Seconds; `pipeline`, `step`, `phase`, `outcome` |

Overall duration includes activation, unwind and disposal. Phase durations cover
entered methods; reports and metrics receive the same measured elapsed time.
Outcomes are `success`, `error`, `cancelled`; phases are `execute`, `error`.
The exporter produces `cis_pipeline_execution_duration_seconds_count` and
`cis_pipeline_step_duration_seconds_count`, plus sums/buckets. Use histogram
counts rather than adding a duplicate completion counter. Central identity stays
authoritative.

Names/aliases are configured metadata, never request identifiers. One factory
admits 16 pipeline names and 64 pipeline/step-alias pairs transactionally at Build,
including with collection disabled. Names are printable, ordinal and at most
256 UTF-8 bytes. An omitted alias uses the type display name in reports/logs and
`step` in metrics. Conditional stream reservations are 48/16/384 combinations;
inadequate explicit overrides fail setup. Read the
[Metrics skill](../../../.github/skills/cislib-metrics/SKILL.md) before changing
schemas or capacity. Telemetry failures never change work.

## Consumer evidence

The existing [package consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CountryData/CountryDataPackageConsumerTests.cs)
uses actual CountryData components with a test-owned entry, local provider, exact
package hashes and build/folder-publish execution. This is component/package
composition, not unchanged production-Main smoke evidence. The
[outside-friend consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CountryData/CountryDataPublicConsumerTests.cs)
compiles three fresh DI-dependent steps against public APIs, checks middle-failure
reverse handlers and retains its caller scope through owned step cleanup.
Native and skill-use results belong to the change owner's verification record;
authored source alone is not a passing result or release claim.
