---
title: "cislib Component Sheet"
type: component-sheet
status: Active
scope: Workspace
owner: Product owner and architecture maintainers
last_reviewed: 2026-11-10
review_cadence: on approved business, technical, component, or integration change
cis:
  stable_id: cislib:reference:component-sheet
  solution_design_schema: 1
  technical_intent_hash: semantic-v1:90692b384b0041bb71ed8ae26100042726f34453dc27ead65f89fad14b52695c
  approved_by: "Andrew Spiteri"
  approved_at: "2026-11-10T13:57:26.2851268\u002B00:00"
  approval_reason: "Approve this CISLIB overall solution design and component sheet as one canonical bundle preserving the accepted business and technical baselines."
  approved_bundle_hash: "sha256:bb80fa7b9d396ec39a5ade1b76267044a7845f2f60625a934f2802c27ac08161"
---

# cislib Component Sheet

This document is one half of the governed overall solution-design bundle. The overall design and component sheet share one source baseline, lifecycle, and approval.

Read the generated catalogue together with **Component-specific notes and accepted exceptions** below. Its empty requirement cells and missing structured `TI-INT-*` catalogue reflect the imported document projection. The authored supplement retains the Active technical intent's actual BRD authority and five boundary contracts; it does not create new component or integration identities.

<!-- cis:component-sheet-managed:start -->
## Component catalogue

| Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
| --- | --- | --- | --- | --- | --- | --- |
| `TI-MOD-DOC-2ED774F339B1` | Logging and environment | Logical component (technical intent) | Preserve bounded diagnostics and explicit lifecycle/delivery observations. | Existing Core environment, `CIS.Logging` and `CIS.Logging.MicrosoftLogger` contracts, options, capture, processing and writers. | Application lifecycle/resources, collector SDKs or a durable business-audit store. | None recorded |
| `TI-MOD-DOC-D7BF0EECC9BE` | Type Manager runtime | Logical component (technical intent) | Prepare the pre-DI catalog, CLR identities, fixed membership and cached queries. | Core `CIS.TypeManagement` definitions, inventory consumption and read-only queries. | Application/configuration construction, DI registration, dynamic plugins or arbitrary load contexts. | None recorded |
| `TI-MOD-DOC-2083BB5AF8F0` | Healthcheck | Logical component (technical intent) | Preserve monitor policy/state, safe collection/execution-fact binding, detached snapshots and bounded reporting. | Core `CIS.Healthcheck`, formatter and existing host-integrated HTTP/local-pipe contracts. | Business retries/restarts, external scheduling, general startup or forced termination. | None recorded |
| `TI-MOD-DOC-C62609274AA1` | Type Manager inventory build helper | Logical component (technical intent) | Generate the supported host assembly inventory from SDK-resolved assets. | Existing `CIS.TypeManager.Build` project and Core package SDK/targets; build-only machinery. | Runtime application dependencies/instantiation, separately deployed services or unsupported publish profiles. | None recorded |

## Component responsibility profiles

The approved technical intent does not provide additional component profiles.

## Component interaction catalogue

No cross-component integration points are currently declared.

## Ownership rules

- Each business rule, durable record, public contract, and state transition has one owning component.
- A component consumes another component only through an explicit `TI-INT-*` integration; direct access to another component's store or internal types is prohibited.
- Deployment may combine components, but it does not merge their ownership or permit cyclic dependencies.
- Feature work may split or merge components only with requirement traceability, ownership migration, affected integration updates, and an accepted architecture decision.
- Shared technical capabilities remain independent of product-component internals.
<!-- cis:component-sheet-managed:end -->

## Component-specific notes and accepted exceptions

No component-specific exceptions are accepted. Record future exceptions through an ADR and link them here.

### Package and ownership model

`CIS-BR-001` retains one `CIS.Core` package/runtime assembly with explicit Logging, Type Manager and Healthcheck namespaces. The inventory helper is a separate build-time assembly included as a necessary asset in that package, not a second runtime package or deployable service. Applications own startup/shutdown, deployment, external resources and explicit runtime/monitor/collector handles. Shared environment/value helpers retain their existing responsibility and dependency boundaries.

The following profiles retain the four identities assigned by native CIS interpretation. They supply the explicit `CIS-BR-*` links absent from the generated catalogue's requirement cells; those cells do not indicate absent business authority.

### Logging and environment

Component `TI-MOD-DOC-2ED774F339B1` carries `CIS-BR-002` and `CIS-BR-003`. It owns the existing Core environment mechanism, `CIS.Logging` and `CIS.Logging.MicrosoftLogger` contracts, bounded capture, rotating incoming storage, frontend/backend processing, interpolation, writer dispatch and explicit observations. Preserve the selected readonly static `EnvironmentOptions`, constructor defaults followed by known overrides, bounded identity interning and module-owned option resolution. The default whole-entry bound remains 64 KiB.

Capture detaches the promised event/metadata values before asynchronous publication. Acceptance, consumption, processing, writer disposition, flush boundary and cleanup settlement remain distinct. Application code starts/stops the runtime and keeps external resources alive through actual settlement. The Microsoft adapter/provider borrows the runtime; writer transfer occurs only where successful explicit startup and the existing contract allow it. Retain creator-disposes/LIFO scope behaviour with its documented detection limits. This component does not own application orchestration, lifecycle/resources, collector SDKs or a durable business-audit store.

### Type Manager runtime

Component `TI-MOD-DOC-D7BF0EECC9BE` carries `CIS-BR-004`. The host awaits `TypeManager.CreateAsync` before DI construction. Runtime preparation validates the trusted deployment inventory, retains actual CLR type/assembly identity and fixed prepared membership, and caches deterministic read-only queries without constructing application objects or attributes. Required failures reject preparation rather than returning a partial catalog or rescanning arbitrary directories.

The module owns `CIS.TypeManagement` preparation/query contracts and runtime state. It does not own application/configuration construction, DI registration, dynamic plugins, arbitrary load contexts or inventory build-task lifecycle. Supported layouts remain framework-dependent, untrimmed .NET 10 conventional build/folder publish on Windows/Linux x64; no AOT, trimming, single-file, self-contained or Visual Studio/MSBuild.exe promise is added.

### Healthcheck

Component `TI-MOD-DOC-2083BB5AF8F0` carries `CIS-BR-005` and `CIS-BR-006`. It owns policy/state coordination, monitor contracts, owner-safe optional collection, detached execution facts and snapshots, shared formatter and existing host-integrated HTTP/local-pipe contracts. The application retains monitor/registration handles and owns external hosting/access policy.

State mutation and detached snapshot copying stay under the coordinator's bounded synchronization. Callbacks, collection, serialization and I/O retain their documented directions outside that state gate. Collection runners retain actual owner leases through callback and handoff settlement. Execution binding consumes detached observations without becoming a scheduler. Formatter and transports consume one detached snapshot; HTTP types do not enter policy/state implementation. Empty-registry Healthy does not certify startup, reporting failure does not mutate health, and timeout/canceled waiting does not prove underlying work stopped. Business retries/restarts, Heartbeat scheduling, generic startup coordination and forced termination remain outside its ownership.

### Type Manager inventory build helper

Component `TI-MOD-DOC-C62609274AA1` carries `CIS-BR-004` and `CIS-BR-008`. Existing `CIS.TypeManager.Build`, package SDK imports and targets inspect metadata from SDK-resolved deployed assets and generate the versioned host inventory for supported build/folder-publish layouts. No application code runs during inventory production. Build/publish/clean semantics and explicit failures for unsupported profile/output layouts retain their existing contracts.

The helper uses SDK build APIs as build-time references. Its Core project reference remains `ReferenceOutputAssembly=false`, `Private=false`, `PrivateAssets=all`; the helper does not enter the application runtime closure. It does not own application runtime dependencies/instantiation, test campaigns, deployment orchestration or unsupported publish profiles. Tests/examples establish selected behaviour only when their actual native checks run.

<!-- Sources: docs/specs/technical-intent-spec.md, Runtime architecture and trust boundaries and Application model and invariants; docs/specs/business-requirements.md, CIS-BR-001 through CIS-BR-008; planning/decisions.md; docs/architecture/csharp-ownership-boundaries.md; docs/modules/logging/environment-options.md; docs/modules/logging/runtime-contract.md; docs/modules/logging/incoming-pipeline.md; planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md; planning/healthcheck/technical-intent.md; implementations/csharp/build/type-manager/README.md; implementations/csharp/src/CIS.Core/CIS.Core.csproj. -->

### Retained interaction contracts

The generated **Component interaction catalogue** does not enumerate the imported TI narrative's contracts. Its statement that no points are declared refers to the absence of structured `TI-INT-*` rows, rather than absence of the following accepted boundary contracts. These five rows reuse the Active technical intent; they are not a new contract registry or manually assigned integration identities.

| Boundary | Owner, contract and failure/compatibility direction | BRD authority |
| --- | --- | --- |
| Host → Core public APIs | Host supplies inputs and owns integration/resources; Core preserves documented validation, ownership and failure observations. Namespaces/package and immutable contracts remain unchanged. | `CIS-BR-001`, `CIS-BR-002`, `CIS-BR-003`, `CIS-BR-004`, `CIS-BR-005`, `CIS-BR-006` |
| Host build → Type Manager inventory → runtime | Existing SDK targets/helper produce the versioned manifest from resolved runtime assets for supported layouts; runtime validates host identity/paths/required assets and fails visibly rather than returning a partial catalog. No application code runs during inventory production. | `CIS-BR-004`, `CIS-BR-008` |
| Logging → custom writers and output | Existing writer contracts govern filtering, bounds, callbacks, ownership, flush/stop and recovery. Independent destinations retain their current semantics. Admission is not durability or remote collector acceptance. | `CIS-BR-002`, `CIS-BR-003` |
| Application/collector facts → Healthcheck state | Existing policy/state coordinator owns mutations; collector/execution owners report facts without replacing state or inventing retry/health authority. Preserve accepted lock/callback and actual-settlement boundaries. | `CIS-BR-005` |
| Detached snapshot → HTTP/pipe/probe | Existing formatter and versioned language-neutral wire contracts define bounded output. HTTP is host-integrated, local pipe access remains OS/local-endpoint policy, and probe behaviour remains its explicit executable contract. Transport errors do not mutate health. | `CIS-BR-006` |

Preserve exact API/configuration/wire shapes, method/route/framing/budget and identity rules in their current contracts and applicable addenda. The generated ownership rule about explicit integration identities must be read with these imported contracts; it does not authorize new `TI-INT-*` identities or require a second registry. Direct mutable-state access and reverse ownership remain prohibited by the retained implementation boundaries.

### Verification, remaining work and authority

`CIS-BR-007` preserves real consumers and portable/frozen development evidence without inventing released history. `CIS-BR-008` retains native Unit, Integration, Architecture, Compatibility and Business Cases verification and the separate publication obligations. Tests, TestSupport and fixtures remain development dependencies; examples and `CisHealthProbe` remain explicit consumers. None adds an independent Core module or enters the shipping runtime closure.

The prepared adoption check uses the reviewed local profile `.cis/local/testing/profiles/healthcheck-diagnostic-unit.json`, SHA-256 `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4`. It selects the two Boolean DataRows of `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic` in `HealthDiagnosticRuntimeAcceptanceBoundaryTests`; the overall design preserves its exact class filter and category exclusions. This is an expected selection, not a reported pass. Genuine approved ownership/allocation, `--layer unit`, parsed native TRX, settlement and unchanged-result reuse remain required for the governed demonstration. Profile limits do not grant execution authority.

Current coverage/mutation, platform, package, performance and release gaps remain open where not actually verified. The accepted source direction is **No user-facing UI**; its supported questionnaire/applicability record is separate. This sheet introduces no product scope, exception, approval, publication or work allowance. Both canonical files require one actual bundle approval through native controls.

<!-- Sources: docs/specs/technical-intent-spec.md, API, integration, and compatibility intent, Frontend technology and Quality attributes and verification direction; contracts/logging/README.md; contracts/healthcheck/v1/; planning/healthcheck/contract-catalog.md; planning/healthcheck/execution-facts.md; planning/decisions.md; implementations/csharp/tests/README.md; planning/cis/cleanup-disposition.md. -->
