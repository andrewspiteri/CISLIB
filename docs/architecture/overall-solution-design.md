---
title: "cislib Overall Solution Design"
type: overall-solution-design
status: Review Required
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: null
review_cadence: on approved business, technical, component, or integration change
cis:
  stable_id: cislib:architecture:overall-solution-design
  solution_design_schema: 1
  technical_intent_hash: semantic-v1:90692b384b0041bb71ed8ae26100042726f34453dc27ead65f89fad14b52695c
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_bundle_hash: null
---

# cislib Overall Solution Design

This document is one half of the governed overall solution-design bundle. The overall design and component sheet share one source baseline, lifecycle, and approval.

Read the generated projection together with the authored mapping under **Design decisions and accepted exceptions**. The imported technical intent records four ownership boundaries, their `CIS-BR-*` authority and five boundary contracts. Empty generated requirement cells and the absence of structured `TI-INT-*` rows do not remove that accepted direction; the authored mapping preserves it without creating new identities or contracts.

<!-- cis:solution-design-managed:start -->
## Architecture drivers

Keep direct, readable code with explicit module and host responsibilities. Keep actual product behaviour and meaningful assertions while using ordinary native development commands. Preserve bounded work, detached observations and truthful failure/settlement facts. A snapshot/report must not mutate health, a wait timeout must not claim underlying work stopped, and a queue admission must not claim durable output delivery.

CIS is development governance tooling only and remains outside the shipping library's runtime references. The cleanup removed obsolete campaigns and checker protocols; this registration does not recreate them. Fixtures, examples and test support remain development or consumer evidence, rather than new product modules.

## System context and boundaries

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

## Logical component topology

The following entries project the ownership boundaries documented in the technical intent. Their classification distinguishes logical components from runtime processes. CIS assigns stable identifiers to imported entries for this review; those identifiers do not introduce new ownership decisions.

The approved technical intent defines the component topology.

| Component | Classification | Responsibility | Owns | BRD authority |
| --- | --- | --- | --- | --- |
| `TI-MOD-DOC-2ED774F339B1` — Logging and environment | Logical component (technical intent) | Preserve bounded diagnostics and explicit lifecycle/delivery observations. | Existing Core environment, `CIS.Logging` and `CIS.Logging.MicrosoftLogger` contracts, options, capture, processing and writers. | None recorded |
| `TI-MOD-DOC-D7BF0EECC9BE` — Type Manager runtime | Logical component (technical intent) | Prepare the pre-DI catalog, CLR identities, fixed membership and cached queries. | Core `CIS.TypeManagement` definitions, inventory consumption and read-only queries. | None recorded |
| `TI-MOD-DOC-2083BB5AF8F0` — Healthcheck | Logical component (technical intent) | Preserve monitor policy/state, safe collection/execution-fact binding, detached snapshots and bounded reporting. | Core `CIS.Healthcheck`, formatter and existing host-integrated HTTP/local-pipe contracts. | None recorded |
| `TI-MOD-DOC-C62609274AA1` — Type Manager inventory build helper | Logical component (technical intent) | Generate the supported host assembly inventory from SDK-resolved assets. | Existing `CIS.TypeManager.Build` project and Core package SDK/targets; build-only machinery. | None recorded |

Dependency direction is inward toward the owning application or domain contract. Cross-component state access is prohibited; interactions use the integration architecture below.

## Data ownership and consistency

Environment options retain the selected readonly static container, first-access constructor defaults followed by known overrides and bounded identity interning. Modules own mutable option values and explicit environment resolution; this product has no new global startup, reset or readiness framework.

Logging retains bounded capture and entry defaults, rotating incoming storage, creation-ordered processing, filtering/interpolation, writer isolation and actual admission/eviction/delivery observations. Preserve the detailed current logging contracts and the 64 KiB whole-entry default. This summary does not replace the source's race, ownership, flush/stop and truncation conditions.

Type Manager retains actual CLR type/assembly identity, fixed lifetime membership, deterministic read-only query results and cached valid implementation mappings. Queries do not rescan or construct application objects. Configuration persistence/creation policy remains a consumer or future-module concern.

Healthcheck retains explicit monitor criticality, policy transitions, counts/revisions, safe collection ownership and immutable detached snapshots. Snapshot capture performs no collection or failure replay. Observed exception/runtime text is projected through the existing bounded diagnostic rules; explicit caller inputs retain their own validation contract. A watchdog may affect a caller's wait without gaining health authority or forced cancellation.

No transactional application system of record is introduced. Logging files and Type Manager manifests have their existing diagnostic/deployment roles; they are not inferred databases. Unrequested caches/search/object stores and AI/model execution do not become selected architecture.

## Integration architecture

Every cross-boundary handoff requires an explicit owned contract.

## Security and trust boundaries

Library inputs, local deployment files, application callbacks and transport peers retain their distinct trust boundaries. Type Manager's manifest is deployment metadata, not a hostile-file sandbox or authenticity signature. The deployment must remain stable while preparation runs. Hosts own access, credentials, network exposure, HTTP concurrency and application data policy.

Healthcheck detail access and local pipe authorization/endpoint protections retain the current transport contracts; this baseline does not infer missing privileged/cross-user/platform qualification. Logging remains diagnostic rather than a transactional audit guarantee. No new product identity provider, tenancy model, regulated-data guarantee, credential store or compliance certification is selected by registration.

## Deployment, operations, and recovery

Application hosts own runtime startup/shutdown, deployment, external resources, cancellation requests and recovery policy. Preserve explicit Logging flush/stop outcomes, file recovery, Type Manager immutable preparation reports and Healthcheck truthful detached facts. Retired generated receipts, campaigns and old recovery owners supply neither operational authority nor new capacity.

Initial platform lanes remain Windows 11 x64 and Ubuntu 24.04 x64. Type Manager's supported layouts remain framework-dependent untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64. No AOT, trimming, single-file, self-contained, arbitrary load-context or Visual Studio/MSBuild.exe hosting claim is added. Exact package and platform qualification remain open where not actually recorded for the current revision.

Use ordinary native restore/build/test/pack and retain only necessary current reports plus compact CIS state. The bounded adoption check needs a legitimate owner, finite work/storage allocation, real parsed native TRX, settlement and unchanged-result reuse. Installation-specific tool paths remain ignored local configuration. Profile caps are not allocation authority.

## Quality and verification architecture

Retain five actual MSTest/VSTest families: Unit, Integration, Architecture, Compatibility and Business Cases, with genuine shared support and fixtures. Ordinary regression excludes `TestCategory=PerformanceObservation` and `TestCategory=ExtendedStress`; focused selection preserves both exclusions. Purpose-specific release verification separately covers accepted performance/stress, compatibility, platforms and package consumers.

Existing release obligations are 95% line / 90% conservative branch coverage across shipping Core and the build helper, 90% conservative mutation, complete declared source scope, no Timeout kill credit and actual instrumented-product activation in children. This direction does not adopt new CIS completion thresholds or claim fresh coverage/mutation evidence. Native assertions protect actual behaviour, public and private dependency boundaries, ownership and immutable snapshots.

The continuation's prepared native selection is the existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` class. Source inspection confirms that this class contains one MSTest method, `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic`, with `[DataRow(false)]` and `[DataRow(true)]`; the expected result is two cases, not an observed pass. It checks real Unicode sanitization/explicit caller projection and detached bounded diagnostics. Preserve the existing local class filter `FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`. It must run through CIS with `--layer unit`, no `--force`, the actual association and parsed TRX. Recheck the class if a descendant changes its test membership. A successful narrow run is not release qualification. Historical cleanup passes remain historical.

Recorded targets of 30 seconds warm, 60 seconds unchanged build plus tests and 300 seconds full regression remain unverified for the cleaned revision. Budget estimates for the adoption task are separate finite proposals; no historical recovery allowance is reused.

## UI design handoff

High-level UI direction is the next distinct stage. It consumes the BRD actors and journeys, the approved experience-surface choices, component capabilities, permissions, contracts, and failure states to establish shared product character, shell, visual language, reusable components, responsive behavior, and accessibility. Detailed screens remain feature-wireframe work. Neither stage may change component ownership, business scope, or trust boundaries silently.

No additional experience-surface projection was recorded.

## Traceability

- Business authority: canonical Active BRD through the approved technical-intent baseline.
- Technical authority: `docs/specs/technical-intent-spec.md` at `semantic-v1:90692b384b0041bb71ed8ae26100042726f34453dc27ead65f89fad14b52695c`.
- Component authority: `docs/references/component-sheet.md`.
- Durable alternatives or exceptions require an ADR before implementation changes the approved direction.
<!-- cis:solution-design-managed:end -->

## Design decisions and accepted exceptions

No additional exceptions are accepted. Durable changes require an ADR and renewed bundle validation.

### Accepted baseline and physical topology

The Active BRD and technical intent register the existing Logging, Type Manager and Healthcheck direction. `CIS-BR-001` keeps these capabilities in one `CIS.Core` package and runtime assembly; applications own lifecycle and external resources. The following references use the component identities supplied by native CIS interpretation:

- Logging and environment, `TI-MOD-DOC-2ED774F339B1`, carries `CIS-BR-002` and `CIS-BR-003`: the existing static environment/options mechanism, bounded diagnostics, Logging processing, Microsoft logger adaptation and explicit delivery/lifecycle observations.
- Type Manager runtime, `TI-MOD-DOC-D7BF0EECC9BE`, carries `CIS-BR-004`: trusted pre-DI inventory consumption, actual CLR identity, fixed prepared membership and cached read-only queries without application-object construction.
- Healthcheck, `TI-MOD-DOC-2083BB5AF8F0`, carries `CIS-BR-005` and `CIS-BR-006`: policy/state, owner-safe optional collection, detached execution facts and snapshots, formatter and host-integrated HTTP/local-pipe reporting.
- Type Manager inventory build helper, `TI-MOD-DOC-C62609274AA1`, carries `CIS-BR-004` and `CIS-BR-008`: SDK-resolved inventory generation and its actual native verification. It is existing Core package build machinery, rather than an application runtime component.

The three runtime modules retain their explicit `CIS.*` namespaces inside the single Core assembly. `CIS.TypeManager.Build`, package SDK imports and targets retain their build-only role; the Core project reference uses `ReferenceOutputAssembly=false`, `Private=false` and `PrivateAssets=all`. Tests, shared TestSupport, fixtures, conformance cases, examples and `CisHealthProbe` keep their development or consumer roles. They add no independently owned Core module or runtime package.

The implemented lane remains C# `net10.0`, SDK `10.0.401` and selected .NET/ASP.NET qualification runtime `10.0.12`, with nullable analysis and warnings as errors. Core retains its approved Microsoft package closure and `Microsoft.AspNetCore.App`. New third-party runtime integrations require separate optional packages. This bundle adds no runtime dependency on CIS.

<!-- Sources: docs/specs/business-requirements.md, CIS-BR-001 through CIS-BR-008; docs/specs/technical-intent-spec.md, Backend technology and topology and Runtime architecture and trust boundaries; planning/decisions.md; implementations/csharp/src/CIS.Core/CIS.Core.csproj; docs/architecture/csharp-dependency-policy.md. -->

### Retained boundary contracts

These five boundaries are the contracts already recorded in the Active technical intent's **API, integration, and compatibility intent** section. The generated integration section does not enumerate them because that imported narrative has no structured `TI-INT-*` rows. This table supplies the retained mapping; it creates no endpoint, authentication scheme or replacement contract registry.

| Boundary | Owner, contract and failure/compatibility direction | BRD authority |
| --- | --- | --- |
| Host → Core public APIs | Host supplies inputs and owns integration/resources; Core preserves documented validation, ownership and failure observations. Namespaces/package and immutable contracts remain unchanged. | `CIS-BR-001`, `CIS-BR-002`, `CIS-BR-003`, `CIS-BR-004`, `CIS-BR-005`, `CIS-BR-006` |
| Host build → Type Manager inventory → runtime | Existing SDK targets/helper produce the versioned manifest from resolved runtime assets for supported layouts; runtime validates host identity/paths/required assets and fails visibly rather than returning a partial catalog. No application code runs during inventory production. | `CIS-BR-004`, `CIS-BR-008` |
| Logging → custom writers and output | Existing writer contracts govern filtering, bounds, callbacks, ownership, flush/stop and recovery. Independent destinations retain their current semantics. Admission is not durability or remote collector acceptance. | `CIS-BR-002`, `CIS-BR-003` |
| Application/collector facts → Healthcheck state | Existing policy/state coordinator owns mutations; collector/execution owners report facts without replacing state or inventing retry/health authority. Preserve accepted lock/callback and actual-settlement boundaries. | `CIS-BR-005` |
| Detached snapshot → HTTP/pipe/probe | Existing formatter and versioned language-neutral wire contracts define bounded output. HTTP is host-integrated, local pipe access remains OS/local-endpoint policy, and probe behaviour remains its explicit executable contract. Transport errors do not mutate health. | `CIS-BR-006` |

Exact public API/configuration/wire shapes, defaults, ownership, budgets, authorization, timeout/cancellation and recovery conditions remain in their existing contracts, module guides and applicable addenda. Healthcheck policy/state remains independent of HTTP implementation types. Shared value/environment capture does not acquire dependencies on concrete output writers, provider orchestration, CIS or test tooling. Applications retain required resources through actual worker and cleanup settlement; a canceled wait does not prove underlying work has stopped.

<!-- Sources: docs/specs/technical-intent-spec.md, API, integration, and compatibility intent; contracts/logging/README.md; contracts/healthcheck/v1/; planning/healthcheck/contract-catalog.md; planning/healthcheck/execution-facts.md; docs/architecture/csharp-ownership-boundaries.md; implementations/csharp/build/type-manager/README.md. -->

### Verification and delivery scope

`CIS-BR-007` preserves meaningful current consumers, portable specifications and frozen development baselines without inventing published history. `CIS-BR-008` retains actual native qualification. The publication ledger has zero publications; preserve every actually published contract from its first publication, including prereleases. Assembly identity remains `CIS.Core`, neutral culture, unsigned and `AssemblyVersion` `1.0.0.0`. Publication destination, signing/distribution policy and the final supported API/behaviour baseline remain open before first publication.

The prepared bounded adoption check is the ignored local profile `.cis/local/testing/profiles/healthcheck-diagnostic-unit.json`, reviewed SHA-256 `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4`. Its actual method is `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic` in `HealthDiagnosticRuntimeAcceptanceBoundaryTests`, with `[DataRow(false)]` and `[DataRow(true)]`. The exact class filter and category exclusions appear in the generated verification section above. Two cases are expected, rather than an observed pass. The profile must remain tied to its actual local tools and native source; its limits grant no ownership or allocation.

The governed demonstration still needs its legitimate approved change/task association, finite time/storage allocation, `--layer unit`, real parsed native TRX, settlement and read-only proof of unchanged reuse. Preserve `--force` exclusion. It adds no product behaviour or assertion change and does not satisfy platform, package, compatibility, coverage, mutation or performance release obligations. The recorded 30-second warm, 60-second unchanged build-plus-test and 300-second full-regression targets remain unverified for the cleaned revision; proposed adoption allowances are separate decisions.

Initial qualification lanes remain Windows 11 x64 and Ubuntu 24.04 x64. Type Manager support remains framework-dependent, untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64. This mapping adds no AOT, trimming, self-contained, single-file, arbitrary load-context or Visual Studio/MSBuild.exe qualification. Configuration, Heartbeat scheduling, generic startup integration, later languages and broader release work remain outside this registration.

### UI handoff and approval scope

The applicable source direction is **No user-facing UI**. API callers, host-integrated HTTP reports, local pipes and the probe executable do not imply a graphical frontend. Carry that accepted direction through the supported questionnaire and no-UI applicability controls; the generic generated UI handoff above does not select screens, visual tokens, a UI framework or visual-preview work.

Both canonical files form one review point. Their managed metadata records actual bundle approval; this authored mapping supplies neither that approval nor work execution authority. Source authority remains the Active BRD and technical intent, interpreted with current explicit decisions and the retained module contracts.

<!-- Sources: docs/specs/technical-intent-spec.md, Frontend technology, Quality attributes and verification direction, Decisions and delivery constraints and Open technical decisions; planning/decisions.md; implementations/csharp/tests/README.md; planning/cis/cleanup-disposition.md. -->
