---
name: cislib-metrics
description: Implement, use or review CISLIB metric definitions, service observations and optional HTTP/Prometheus integration, including identity, dimension limits and collection lifetime.
---

# CISLIB Metrics

Read [AGENTS.md](../../../AGENTS.md) and the actual
[portable contract](../../../contracts/metrics/README.md) first. Use the
[Core API guide](../../../docs/modules/metrics/core.md),
[collection policy](../../../docs/modules/metrics/collection.md) and current
[public types](../../../implementations/csharp/src/CIS.Core/Metrics/MetricContracts.cs).
For existing module instrumentation, read the
[runtime service catalog](../../../contracts/metrics/runtime-services.md) and
the bootstrap/startup catalog in the portable contract.

Inspect current code and [CIS-0009 evidence](../../../docs/changes/CIS-0009/verification.md)
before treating a hosting API, example, exported name or acceptance claim as
verified. The initial skill delivery preceded the WORK-005 integration proof.
Use the [current compiling example](../../../docs/modules/metrics/example.md)
and [hosting guide](../../../docs/modules/metrics/hosting.md) with the actual
recorded native checkpoints; do not invent APIs from the supplied sketches.
Authored source or a fixture alone does not establish a passing scrape. Check
post-correction input currentness and remaining work in the evidence; local
passes do not establish full platform, coverage, mutation or release qualification.

## Choose one owned meaning

Identify the question, owning operation/state, unit, entry/exit boundary, finite
dimensions and lifetime. Check native/library instruments before adding a metric.
Histogram count normally supplies operation count; outcome supplies its failure
breakdown. Logging frontend returns, backend processing and writer delivery are
different facts and must not be summed as unique records.

Use `IMetrics`, reusable `MetricDefinition` handles, string-only
`MetricDimensions` and `MetricObservation` in module/application code. Core stays
independent of OpenTelemetry. The optional `CIS.Metrics.OpenTelemetry` package owns
SDK/provider/views/exporter integration; ordinary consumers neither construct the
internal runtime nor manage a second provider.

- Counter: nonnegative `long` additions. Up/down counter: signed `long` deltas.
- Gauge: finite absolute `double` state. Histogram: finite nonnegative values.
- Timer: seconds, monotonic timing, standard duration bounds unless overridden.
- Observable gauge: bounded thread-safe values already retained by the owner.

Declare static names/schema once per service lifetime. Keep names portable and
stable, and let the exporter apply unit/counter suffixes. Check translated names
and generated histogram children for collisions; distinct logical spellings do
not guarantee distinct exported families. Do not create per-request definitions,
callbacks, event histories, an aggregator, serializer or sampling scheduler.

## Preserve bounds and identity

Estimate the product of dimension value counts before admitting a stream. The
default profile has 96 CIS/application definitions, 128 total streams, 128 ordinary
or 64 generic-histogram combinations, 1,024 inbound-duration combinations and a
16,384 sum of configured dimension capacities. Native reservations consume that
budget; SDK zero-dimension/overflow points are additional. Consult the actual
captured options for overrides. Eight keys and 32 finite bounds are v1 maxima.
Use bounded registered categories or aggregate outcomes/state when no finite
alias registry exists. IDs, raw paths/URLs, queries, payloads, secrets, arbitrary
messages, logger categories and CLR type names are not dimension values. Hashing
does not bound cardinality. Native keys need bounded producers, not truncation.

Every exported sample keeps centrally captured `application` and `environment`
from `EnvironmentOptions.ApplicationName` and `EnvironmentOptions.EnvironmentName`.
Do not supply those keys as measurement dimensions, substitute ASP.NET identity,
or override them through resource attributes. The optional exporter projects the
mandatory resource labels onto ordinary, histogram, overflow, native and
`target_info` samples; resource-only metadata is insufficient. Preserve the
existing identity resolver's Unicode validation and defaults.

## Keep real operation and owner lifetimes

Capture one binding at operation entry and use it for every exit, including a
balanced active increment/decrement. Publish complete handle bundles. Preserve
original results, exceptions, cancellation classification, scheduling and health
policy when metric recording fails.

A timer's first `Stop` or `Dispose` fixes one recording attempt. Supply outcome
explicitly through completion dimensions, disjoint from initial keys. Invalid
completion drops the attempt without allowing disposal to retry; disposal alone
does not infer success. Keep timing state local to each concurrent operation.

Observable callbacks do bounded in-memory reads, without I/O, discovery,
collector evaluation, application work or long-held application locks. Omit
unknown facts instead of inventing zero durations or histogram observations.
Close collection admission and await actual callback execution plus result
materialization before disposing callback owners. HTTP timeout/cancellation and
provider disposal alone do not prove that drain.

Pre-DI Type Manager/Configuration work is not replayed or rerun. Expose retained
terminal counts, prepared state and known elapsed/last-success facts instead.
Mutable owners attach to one host; immutable reports can be borrowed independently.
Identity, views, capacities and exporter policy are a startup snapshot: v1 live
configuration updates do not rebuild/reset the provider.

## Reuse native HTTP and verify the changed boundary

Incoming request duration and active requests use native ASP.NET instruments.
Do not add duplicate request timing/counting. Keep one original route-template
observation through exception-handler re-execution, with final status including
authorization failures. A handled exception may lack `error.type`; status-based
HTTP errors remain necessary. Management branches precede the business readiness
gate and retain deployment access policy. Metrics/health requests are excluded
from completed business duration statistics, while native active requests still
include management work.

For RestClient, use its [actual example](../../../docs/modules/rest-client/example.md)
and [fixed catalog](../../../contracts/metrics/rest-client.md). Its captured logical
binding includes retries, body work and actual cleanup, with completed application
byte observations. Outgoing Auto uses final known REST composition and the exact
root meter factory. Native outbound duration ends at headers; it does not establish
logical duration, owned dispatches or consumed bytes. Database/transfer remain
future modules; do not implement them to add metrics.

Update the affected catalog, documentation and meaningful existing native tests.
Use [portable cases](../../../conformance/metrics/v1/primitive-cases.json) for
portable semantics, and real SDK/exporter output when changing integration.
Check mandatory labels, bounded overflow, owner drain and the actual changed
module's normal/failure behavior. Response cap and the measured 64 MiB retained
memory target are different claims. Do not re-test the whole SDK or expand a
focused change into release qualification.

Follow the repository's eligible work/allocation and coordinated native workflow;
this skill grants no execution, disclosure or publication authority. Report the
meaning, boundaries, dimensions, native reuse, focused evidence and real limits.
