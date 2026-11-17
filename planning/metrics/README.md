---
title: Metrics supplied requirements and reconciliation
type: planning-reference
status: Draft
---

# Metrics requirements and reconciliation

The two supplied v1.0 documents are inputs for the next module after Configuration.
Their embedded agent prompts, workflow commands and example APIs are reference
material. They do not themselves grant implementation authority or a new allocation.
Andrew Spiteri subsequently explicitly approved the profile, five-task plan and
shared allocation. The current change is [CIS-0009](../../docs/changes/CIS-0009/proposal.md), on local
branch `codex/metrics-bounded-collection`, based on merged `main`
`1f72e9ea4d8630edacb59c01795ebc8467807cd8`.

| Preserved source | SHA-256 |
| --- | --- |
| [Implementation specification](sources/CISLIB_Metrics_Implementation_Specification_v1.0.md) | `b40ab50eaf9bfd679b1ba85196c4470e39930bf46534ad2532cced41ef687b13` |
| [Agent guide](sources/CISLIB_Metrics_Agent_Guide_v1.0.md) | `e3cf1558b78df91a3c0a28bda86afcc02c2b00b7be97064507cbad480a2b8bf5` |

Both originals are retained byte-for-byte; `.gitattributes` prevents line-ending
conversion. The existing product BRD, technical intent, accepted architecture,
module contracts, native MSTest route and completed CIS-0001–CIS-0008 owners remain
unchanged. Actual implementation evidence follows the separate authorization;
the intake checks below do not claim that an unrun implementation passed.

## Approved scope and profile

Implement MET-01–MET-12 and A-01–A-14 as specified: portable counters, up/down
counters, gauges, histograms/timers and observable gauges; a thin BCL-backed Core
facade; one optional `CIS.Metrics.OpenTelemetry` integration; actual `/_metrics`;
authoritative identity, finite collection/scrape limits, native inbound HTTP and
automatic useful observations of the six existing modules. Include portable
contracts/fixtures, compiling example, metric catalogs, queries, concise repository
skill and routing, meaningful native checks and one focused footprint observation.

Keep RestClient/database/transfer requirements as future contracts. Do not build
those services, a metrics engine, serializer, collector, history, scheduler,
autoscaler, dashboard, replacement test harness or release campaign.

The supplied numeric policy was selected in the actual profile/allocation decision,
not invented as an earlier historical approval: 96 definitions, 128 streams, 128 ordinary/64 generic-histogram/1,024
HTTP-duration combinations, sum of configured dimensioned capacities 16,384, at
most 32 finite buckets, eight measurement keys, and source section 7's string and
resource limits. Use 14 supplied duration boundaries, four-MiB response cap, two
scrape handlers/no queue, five-second HTTP wait/write timeout, 300-ms cache and
exemplars off. Identity retains EnvironmentOptions' existing Unicode limits.
The 64-MiB incremental retained-memory target is a measured acceptance target,
separate from the four-MiB response cap and the disk allocation.

## Reconciliation with implemented contracts

| Existing boundary | Metrics consequence |
| --- | --- |
| Core's exact Logging/DI 10.0.12 NuGet closure plus ASP.NET framework | Keep unchanged. Approve the optional package and its pinned third-party closure explicitly; do not move the SDK/exporter into Core. |
| .NET SDK 10.0.401 / runtime and ASP.NET 10.0.12 | Preserve pins; native `Gauge<T>` is available without another Core NuGet. |
| EnvironmentOptions | Capture actual ApplicationName and EnvironmentName once. Preserve `unknown`, validation and override behavior. Never substitute ASP.NET identity or apply ordinary-value limits to valid identity. |
| Type Manager | Reuse PreparationReport's elapsed time and counts. Never rediscover or replay pre-container operations. |
| Configuration | Reuse retained validity/revision/last-success facts; at most one terminal initialization duration/status snapshot if needed. Live changes do not rebuild Metrics. |
| Startup and Heartbeat | Keep sequential construction/initialization and real owner barriers. Coordinator remains independent of optional SDKs. Metrics activation precedes normal work; collection admission/drain precedes callback-owner teardown. |
| Logging | Distinguish incoming/consumed/evicted/suppressed and per-writer facts. General spool-byte accounting is absent; do not invent it or change overwrite policy. |
| Healthcheck | Observe actual collector evaluations and existing readiness/state snapshots. Scraping never triggers evaluation or changes readiness. |

Current constructors and precreated service instances must remain usable. Shared
contracts must define explicit host-owned attachment and callback lifetime; a
mutable global pointer to the latest provider is excluded. Identifier dimensions
use finite configured aliases; otherwise retain aggregate outcomes/state and omit
unavailable identifier detail. CLR names, GUIDs and arbitrary names are not labels.

## Initial technical proof assessment

The referenced SDK 1.21.0/exporter 1.21.0-beta.1 public sources expose the required
resource projection and response cap. The exporter is prerelease. At intake, no
OTel packages were present in the local cache and compatible restore, locks and
actual behavior were unverified. This preserves that initial assessment; the
approved source mappings, locked restore and subsequent native observations are
recorded in [CIS-0009 evidence](../../docs/changes/CIS-0009/verification.md).

Primary implementation references are the pinned
[exporter options](https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet/coreunstable-1.21.0-beta.1/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/PrometheusAspNetCoreOptions.cs),
[SDK view selection](https://raw.githubusercontent.com/open-telemetry/opentelemetry-dotnet/core-1.21.0/src/OpenTelemetry/Metrics/MeterProviderSdk.cs),
[ASP.NET hosting meter](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Hosting/Hosting/src/Internal/HostingMetrics.cs)
and [DI meter factory](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/Microsoft.Extensions.Diagnostics/src/Metrics/DefaultMeterFactory.cs).

Use one combined effective view for identity-key exclusion, allowed keys,
cardinality and buckets. Capture exact host-owned CIS/native Meter instances before
provider activation and drop foreign/unselected instruments; do not expose arbitrary
consumer scopes. The pinned SDK has an explicit Drop configuration; returning null
can permit its default stream. Extra uncontrolled views/readers or a prebuilt
provider remain unsupported when they cannot meet policy. Prove two-host isolation
with real CIS and native HTTP observations, including reversed activation order.

Provider disposal or an expired HTTP request does not prove a synchronous callback
has settled. First establish narrow collection/callback admission and actual drain
through existing host ownership, then hold a real callback across timeout/stop and
prove its owner remains usable. Do not solve this with a second collector or generic
lifecycle registry. This is an early implementation gate before broad module edits.

## CIS route and present authority

Installed CIS is `0.3.0+2b916962dbedef18bdde8959e3144184d37d64f9`.
The registered workspace has exactly one product-owned authority repository.
`brd feature intake --dry-run` was attempted with `repositoryMode=existing`, this
repository and its existing docs root. It rejected the target:

> Choose a product-owned participant with its existing documentation root. A feature cannot reclassify an authority or external dependency.

No feature-intake apply/hash, canonical feature approval or Active feature exists.
The workspace was not reclassified and CIS was not upgraded. The supported
in-place Draft change/outcome route was used instead. `plan derive` requires an
Active/current backlog feature, so it is not eligible for these unapproved inputs.
This specific limitation does not require a replacement product BRD or repository.

The [bounded plan](implementation-plan.md) and [acceptance map](acceptance.md) are
bound to authorization `cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e`,
decision `CISLIB-METRICS-2026-11-17:USER-APPROVED-PROFILE-ALLOCATION`. The canonical
five tasks and finite allocation are recorded in CIS. Existing completed budgets
fund no Metrics execution. Preparation/source review remain distinct from native
implementation results.

## Approved package-source correction

Andrew Spiteri explicitly approved adding only
`implementations/csharp/NuGet.config` to support the five required OpenTelemetry
package IDs. The existing package sources and every previous mapping are retained.
The original `metrics` authorization scope remains byte-equivalent in meaning and
paths; a separate narrow `metrics-package-sources` scope records the correction so
started task contracts and their evidence remain valid. The first same-scope
representation made those contracts stale and was replaced through the supported
CIS authorization route, without editing protected scope hashes or accounting.

Supporting WORK-006 owns only this one-file mapping and its XML comparison. Its
120 seconds and one attempt are redistributed unused WORK-001 capacity. The shared
36,000 seconds,12 attempts,900 restoration seconds and physical6-GiB storage limit
are unchanged. `authorization-package-sources-compatible-request.json` names the
same existing human approval with a distinct corrected-representation reference;
it does not claim another human decision or functional allowance.


The subsequent explicit user approval permits one additional exact
`System.Diagnostics.DiagnosticSource` mapping, required by the downloaded
OpenTelemetry.Api1.21.0 net10 asset. WORK-007 owns this correction without changing
the completed five-ID WORK-006 contract. Its180 seconds and one attempt are
redistributed unused foundation time and an unused collection attempt. These two
small support leaves preserve the original five implementation owners and three
outcomes; all shared totals and restoration remain unchanged. The six mappings
are now applied. Subsequent Windows checkpoints include locked collection,
actual host integration, the real source example, isolated package build/publish
and one scoped footprint observation. Their results, post-correction input
currentness and remaining checks belong to
[CIS-0009 evidence](../../docs/changes/CIS-0009/verification.md).

## Local implementation checkpoint

The approved Core facade, optional provider/HTTP integration and six-module
observations are implemented locally. The initial proposal and source-assessment
history remain recorded here and in the bounded plan; they are not a second authority.
Recorded checkpoints include the 5,787-case Core unit run, 65-case real-host run,
source example and isolated package build/publish, plus the one 6.20-MiB footprint
observation. Architecture reached 352/352 after moving the Healthcheck attachment
lookup into Core Metrics hosting. These are scoped checkpoints, not proof that
all inputs remained current after every later correction.

The post-correction host/Healthcheck run passed 123/123, the isolated packaged
consumer passed, and Compatibility passed 179/179. The whole TimingScope
component reached 100% (8/8 killed) after two meaningful invalid-completion cases
were added; the subsequent normal Metrics Unit run passed 118/118. Consult the
current verification record for exact scope and currentness. Full platform qualification,
whole-shipping 95/90 coverage and the 90% mutation release gate remain separate.
