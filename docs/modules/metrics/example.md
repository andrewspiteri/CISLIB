---
title: Metrics in CountryData
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.example
---

# Metrics in CountryData

[CountryData](../../../implementations/csharp/examples/CountryData/README.md) is
the canonical runnable example. CISApplication composes the installed
CIS.Metrics.OpenTelemetry registrar through its prepared Type Manager. The
integration uses the existing provider, views, central identity and reserved
/_metrics branch. Application code uses IMetrics without a collector or duplicate
HTTP timer. See [hosting](hosting.md), [HTTP policy](http.md) and [collection](collection.md).

From implementations/csharp with SDK 10.0.401 and NET/ASP.NET Core 10.0.12:

```powershell
$env:CORE_SERVICE_NAME = "CountryData"
$env:CORE_ENVIRONMENT = "local"
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

After initial publication:

```text
curl http://127.0.0.1:5087/country/MT
curl -H "Accept: text/plain; version=0.0.4" http://127.0.0.1:5087/_metrics
```

Only these application definitions are added, without dimensions:

| Logical definition | Exported family | Observation |
| --- | --- | --- |
| app.country_cache.entries, Count | app_country_cache_entries | Distinct current countries, not alias count. |
| app.country_cache.last_success_timestamp, Seconds | app_country_cache_last_success_timestamp_seconds | Publication Unix seconds; omitted before first success. |

Callbacks read retained immutable state and perform no I/O. Failed refresh
preserves count and timestamp. Cache age requires no third gauge:

```promql
time() - app_country_cache_last_success_timestamp_seconds{application="CountryData"}
```

Existing native incoming/outgoing and CIS module observations remain authoritative.
Route labels use /country/{identifier}, never names, codes or raw identifiers.
Central application/environment labels apply to ordinary, histogram, overflow,
native and target_info samples. Management duration exclusions and host-wide
native active requests follow the unchanged HTTP policy. Apply normal host and
deployment policy to management access.

Collection admission closes and actual callback execution/materialization drains
before owners are disposed. Provider disposal or an expired scrape is not that
barrier. An absent/disabled adapter uses the Core no-op facade without changing
catalog behavior.

Package consumers use the versioned Core SDK, supplying its exact runtime
reference, plus exact CIS.Metrics.OpenTelemetry and CIS.RestClient.Resilience
PackageReferences. Do not duplicate the SDK's Core reference. Metrics retains
OpenTelemetry 1.21.0, Prometheus exporter 1.21.0-beta.1 and privately deployed
DiagnosticSource 11.0.0 on .NET 10.0.12. Resilience uses Polly.Core 8.9.1; Core's
Logging/DI abstractions stay 10.0.12 without either optional SDK.

[CIS-0009 evidence](../../changes/CIS-0009/verification.md) retains its historical
collector/HTTP/package scope. Actual CountryData observations must identify their
current inputs; this guide establishes no passing scrape, live-provider smoke or
platform/release qualification. Test controls stay outside the application.
