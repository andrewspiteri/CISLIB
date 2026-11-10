---
title: "CISLIB product business requirements — registration proposal"
type: specification
owner: Repository maintainers
last_reviewed: null
review_cadence: on accepted product-direction change
status: Review Required
cis:
  stable_id: cislib:spec:business-requirements
  brd_schema: 1
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# CISLIB product business requirements

This unapproved proposal maps existing CISLIB requirements and current decisions at reviewed revision `f05b02c0c4967bfee2aabb1f4140709923ad5d3a` into a product-level baseline for canonical registration. It proposes no new feature, product acceptance, release claim or replacement of the source documents. The current explicit user decisions and `planning/decisions.md` take precedence over superseded names, package splits and historical task instructions in those sources. Human review of this exact mapping and CIS approval records remain outstanding.

## Executive summary

CISLIB supplies reusable application foundations through one C# `CIS.Core` package and assembly: environment values and Logging, Type Manager, and Healthcheck. Applications retain ownership of startup, shutdown, business execution, deployment and external resources. The product is independent of Change Impact Studio (CIS), collector SDKs and application orchestration. Its MIT licence and official maintainer policy remain unchanged. [S01, S02, S03]

The cleaned implementation contains these modules and genuine Type Manager build assets. Existing native tests, examples, portable contracts and fixtures remain their respective development and consumer artefacts. Their presence is not proof of current release qualification. The publication ledger has zero publications; `0.1.0-dev.1` is a development version. [S01, S02, S03]

## Business outcomes

- Give application developers one maintained implementation of common logging, managed-type discovery and health reporting instead of requiring each application or coding agent to invent them. [S04, S05, S06]
- Preserve predictable host ownership, bounded work and diagnostics with truthful failure and delivery observations. A successful queue admission is not durable delivery; a reported timeout does not prove underlying execution has stopped. [S01, S04, S06]
- Keep accepted source, binary, configuration, wire and documented behavioural contracts compatible from the first actual publication, including prereleases. [S01, S03, S04, S05]
- Supply meaningful native verification and examples without making test tooling, CIS or a retired campaign protocol a runtime dependency. [S01, S02, S07]

## Scope

The current implemented language is C# on .NET 10. One `CIS.Core` package/assembly contains `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement` and `CIS.Healthcheck`, plus the existing environment support. HTTP Healthcheck integration stays inside Core under the user-approved ASP.NET shared-framework dependency. `CIS.TypeManager.Build` is packaged build machinery, not a second application runtime or independently deployed service. [S01, S02, S03, S06]

The product has no visual frontend. Machine-facing Healthcheck HTTP and local-pipe reporting, the matching probe executable, consumer examples and IDE/native developer workflows do not create a web or desktop application product. [S02, S06, S07]

Current scope preserves existing Logging/environment behaviour, automatic Type Manager inventory and prepared queries, Healthcheck policies/collection/execution-fact binding and reporting, native verification, consumer examples and portable contracts. Full qualification, publication and feature-gap assessment are separate work. Heartbeat scheduler and generic startup-provider integrations remain absent. Configuration, wider Startup Pipeline work, later languages and other future specifications are retained without being labelled implemented or authorized by this registration. [S01, S02, S05, S06]

## Stakeholders and actors

| Actor | Product responsibility |
| --- | --- |
| Application developer and host | Supply options, own the application lifecycle and resources, await Type Manager preparation before DI construction, and integrate reporting explicitly. |
| Application/operator consumer | Inspect truthful log delivery/flush/stop observations and detached health/readiness reports within the host's access and deployment policy. |
| CISLIB maintainer | Maintain the official repository, review changes and publication decisions, preserve actual published compatibility and require appropriate native qualification. |
| Test/build tooling | Exercise actual product behaviour and package/build integration; development tooling is not runtime product authority. |

These roles summarize the existing ownership model; they do not grant any actor a new approval or work allocation. [S01, S03, S05, S06, S07]

## Business capabilities and processes

Applications establish environment/module options, then use Logging to capture and process bounded diagnostic occurrences through independent destinations. Hosts can explicitly observe flush/stop outcomes without confusing admission with delivery. Before building the DI container, a host asks Type Manager to prepare its declared managed application assembly set and uses its completed read-only catalog and cached queries. An application can create Healthcheck state and monitor instances, optionally own collector execution, and publish detached health/readiness reports over the supported host-integrated transports. [S01, S04, S05, S06]

Developers use ordinary .NET restore/build/test/pack and IDE MSTest support. Product-specific assertions and fixtures protect actual behaviour and accepted boundaries. Release work separately qualifies the required native families, platform/package profiles, conservative coverage and mutation obligations before any publication claim. [S01, S02, S07]

## Functional requirements

The identifiers below are proposed product-summary identifiers, not replacements for existing module contract IDs or managed CIS identities. Linked detailed source requirements retain their exact meanings and limits.

| ID | Requirement | Source |
| --- | --- | --- |
| CIS-BR-001 | Provide Logging, Type Manager and Healthcheck through the existing single `CIS.Core` package/assembly, independent of CIS orchestration and application-owned infrastructure. | S01–S03 |
| CIS-BR-002 | Preserve the user-selected readonly static `EnvironmentOptions` entry point, constructor defaults followed by known overrides, bounded identity interning and module-owned explicit option resolution. | S01, S02 |
| CIS-BR-003 | Preserve bounded Logging capture, rotating incoming storage, processing/interpolation, custom writers, independent output policy, correlation/scopes and explicit truthful lifecycle/delivery observations, including the selected 64 KiB whole-entry default. | S01, S04 |
| CIS-BR-004 | Prepare the declared managed assembly set before DI construction, automatically generate the supported host inventory through the existing `CIS.TypeManager.Build` assets, expose actual CLR type identities and fixed prepared membership, and cache valid read-only queries without constructing application objects. | S01, S05 |
| CIS-BR-005 | Preserve application-owned health state and monitor policies, detached immutable snapshots, explicit execution-fact binding and owner-safe optional collection without taking ownership of business retries or application shutdown. | S01, S06 |
| CIS-BR-006 | Preserve bounded Healthcheck serialization, reserved HTTP reporting, local pipe/probe behaviour and host-owned access/deployment boundaries with the exact existing wire and transport contracts. | S06 |
| CIS-BR-007 | Retain meaningful native consumer examples, portable specifications and behavioural/wire fixtures, with current consumer compatibility and frozen development baselines honestly distinguished from published release history. | S01–S03, S05–S07 |
| CIS-BR-008 | Execute the applicable unit, integration, architecture, backwards-compatibility, business, coverage, mutation and useful performance verification required for publication; keep narrow checks and historical results scoped to what actually ran. | S01, S04, S05, S07 |

## Quality, regulatory, and operational requirements

- Keep direct, readable C# with nullable analysis and warnings as errors. Use the retained native MSTest/VSTest workflow and ordinary IDE support. [S01, S07]
- Preserve the approved Microsoft runtime dependency closure and `Microsoft.AspNetCore.App`; third-party runtime integrations require separate optional packages. Development-only tooling and the Type Manager build helper must not enter the runtime dependency closure. [S01, S03]
- Preserve accepted 95% line / 90% conservative branch coverage and 90% conservative mutation release obligations across shipping Core and the build helper, including actual child activation and no Timeout kill credit. No new policy activation or fresh pass is implied. [S01, S07]
- Windows 11 x64 and Ubuntu 24.04 x64 are the selected initial qualification lanes. Supported Type Manager deployment is framework-dependent, untrimmed .NET 10 in conventional build or folder-publish layouts. Actual qualification remains to be recorded for the current revision. [S01, S05]
- The recorded performance targets are 30 seconds warm, 60 seconds unchanged build plus tests and 300 seconds full regression. They remain unverified for the cleaned revision; this baseline does not create a benchmark campaign or increase an old allowance. [S01]
- Retain MIT licensing and maintainer-only official publication controls. The source material establishes no sector-specific regulatory certification or new data-residency guarantee. Host deployment and data policy remain the application's responsibility. [S01, S03, S06]

## Constraints and assumptions

Use `net10.0`, SDK `10.0.401` and the selected .NET/ASP.NET qualification runtime `10.0.12`. The native ApiCompat tool separately requires .NET `8.0.31`; that tool-host requirement does not retarget the library. Keep existing namespaces, assembly identity `CIS.Core`, neutral culture, unsigned assembly and `AssemblyVersion` `1.0.0.0`. [S01–S03, S07]

The existing two approved runtime NuGets remain `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both `10.0.12`, with their recorded closure. Earlier zero-package, separate-bridge and optional-ASP.NET proposals are superseded by current repository decisions. [S01, S03]

Library operations assume the host supplies stable deployment resources and follows documented ownership and access contracts. Type Manager is not a plugin system, arbitrary load-context manager, hostile-file integrity verifier or application-object factory. Healthcheck is not a scheduler, retry engine, DI container or forced-cancellation mechanism. [S05, S06]

Registration does not authorize future language ports, Configuration, Heartbeat implementation, broader engineering-policy adoption, full release qualification or publication. Retired campaign time/storage/attempt ceilings keep their original scope and supply no adoption capacity. [S01]

## Success measures

The product baseline is useful when it faithfully maps the retained requirements and decisions, preserves module/host ownership and compatibility, and exposes current obligations without claiming missing implementation or qualification. Product release success still requires actual applicable native evidence and separate publication decisions. [S01–S07]

The separate bounded CIS adoption task succeeds only with its own approved canonical work owner, finite allocation, actual selected native report, settled time/storage ownership and read-only proof of unchanged-result reuse. Registering this BRD is preparation rather than that result. [S08]

## Traceability

| ID | Existing source and role |
| --- | --- |
| S01 | `planning/decisions.md`: current accepted architecture, behaviour, compatibility and open release obligations; precedence over historical mechanics. |
| S02 | `README.md`: current product scope, native operating route, dependency/toolchain pins and true adoption status. |
| S03 | `docs/architecture/csharp-dependency-policy.md`, `csharp-assembly-policy.md`, `compatibility-policy.md`, `CONTRIBUTING.md`, `LICENSE.md`: current package, dependency, compatibility and maintainer rules. |
| S04 | `planning/logging/sources/core-library-implementation-guide-v1.md`, `planning/logging/priority-spool/brd-v1.2.md`, current `docs/modules/logging/` guides: detailed Logging source and selected frontend behaviour; historical names/package assumptions are interpreted through S01. |
| S05 | `planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md`, `implementations/csharp/build/type-manager/README.md`, `docs/modules/type-management/configuration-boundary.md`: Type Manager scope, inventory, query and build/runtime boundaries. |
| S06 | `planning/healthcheck/technical-intent.md`, `contract-catalog.md`, `execution-facts.md`, `collection-contract-amendment.md`, `execution-contract-amendment.md`, `registration-diagnostic-amendment.md`, `docs/modules/healthcheck/`, `contracts/healthcheck/v1/`: retained Healthcheck contracts and applicable amendments. |
| S07 | `implementations/csharp/tests/README.md`, `implementations/csharp/verification/mutation/README.md`: retained native verification scope and current obligations. |
| S08 | `planning/cis/cleanup-disposition.md`, especially the 10 November continuation decision package: retained preparation/execution distinction and bounded adoption acceptance. |

These are source mappings for review, not human `Adopted`/`Reference`/`Rejected` dispositions of CIS-discovered candidates. The supported intake must produce current candidate identities, hashes and review tokens before those decisions can be recorded.

## Open questions

This mapping introduces no proposed answer to an unresolved product decision. The maintainer must review whether the transfer faithfully captures the existing baseline and whether source dispositions and canonical adoption are appropriate. Publication destination, signing/distribution and final supported API/behaviour baseline remain open before first publication as already recorded in S01; they are not silently answered or newly required feature work for this bounded internal adoption check.

Human approval of the exact canonical payload and its source assessments is pending. The separate first-use accounting capability and work-allocation decision remain in the adoption handoff; neither is a new library feature or an approval supplied by this document.


## Canonical registration

The following CIS-managed evidence records source review separately from the product's open questions. Source dispositions and canonical approval require the actual maintainer's decision.

<!-- cis:brd-evidence
## CIS participant baseline
-->

<!-- cis:baseline:start -->
<!-- cis:brd-evidence
| Repository | Graph build | Head | Dirty |
| --- | --- | --- | --- |
| none | none | none | false |
-->
<!-- cis:baseline:end -->

<!-- cis:brd-evidence
## Source assessment
-->

<!-- cis:sources:start -->
<!-- cis:brd-evidence
| ID | Type | Repository | Path | Hash | Assessment | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| BRD-SRC-77aaeee8b0d1 | business-requirements | cislib | planning/logging/priority-spool/brd-v1.2.md | sha256:1cb6b678d7ea252075ca6cb90a0125df33557c31fb956cf9357475ce5fa36477 | Unreviewed | TODO |
-->
<!-- cis:sources:end -->
