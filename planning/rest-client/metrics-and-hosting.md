---
title: RestClient Metrics and hosting proposal
type: planning-reference
status: Draft
---

# RestClient Metrics and hosting — proposed reconciliation

Status: planning draft; none of the new options, types or integration below exists yet.
Basis: complete RestClient specification/guide v1.0, actual main `7b5923c0`, existing Metrics contracts and hosting.
The supplied workflow text is reference material; this draft grants no implementation or native-execution authority.

## Conditional catalog and unchanged global bounds

Preserve every Metrics-only application's current nineteen Core reservations, including explicit `MaxDefinitions=19` and small stream profiles.
Reserve the four REST logical profiles only when final composition contains the actual configured REST registry and automatic Core collection is enabled.
Proposed `bool? CollectOutgoingHttp`: null/Auto enables outgoing collection for configured REST composition; true explicitly enables it; false explicitly opts out.
This option is proposed, not an available `MetricsOptions` member. Absence of REST with Auto retains the old catalog exactly.
Resolve the final REST-presence marker from known registration metadata before provider construction, without invoking user factories or constructing transport.
Capture one immutable effective profile then; this must work with AddCisMetrics before or after REST registration and never rebuild a running provider.
Explicit native opt-out does not disable logical REST observations; `CollectCoreModules=false` still suppresses automatic logical module binding.

| Logical/native instrument | Kind / unit | Kept dimensions | Capacity |
| --- | --- | --- | --- |
| `cis.restclient.operation.duration` | Timer / Seconds | client, operation, outcome | 512 |
| `cis.restclient.active.operations` | Up/down / Count | client | 128 |
| `cis.restclient.payload.uploaded` | Counter / Bytes | client, operation | 128 |
| `cis.restclient.payload.downloaded` | Counter / Bytes | client, operation | 128 |
| `http.client.request.duration` | Native histogram / seconds | http.request.method | 64 |
| `http.client.active_requests` | Native up/down / requests | http.request.method | 128 |

The full default composition becomes twenty-three Core plus four native streams: 27 streams, configured capacity sum 4,416.
Derivation: existing Core/inbound sum 3,328 + logical REST 896 + outgoing native 192; the old 3,520 footprint included its extra application pair.
Keep global limits at 96 definitions / 128 streams / 16,384 configured capacity / four-MiB encoded response. SDK special points remain additional.
The duration profile keeps fourteen finite bounds. Four-MiB export failure can precede theoretical full cardinality; it is not a heap bound.
Reserve native bases and generated children before producer activation, validate translated collisions, and update selected/reserved catalog snapshots intentionally.
Existing Metrics-only metadata tests keep their old expectations; add configured-REST expectations without requalifying the old footprint as a new result.

## Exact native and logical ownership

Set owned SocketsHttpHandler.MeterFactory to the root's existing IMeterFactory before first use; reuse managed pools, never a pool per call.
Capture the matching root-created System.Net.Http Meter in the existing Metrics context and extend its single total selector/AddMeter configuration.
Accept only the two pinned native kinds/units on that exact Meter; drop process-global/foreign meters and every other outgoing instrument or key.
On .NET 10, active requests is UpDownCounter<long>, not the .NET 11 observable form described by newer docs; DiagnosticSource 11 does not change the runtime version.
Native duration/active work end at headers. They do not replace logical duration, measure decoded body work, or guarantee explicit attempt counts.
A bare borrowed HttpClient hides its handler configuration/provenance: do not claim isolated outgoing collection or validate hidden redirects/cookies/retry layers by inference.
Keep borrowed-resource nondisposal and a documented supported managed transport profile; no private-field inspection or independent provider.

One real mutable REST registry claims one host and atomically publishes one complete four-handle binding, rather than four instruments per client.
Validate at most sixteen client aliases and thirty-two total client/operation pairs, including each client's literal request fallback.
Alias strings must fit ordinary Metrics value bounds (valid, control-free Unicode within 256 UTF-8 bytes), with ordinal comparison.
Unknown per-call operations use the predeclared request fallback and bounded diagnostics; retain no URL/unknown-name cache.
Capture the binding once at logical admission with the entry timestamp, and pass that exact context through retries and body adapters.
Record final duration once after actual cleanup, and decrement active work once on result, cancellation, stopping and thrown-defect paths.
Count bytes returned by owned application/decompressed response reads and accepted by completed request-content writes, including retransmission on real retries.
Count no Content-Length inference or unknown partial bytes from a write that threw; signing may materialize only an explicitly bounded reusable representation.
Do not add a placeholder transfer timer, duplicate logical completion counter, SDK type to consumer APIs, or event replay.

## Required pre-drain connection

ApplicationStopping alone is insufficient: Generic Host invokes lifecycle StoppingAsync first, while an active Heartbeat callback may await REST.
In the existing Startup hosting cutoff, signal the optional REST owner's BeginStopping before Configuration/Heartbeat drains, then join actual REST settlement with all existing barriers.
Give the Heartbeat-only bridge the same early REST cutoff and real joined drain. Preserve the coordinator's module independence and existing ordered stopping guards.
Close admission first, request per-operation cancellation outside control gates, and retain tracked operation/cancellation tasks until actual cleanup finishes.
An expired caller/host wait is not a drain. Do not dispose borrowed clients/handlers or owned transport/provider dependencies while operations remain active.
Keep startup-token cutoff and repeated stop/dispose idempotent through the existing host adapters; introduce no generic closing-participant registry or lifecycle framework.

## Coherent RC-5 / RC-6 ownership and focused evidence

RC-5 owns Core RestClient Runtime/Hosting orchestration, operation admission/deadline/settlement and the narrow Startup/Heartbeat hosting changes.
RC-6 owns the fixed REST metrics bundle/catalog plus existing optional Metrics Options, Settings, Context, StreamCatalog and registration selection changes.
Agree the operation/body-observation seam first; RC-6 does not edit RC-5's orchestrator or RC-2's native content/codec files concurrently.
The outer operation passes its captured observation context to existing native body wrappers; no new callback/stream framework is required.
Include one real header-first/held-body exchange, partial/retried byte observations, balanced active/terminal metrics on failure/cancel/throw, and unknown-alias fallback.
Use two real hosts/factories to prove outbound isolation, mandatory identity, only method retained, foreign-meter drop, opt-out and registration-order independence.
Exercise a held HB-to-REST call during ordered host stop: cancellation is observed, cleanup remains held, dependencies remain usable, then actual release permits all drains.
Also retain Core-only/no collector behavior and existing small Metrics-only profiles. Reuse current MSTest/Kestrel/ProcessRunner/TemporaryDirectory fixtures; no new harness or broad memory campaign.

## Resource boundary

Metrics' allocation is closed. REST needs its own approved shared work/attempt grant; planning is not authority to restore dependencies or execute tests.
Reuse the existing physical six-GiB ceiling/reference with all retained claims and the included restoration reserve; do not reset it or create a second pool.
Obtain a fresh read-only counted-storage/headroom observation before later native admission. New resilience packages, isolated feeds/caches, package build/publish outputs and test scratch share that headroom.
No cleanup is proposed here. Reuse selected dependency caches safely, keep temporary consumer ownership explicit, and avoid duplicating large bodies or worktrees per acceptance row.
