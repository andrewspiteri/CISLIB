---
title: "CISLIB product technical intent — registration proposal"
type: specification
owner: Repository maintainers
review_cadence: on accepted product-direction change
status: Draft
scope: Workspace
last_reviewed: null
cis:
  stable_id: cislib:spec:technical-intent
  technical_intent_schema: 1
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# CISLIB product technical intent

This registration document maps existing source direction and the cleaned implementation at reviewed revision `f05b02c0c4967bfee2aabb1f4140709923ad5d3a` into one product-level technical baseline. It does not alter accepted requirements, resolve new technical choices or certify release readiness. Canonical technical-intent approval is recorded separately through supported CIS controls in the document metadata. Source paths below are relative to the CISLIB repository.

## CIS technical baseline

<!-- cis:technical-intent-baseline:start -->
| Kind | ID | Version | Evidence |
| --- | --- | --- | --- |
| brd | cislib:spec:business-requirements | semantic-v1:sha256:6190ff89fbcb18f0e009e516f8cd1bafa6855e5891713c66dbc9eed57e3bc5ce | Active canonical BRD |
<!-- cis:technical-intent-baseline:end -->

## Approved business baseline

The canonical product BRD at `docs/specs/business-requirements.md` is Active and approved by Andrew Spiteri. Its `CIS-BR-*` identifiers supply the accepted business authority for this technical mapping; the managed baseline above binds the actual approved semantic digest. `planning/decisions.md` and current explicit user decisions govern superseded historical names, package boundaries and orchestration instructions.

The retained product outcomes are reusable Logging/environment behaviour, pre-DI managed-type preparation and application-owned Healthcheck, one `CIS.Core` package, host ownership, compatibility from first actual publication and meaningful native verification. This technical proposal traces to those existing outcomes without converting future specifications into implemented capabilities.

## Design goals and principles

Keep direct, readable code with explicit module and host responsibilities. Keep actual product behaviour and meaningful assertions while using ordinary native development commands. Preserve bounded work, detached observations and truthful failure/settlement facts. A snapshot/report must not mutate health, a wait timeout must not claim underlying work stopped, and a queue admission must not claim durable output delivery.

CIS is development governance tooling only and remains outside the shipping library's runtime references. The cleanup removed obsolete campaigns and checker protocols; this registration does not recreate them. Fixtures, examples and test support remain development or consumer evidence, rather than new product modules.

## Technical surface and ownership

### Product surfaces

The primary surface is the .NET in-process library API. Supporting machine-facing surfaces are host-integrated Healthcheck HTTP reporting, local pipe protocol and matching non-core probe executable. Type Manager supplies build-time inventory generation and runtime catalog/query APIs. Console/file logging are output capabilities used by a host, not standalone application frontends.

### Frontend technology

No user-facing UI

The product has no frontend application or browser/mobile/desktop presentation technology. HTTP routes are health-reporting contracts inside an application host. The line above records the settled source direction. Accepted-document questionnaire derivation requires the actual approved technical baseline; the canonical no-visual-UI applicability record remains separate and governed by supported questionnaire/adoption controls.

### Backend technology and topology

Use existing C# `net10.0`, pinned SDK `10.0.401`, selected .NET/ASP.NET runtime `10.0.12`, nullable analysis and warnings as errors. Maintain one repository, one `CIS.Core` package/assembly and module namespaces `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`. The product is a modular reusable library; there is no new server process, distributed-service topology, application database or UI deployable.

The explicit component-ownership table below records the existing physical responsibilities. Cross-draft `TI-MOD-*` labels in the adoption handoff are proposals, not manually installed managed CIS identities. Reconcile those labels to the actual component identities supplied by native CIS interpretation for the downstream solution bundle. Test projects, fixtures, examples, frozen consumers and `CisHealthProbe` keep their existing roles. Their presence adds no product-ownership participant.

## Runtime architecture and trust boundaries

| Component | Responsibility | Owns | Must not own | BRD authority |
| --- | --- | --- | --- | --- |
| Logging and environment | Preserve bounded diagnostics and explicit lifecycle/delivery observations. | Existing Core environment, `CIS.Logging` and `CIS.Logging.MicrosoftLogger` contracts, options, capture, processing and writers. | Application lifecycle/resources, collector SDKs or a durable business-audit store. | CIS-BR-002/003 |
| Type Manager runtime | Prepare the pre-DI catalog, CLR identities, fixed membership and cached queries. | Core `CIS.TypeManagement` definitions, inventory consumption and read-only queries. | Application/configuration construction, DI registration, dynamic plugins or arbitrary load contexts. | CIS-BR-004 |
| Healthcheck | Preserve monitor policy/state, safe collection/execution-fact binding, detached snapshots and bounded reporting. | Core `CIS.Healthcheck`, formatter and existing host-integrated HTTP/local-pipe contracts. | Business retries/restarts, external scheduling, general startup or forced termination. | CIS-BR-005/006 |
| Type Manager inventory build helper | Generate the supported host assembly inventory from SDK-resolved assets. | Existing `CIS.TypeManager.Build` project and Core package SDK/targets; build-only machinery. | Runtime application dependencies/instantiation, separately deployed services or unsupported publish profiles. | CIS-BR-004/008 |

Applications own startup/shutdown, option inputs, external resources, listeners, deployment and background work. Logging exposes bounded capture/queue behaviour and independent backend output paths. The consumer processes queued occurrences and writers own only their documented resources. Preserve callback/provider/scope ownership and recoverable-failure containment; caller expressions and fatal process failures retain their documented limits.

The host explicitly awaits `TypeManager.CreateAsync` before DI container construction. The runtime validates a stable selected deployment inventory, reuses or loads required assemblies in the default context and builds the completed catalog without constructing application objects or attributes. Missing/stale/unsupported inventory and required load failures reject preparation; no arbitrary-directory or loaded-only fallback is permitted.

Healthcheck policy/state remains independent of HTTP types by implementation boundary even though HTTP integration is in the same Core assembly. State mutation, scheduling/collection ownership, execution-fact binding, formatting and transports retain their documented directions. The formatter operates on exactly one detached snapshot. Transports do not become health-policy or application lifecycle owners.

Core's approved runtime closure is `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both `10.0.12`, plus recorded shared-framework dependencies and user-approved `Microsoft.AspNetCore.App`. Do not remove the established NuGets or Core package-pruning exception as an unrelated migration. Third-party runtime integrations require separate optional packages; development tooling and the MSBuild helper must not enter Core's runtime assembly closure.

## Application model and invariants

Environment options retain the selected readonly static container, first-access constructor defaults followed by known overrides and bounded identity interning. Modules own mutable option values and explicit environment resolution; this product has no new global startup, reset or readiness framework.

Logging retains bounded capture and entry defaults, rotating incoming storage, creation-ordered processing, filtering/interpolation, writer isolation and actual admission/eviction/delivery observations. Preserve the detailed current logging contracts and the 64 KiB whole-entry default. This summary does not replace the source's race, ownership, flush/stop and truncation conditions.

Type Manager retains actual CLR type/assembly identity, fixed lifetime membership, deterministic read-only query results and cached valid implementation mappings. Queries do not rescan or construct application objects. Configuration persistence/creation policy remains a consumer or future-module concern.

Healthcheck retains explicit monitor criticality, policy transitions, counts/revisions, safe collection ownership and immutable detached snapshots. Snapshot capture performs no collection or failure replay. Observed exception/runtime text is projected through the existing bounded diagnostic rules; explicit caller inputs retain their own validation contract. A watchdog may affect a caller's wait without gaining health authority or forced cancellation.

No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.

## API, integration, and compatibility intent

| Boundary | Owner, contract and failure/compatibility direction |
| --- | --- |
| Host → Core public APIs | Host supplies inputs and owns integration/resources; Core preserves documented validation, ownership and failure observations. Namespaces/package and immutable contracts remain unchanged. |
| Host build → Type Manager inventory → runtime | Existing SDK targets/helper produce the versioned manifest from resolved runtime assets for supported layouts; runtime validates host identity/paths/required assets and fails visibly rather than returning a partial catalog. No application code runs during inventory production. |
| Logging → custom writers and output | Existing writer contracts govern filtering, bounds, callbacks, ownership, flush/stop and recovery. Independent destinations retain their current semantics. Admission is not durability or remote collector acceptance. |
| Application/collector facts → Healthcheck state | Existing policy/state coordinator owns mutations; collector/execution owners report facts without replacing state or inventing retry/health authority. Preserve accepted lock/callback and actual-settlement boundaries. |
| Detached snapshot → HTTP/pipe/probe | Existing formatter and versioned language-neutral wire contracts define bounded output. HTTP is host-integrated, local pipe access remains OS/local-endpoint policy, and probe behaviour remains its explicit executable contract. Transport errors do not mutate health. |

Exact APIs, enum/JSON shapes, routes, status codes, pipe framing/budgets, authorization rules, options, defaults, timeout/cancellation and recovery conditions remain in `contracts/`, `conformance/`, `planning/healthcheck/contract-catalog.md`, `execution-facts.md`, applicable addenda and current consumer guides. This registration adds no endpoint, authentication scheme, retry, persistence or compatibility promise.

The publication ledger has zero publications. Preserve current consumer and meaningful frozen development baselines without inventing released history. Preserve every actually published contract indefinitely from first publication, including prereleases. Keep `CIS.Core`, neutral culture, unsigned assembly and `AssemblyVersion` `1.0.0.0`; package/file/informational versions identify releases.

## Security and privacy intent

Library inputs, local deployment files, application callbacks and transport peers retain their distinct trust boundaries. Type Manager's manifest is deployment metadata, not a hostile-file sandbox or authenticity signature. The deployment must remain stable while preparation runs. Hosts own access, credentials, network exposure, HTTP concurrency and application data policy.

Healthcheck detail access and local pipe authorization/endpoint protections retain the current transport contracts; this baseline does not infer missing privileged/cross-user/platform qualification. Logging remains diagnostic rather than a transactional audit guarantee. No new product identity provider, tenancy model, regulated-data guarantee, credential store or compliance certification is selected by registration.

## Operations, migration, and recovery intent

Application hosts own runtime startup/shutdown, deployment, external resources, cancellation requests and recovery policy. Preserve explicit Logging flush/stop outcomes, file recovery, Type Manager immutable preparation reports and Healthcheck truthful detached facts. Retired generated receipts, campaigns and old recovery owners supply neither operational authority nor new capacity.

Initial platform lanes remain Windows 11 x64 and Ubuntu 24.04 x64. Type Manager's supported layouts remain framework-dependent untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64. No AOT, trimming, single-file, self-contained, arbitrary load-context or Visual Studio/MSBuild.exe hosting claim is added. Exact package and platform qualification remain open where not actually recorded for the current revision.

Use ordinary native restore/build/test/pack and retain only necessary current reports plus compact CIS state. The bounded adoption check needs a legitimate owner, finite work/storage allocation, real parsed native TRX, settlement and unchanged-result reuse. Installation-specific tool paths remain ignored local configuration. Profile caps are not allocation authority.

## Quality attributes and verification direction

Retain five actual MSTest/VSTest families: Unit, Integration, Architecture, Compatibility and Business Cases, with genuine shared support and fixtures. Ordinary regression excludes `TestCategory=PerformanceObservation` and `TestCategory=ExtendedStress`; focused selection preserves both exclusions. Purpose-specific release verification separately covers accepted performance/stress, compatibility, platforms and package consumers.

Existing release obligations are 95% line / 90% conservative branch coverage across shipping Core and the build helper, 90% conservative mutation, complete declared source scope, no Timeout kill credit and actual instrumented-product activation in children. This direction does not adopt new CIS completion thresholds or claim fresh coverage/mutation evidence. Native assertions protect actual behaviour, public and private dependency boundaries, ownership and immutable snapshots.

The continuation's prepared native selection is the existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` class. Source inspection confirms that this class contains one MSTest method, `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic`, with `[DataRow(false)]` and `[DataRow(true)]`; the expected result is two cases, not an observed pass. It checks real Unicode sanitization/explicit caller projection and detached bounded diagnostics. Preserve the existing local class filter `FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`. It must run through CIS with `--layer unit`, no `--force`, the actual association and parsed TRX. Recheck the class if a descendant changes its test membership. A successful narrow run is not release qualification. Historical cleanup passes remain historical.

Recorded targets of 30 seconds warm, 60 seconds unchanged build plus tests and 300 seconds full regression remain unverified for the cleaned revision. Budget estimates for the adoption task are separate finite proposals; no historical recovery allowance is reused.

## Architecture guidelines and applicable standards

Preserve current dependency/assembly/ownership/compatibility/platform policies in `docs/architecture/`, native test guidance, module contract catalogs and `planning/decisions.md`. Human-readable source requirements retain their original files and identities. No Active CIS standard is fabricated from a documentation metadata status, and optional standards/security capabilities remain explicit follow-up work.

## Decisions and delivery constraints

The following constraints already appear in current decisions: single Core package/assembly; C#/.NET 10; approved Microsoft dependency closure plus ASP.NET shared framework; explicit host lifecycle; native MSTest/VSTest; Type Manager pre-DI inventory/catalog boundaries; Healthcheck policy/transport/execution boundaries; MIT/maintainer publication; compatibility from first actual publication; separate release verification. Historical decisions that conflict with this current direction remain source history, not alternative active architecture.

This registration does not implement Configuration, Heartbeat scheduler, generic startup-provider integration, later languages, a product-gap inventory or new qualification. Source examples/fixtures stay in their existing roles. CIS first-use accounting and durable ad hoc tasks are delivered development capabilities; native execution still needs legitimate canonical ownership and finite allocation.

## Open technical decisions

No new product architecture choice is proposed here. Canonical acceptance of this mapping is recorded in the managed approval metadata; this statement is not a saved answer or accepted `TI-DEC-*` row. Existing publication destination, signing/distribution and final supported API/behaviour baseline decisions remain open before first publication under `planning/decisions.md`.

Canonical work ownership, the approved plan and finite allocation are governed separately in the handoff. Delivered CIS accounting capabilities do not grant that authority. Do not manufacture accepted decision records to satisfy validation; any genuinely required closure attestation needs its actual human authority.

## Source traceability

| Scope | Authoritative/current source mapping |
| --- | --- |
| Precedence, current architecture and obligations | `planning/decisions.md`, `README.md`, `AGENTS.md` |
| Runtime/package/assembly boundaries | `docs/architecture/csharp-dependency-policy.md`, `csharp-assembly-policy.md`, `csharp-ownership-boundaries.md`, `compatibility-policy.md`, `csharp-platform-policy.md`, `implementations/csharp/eng/runtime-dependencies.json` |
| Environment and Logging | Current `docs/modules/logging/` guides; `planning/logging/priority-spool/brd-v1.2.md`; `planning/logging/sources/core-library-implementation-guide-v1.md` interpreted through current decisions |
| Type Manager runtime/build integration | `planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md`, `implementations/csharp/build/type-manager/README.md`, `docs/modules/type-management/configuration-boundary.md` |
| Healthcheck policy/collection/execution/transport | `planning/healthcheck/technical-intent.md`, `contract-catalog.md`, `execution-facts.md`, `collection-contract-amendment.md`, `execution-contract-amendment.md`, `registration-diagnostic-amendment.md`, current `docs/modules/healthcheck/`, `contracts/healthcheck/v1/` |
| Native qualification and open adoption | `implementations/csharp/tests/README.md`, `implementations/csharp/verification/mutation/README.md`, `planning/cis/cleanup-disposition.md` |

The source documents are preserved. These proposal labels and summaries are not CIS-managed identities, lifecycle states or approval records.
