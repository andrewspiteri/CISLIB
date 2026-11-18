# Metrics contract — v1

Configured RestClient observations and their conditional catalog are specified
in the [REST service catalog](rest-client.md).

This is the portable Metrics contract selected for CISLIB v1. It accompanies the
[supplied requirements](../../planning/metrics/sources/CISLIB_Metrics_Implementation_Specification_v1.0.md)
and [C# Core binding](../../docs/modules/metrics/core.md). It specifies behavior and
expected exported compatibility; authoring this contract does not establish an
exporter, endpoint, platform or release verification result. Exact observations
belong to [CIS-0009 verification](../../docs/changes/CIS-0009/verification.md).

## Quantities and operations

| Instrument | Input | Meaning |
| --- | --- | --- |
| Counter | Nonnegative signed 64-bit integer addition; default addition one | Cumulative occurrences or quantity. Scraping never resets it. |
| Up/down counter | Signed 64-bit integer delta | Additive state; negative state is observable, without clamping. |
| Gauge | Finite binary64 value | Absolute state. Concurrent accepted writes use backend last-value semantics. |
| Histogram | Finite nonnegative binary64 observation | Cumulative distribution with explicit finite bounds; SDK supplies positive infinity. |
| Timer | Finite nonnegative binary64 seconds | Duration histogram plus an operation-local monotonic timing scope. |
| Observable gauge | Bounded list of finite binary64 observations | Collection reads an already-owned, thread-safe in-memory snapshot. |

The unit vocabulary is Count (no physical suffix), Seconds (`s`), Bytes (`By`) and
Ratio (`1`). Units describe one stable quantity. A timer requires Seconds. A
non-duration histogram declares appropriate explicit bounds; a seconds histogram
may use the standard duration profile. Bounds are finite, nonnegative, strictly
increasing, and number at most 32. The standard 14 finite duration bounds are:

```text
0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25,
0.5, 0.75, 1, 2.5, 5, 7.5, 10
```

No rate window, summary/quantile engine or observation history is part of v1.
Derive rates downstream from cumulative quantities. A histogram's count already
counts its observations. Float64 export does not promise exact accounting of
large integer totals indefinitely.

## Definitions and dimensions

Create reusable handles once for a long-lived owner. Definitions have a stable
logical name, kind/numeric surface, unit, description, allowed dimension-key set
and optional histogram bounds. Equivalent registration reuses the handle;
incompatible repeated registration fails clearly. Observable re-registration also
requires the same callback registration. The ordinary API creates no arbitrary
instrumentation scope.

CIS definitions use `cis.<module>.<quantity>`; application definitions use a
stable domain prefix such as `app.orders.completed`. Do not embed a deployment
identity, request value or generated counter/histogram suffix in a logical name.
Names/keys use the lowercase ASCII subset `[a-z][a-z0-9._]*`, bounded to 128/64
characters. Description is at most 1,024 UTF-8 bytes. Reject translated-name and
generated-child collisions, including dimension keys that translate identically.

Dimensions contain only copied string key/value pairs, never arbitrary objects
or implicit `ToString` conversion. The default dimension set is empty. Declared
keys are allowed, not required: a measurement may omit them. Pair order is not
semantic; keys and values compare ordinally. Keys must be unique and declared.
At most eight ordinary keys are permitted per definition/measurement; each
CIS-generated value is valid Unicode and at most 256 UTF-8 bytes. Do not truncate
invalid values into different categories.

`application`, `environment`, `le`, `quantile`, `otel_metric_overflow`,
`otel_scope_name`, `otel_scope_version` and `otel_scope_schema_url` are reserved,
including collisions after underscore translation. Use finite
producer-owned categories. IDs, raw paths/URLs, queries, SQL, secrets, payloads,
messages and arbitrary type/logger names are unsuitable labels. Hashing does not
reduce cardinality.

Invalid definitions/options and known startup catalog exhaustion fail startup.
Invalid recording data drops the entire measurement without throwing into normal
business work: negative counter/histogram/timer input, nonfinite numbers, unknown,
duplicate or reserved keys, or over-limit values. Diagnostics are bounded rather
than one log per drop. A later runtime definition rejected for capacity returns a
no-op handle; admitted streams continue. Do not change readiness or fail successful
application work because metric cardinality/capacity was exhausted.

## Timing and observable state

Start captures monotonic time and copied initial dimensions. The first Stop or
Dispose fixes the elapsed seconds and makes at most one recording attempt. Later
Stop calls return the first duration and ignore new dimensions; Dispose does not
record again. This remains true for concurrent completion and across awaits.

Completion dimensions are disjoint additions to initial dimensions. If their
combined set is invalid, the attempt is dropped and the scope remains completed;
later disposal cannot retry with a smaller set. Dispose alone uses initial
dimensions and infers neither success nor failure. An owner supplies the actual
bounded outcome explicitly. There is no registry of running scopes.

Observable callbacks do no I/O, discovery, startup, health evaluation or business
work. They may return an empty list when a value is unavailable. Empty is not a
zero measurement, and an unavailable duration never becomes a fake histogram zero.
An ordinary callback failure omits that callback's data for that collection with
bounded diagnostics. Callback registration belongs to the host/module lifetime.
Close collection admission and drain actual callbacks before disposing their
owners; HTTP timeout/cancellation does not prove callback settlement.

Disabled collection retains no replayable observations. Pre-container Type
Manager/Configuration work is not repeated or replayed into later counters.
Already-owned terminal facts may be observed once known; unavailable facts stay
omitted. Existing services remain usable without installing Metrics.

## Export identity, names and bounds

Every exported sample, including native streams, histogram children, overflow and
metadata, carries immutable authoritative `application` and `environment` values
from the existing environment resolver. C# uses EnvironmentOptions.ApplicationName
and EnvironmentOptions.EnvironmentName, including the existing `unknown` default
and identity limits. Ordinary dimension-value limits do not replace identity
limits. Callers cannot override identity using measurement/resource attributes.

The optional integration owns SDK aggregation, one selected provider/reader,
combined identity/cardinality/bucket views and standard exporter encoding. Core
does not contain an SDK, custom aggregator, serializer, collector or per-event
queue. No third-party type appears in the ordinary consumer API.

Expected underscore-escaped sample names include:

| Logical name / kind / unit | Required sample names |
| --- | --- |
| `app.orders.completed` / counter / Count | `app_orders_completed_total` |
| `app.payload.downloaded` / counter / Bytes | `app_payload_downloaded_bytes_total` |
| `cis.heartbeat.execution.duration` / timer / Seconds | `cis_heartbeat_execution_duration_seconds_bucket`, `_sum`, `_count` |
| `app.batch.size` / histogram / Count | `app_batch_size_bucket`, `_sum`, `_count` |
| `app.payload.size` / histogram / Bytes | `app_payload_size_bytes_bucket`, `_sum`, `_count` |
| `cis.healthcheck.ready` / gauge / Count | `cis_healthcheck_ready` |
| `http.server.request.duration` / native histogram / Seconds | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |

In that table, `_sum` and `_count` replace `_bucket` on the same base. The portable
fixture contains each exact spelling. These are required conformance expectations;
the recorded Windows optional-integration checks exercise all eight literal
families. [CIS-0009 evidence](../../docs/changes/CIS-0009/verification.md) records
their actual scope and input currentness. Optional metadata does not permit
omitting identity or changing meanings, and this result is not release or
cross-platform qualification.

The selected default profile is finite: 96 CIS/application definitions, 128 total
streams including native reservations, 128 ordinary combinations per stream, 64
per generic histogram, 1,024 for inbound HTTP duration, and a 16,384 sum of
configured dimensioned capacities. SDK zero-dimension/overflow points are
additional. No limit silently means unlimited or grows automatically.

Composition metadata is bounded too: scope name 128 ASCII characters, scope
version 64 UTF-8 bytes and schema URL 512 UTF-8 bytes. At most 16 extra resource
attributes have 64-character ASCII keys and 1,024-byte UTF-8 values; validate
translated-key collisions. Mandatory identity keeps its existing resolver limits.

The enabled canonical route is `GET /_metrics`, respecting the application path
base, without a silent `/metrics` alias or conflicting business route. It precedes
the business readiness gate and retains application management-access policy.
The endpoint profile uses four MiB complete encoded response, two
active scrape handlers with no queue, five-second wait/write budget, 300-ms cache
and exemplars disabled. Scraping never resets data or executes application work.
The 64-MiB incremental retained-memory target requires actual measurement and is
separate from the response cap or disk allocation. Scrape/overflow failure does
not alter health/readiness. No metrics endpoint is installed when the integration
is absent/disabled. Access remains application/deployment-owned.

Changing a published name, unit, numeric surface, meaning, dimensions, reset or
operation boundary is a compatibility change. Future RestClient/database/transfer
instrumentation must follow these meanings; those services are not implemented by
this contract. Other language bindings must execute applicable portable cases and
actual exporter checks before advertising support.

## Bootstrap and startup catalog

The optional integration attaches the following fixed definitions to actual
module owners. Applications make no additional recording calls. Duration timers
use the standard fourteen bounds; snapshot gauges have no histogram children.
All names below are logical names before the selected exporter adds unit suffixes.

| Logical name | Kind / unit | Dimensions and meaning |
| --- | --- | --- |
| `cis.startup.operation.duration` | Timer / Seconds | `stage=construction\|initialization\|completion`, `outcome=success\|failed\|cancelled`; actual root resolution, each sequential participant, and final activation/readiness confirmation respectively. |
| `cis.startup.completed` | Observable gauge / Count | No dimensions; zero until the coordinator's real completion publication, then one. |
| `cis.configuration.operation.duration` | Timer / Seconds | `operation=initial_load\|refresh`, `outcome=success\|failed\|cancelled`; each actual serialized entry attempt through retrieval, mapping, commit and its synchronous notification. |
| `cis.configuration.prepared` | Observable gauge / Count | No dimensions; one after all initial entries were populated, otherwise zero. |
| `cis.configuration.entries` | Observable gauge / Count | `state=valid\|invalid\|disabled`; aggregate current entry counts after successful initial preparation. Disabled entries are excluded from the invalid count. |
| `cis.configuration.initialization.duration` | Observable gauge / Seconds | No dimensions; one retained terminal duration of the complete sequential initial load, omitted until terminal. |
| `cis.configuration.last.success` | Observable gauge / Seconds | No dimensions; latest retained successful entry reconciliation as Unix seconds, omitted until one exists. |
| `cis.typemanager.assemblies` | Observable gauge / Count | No dimensions; selected assembly count from the completed report. |
| `cis.typemanager.types` | Observable gauge / Count | No dimensions; cataloged type count from the completed report. |
| `cis.typemanager.initialization.duration` | Observable gauge / Seconds | No dimensions; preparation elapsed time from the completed report. |

Startup has no existing bounded public participant-alias registry. Participant
durations therefore aggregate under `initialization`; CLR type names and
registration positions are never labels. Configuration mapping is part of the
actual load/refresh attempt, without another overlapping remap timer. A subscriber
failure remains its existing separate notification diagnostic and does not turn a
valid committed refresh into a failed reconciliation.

Each operation captures its complete binding at entry. An attachment during that
operation cannot replay it or divide its entry/exit between providers. Missing
pre-container events stay missing: later attachment exposes retained terminal
snapshots only. Unknown initial durations and entry state are omitted, rather
than seeded with zero. A zero prepared/completed indicator is a known state.

Mutable Startup/Configuration owners claim one host runtime. An immutable Type
Manager report may supply snapshots to independent hosts without retaining either
runtime or repeating discovery. A failed Type Manager preparation publishes no
manager/report, so no synthetic failed-instance gauge is created. Host collection
admission and actual callback settlement still precede owner disposal. Native
module/exporter verification is recorded separately in the change evidence.
