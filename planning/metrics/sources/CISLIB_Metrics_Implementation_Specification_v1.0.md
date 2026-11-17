# CISLIB Metrics — implementation specification

**Version:** 1.0  
**Date:** 17 November 2026  
**Purpose:** implementation handoff for agents working on CISLIB and its applications  
**Status:** specification prepared for implementation; the APIs and new repository paths below are proposed deliverables, not claims that the module already exists  
**Companion:** [CISLIB_Metrics_Agent_Guide_v1.0.md](CISLIB_Metrics_Agent_Guide_v1.0.md)

## 1. Outcome and scope

Implement a Metrics module with a small, language-neutral CISLIB API. C# is the first binding. Its implementation must reuse `System.Diagnostics.Metrics`, OpenTelemetry .NET aggregation, and the OpenTelemetry Prometheus ASP.NET Core exporter. Applications expose metrics at the reserved `/_metrics` endpoint. Every exported sample carries the authoritative `application` and `environment` dimensions.

Metrics are cumulative aggregate state, not a buffer of individual observations. Bound their resource use through finite instrument and stream catalogs, dimension cardinality, histogram buckets, string sizes, and scrape limits. Do not implement a second metrics engine.

The delivery includes automatic instrumentation of appropriate existing CISLIB services and incoming application API requests. It also establishes the instrumentation contract future modules must follow, including RestClient and database services. A new RestClient or database implementation is outside this delivery.

The words **must**, **should**, and **may** express requirements, recommendations, and optional behavior respectively. The public requirements come from the user’s instructions. Numeric limits and API spellings in this document are concrete v1 design defaults selected for implementation; they are not claimed to have been separately specified or benchmarked by the user.

### 1.1 Requirements and ownership

| ID | Requirement | Main implementation responsibility |
|---|---|---|
| MET-01 | Provide counter, up/down counter, gauge, histogram, and timer semantics. | Portable contract and C# primitive adapter |
| MET-02 | Keep application and module usage independent of Microsoft and OpenTelemetry instrumentation types. | Public API review |
| MET-03 | Reuse native .NET instrumentation and the OpenTelemetry SDK/exporter. | Optional integration package |
| MET-04 | Serve valid Prometheus-compatible output at `/_metrics`. | Hosting adapter |
| MET-05 | Put authoritative application and environment labels on every exported sample. | Provider, views, exporter configuration |
| MET-06 | Bound retained aggregate state and scrape work; preserve business operation when runtime capacity is exhausted. | Registration policy and SDK configuration |
| MET-07 | Capture inbound API request counts, duration, HTTP error rates, and native active requests automatically. | ASP.NET Core integration |
| MET-08 | Instrument appropriate existing CISLIB services without altering their behavior. | Each module owner |
| MET-09 | Specify automatic logical-operation, HTTP, and transfer metrics for the future RestClient. | Future-module contract |
| MET-10 | Support throughput and autoscaling queries through cumulative quantities and stable dimensions. | Metric catalog and examples |
| MET-11 | Preserve startup, readiness, package, and lifecycle contracts. | Integration owner |
| MET-12 | Deliver portable contracts, focused conformance evidence, usage documentation, an agent skill, and repository instructions. | All implementation outcomes |

### 1.2 Explicit exclusions

Do not build a metrics database, history ring buffer, custom aggregation queue, Prometheus serializer, custom collector, separate scheduling service, adaptive memory allocator, generic plugin framework, dashboard product, or autoscaling controller. Do not implement other language bindings now. Do not introduce an OTLP collector as a substitute for the required application endpoint. A future optional exporter can use the same facade.

Do not add application-specific business measurements indiscriminately. Automatic framework instrumentation can observe transport outcomes and timing; a successful HTTP response does not establish a successful business transaction.

## 2. Repository baseline and constraints

This handoff was checked against CISLIB `main` at [`1f72e9ea4d8630edacb59c01795ebc8467807cd8`](https://github.com/andrewspiteri/CISLIB/commit/1f72e9ea4d8630edacb59c01795ebc8467807cd8), with Configuration already merged, and CIS at [`75283190777ad691811ade6a7eff247a74105728`](https://github.com/andrewspiteri/CIS/commit/75283190777ad691811ade6a7eff247a74105728). Inspect the actual checkout before editing; later accepted repository decisions supersede observations of this baseline.

Read the current root `AGENTS.md`, `README.md`, `planning/decisions.md`, `docs/architecture/csharp-dependency-policy.md`, and `.github/instructions/cis-import.instructions.md`. The following constraints are material to this feature:

- Portable contracts and behavioral fixtures belong under `contracts/` and `conformance/`; C# implementation belongs under `implementations/csharp/`.
- Preserve existing module public contracts, defaults, constructor behavior, and lifecycle. Do not require applications to rewrite existing service usage simply to enable metrics.
- `CIS.Core` must remain independent of collector SDKs. Its approved runtime dependencies are an explicit closure, not every package with a Microsoft prefix. OpenTelemetry and its exporter belong in a separate optional integration package.
- Reuse the existing native C# MSTest projects and ordinary `dotnet` workflow. Keep new verification code in C#. Do not introduce a test harness project or a second orchestration route.
- Existing CIS owners, allocations, and accepted results are historical records. Do not reopen completed work or reuse its authority for Metrics.

The baseline selects .NET SDK 10.0.401 and .NET/ASP.NET Core runtime 10.0.12. This feature must use the checkout’s selected versions rather than silently upgrading the repository. See the [repository README](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/README.md), [agent instructions](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/AGENTS.md), and [dependency policy](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/docs/architecture/csharp-dependency-policy.md).

## 3. Architecture and package boundary

Use the following direction of dependencies:

| Layer | Owns | Must not own |
|---|---|---|
| Portable `contracts/metrics/` | Instrument behavior, naming, dimensions, identity, lifecycle, boundedness, exported compatibility | .NET types or exporter options |
| `CIS.Core`, namespace `CIS.Metrics` | Public CISLIB types, thin primitive adapters over BCL instruments, timer helper, appropriate Core-module recording | OpenTelemetry SDK/exporter package references or aggregate storage |
| Optional `CIS.Metrics.OpenTelemetry` package | Provider, SDK views, explicit collection catalog, resource identity, Prometheus exporter, ASP.NET Core integration | A second implementation of SDK aggregation or wire formatting |
| Application composition | Opt-in installation of the integration, application metric declarations, management-route access, deployment scrape configuration | Reimplementation of Core service instrumentation |
| Module owner | Stable definitions and measurements at real operation/state boundaries | An independent provider, endpoint, scheduler, or per-module event buffer |

The proposed optional package name is `CIS.Metrics.OpenTelemetry`. Align project, assembly, and namespace details with repository conventions without moving the collector into Core. Registration extensions that integrate with the C# host may use `IServiceCollection` and `IApplicationBuilder`; normal business-code instrumentation must use only CISLIB-owned metric types.

Use one provider and one Prometheus reader per application host by default. Repeated integration registration must not produce duplicate providers, endpoints, or observations. If an application already owns OpenTelemetry, integrate through the shared builder before its provider is built. Every stream exposed by the required endpoint must satisfy the CIS catalog, identity, view, and capacity policy. An already-built provider or external view/reader configuration that cannot satisfy those requirements is unsupported and must be reported clearly; do not install a second reader silently or weaken the endpoint contract.

An explicitly bounded definition cache and configuration catalog are allowed. They hold metadata and reusable instrument handles, not measurement history or independently aggregated values. The SDK owns aggregation, concurrent updates, collection, cardinality overflow, and scrape encoding.

### 3.1 Dependency selection

The verified reference implementation APIs are OpenTelemetry .NET SDK **1.21.0** and `OpenTelemetry.Exporter.Prometheus.AspNetCore` **1.21.0-beta.1**. The exporter is still a prerelease component. Record that dependency status and pin the selected compatible versions and lock files in the optional package. The current exporter provides resource-label projection and a scrape-response size cap needed by this design. Do not assume those APIs exist in older examples or copy an obsolete SDK configuration method.

Recheck package compatibility when implementation begins. A newer compatible version is acceptable if the required behavior and focused exporter tests still pass. If a required SDK capability is unavailable, report that concrete gap; do not work around it by implementing a custom exporter. The user’s direct endpoint requirement remains in scope. Primary references: [exporter README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md), [exporter options](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/PrometheusAspNetCoreOptions.cs), and [SDK customization](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md).

## 4. Portable public contract

### 4.1 Instrument behavior

| CISLIB concept | Numeric surface | Operation and meaning | C# backend | Prometheus representation |
|---|---|---|---|---|
| Counter | Signed 64-bit integer input restricted to nonnegative additions | `Add(amount)` accumulates occurrences or quantity | `Counter<long>` | Monotonic counter, `_total` |
| Up/down counter | Signed 64-bit integer delta | `Add(delta)` tracks additive changes such as active work | `UpDownCounter<long>` | Gauge |
| Gauge | Finite 64-bit floating-point value | `Set(value)` reports an absolute value; it does not add | Native synchronous gauge where supported | Gauge |
| Histogram | Nonnegative finite 64-bit floating-point observation | `Record(value)` records a distribution using configured explicit boundaries | `Histogram<double>` | Classic histogram buckets, sum, count |
| Timer | Nonnegative finite seconds | Records elapsed time as a duration histogram | Histogram plus monotonic elapsed-time helper | Histogram with `_seconds` unit |
| Observable gauge | Finite 64-bit floating-point snapshot values | Reads already-owned state during collection | `ObservableGauge<double>` | Gauge |

This is the required v1 surface. Do not introduce arbitrary numeric generic types, a separate rate/meter instrument with time windows, or a custom summary/quantile engine. Count-based histograms and other future numeric forms can be explicit extensions later. Float64 gauges represent large integer values approximately beyond the exact integer range; do not promise exact financial/accounting arithmetic or indefinite counter precision in Prometheus.

Counters reset only with a new instrument/provider process lifetime, not after scraping. Gauge updates replace the previously reported value for that dimension combination. Multiple concurrent gauge writers should be avoided; if used, the last observation accepted by the backend wins. Up/down counters remain additive. Do not silently clamp negative active-work values; a negative value reveals an instrumentation defect.

All v1 histogram observations, including timers and byte sizes, must be nonnegative. Signed distributions are a future explicit extension, not an assumption based on one backend accepting negative values. Boundaries must be finite, nonnegative, strictly increasing, and appropriate to the unit. The exporter supplies the implicit positive-infinity bucket. Quantiles are computed downstream from histogram data.

The semantics follow the [OpenTelemetry Metrics API](https://opentelemetry.io/docs/specs/otel/metrics/api/) and [.NET instrumentation model](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation). CISLIB supplies a portable facade around those facilities.

### 4.2 Proposed C# consumer shape

The following signatures define the intended small surface. They are a specification, not code claimed to compile against the current repository. The implementation must finish XML documentation and idiomatic argument types, then publish compiling examples with the actual API.

```csharp
// Namespace: CIS.Metrics
public interface IMetrics
{
    ICounter CreateCounter(MetricDefinition definition);
    IUpDownCounter CreateUpDownCounter(MetricDefinition definition);
    IGauge CreateGauge(MetricDefinition definition);
    IHistogram CreateHistogram(MetricDefinition definition);
    ITimer CreateTimer(MetricDefinition definition);

    // Registration lasts for the host/module lifetime, never one request.
    void RegisterObservableGauge(
        MetricDefinition definition,
        Func<IReadOnlyList<MetricObservation>> observe);
}

public interface ICounter
{
    void Add(long amount = 1, MetricDimensions dimensions = default);
}

public interface IUpDownCounter
{
    void Add(long delta, MetricDimensions dimensions = default);
}

public interface IGauge
{
    void Set(double value, MetricDimensions dimensions = default);
}

public interface IHistogram
{
    void Record(double value, MetricDimensions dimensions = default);
}

public interface ITimer
{
    void Record(double elapsedSeconds, MetricDimensions dimensions = default);
    ITimingScope Start(MetricDimensions dimensions = default);
}

public interface ITimingScope : IDisposable
{
    double Stop(MetricDimensions completionDimensions = default);
}
```

`MetricDefinition` contains a stable name, description, unit, and allowed dimension keys. `MetricDimensions` is a small immutable value containing string keys and string values in v1; it must not accept arbitrary objects or call their `ToString()` methods. `MetricObservation` pairs a gauge value with dimensions. These are CISLIB types. Exact constructors and collection conveniences may follow existing conventions, but examples and contract fixtures must agree.

Use a finite v1 unit vocabulary: count (no physical-unit suffix), seconds (`s`), bytes (`By`), and ratio (`1`). A small CISLIB unit type may represent these without exposing SDK types; arbitrary unit text is not part of v1. A definition and its exported name must identify one quantity in one unit.

Do not expose `Meter`, `Counter<T>`, `TagList`, `MeterProvider`, SDK view builders, `HttpContext`, or platform stopwatch types through this ordinary consumer contract. Hosting-specific extension methods belong to the C# integration layer. Another language should be able to implement the same operations and semantics with its own standard OpenTelemetry binding.

### 4.3 Definition and handle lifetime

Create instruments once per long-lived module/service or static application metric declaration and reuse the handles. Do not create a metric name, scope, callback, or definition for every request, tenant, URL, or entity.

Registration identity is the owning scope and metric name. Composition assigns one bounded facade scope, `CIS.Metrics`, by default; the public v1 consumer API does not create arbitrary scopes. All CIS/application names within that scope must be unique by their stable module/domain prefix. Platform-native instruments retain their selected native scopes. The kind, numeric representation, unit, description, and dimension schema must agree on repeated registration. Equivalent registration reuses the handle. An incompatible definition is a clear registration error. Names that collide after Prometheus translation, including collisions with generated histogram/counter children or exporter-reserved names, are invalid.

Declare the known CISLIB/native catalog at composition time. Applications should register their static definitions during service construction/startup. The bounded metadata catalog may admit a later static definition within the remaining configured limits, but must never expand its limits automatically. Reject invalid definitions clearly; a capacity rejection after startup returns a no-op handle and emits a bounded diagnostic so the business operation can continue. Avoid retaining rejected names or values in an unbounded cache.

When metrics collection is disabled, recording is inexpensive and does not accumulate replayable data. A host without the optional integration must continue to run all existing modules normally.

### 4.4 Timer and callback guarantees

Timers use a monotonic clock and report seconds. `Start` creates only the small operation-local timing scope. The first `Stop` or `Dispose` atomically fixes the elapsed duration and makes at most one recording attempt; a valid measurement records exactly once. Later calls return that first duration, ignore newly supplied completion dimensions, and do not record again. This must remain true if completion and disposal race. There is no central registry of running timers.

`Stop(completionDimensions)` adds final dimensions such as a bounded outcome to the initial dimensions. Initial and completion keys must be disjoint, and the combined set must satisfy the definition and limits. Invalid combined dimensions drop that recording attempt while leaving the scope completed; a later `Dispose` must not retry using only the initial dimensions. A plain `Dispose` attempts to record the initial dimensions only; it must not guess success or infer an exception from disposal. Instrumentation that needs an outcome must provide it explicitly before disposal. Timers must support asynchronous operations without thread affinity.

Observable callbacks read a bounded, thread-safe in-memory snapshot of state the module already owns. They must not query a database, perform network/file I/O, acquire a long-held application lock, or discover modules during a scrape. Registration belongs to the host/module lifetime; stop collection before disposing callback owners. Do not add a per-request callback or an independent dynamic-unregistration subsystem.

A callback may return no observation when a value is genuinely unavailable. Do not invent a zero initialization duration or a zero histogram observation. Catch ordinary callback failures at the adapter boundary, omit that callback’s observation for the collection, and emit a bounded diagnostic. Cancellation/shutdown must not start background work to finish a scrape.

### 4.5 Invalid input and capacity behavior

Validate definitions, duplicate schemas, options, and known catalog capacity during startup. Invalid configuration fails startup with a useful explanation. Runtime measurement overflow is different from invalid configuration.

For recording, nonfinite numbers, a negative counter increment, a negative histogram/timer observation, unknown or duplicate dimension keys, reserved identity keys, and over-limit CIS-generated values invalidate that measurement. Drop the entire invalid measurement and report a bounded diagnostic; do not truncate values into ambiguous categories or throw into normal business execution. This policy does not hide ordinary application exceptions or process-fatal conditions. Tests must prove both invalid-input handling and successful normal recording.

SDK cardinality exhaustion follows section 7. Neither capacity exhaustion nor a failed scrape changes readiness, triggers a restart, or fails a successful business operation.

## 5. Naming, dimensions, and compatibility

CISLIB-owned metric names use a stable `cis.<module>.<quantity>` prefix. Application metrics use a stable domain prefix such as `app.orders.completed`; do not embed the deployment’s application name or environment into the metric name. These are labels. Do not include `_total`, bucket children, or a physical unit twice; let the configured standard exporter translate names and append standard suffixes.

Select the exporter’s underscore escaping with suffixes and keep it fixed for v1. Examples:

| Logical instrument | Kind / unit | Exported family or sample |
|---|---|---|
| `app.orders.completed` | Counter / count | `app_orders_completed_total` |
| `cis.heartbeat.execution.duration` | Timer / `s` | `cis_heartbeat_execution_duration_seconds_bucket`, `_sum`, `_count` |
| `cis.restclient.payload.downloaded` | Counter / `By` | `cis_restclient_payload_downloaded_bytes_total` |
| `cis.healthcheck.ready` | Gauge / count | `cis_healthcheck_ready` |
| `http.server.request.duration` | Native histogram / `s` | `http_server_request_duration_seconds_bucket`, `_sum`, `_count` |

The implementation must verify actual exporter names once and commit matching conformance expectations. Standard native names are not renamed merely to add a CIS prefix. Another language must preserve the common meanings and exported compatibility; platform-specific runtime metrics may remain an explicitly separate optional catalog.

Dimensions must be useful bounded categories: registered operation, named client, route template, HTTP method, status code, finite outcome, configured monitor or work-item identifier. Never use user/customer IDs, correlation IDs, full URLs, raw paths, query strings, SQL text, connection strings, secrets, payloads, arbitrary exception messages, or full filenames as metric dimensions. Do not hash such values into a supposedly safe dimension; hashing does not reduce their cardinality.

Common operation outcomes are `success`, `error`, `timeout`, and `cancelled`. Use only outcomes the owning service can actually distinguish. An HTTP status is a transport outcome; 4xx, 5xx, and throttling remain separately queryable. A timeout must not be inferred solely from a general cancellation exception when caller cancellation can produce the same exception.

Changing a published metric’s name, unit, meaning, numeric type, required dimensions, reset behavior, or operation boundary is a compatibility change. Handle it through existing CISLIB contract/versioning policy. Keep the portable contract authoritative and avoid adding required members to already-published interfaces without compatibility handling.

References: [OpenTelemetry metric conventions](https://opentelemetry.io/docs/specs/semconv/general/metrics/), [Prometheus naming](https://prometheus.io/docs/practices/naming/), and [histograms](https://prometheus.io/docs/practices/histograms/).

## 6. Mandatory application and environment identity

For every sample emitted by the endpoint:

```text
application = EnvironmentOptions.ApplicationName
environment = EnvironmentOptions.EnvironmentName
```

Use the existing CISLIB environment resolver. The actual environment property is `EnvironmentOptions.EnvironmentName`. Do not introduce a second resolver, substitute the ASP.NET environment name, or change current defaults. The current resolver defaults the environment to `unknown` and already validates identity strings. Preserve its behavior; read its resolved values once for the provider lifetime. See [EnvironmentOptions.cs](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs).

Identity is immutable for that provider/process lifetime. Changing it requires restart. The module must not provide an independent override for either mandatory label. Optional OTel resource configuration must not replace these authoritative values.

For C#:

1. Add resource attributes named exactly `application` and `environment` with the resolved values. Standard resource attributes such as `service.name` and `deployment.environment.name` may also describe the same identity.
2. Configure `ResourceConstantLabels` to select exactly these mandatory resource keys for projection onto every metric point.
3. Prevent identically named measurement attributes from surviving each effective view. Prefer the catalog’s explicit allowed-key list; otherwise use `ExcludedTagKeys`. These two SDK options are mutually exclusive.
4. Include the identity protection, tag selection, cardinality limit, and histogram boundaries in the same effective view for an instrument. SDK views are not sequential transformation passes; overlapping views can create additional streams.
5. Keep exporter `target_info` enabled and verify its identity as well. Disable exemplars in the default profile so dropped attributes are not retained through exemplar metadata.

Resource attributes alone do not satisfy this requirement: by default they may appear only on `target_info`. Also, the selected exporter can concatenate a resource and point attribute with the same label key; resource values are not an override mechanism. Exclusion is therefore necessary.

The guarantee includes CISLIB and application metrics, selected native instruments, histogram buckets/sum/count, SDK overflow points, and any emitted `_created` or metadata samples. It applies to all negotiated formats the endpoint supports. A caller attempting to supply `application` or `environment` through the facade must not change the identity; the invalid measurement is rejected. A native instrument with conflicting tags must have those tags removed by the view.

Additional resource metadata must come from a finite, validated list. Do not enable unrestricted resource detectors or project all resource fields as labels by default. Keep Prometheus target relabeling from overwriting `application` or `environment`; `job` and `instance` are separate deployment labels. Test the endpoint’s guarantee directly, independently of downstream Prometheus relabeling.

References: [exporter resource projection](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md#resourceconstantlabels) and [view filtering and cardinality](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry/Metrics/View/MetricStreamConfiguration.cs).

## 7. Bounded aggregate storage and scrape work

### 7.1 Default policy

The following limits are v1 defaults. They are intentionally finite and configurable through CISLIB-owned options. Increasing a limit must be explicit and must remain within the validated total configuration. Zero or an omitted option must not accidentally mean unlimited.

| Control | Default | Enforcement |
|---|---:|---|
| CISLIB/application instrument definitions | 96 | Bounded metadata catalog; no names created from runtime input |
| Collected SDK streams, including selected native streams | 128 | Explicit catalog and SDK `SetMaxMetricStreams` |
| Ordinary per-stream dimension combinations | 128 | View `CardinalityLimit` |
| Generic histogram dimension combinations | 64 | Histogram view override |
| Inbound HTTP duration combinations | 1,024 | Dedicated view; validate against declared route/method/status needs |
| Sum of configured dimensioned capacities of admitted streams | 16,384 | CIS configuration/admission validation, not a claimed SDK global heap limiter |
| Finite histogram boundaries per instrument | 32 maximum | Definition/view validation |
| Measurement dimension keys | 8 maximum | CIS definition/recording validation; common and SDK-generated labels are additional |
| CIS metric name / dimension key | 128 / 64 ASCII characters | Definition validation; portable lowercase naming subset |
| CIS-generated dimension value | 256 UTF-8 bytes | Facade validation; fixed categories should normally be far shorter |
| Description | 1,024 UTF-8 bytes | Definition validation |
| Scope name / version / schema URL | 128 ASCII characters / 64 UTF-8 bytes / 512 UTF-8 bytes | Fixed composition/native catalog metadata; no per-request scope values |
| Additional resource attribute key | 64 ASCII characters | Startup validation, including translated-name collisions |
| Additional resource attributes | 16 maximum, each value at most 1,024 UTF-8 bytes | Startup configuration validation; identity retains existing resolver limits |
| Serialized scrape response | 4 MiB | Exporter `MaxScrapeResponseSizeBytes = 4_194_304` |
| Concurrent scrape handlers / queued requests | 2 / 0 | Standard ASP.NET/.NET concurrency limiting on the reserved branch |
| Scrape response timeout | 5 seconds | Exporter timeout plus cancellation-aware hosting |
| Scrape response cache | 300 milliseconds | Existing exporter cache |
| Exemplars | Off | SDK exemplar policy |

The sum-of-capacities rule includes native instruments that appear lazily: reserve their declared stream capacities at startup. If later static application definitions are supported, reserve their fixed capacity before admitting the stream. This is small metadata admission accounting, not a runtime measurement allocator. A fixed catalog with explicit view selection is preferred.

The SDK has special zero-dimension and overflow points in addition to the configured ordinary cardinality limit. Include those separately in capacity calculations; do not claim 128 is the absolute number of all retained points. Several views/readers can multiply storage. The default profile uses one effective stream per selected instrument and one reader.

Use the standard 14 finite HTTP/duration boundaries, in seconds:

```text
0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25,
0.5, 0.75, 1, 2.5, 5, 7.5, 10
```

Long-running jobs may explicitly choose a suitable finite duration profile. A non-duration histogram must declare an appropriate finite bucket profile; do not apply second-based buckets to bytes. A histogram with B finite boundaries emits B + 3 classic series per dimension combination: B + 1 buckets, sum, and count, before optional metadata.

The HTTP capacity is a tuning default, not a promise that every API fits. For example, 32 routes × 4 methods × 8 observed statuses already gives 1,024 combinations. Review the actual route and operation catalog and any extra dimensions; raise that stream’s limit or simplify dimensions within the overall budget when the normal workload would otherwise overflow. Do not quietly accept loss of ordinary endpoint breakdown.

### 7.2 Native attributes and string bounds

The facade can validate CIS-generated values. SDK views ordinarily select attribute keys; they do not provide a general string-truncation/value-normalization pipeline for all native instrumentation. Do not claim a facade length option sanitizes arbitrary third-party attributes.

Allow only native attributes with bounded producers. For inbound requests, the default duration view keeps method, declared route template, and response status. Route templates come from the application’s finite route catalog; validate unusually large template metadata at composition time or omit that attribute for an unsupported producer. Unmatched URLs must not become route labels. Known method normalization must be verified for the chosen framework. The active-request view retains only the native bounded method/scheme dimensions.

Optional error types, destination hosts, client identifiers, or runtime metrics require a documented finite producer and reviewed capacity. Drop an unsuitable key through the view rather than retaining arbitrary request-derived strings. Preserve the existing EnvironmentOptions identity size contract, which permits up to 255 Unicode scalar values; do not impose the smaller ordinary dimension limit on a valid existing identity.

### 7.3 Saturation and failure behavior

| Condition | Required behavior |
|---|---|
| New attribute combination exceeds stream cardinality | Use standard SDK overflow aggregation; admitted combinations continue updating |
| SDK overflow point | Export with `otel_metric_overflow="true"` and mandatory identity; original breakdown may be absent |
| Known startup catalog exceeds stream/point/bucket policy | Fail configuration validation with the offending definition/limit |
| Late registration exceeds allowed metadata/stream capacity | Do not grow; return a no-op handle with a bounded diagnostic |
| Invalid runtime measurement | Drop that measurement and diagnose without interrupting business work |
| Scrape response exceeds configured cap | Return a non-success response using exporter behavior; never a truncated successful document |
| Scrape concurrency exhausted | Reject promptly using the configured standard limiter; no unbounded waiting queue |
| Slow or disconnected scrape client | End the HTTP wait/write on timeout/cancellation and release its handler permit; do not claim synchronous SDK collection is preempted |
| Exporter trouble | Preserve application operation/readiness; emit bounded diagnostics through existing facilities |

For additive counters and histograms, overflow retains aggregate contributions while losing the original dimension breakdown. Do not describe overflow gauges as useful summed totals. Stream exhaustion is distinct from point overflow and can cause a stream not to be collected; known streams must fit before startup succeeds. Do not implement LRU eviction/reset of cumulative points, because it would silently change time-series semantics.

Use standard SDK/exporter diagnostics where available. Any CIS diagnostic must have fixed categories and must not recursively record failures through the same failing path or log once per occurrence. Avoid retaining the original offending label strings merely for diagnosis.

The exporter request timeout limits the HTTP response wait/write. It does not forcibly cancel a synchronous SDK collection or a callback already running; the selected exporter can collect with an infinite SDK timeout internally. Use the SDK/exporter’s existing collection coalescing, the bounded handler limit, and the callback contract above. Do not add an unbounded queue of replacement collections when HTTP requests time out. V1 does not promise hard preemption of a misbehaving callback or that all collection work has ended at the HTTP deadline. See the shipped [collection manager](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.HttpListener/Internal/Shared/PrometheusCollectionManager.cs).

### 7.4 What the memory commitment means

The contract is finite retained aggregate state and bounded scrape work under a validated configuration. It is not an exact SDK-wide byte allocator. The response-size limit is not a cap on total metric memory: aggregate metadata, histogram buckets, SDK reserved points, multiple response formats, caches, and temporary buffers also consume memory.

Perform one focused retained-memory and scrape-size observation using the actual default selected catalog, representative maximum-length allowed labels, populated allowed combinations, and the configured concurrent scrape limit. Record runtime/package versions, catalog, warm-up, observed retained managed-memory difference, peak observations if available, response sizes, and whether growth plateaus under repeated new combinations. Use an existing native C# example/test or a small C# invocation in existing verification infrastructure; do not create a benchmark product.

Use **64 MiB incremental retained managed memory** as the initial acceptance target for the default selected metrics catalog after warm-up and collection. This is a measurement target, not a hard allocator guarantee or whole-process RSS limit. If it is missed, reduce unnecessary streams/dimensions/buckets or report a concrete tuning decision before claiming the default profile acceptable. The endpoint size cap may bind before every hypothetical series is populated; test and document that interaction rather than promising all theoretical maxima can be scraped simultaneously.

Do not expose a fabricated `metrics_memory_used_bytes` measurement that purports to attribute SDK heap usage. A process-memory metric, measured test delta, configured capacity, and true per-module allocation measurement are different facts.

References: [SDK cardinality/aggregation semantics](https://opentelemetry.io/docs/specs/otel/metrics/sdk/), [.NET SDK metrics memory guidance](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md), and [exporter options](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/PrometheusAspNetCoreOptions.cs).

## 8. Reserved endpoint and application lifecycle

Enabling the optional integration exposes `GET /_metrics` through the standard exporter. The canonical path is reserved and must not be replaced by a business endpoint or silently changed to `/metrics`. Respect the application’s configured path base. Reject incompatible route ownership clearly. Extra aliases are not part of v1.

The response must use the exporter’s correct Prometheus/OpenMetrics content negotiation, content type, escaping, family types, histogram encoding, and concurrency behavior. Keep cumulative temporality. Never reset metrics on a scrape and never make scraping invoke application startup, a health evaluation, or a business operation.

Place the reserved metrics branch alongside existing management/health branches, before the business readiness gate. It must remain available while ordinary business requests are blocked for startup or unhealthy readiness. It must not bypass a separately configured management-access policy. Applications/deployments own access to management endpoints; document how the existing host or network policy protects this route, without introducing a second authentication system.

When metrics are disabled, no exporter or metric history buffer is active and no `/_metrics` route is installed by the module. Installing the integration enables its default automatic module/API collection; existing applications that do not install it retain their behavior.

Use the existing host/container lifecycle. Register OpenTelemetry hosting before dependent service startup so collection is active when ordinary `IStartable` work emits. Preserve the existing two-stage Startup coordination and the ordering of `AddCisHeartbeat` / `AddCisStartup`; do not introduce another startup coordinator or scheduler. Dispose the provider once through its existing owner, after measured work has stopped and before callback owners become unusable. Registration order alone does not prove disposal safety: stop new scrape/callback admission and account for an in-progress collection before disposing its state owners, using the existing/native lifecycle. An expired HTTP request does not establish that its collection callback has finished.

Type Manager discovery and Configuration preparation can occur before the container/provider exists. Do not replay those events, rerun discovery/loading, or create a second bootstrap provider. Expose useful already-owned terminal facts through observable gauges: discovered counts, prepared-state indicator, or last initialization duration. If a duration was not previously retained, the owning operation may retain one final duration/status value. An unavailable value is omitted until known. Live counters/histograms explicitly begin with active collection.

The integration must preserve [Startup lifecycle behavior](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/docs/modules/startup/README.md) and the existing [Healthcheck transport design](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/docs/modules/healthcheck/transports.md).

## 9. Automatic inbound API metrics

### 9.1 Use the framework’s measurements

On the selected .NET/ASP.NET Core version, subscribe to the relevant `Microsoft.AspNetCore.Hosting` instruments:

- `http.server.request.duration`: request-duration histogram with completed-request count and sum.
- `http.server.active_requests`: native active HTTP work.

Derive completed request count/rate from the duration histogram’s `_count`. Derive HTTP server error rates from final status dimensions. Do not create a second middleware timer/counter for the identical incoming request boundary. Subscribe to explicitly selected meters/instruments; adding an instrumentation convenience package must not implicitly enable an unreviewed collection of streams.

Native hosting duration measures the framework request boundary through pipeline/response completion; it is not client-observed network round-trip time. Keep that meaning in all docs. Native active requests are host-wide and have fewer dimensions than completed requests; do not describe them as per-route business concurrency.

### 9.2 Thin policy/enrichment middleware

Add middleware only for behavior the framework does not already supply: CIS policy, bounded enrichment, and reserved-route exclusion. Use the framework’s supported metrics feature/endpoint metadata. It must not independently aggregate events or serialize metrics.

Route-aware enrichment runs after routing and before authorization/business execution so normal authorization failures and other handled responses are included. Host-level recording remains active for failures that occur earlier. Use the route template, for example `/orders/{orderId}`, never `/orders/12345`. Unmatched routes have no route label or one fixed unmatched category.

Preserve one incoming-request observation through exception-handler re-execution and preserve the original route where the framework exposes it. Make enrichment idempotent so re-execution cannot double-add tags. HTTP 4xx, 5xx, and 429 remain distinguishable. Do not depend on `error.type` for the 5xx rate: .NET 10 handled-exception diagnostics can suppress that attribute even though the response is still an error. Arbitrary business failure in a 200 response needs an application metric.

Exclude `/_metrics`, `/_health`, and actual `/_health/...` management routes from completed-request/duration business statistics. Do not accidentally exclude a similarly prefixed business path such as `/_healthcare`. The existing health routes can be middleware branches, so endpoint-only metadata is not sufficient everywhere; use the supported HTTP metrics feature on those branches where required.

The framework’s request-metric disable feature does **not** remove these requests from native `http.server.active_requests`. Document this distinction. Do not duplicate active-request instrumentation merely to hide it. An exact business-only active-work signal can be a separately defined future requirement.

Runtime, GC, thread-pool, Kestrel connection, and diagnostics metrics are optional profiles with explicit budgets. They are not mandatory portable CISLIB metrics.

References: [ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0), [HTTP metrics customization](https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0), and [.NET 10 exception diagnostics](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).

## 10. Existing CISLIB service instrumentation

Instrumentation belongs inside the service that owns the event/state and is automatic once collection is enabled. Reuse the facade/BCL adapter; preserve current constructors and consumers where possible through existing composition or compatible overloads. Do not use reflection to intercept every method or introduce a mutable process-global pointer to the latest provider.

The following is the minimum design catalog. Bind each entry to actual current operations. If an operation/state does not exist, record a short not-applicable explanation instead of inventing new service behavior. Final accepted names and dimensions must be checked into the portable catalog before release.

| Module | Required useful signals | Proposed logical instruments / dimensions |
|---|---|---|
| Logging | Accepted/suppressed records where observed; actual overwrite/discard outcomes; writer failures; current owned spool usage/capacity | `cis.logging.records` counter with bounded `outcome`; separate `cis.logging.discarded.records` counter with `reason`; `cis.logging.writer.failures`; `cis.logging.spool.usage` / `.capacity` in bytes where actual byte accounting exists |
| Heartbeat | Execution count and failures from a duration histogram; active executions; scheduling delay only if already observable | `cis.heartbeat.execution.duration` timer with registered `work` and `outcome`; `cis.heartbeat.active.executions` up/down counter with `work` |
| Healthcheck | Evaluation duration/result, current readiness, monitor state from the existing policy | `cis.healthcheck.evaluation.duration` timer with registered `monitor` and bounded `outcome`; `cis.healthcheck.ready` gauge; bounded monitor-state snapshot consistent with existing state enums |
| Startup | Actual stage/participant duration and outcome, current completion state | `cis.startup.operation.duration` timer with bounded `stage`, registered `operation`, `outcome`; `cis.startup.completed` gauge |
| Configuration | Actual load/refresh/remap duration and outcome; prepared/current state; last-success facts when already owned | `cis.configuration.operation.duration` timer with `operation` and `outcome`; bootstrap/current snapshot gauges, without configuration values as labels |
| Type Manager | Resulting discovered assembly/type counts; final initialization duration/status if retained | `cis.typemanager.assemblies`, `cis.typemanager.types`, and `cis.typemanager.initialization.duration` observable gauges; aggregate counts, not one label per type |

For timed operations, histogram count plus outcome normally supplies executions and errors; do not also add an identical executions counter without a distinct reason. For Logging, accepted/processed/discarded stages are different facts: do not sum different stages into a counter advertised as unique records. Do not time every log call, change the logging overwrite policy, or emit recursive log messages about every dropped metric.

Only use bounded configuration identifiers for `work`, `monitor`, and `operation`, and include their counts in capacity planning. Never enumerate CLR type names, logger categories, or arbitrary registration objects as unbounded labels. Do not invent a skipped-heartbeat outcome if the actual scheduler waits rather than skips. Preserve existing readiness decisions; a metric is an observation of them, not a new health policy.

For every changed module, update its own usage documentation to list automatically available metrics and demonstrate that application callers need no additional instrumentation calls for those operations.

## 11. Contract for future RestClient and other modules

This section is a required design input for those future modules. Do not implement a RestClient or database abstraction as part of this Metrics change. Current acceptance verifies that the facade and exporter can represent these signals; tests requiring an actual RestClient belong to its later implementation.

### 11.1 Future RestClient boundaries

Reuse native `System.Net.Http` instruments for transport-level duration, active requests, connection counts/duration, and queueing where useful. The native `http.client.request.duration` boundary ends when response headers have been read. It does not measure application body consumption or deserialization. Its count is neither guaranteed to equal one logical CISLIB call nor every physical wire transmission: redirects, handler placement, retries, and short-circuiting affect the relationship.

Add only the missing CISLIB semantic layer:

| Signal | Boundary / meaning | Proposed instrument |
|---|---|---|
| Logical operation duration and count | Public RestClient operation entry through completion/failure of the work that API promises, including its retries and body/deserialization work | `cis.restclient.operation.duration` timer; `client`, registered `operation`, `outcome` |
| Active logical operations | Increment at operation entry, decrement once in every exit path | `cis.restclient.active.operations` up/down counter; `client` |
| Application payload uploaded | Actual bytes successfully accepted by the wrapped content/transport stream, under the declared payload definition | `cis.restclient.payload.uploaded` counter, `By`; `client`, registered `operation` |
| Application payload downloaded | Actual bytes returned by instrumented body reads; partial consumption counts only consumed bytes | `cis.restclient.payload.downloaded` counter, `By`; `client`, registered `operation` |
| Transfer duration, when a stream escapes the call | Body-transfer lifetime with explicit EOF/disposal/cancellation/failure outcome | `cis.restclient.transfer.duration` timer; bounded `client`, `direction`, `outcome` |

When an API returns a stream, logical-call completion can precede body consumption. Record that call boundary honestly and let the transfer layer measure consumption until EOF or disposal; early disposal is not a completed download. A stream that is never consumed must not add its `Content-Length` to downloaded bytes.

Counters measure application payload at a documented stream boundary, not TLS framing, HTTP headers, compressed wire traffic, or true network-interface throughput. Count actual reads/writes without buffering bodies solely for metrics. Declare whether retry attempts retransfer and therefore add bytes again; the default is to count actual processed bytes for every attempt. Native `System.Net.Http` does not provide these payload counters automatically.

Use existing resilience instrumentation for retry/timeout/circuit-breaker events when it supplies the required signal. Add a CIS metric only for a distinct missing semantic fact. Circuit-open failure before an HTTP request is still one failed logical operation even if no native HTTP duration exists. Distinguish timeout from caller cancellation using the owning cancellation policy.

Use configured client/operation names. Do not add a raw URL, path, query string, request ID, response payload, or exception message. Native HTTP enrichment APIs can add bounded tags where supported, but enrichment for request duration must not be assumed to apply to connection/queue instruments. Review destination host/port dimensions before enabling them for arbitrary-URL clients.

References: [.NET networking metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-system-net) and [HTTP metrics enrichment](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.metrics.httpmetricsenrichmentcontext).

### 11.2 Future database, transfer, queue, and application modules

Every new internal service must identify its important operations and owned state when defining its contract. Reuse standard library instrumentation first. Typical signals are operation duration/count/outcome, bytes processed, active work, queue depth, and capacity. A database module may count logical queries or use the count of a query-duration histogram; it must define whether retries are separate attempts and keep SQL text and parameter values out of labels.

Applications use the same facade for domain facts such as orders completed or jobs processed. Framework HTTP instrumentation cannot infer those facts. Record them at the event that establishes the business outcome, using stable low-cardinality categories. The agent guide and skill in this delivery are mandatory routing material for future module/application work.

## 12. Queries, rates, and autoscaling use

Do not create a background task that calculates requests/second or bytes/second in the application. Export cumulative counters or histogram counts and derive rates in Prometheus. Keep counters cumulative across scrapes; a process restart is a normal reset. Apply `rate` per series before summing across instances.

Example Prometheus target configuration:

```yaml
scrape_configs:
  - job_name: cislib-applications
    metrics_path: /_metrics
    scrape_interval: 15s
    scrape_timeout: 10s
    static_configs:
      - targets: ["api-1:8080", "api-2:8080"]
```

These are illustrative targets; use the deployment’s discovery mechanism. Scrape each instance individually. A load-balanced target that selects a different replica each time does not provide a reliable cumulative series.

Example completed API requests per second:

```promql
sum by (application, environment) (
  rate(http_server_request_duration_seconds_count[2m])
)
```

Example HTTP 5xx proportion, filling a missing error numerator only for groups with an observed request denominator:

```promql
(
  sum by (application, environment) (
    rate(http_server_request_duration_seconds_count{
      http_response_status_code=~"5.."
    }[5m])
  )
  or on (application, environment)
  (
    0 * sum by (application, environment) (
      rate(http_server_request_duration_seconds_count[5m])
    )
  )
)
/
sum by (application, environment) (
  rate(http_server_request_duration_seconds_count[5m])
)
```

This handles a service with successful traffic and no 5xx series yet. A missing denominator remains absent, and a zero request rate still needs an explicit idle-display policy. It does not repair stale or overflowed telemetry; deployment rules must also check scrape/data health.

Example p95 request latency:

```promql
histogram_quantile(
  0.95,
  sum by (le, application, environment) (
    rate(http_server_request_duration_seconds_bucket[5m])
  )
)
```

Future RestClient download throughput, once that module exists:

```promql
sum by (application, environment, client) (
  rate(cis_restclient_payload_downloaded_bytes_total[2m])
)
```

Add explicit application/environment selectors in application-specific dashboards and autoscaler rules. Do not precompute percentile gauges and average them across replicas. Do not average instance error percentages without weighting by request counts.

Autoscaling consumers must distinguish idle, missing, stale, failed-scrape, and overflowed data. Do not convert missing data to zero unconditionally. Zero-initialize legitimate fixed counter/gauge groups where supported, but never record a fake zero-duration operation to manufacture histogram series. If a status/operation dimension is lost to overflow, filtered error/rate breakdowns can undercount; a total that includes the overflow series is more robust. Alert on overflow and scrape failures, and document fallback/stabilization policy in deployment configuration. This module supplies metrics; it does not deploy or tune an autoscaler.

References: [Prometheus query functions](https://prometheus.io/docs/prometheus/latest/querying/functions/), [instance labels](https://prometheus.io/docs/concepts/jobs_instances/), and [histogram aggregation](https://prometheus.io/docs/practices/histograms/).

## 13. Configuration and usage deliverables

Provide a small CISLIB-owned options model with the limits above, automatic inbound/module collection controls, explicit optional native profiles, histogram overrides, and the fixed reserved endpoint behavior. Validate options once at startup. Use the existing Configuration integration where appropriate, without changing its preparation/lifecycle contract.

Capture resource identity, the selected collection catalog, views, capacity policy, histogram profiles, and exporter/hosting options as a startup snapshot. Changes to that configuration require restart in v1. Do not rebuild or replace the provider in response to live Configuration updates: that would reset cumulative data and risk duplicate lifecycle ownership. Late admission of a static application definition within already-reserved policy is not a configuration reload feature.

The normal C# experience should be equivalent to the following intent. These names are proposed, and the implementation must replace this sketch with a compiling example:

```csharp
// Composition: install the optional integration once.
builder.Services.AddCisMetrics(options =>
{
    options.HttpServer.Enabled = true;
    options.Scrape.MaxResponseBytes = 4 * 1024 * 1024;
});

var app = builder.Build();

// Existing management branches and metrics precede the business readiness gate.
app.UseCisMetrics();

// Existing routing/startup/readiness composition remains authoritative.
```

The finished example must show the real ordering with the existing Healthcheck/Startup APIs, one custom application counter/timer, an API route, sample scrape output with mandatory identity, and a matching query. Do not publish a sample that accidentally builds a second provider or duplicates native HTTP counts.

Deliver these repository assets, using existing conventions for filenames/frontmatter:

| Proposed location | Content |
|---|---|
| `planning/metrics/` | Original requirements/specification and concise implementation decisions |
| `contracts/metrics/README.md` | Portable API semantics, names, dimensions, identity, compatibility and bounds |
| `conformance/metrics/` | Small language-neutral behavioral examples/fixtures, consumed by existing tests where practical |
| `implementations/csharp/src/CIS.Core/Metrics/` | Public abstraction and thin C# primitive/timer implementation |
| `implementations/csharp/src/CIS.Metrics.OpenTelemetry/` | Optional integration package |
| Existing appropriate C# test projects | Primitive, integration, lifecycle and affected-module checks |
| `implementations/csharp/examples/metrics/` | One small working application example |
| `docs/modules/metrics/README.md` | Consumer guide, configuration, native boundaries, query examples and troubleshooting |
| Existing module guides | Their automatic metric catalog and any timing/outcome limitations |
| `.github/skills/cislib-metrics/SKILL.md` | Concise future-agent workflow referencing contracts and real examples |
| Root `AGENTS.md` | Short routing hook outside the managed CIS guidance block |

At the inspected head, CISLIB has no `.github/skills/` directory. These are proposed new assets, not an instruction to overwrite an existing skill if the checkout has since gained one. Keep the skill concise and reference the canonical docs; do not copy this entire specification into every instruction file.

## 14. Acceptance and focused verification

Use the existing native C# stack. Each case verifies CISLIB’s wrapper contract, configuration, or integration, not the internal correctness of the upstream SDK. Reuse suitable existing fixtures and evidence. A few parameterized cases can cover related rows.

| ID | Acceptance condition and evidence |
|---|---|
| A-01 | Counter additions accumulate; up/down deltas are signed; gauge values replace; histogram bucket/count/sum match representative nonnegative observations. Timer completion with valid dimensions records once, invalid completion drops without disposal retry, and a Stop/Dispose race has one completion attempt. |
| A-02 | Equivalent definitions reuse a handle; conflicting type/unit/schema and translated-name collisions fail clearly; definition/name limits are enforced without an unbounded rejected-name cache. |
| A-03 | Invalid values/dimensions are dropped with bounded diagnostics; normal recording remains correct and business execution continues. |
| A-04 | Public consumer contract has no SDK/.NET instrumentation types; Core has no collector SDK dependency; optional package closure and lock files are correct. |
| A-05 | A real `GET /_metrics` parses as the negotiated exposition format; cumulative data survives repeated scrapes; correct content type, escaping, and histogram children appear. |
| A-06 | Every sample has the exact resolved application/environment, including native, histogram, overflow, and target-info samples. Facade/native tag override attempts cannot change them. Exercise identity with characters requiring escaping. |
| A-07 | With a deliberately small cardinality limit, existing series keep updating, additional combinations aggregate to visible overflow, identity survives, and application work succeeds. Do not assert precise SDK-private allocation counts. |
| A-08 | Known catalog/limits are validated; a late over-capacity definition cannot grow storage; oversize scrape fails cleanly; concurrent/slow scrapes release bounded resources. |
| A-09 | Incoming success, 4xx, 5xx, handled exception, authorization failure, and cancellation/no-response behavior match documented native semantics. Error-handler re-execution records one incoming request with the correct route behavior. |
| A-10 | Changing path IDs/unmatched URLs does not create raw-path labels. Scrapes and health probes are absent from completed business-request statistics; native active-request inclusion is documented accurately. |
| A-11 | One representative normal/failure path per changed existing module emits its intended metrics automatically and preserves the existing service result, exception, ordering, and defaults. |
| A-12 | Provider activation precedes ordinary startup work; metrics remain reachable while business readiness is closed; shutdown disposes once, prevents new collection admission, and never lets an in-progress callback read an already-disposed owner. Configuration updates do not rebuild the provider. Pre-container snapshots are exposed without event replay or extra discovery/loading. |
| A-13 | One focused measurement of the default catalog records retained-memory/response behavior and the 64 MiB target result. Repeated new label combinations plateau under the configured caps. |
| A-14 | A working example, actual exported names, PromQL snippets, portable fixtures, repository skill, and AGENTS routing agree with the implemented API. Skills validate through the repository’s installed tooling. |

Future RestClient acceptance is carried forward, not required to pass before that service exists: one logical operation across owned retries; active count restored on every exit; native headers boundary distinguished from body duration; partial/streamed/cancelled transfers count actual bytes; no extra body buffering; caller cancellation and timeout remain distinct when the service can know the cause.

Run affected builds/tests while editing. The current baseline’s ordinary commands, from `implementations/csharp`, are:

```text
dotnet tool restore --configfile eng/Tools.NuGet.config
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Release --no-restore
dotnet test CisLib.slnx -c Release --no-build --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
```

Use narrower affected-project/filter invocations during development and coordinate shared output directories. Updating package references/locks requires the repository’s normal lock-update step before locked restore. Do not run the full stress/performance/compatibility/release campaign on every change. A focused pass is evidence for its tested scope, not full platform/release qualification.

## 15. Implementation workflow and parallel ownership

Follow the installed CIS workflow and current repository instructions. Locate the existing registered workspace; do not recreate it. Preserve this supplied specification as source input. Metrics is a new feature in an existing product, so do not replace the product-wide BRD with this document.

Use the installed `cis brd feature intake` route, inspecting current command help and the [feature-intake manual](https://github.com/andrewspiteri/CIS/blob/75283190777ad691811ade6a7eff247a74105728/docs/manual/cis_brd_feature_intake.md). It supports existing-repository intake and a preview/apply flow tied to the reviewed preview hash. Reuse accepted product decisions. Once applicable feature authority exists, use the supported `cis plan derive <change-id> --file <repo-relative-spec.md>` route when eligible. Do not invent task IDs, approvals, executable authority, or remaining allocations.

Do all preparatory work within existing authorization. Do not ask the user to repeat decisions already made here or require extra impact/plan approvals that the current workflow can derive. If actual new approval is unavoidable, present the concrete prepared change and identify the exact current rule requiring it. Historical work owners and completed budgets are not reusable authority.

Use three coherent implementation outcomes as the default decomposition:

| Outcome | Owns | Dependency and parallelism |
|---|---|---|
| 1. Portable metrics primitives | Contract, Core facade/adapters, timer, definition policy, primitive fixtures/tests | Establish shared public types and metric catalog first; one owner controls shared API files |
| 2. Collection and application integration | Optional package, provider/views/limits, reserved route, native API metrics, integrated example, usage skill/docs | Starts after outcome 1; owns project/package changes and shared host files; integrates outcome 3 and completes host acceptance |
| 3. Existing service instrumentation | Named Logging/Heartbeat/Healthcheck/Startup/Configuration/Type Manager files, their focused tests and docs | Runs in parallel with outcome 2 after shared definitions; no edits to provider/shared host files |

Assign nonoverlapping file ownership before parallel edits. One owner coordinates solution/package/API changes and builds sharing output folders. Keep documentation, focused verification, independent review, and completion evidence within these outcomes. Do not add standalone planning, coordination, evidence-generation, or final-sweep tasks without a separate owned product outcome.

Outcome 2 can implement in parallel with outcome 3, but cannot complete until outcome 3 is integrated and the affected host checks pass. Represent this completion condition in the installed workflow without accidentally serializing the independent implementation work. Split a separate integration outcome only if the actual workflow cannot represent that ownership coherently.

When integrating, resolve only material remaining risks. Reuse valid focused results; administrative changes alone do not justify rerunning tests. Publish a compact completion record: what changed, why, exact affected tests/footprint observation, remaining limitations, and actual skill/docs paths. Do not claim release readiness or publish/merge a release merely because the module’s focused checks pass.

## 16. Definition of done

The change is complete when MET-01 through MET-12 and A-01 through A-14 are satisfied within their stated scope; current applications retain their behavior; the example produces the documented output at `/_metrics`; memory/overflow behavior has measured evidence; all actual consumer signatures and metric names are documented; and future agents are routed to a working skill and guide.

The future RestClient/database contracts must be preserved as future requirements, with no speculative service implementations. Any deviation must identify the affected requirement, the actual technical constraint, and the chosen compatible alternative. Do not quietly weaken mandatory identity, finite limits, direct Prometheus exposure, or the portable consumer API.

## 17. Source index

The user’s requirements and accepted repository instructions govern product scope. The following primary sources support implementation facts; they are references, not instructions to expand scope:

1. [Microsoft: metrics collection](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection) and [instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation).
2. [OpenTelemetry Metrics API](https://opentelemetry.io/docs/specs/otel/metrics/api/) and [SDK](https://opentelemetry.io/docs/specs/otel/metrics/sdk/).
3. [OpenTelemetry .NET SDK customization](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md).
4. [Pinned Prometheus ASP.NET Core exporter README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md), [options](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/PrometheusAspNetCoreOptions.cs), and [view configuration](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry/Metrics/View/MetricStreamConfiguration.cs).
5. [Prometheus metric types](https://prometheus.io/docs/concepts/metric_types/), [exposition formats](https://prometheus.io/docs/instrumenting/exposition_formats/), [naming](https://prometheus.io/docs/practices/naming/), and [query functions](https://prometheus.io/docs/prometheus/latest/querying/functions/).
6. [ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0), [HTTP customization](https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0), and [error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).
7. [System.Net.Http built-in metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-system-net) and [enrichment API](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.metrics.httpmetricsenrichmentcontext).
8. [CISLIB root guidance](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/AGENTS.md), [dependency policy](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/docs/architecture/csharp-dependency-policy.md), and [CIS import instructions](https://github.com/andrewspiteri/CISLIB/blob/1f72e9ea4d8630edacb59c01795ebc8467807cd8/.github/instructions/cis-import.instructions.md).
