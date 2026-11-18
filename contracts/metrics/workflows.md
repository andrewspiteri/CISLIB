# Workflow pipeline observations — v1

This catalog accompanies the [workflow requirements](../../planning/workflows/README.md)
and [C# instrumentation guide](../../docs/modules/metrics/workflows.md). Definitions
and authored checks are contracts; actual native results and remaining limits
belong to [CIS-0013](../../docs/changes/CIS-0013/implementation-plan.md).

| Logical name | Kind / unit | Dimensions | Reserved normal combinations |
| --- | --- | --- | --- |
| `cis.pipeline.execution.duration` | Timer / Seconds | pipeline, outcome | 48 |
| `cis.pipeline.active.executions` | Up/down counter / Count | pipeline | 16 |
| `cis.pipeline.step.duration` | Timer / Seconds | pipeline, step, phase, outcome | 384 |

`phase` is `execute` or `error`. `outcome` is `success`, `error` or `cancelled`.
Reported business failures and unexpected faults both use `error`; retained
reports and structured logs carry their distinct meaning. Handler cancellation
has its own phase outcome and never replaces the primary execution outcome.

One accepted Execute captures one complete module-lifetime three-handle binding.
Its overall duration and active interval include activation, forward work,
reverse handlers and actual owned step/scope disposal. Record one overall
histogram observation only after the final outcome is known. First completion
is final. Rejected repeated/concurrent invocations add no observations. The
histogram's count supplies completed executions; there is no duplicate counter.
If the clock fails and final duration is unavailable, settle active work and
publish the terminal log/report without inventing a histogram duration.

Each entered Execute/Error method supplies one phase duration through its actual
awaited completion. Unentered methods, constructors and disposal do not invent
step-duration samples. The runner measures with its monotonic clock once and
passes the identical seconds to its report and `ITimer.Record`; the collector
does not start an independent timer. Standard fourteen duration bounds apply
unless a normal fixed Metrics stream profile explicitly overrides them.

Build transactionally admits at most sixteen distinct pipeline names and
sixty-four distinct `(pipeline, metric-step-alias)` pairs per factory owner.
Repeat builds and duplicate registration positions reuse admitted metadata.
Failed admission changes neither retained set. These bounds hold with no
collector, disabled collection or logging opt-out. Metadata contains no live
execution, task, context, service or history.

Configured names compare ordinally, are nonempty printable Unicode and fit the
ordinary 256-UTF-8-byte value bound. Explicit Add aliases select the `step`
label. An omitted alias uses the literal `step`; the CLR display name remains
available in reports/logs. Positions distinguish duplicate registrations there,
without a new label. No execution ID, correlation ID, context value, error
code/message, exception type or arbitrary CLR name becomes a metric dimension.
There is no truncation, hashing or retained unknown-label fallback cache.

`AddCisWorkflows` installs passive opt-in metadata. The optional collector reads
the final composition before activating producers in either registration order,
then attaches the exact registered instrumentation owner. `CollectCoreModules=false`
or disabled Metrics suppresses the automatic binding. Runs accepted before
attachment remain unobserved; later attachment never replays them or creates an
unmatched active decrement. Mutable instrumentation belongs to one Metrics host.

The normal domain is `3P + P + 6R`, so P=16/R=64 reserves 48+16+384=448
combinations. Smaller explicit overrides fail setup instead of accepting normal
overflow. Unused hosts retain old catalogs. Default Core/incoming selection with
Workflows has twenty-two Core reservations, twenty-four total streams and capacity
sum 3,776. Adding configured REST with outgoing Auto gives twenty-six Core
reservations, thirty total streams and sum 4,864. Global 96-definition,
128-stream, 16,384-capacity and four-MiB encoded-response limits are unchanged.
SDK special/overflow points remain additional; this arithmetic is not a heap or
platform qualification claim.

Expected exported families are `cis_pipeline_execution_duration_seconds` and
`cis_pipeline_step_duration_seconds`, each with `_bucket`, `_sum`, `_count`
children, plus `cis_pipeline_active_executions`. Every sample retains central
`application` and `environment` identity, including overflow. Native exposition
verification remains separate from declaring these required spellings.
