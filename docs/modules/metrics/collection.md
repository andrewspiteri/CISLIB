---
title: Metrics collection policy
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.collection
---

# Metrics collection policy

CIS.Metrics.OpenTelemetry is optional. Core retains its existing dependency
closure and [CIS-owned facade](core.md). Collection uses OpenTelemetry SDK and
hosting 1.21.0 plus the prerelease Prometheus ASP.NET Core exporter 1.21.0-beta.1.
Aggregation, overflow, collection coalescing and serialization remain upstream.
See the pinned [exporter API](https://github.com/open-telemetry/opentelemetry-dotnet/blob/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md).

The optional net10.0 closure also includes OpenTelemetry.Api and
OpenTelemetry.Api.ProviderBuilderExtensions 1.21.0, plus
System.Diagnostics.DiagnosticSource 11.0.0 required by the API package.
DiagnosticSource 11.0.0 has a net10.0 asset with no further package dependencies;
it does not require moving the host to .NET 11. The selected .NET/ASP.NET Core
runtime remains 10.0.12 and SDK remains 10.0.401. Normal optional-consumer
build/publish output must carry the resolved DiagnosticSource package runtime
asset; the shared framework's DiagnosticSource 10 copy is insufficient.
Core-only applications retain the original Logging/DI abstractions 10.0.12
closure and acquire no OpenTelemetry or DiagnosticSource package reference.
See the [dependency policy](../../architecture/csharp-dependency-policy.md)
for the exact six-package locked optional closure.

The first governed optional restore failed NU1100 on the then-unmapped
DiagnosticSource dependency. A subsequent NU1102 reported 11.0.0 as both
requested and nearest version. Both preceded collection tests. The approved
sixth exact mapping and a cache-bypassed restore resolved the unchanged pinned
closure, including the DiagnosticSource 11 net10.0 runtime asset. The optional
assembly compiled and the final locked collection proof passed all 31 cases
with zero failures/skips. This is the retained collection checkpoint. Subsequent
WORK-005 checkpoints include the real example, isolated package build/publish,
host integration and the single footprint observation; their exact results and
post-correction input currentness are recorded in
[CIS-0009 evidence](../../changes/CIS-0009/verification.md).

~~~csharp
using CIS.Metrics.OpenTelemetry;

builder.Services.AddCisMetrics(options =>
{
    options.CollectIncomingHttp = true;
    options.CollectCoreModules = true;
});
~~~

Install after callback-owner lifecycle services and before Heartbeat/Startup.
Use sequential stopping and await actual host stop before disposing callback
owners. Provider activation verifies the finished resource before ordinary
startup work. The integrated hosting layer owns reserved-route admission,
readiness ordering, management exclusions and supported host-order validation;
registering a provider alone does not prove those application contracts.

Enabled=false registers a no-op facade without a provider/exporter. Otherwise
there is one SDK-owned DI provider and one cumulative Prometheus reader.
Identical repeated installation is idempotent; a conflicting profile fails.
The root meter factory owns meters and DI owns provider disposal. Core never
builds or disposes another provider.

## Ownership and identity

Existing OpenTelemetry hosting, tracing and logging can share the container.
CIS exclusively owns metrics configuration. Prebuilt providers, independent
readers/views, global resource callbacks and later metric/exporter additions
or replacements are unsupported and fail clearly. Global
OpenTelemetryBuilder.ConfigureResource also configures metrics and crosses this
boundary even in an application primarily using tracing.

The guard captures the SDK configuration descriptor type through its public
registration method and compares exact owned descriptors before deferred SDK
configuration. Independent validation at activation also catches replacement
provider factories that bypass SDK callbacks. There is no private-field
reflection, user-factory inspection or arbitrary-builder certification.

The resource starts empty and captures EnvironmentOptions.ApplicationName and
EnvironmentOptions.EnvironmentName once as the application/environment labels.
Existing Unicode validation and the unknown environment default remain intact.
ASP.NET environment names and OTEL_RESOURCE_ATTRIBUTES do not override identity.
At most 16 additional bounded string attributes are accepted, with unique
normalized keys. Only the mandatory two keys project onto every measurement.
Target-info stays enabled, exemplars stay off, and actual exporter options and
the finished provider resource are checked before activation succeeds.

## Selection and finite profiles

Each host captures exact factory-created CIS.Metrics and
Microsoft.AspNetCore.Hosting meter references before provider activation.
One total view selector combines allowed keys, cardinality and histogram bounds.
Foreign/unselected instruments return SDK Drop; null is never a filtering
strategy because it can permit the default stream. No exported host ID or
public arbitrary scope is needed.

Native selection contains only http.server.request.duration and
http.server.active_requests with pinned framework kinds, units and descriptions.
Duration retains method, declared route and status; active requests retain
method and scheme. Reserved identity keys never survive the view. Completed
request counts derive from the duration histogram, without a duplicate timer.
Disabling completed management-request duration does not remove management work
from native active-request measurements.

The automatic module catalog reserves 19 Core streams and capacity before
producers appear. Actual owner attachment creates handles/callbacks; placeholder
callbacks and event replay are excluded. Unavailable facts emit nothing.
Immutable Type Manager report snapshots may be observed independently per host.
Mutable live owners belong to one host, and borrowed Logging aliases are
deduplicated by reference.

| Control | Default |
| --- | --- |
| Core/application definitions; total SDK streams | 96; 128 |
| Ordinary; generic histogram; HTTP-duration capacity | 128; 64; 1,024 |
| Sum of configured dimension capacities | 16,384 |
| Finite histogram bounds | At most 32 |
| Scrape response; concurrent handlers; waiting queue | 4 MiB; 2; 0 |
| HTTP wait/write timeout; cache | 5 seconds; 300 milliseconds |

The standard seconds profile has 14 finite bounds:
0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10.
Streams[name] overrides positive capacity and optional finite histogram bounds;
non-histograms reject bounds. Configured late producers reserve metadata before
publication. Other late declarations use the remaining fixed budget. Capacity
exhaustion after startup returns a no-op while admitted instruments keep updating.
Rejected names are not retained.

SDK zero-dimension/overflow points are additional. Multiple negotiated buffers
and SDK state consume memory beyond the response cap. A 14-bound histogram
emits 17 classic series per dimension combination: HTTP alone can reach 17,408.
The response cap can bind before all hypothetical series are populated.
The pinned exporter has an 85,000-byte initial buffer and rejects lower caps;
the v1 default remains four MiB. Under the fixed underscore strategy, alternative
escaping requests render and report the effective underscore scheme. Legacy
Prometheus/OpenMetrics versions and version-1 media types retain their own
negotiated versions.
The separate default-catalog 64 MiB retained-managed-memory observation is a
measured acceptance target, not a runtime heap guarantee.

## Callback lifetime and evidence

Callbacks perform bounded in-memory reads only. Core counts execution, Count
access and observation materialization through finally. Invalid observations
are omitted individually; ordinary callback/list failures omit that collection.
Diagnostics have fixed bounded categories. Stopping closes callback admission;
Stop waits for actual admitted callbacks, then releases targets. Later SDK
collections return empty without reading stopped owners.

An expired HTTP response releases its wait, not a synchronous callback.
Provider disposal and request timeout do not establish owner settlement.
The host barrier keeps owners usable until actual Core drain completes.
Default native HTTP instruments are synchronous; a native observable profile
would need its own explicit lifetime proof.

The locked native collection proof passed 31/31 cases, zero failed/skipped,
with current complete collection scope at execution
fd6199e633ea4200ae005be912f159e2. Real SDK/exporter/Kestrel output verified all
supported text/OpenMetrics formats and negotiated effective escaping,
every-sample authoritative identity, histogram/overflow/native samples, foreign
configuration rejection, two-host isolation and actual Core callback drain
after an expired scrape.

WORK-005 adds the product endpoint, native HTTP/readiness policy, actual callback
owner lifecycle and automatic module binding. Recorded Windows checkpoints also
exercise the real source example and isolated packaged build/publish, including
the deployed DiagnosticSource 11 assembly on .NET 10.0.12 and a Unicode identity
over 256 UTF-8 bytes within the existing scalar limit. The single default-catalog
footprint observed a 6.20-MiB retained delta for its stated workload.
The [verification record](../../changes/CIS-0009/verification.md) preserves the
restore failures, original checkpoints, later corrections and actual currentness.
Compatibility, mutation and post-correction checks must be read there rather
than inferred from these earlier passes. Focused coverage remains below the
full 95/90 release gates; no full platform or release qualification is claimed.
