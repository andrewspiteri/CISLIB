# CISLIB Metrics — guide and instructions for future agents

**Version:** 1.0  
**Date:** 17 November 2026  
**Audience:** agents implementing Metrics, instrumenting a CISLIB module, or adding metrics to an application  
**Canonical design:** [CISLIB_Metrics_Implementation_Specification_v1.0.md](CISLIB_Metrics_Implementation_Specification_v1.0.md)

## 1. Start from the actual implementation

The specification describes a new module. Its example API names and proposed repository paths are not proof that those APIs already exist. Before writing application or module code, inspect the current checkout, its `AGENTS.md`, the actual public metric types, module documentation, and working examples. Use the implemented contract when it exists. If it materially conflicts with an accepted requirement, identify the conflict and resolve it through the current repository workflow; do not silently invent another API.

CISLIB will have multiple language bindings. C# is first, but normal application/module code must use CISLIB-owned metric definitions, handles, dimensions, and timing helpers. Keep platform hosting and exporter details in the binding/integration package. In C#, Core must not acquire an OpenTelemetry collector/exporter dependency.

The proposed repository destinations are `contracts/metrics/README.md`, `docs/modules/metrics/README.md`, and `implementations/csharp/examples/metrics/`. Once they exist, keep this guide, the skill, and the working example aligned with their actual version. Before they exist, use the supplied specification for implementation work.

## 2. Choose the measurement before writing code

First identify the question the metric answers and the event or state that owns the answer. Record the meaning, unit, timing boundary, dimensions, expected cardinality, and reset/lifetime in the module’s metric catalog.

| Need | Use | Example |
|---|---|---|
| Accumulating occurrences or quantity | Counter | Completed business events, bytes actually processed |
| Additive work entering/leaving | Up/down counter | Active operations; `+1` at entry and `-1` in every exit path |
| Current absolute state | Gauge | Queue depth or currently allocated bytes |
| Already-owned current state | Observable gauge | Existing spool utilization or discovered type count |
| Distribution of values | Histogram | Payload size in bytes |
| Operation latency | Timer, backed by a histogram | Job duration in seconds, with final bounded outcome |
| Calls/second or bytes/second | Downstream `rate` of a cumulative counter/histogram count | Prometheus query; no application sampling loop |
| Incoming API request counts/timing/status | Existing automatic hosting instrumentation | Native duration histogram count and status labels |
| Outbound HTTP transport timing | Native HTTP instrumentation | Headers boundary; distinct from logical RestClient completion |

Check existing native/library metrics before adding anything. Do not create a second metric with the same boundary merely because a custom name seems convenient. A histogram’s count often already supplies operation count, and an outcome label supplies the failure breakdown.

Application business outcomes still need explicit application metrics. An HTTP 200 response alone does not prove an order was accepted or a job completed successfully.

## 3. Use the facade consistently

Create static definitions and reusable handles once per module/service lifetime. Use a stable `cis.<module>.<quantity>` or application-domain prefix. Do not generate definitions or metric names from request data. Use seconds and bytes as base units and let the configured exporter add standard Prometheus suffixes.

The v1 numeric contract is int64 counter/up/down additions and float64 gauge/histogram observations. Counters accept nonnegative additions; gauges set absolute values; up/down counters add deltas. All v1 histogram observations are finite and nonnegative; duration observations use seconds. Do not expose arbitrary object-valued dimensions or SDK types through the consumer API.

Timer scopes use a monotonic clock. Their first completion makes one recording attempt; valid data records once even if explicitly stopped and then disposed. Invalid completion dimensions drop the attempt and leave the scope completed, so disposal cannot retry with different dimensions. Supply the final outcome explicitly; disposal must not infer success. Add final dimensions only through the supported CIS timer completion API, without duplicating initial keys. Balance active-work updates in a `finally` path and preserve the service’s existing return value, exception, and cancellation behavior.

Illustrative language-neutral pattern, to be translated into the actual published API:

```text
service construction:
    obtain reusable duration and active-operation handles

operation:
    active.add(+1, stable_dimensions)
    timing = duration.start(stable_dimensions)
    outcome = "error"
    try:
        result = perform_existing_operation()
        outcome = "success"
        return result
    catch known_timeout:
        outcome = "timeout"
        rethrow unchanged
    catch known_caller_cancellation:
        outcome = "cancelled"
        rethrow unchanged
    finally:
        timing.stop({ outcome })
        timing.dispose()       # must not record again
        active.add(-1, stable_dimensions)
```

Classify cancellation only where the service knows its origin. Do not rewrite existing exceptions to make metrics simpler. For concurrent operations, keep timing state local to the operation; do not maintain a global active-timer dictionary.

Observable callbacks read bounded, thread-safe snapshots already owned by the service. They do not perform I/O, enumerate arbitrary live objects, discover modules, or run application work during scraping. Their registration lasts for the host/module lifetime. Omit unavailable observations rather than inventing a zero. A histogram must not receive a fake zero observation just to make a series appear.

## 4. Protect identity and dimension bounds

Every exported sample must contain:

| Exported label | Authoritative source |
|---|---|
| `application` | `EnvironmentOptions.ApplicationName` |
| `environment` | `EnvironmentOptions.EnvironmentName` |

These labels are supplied centrally. Application/module callers must not set them. Do not substitute the ASP.NET environment or introduce a second configuration source. Identity is fixed for the provider lifetime and retains the existing resolver’s defaults and validation.

In the C# backend, resource attributes must be projected onto every metric through the exporter’s `ResourceConstantLabels` setting. Each effective view must remove reserved measurement keys. Resource-only metadata is insufficient, and resource/point collisions must not be assumed to resolve in favor of the resource. Preserve this on native metrics, histogram children, SDK overflow, and `target_info`.

Choose finite categories such as configured client names, registered operation names, HTTP route templates, finite outcomes, and status codes. Never label with IDs, raw paths, URLs, query strings, SQL, secrets, payloads, arbitrary messages, or unbounded logger/type names. Hashing a value does not fix its cardinality. Do not attach all resource metadata to every sample.

Estimate the product of dimension value counts. For example, 20 operations × 3 clients × 4 outcomes gives up to 240 combinations, exceeding a generic histogram’s default of 64. Remove an unnecessary dimension or choose an explicit stream limit inside the total policy before accepting that design. Do not hide a normal-workload capacity problem behind overflow.

CIS-generated values can be validated by the facade. Native SDK views generally filter keys rather than truncate arbitrary values. A native attribute requires a bounded producer; otherwise omit that key. Route templates are configuration metadata, while raw unmatched paths are untrusted high-cardinality data.

## 5. Preserve the bounded-memory design

The default policy includes 96 CIS/application definitions, 128 total streams, 128 ordinary combinations per stream, 64 per generic histogram, a 1,024-combination inbound HTTP duration profile, a 16,384 sum of configured dimensioned capacities, at most 32 finite histogram boundaries, and a 4 MiB scrape response. Scrape handlers allow two active requests and no waiting queue. These are tunable implementation defaults, not a claim that all aggregate memory is limited to 4 MiB.

Native streams that appear lazily still consume reserved catalog capacity. SDK special zero-dimension/overflow points are additional. One provider/reader is the default. Use SDK cardinality overflow, aggregation, and exporter caching/limits; do not implement an independent LRU aggregator, event buffer, serializer, or byte-budget allocator.

Provider/views/catalog policy, histogram profiles, identity, and exporter options are a validated startup snapshot. V1 configuration changes require restart; do not rebuild the provider on a live Configuration update. HTTP scrape timeout does not preempt a synchronous callback. Keep callback owners valid through any in-progress collection and verify shutdown through the existing host lifecycle.

Runtime capacity exhaustion must not fail successful application work or change readiness. Existing admitted series continue to update; overflow is visible, with identity preserved. An invalid measurement is dropped with bounded diagnostics. Invalid definitions/options are diagnosed clearly during startup. Do not produce a log per dropped observation or a recursive diagnostic metric.

The specification requires one focused observation of default-catalog memory/response behavior, with an initial 64 MiB incremental retained managed-memory target. That is a measured acceptance target, not a runtime heap guarantee. Do not fabricate a metric claiming to measure the SDK’s exact memory consumption.

## 6. Automatic metrics and timing boundaries

### Existing CISLIB modules

Each module owns its instrumentation at real operation/state boundaries. Enabling Metrics must make those observations available without extra calls by application users. Preserve constructors, service defaults, scheduling, readiness rules, and logging overwrite behavior.

Cover Logging’s actual volume/discard/writer state, Heartbeat work, Healthcheck evaluation/readiness, Startup operations, Configuration operations/state, and Type Manager result snapshots as specified. Use the actual current service behavior; do not invent an event such as a skipped execution if the scheduler waits instead.

Pre-container discovery/configuration work must not be replayed into later counters. Expose bounded terminal facts already retained by the owner through observable gauges. If needed, the owning operation may retain one final duration/status snapshot, not a history. Do not create a second bootstrap provider.

### Incoming APIs

Use native ASP.NET hosting request-duration and active-request instruments. Request count/rate comes from the duration histogram count, and HTTP error rates come from final status. Thin middleware may apply policy, enrich bounded tags, and exclude reserved routes; it must not duplicate native recording.

Keep the original route template and one incoming-request observation through exception-handler re-execution. Include ordinary authorization failures. A handled .NET 10 exception may lack `error.type`, so status-based HTTP error metrics remain necessary.

The reserved metrics and health branches precede the business readiness gate. Exclude their requests from completed business-request/duration statistics. The native active-request measurement still includes management requests; do not claim it is business-only concurrency. Preserve any separate management-access policy.

### Future RestClient

The Metrics delivery does not implement RestClient. When that module is built, reuse native HTTP transport metrics and add only missing logical-operation/transfer semantics. Native HTTP duration ends at response headers; a logical RestClient call includes the retries/body/deserialization work its public API performs. A native histogram count is neither a guaranteed logical-call count nor a physical-attempt/retry count: redirects, handler placement, internal retries, and short-circuiting affect it. Do not infer a fixed number of native observations from a configured retry count.

A returned stream needs a separate consumption lifetime. Count actual payload reads/writes, including partial and retried transfer according to the declared boundary. Do not use `Content-Length` as bytes consumed or buffer a body solely to measure it. Distinguish payload bytes from wire/TLS traffic. Use configured client/operation names and bounded outcomes.

## 7. Test the changed contract, then stop

Use existing native tests. Verify meaningful boundary behavior: correct instrument semantics; exactly-once timing; required identity and override protection; a real parseable scrape; small-capacity overflow; route normalization/probe exclusion; lifecycle; and one representative normal/failure path for each changed module. Keep RestClient-specific tests with its future implementation.

Do not re-test the entire SDK, build a new harness, add broad benchmarks, or run full release qualification for each small instrumentation change. Broaden testing only for a concrete remaining risk or a required current gate. Reuse materially valid evidence. A focused pass is not a claim of release readiness.

For deployment examples, derive rates from cumulative series and apply `rate` before summing across replicas. Scrape individual instances. Do not silently turn missing, stale, failed, or overflow-filtered data into zero for autoscaling. Fixed zero counter/gauge initialization may be legitimate; a fake histogram observation is not.

## 8. Repository skill to install during implementation

The following is a copy-ready starting point for `.github/skills/cislib-metrics/SKILL.md`. Inspect for an existing exact-name skill first. Adjust links to the actual repository assets and use real published examples before the Metrics implementation is marked complete.

```markdown
---
name: cislib-metrics
description: Implement, use, or review CISLIB metrics in a module or application. Use for metric definitions, automatic service or HTTP instrumentation, the reserved metrics endpoint, Prometheus/OpenTelemetry integration, cardinality and memory limits, and cross-language metric compatibility.
---

# CISLIB Metrics

Read the root AGENTS.md and the actual implemented metric contract first.
Use ../../../contracts/metrics/README.md and
../../../docs/modules/metrics/README.md, plus the current C# example under
../../../implementations/csharp/examples/metrics/.
If Metrics is not implemented yet, use the accepted feature specification;
do not pretend proposed API names already exist.

## Workflow

1. Identify the operation/state, unit, timing boundary, and intended query.
   Inspect native/library metrics before adding a custom instrument.
2. Use the CISLIB facade in application/module code. Keep SDK/exporter and
   platform hosting details in the optional integration package; do not
   add collector dependencies to CIS.Core.
3. Declare stable reusable handles. Use counters for quantities, up/down
   counters for deltas, gauges for absolute state, and histograms/timers
   for distributions. Never create per-request names or callbacks.
4. Keep application/environment centrally authoritative. Use finite,
   allowed dimensions and calculate their cardinality before adding them.
   Never label with raw URLs, IDs, SQL, secrets, or arbitrary messages.
5. Reuse native aggregation, overflow, collection, and Prometheus encoding.
   Do not add event histories, custom exporters, schedulers, or allocators.
6. Preserve current service behavior and lifetime. Record timer completion
   once, balance active work in all exit paths, and keep callbacks bounded
   and free of I/O. Expose early bootstrap facts without replaying events.
7. Reuse incoming HTTP instrumentation. Distinguish native outbound headers
   timing from logical operations and streamed body consumption. Count
   actual payload bytes without extra buffering when the module owns them.
8. Update the module metric catalog, working example, and focused native
   tests for changed behavior. Check a real scrape and mandatory labels
   when integration changes. Do not expand into release campaigns without
   a concrete requirement.

Follow the installed CIS workflow for supplied inputs, eligible work,
bounded planning, file ownership, and native verification. Keep tests,
documentation, and review with coherent implementation outcomes. Do not
reuse completed historical work owners or invent approvals.

Report the metric meaning, boundaries, dimensions, existing/native reuse,
changed files, focused evidence, and any real limitation.
```

This repository skill is an implementation deliverable. A personal assistant skill can carry the same workflow and reference this handoff, but it does not replace the checked-in contract, repository instructions, or actual code example.

## 9. Root AGENTS.md routing section

Add a short section outside the managed `cis:repository-guidance` markers. Do not overwrite imported CIS instructions. Proposed text:

```markdown
## Metrics in modules and applications

When implementing or changing metrics, read
`.github/skills/cislib-metrics/SKILL.md`, `contracts/metrics/README.md`, and
`docs/modules/metrics/README.md`. Use the current compiling example and
actual public API; do not invent an API from an old planning sketch.

Use CISLIB-owned metric abstractions in module/application code. Keep
OpenTelemetry/exporter dependencies in the optional integration package.
Reuse native instrumentation and SDK aggregation. Every exported sample
must retain the authoritative application and environment labels.

Each internal service must instrument appropriate owned operations/state
automatically, with stable names, bounded dimensions, documented timing,
and focused native verification. Preserve existing behavior and lifecycle.
Do not add custom metric histories, exporters, per-second sampling loops,
or speculative future-service implementations.
```

The implementation agent must validate the repository skill using the current installed CIS tooling. At the reviewed CIS baseline the routes are:

```text
cis skills inventory --repo <repository> --format agent
cis skills validate --repo <repository> --strict --format agent
cis skills audit --repo <repository> --no-llm --format agent
```

Inspect installed command help before using them. A normal audit is read-only with respect to skill disposition; it must not quarantine, merge, or delete skills. The `--no-llm` route avoids creating a new source-disclosure requirement. See the [CIS skill-governance procedure](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/.github/skills/cis-skill-governance/SKILL.md).

## 10. Implementation-agent handoff prompt

The following prompt can accompany this guide and the specification:

> Implement the CISLIB Metrics module from the supplied `CISLIB_Metrics_Implementation_Specification_v1.0.md` and agent guide. Inspect the current CISLIB checkout, root instructions, dependency policy, native workflow, and installed CIS version first. Preserve the supplied document as feature input in the existing product; do not replace the product-wide BRD or recreate the workspace.
>
> Establish the portable contract and thin Core primitives first. Implement the optional OpenTelemetry/Prometheus integration with `/_metrics`, authoritative application/environment labels, finite storage/scrape controls, and automatic incoming API metrics. Instrument appropriate existing CISLIB services without changing their behavior. Preserve the future RestClient/database contracts without implementing those services now.
>
> After shared contracts are established, parallelize provider/hosting work and existing-module instrumentation using explicit nonoverlapping file ownership. One owner coordinates project/package changes and builds sharing outputs. Keep documentation, focused native tests, independent review, and completion evidence within coherent implementation outcomes. Follow current CIS authority/allocation rules, reusing already accepted decisions; do not invent approvals or reopen completed historical work.
>
> Deliver the real compiling example, portable catalog/fixtures, module usage documentation, checked-in `cislib-metrics` skill, and AGENTS routing hook. Verify the specification’s acceptance matrix, including one focused memory/response observation. Report exact changed behavior, meaningful evidence, and remaining limitations. Do not claim implementation, full qualification, publication, or installation until each has actually occurred.

## 11. Required reading for implementation agents

Repository and feature authority take precedence over generic workflow suggestions. Use the version actually installed in the workspace; remote links below identify the reviewed guidance, not an instruction to upgrade CIS:

- [CISLIB agent instructions](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/AGENTS.md), [dependency policy](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/docs/architecture/csharp-dependency-policy.md), and [import instructions](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/.github/instructions/cis-import.instructions.md).
- CIS [supplied requirements](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/.github/skills/cis-govern-business-requirements/SKILL.md), [bounded planning](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/.github/skills/cis-bounded-planning/SKILL.md), [execution](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/.github/skills/cis-agent-execution/SKILL.md), and [native verification](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/.github/skills/cis-verification/SKILL.md).
- [.NET metrics collection](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection), [OpenTelemetry API semantics](https://opentelemetry.io/docs/specs/otel/metrics/api/), [SDK limits](https://opentelemetry.io/docs/specs/otel/metrics/sdk/), and the [pinned exporter documentation](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md).

This guide is complete when future agents can locate the real API, add a correctly scoped metric, preserve identity and bounds, avoid duplicating native instrumentation, and verify the change through the existing workflow without reconstructing the design from prior chat.
