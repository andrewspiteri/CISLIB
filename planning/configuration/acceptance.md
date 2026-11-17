---
title: Configuration acceptance and proposed native verification
type: planning-reference
status: Draft
---

# Configuration acceptance and proposed native verification

This is a proposed verification map for the [restored specification](sources/CIS_Configuration_Module_Specification_v1.0.md)
and [implementation handoff](sources/CIS_Configuration_Implementation_Handoff_v1.0.md).
Every U01–U12 and CFG-A01–CFG-A28 identifier is retained below. It is not a manual
test catalogue or an executed result. P-dependent evidence assumes the **single
P01–P08 bundle was selected by the user on 17 November 2026** in the
[requirements register](README.md); update affected expectations if that decision
selects a different contract. Do not relabel recommendations as historical approval.

## Coherent evidence families

These families group implementation outcomes and tests, not separate compulsory
CIS tasks. Each owning task includes its applicable tests, documentation and review.
Use the actual existing projects and add exact test symbols when implementation
is authorized; none of the descriptions below claims an installed Configuration API.

| Family | Planned evidence and existing project boundaries |
| --- | --- |
| F1 — Discovery, identity and bootstrap | Unit and Integration against actual `ITypeManager` queries, real fixture inventories and Microsoft DI with ordinary validation. Prove candidate eligibility, captured defaults, exact concrete-root reuse, host isolation and bootstrap-before-consumer ordering. Preserve existing diagnostic/public names. |
| F2 — Effective values and coordinated commits | Unit cases for presence-aware composition, conversion/validation, deterministic checksum, override removal, retained root and synchronized reads. Controlled commit boundaries/signals prove exclusion; ordinary property access is not advertised as atomic. |
| F3 — Provider and serialized refresh | Unit/Integration using controlled provider reads and signals for Found/Missing/Unchanged/Failure, ordered attempts, coalescing, watch gaps, invalid-source bookkeeping and notifications. Test doubles prove core/provider-contract behavior only. Real adapter behavior needs its own concrete integration evidence. |
| F4 — Startup, Heartbeat, health and lifetime | Integration and BusinessCases through the actual Startup host, retained concrete objects, existing Heartbeat owner/drain and Healthcheck policy. Prove direct initial/reconciliation work, automatic recurring activation, cancellation/commit cutoff and actual settlement. No replacement scheduler/container/readiness engine. |
| F5 — Consumer and compatibility boundaries | Architecture, Compatibility and BusinessCases plus affected Unit/Integration assertions. Preserve Core/package/dependency/environment/Type Manager behavior and exercise actual selected source/package consumers. Keep release/platform/coverage/mutation qualification distinct from focused module checks. |

Use short controlled asynchronous phases and bounded test observation waits rather
than ordering sleeps. Cleanup first closes admission/releases test holds and joins
actual owned work before disposing dependencies. A canceled wait is not settlement.
No custom runner, Python C# harness, mutation engine or verification archive is
proposed. Reuse materially unchanged valid evidence; metadata changes alone do not
justify rerunning native suites.

## First useful demonstrations

| Demonstration | Planned observable proof |
| --- | --- |
| Retained concrete identity — F1/F2/F4 | Discover a real configuration class via the prepared Type Manager. Resolve it through two consumers and two scopes, perform one valid reload, and assert the same root/reference and existing subscription survive. A separate host receives a distinct root. |
| Masked common update/removal — F2/F3 | Start with a common value masked by an application property. Change only common: source bookkeeping advances, accepted effective revision/event count does not. Remove the application override: the latest common value appears on the same root with one accepted-change notification. |
| Consistent shared-lock read — F2 | Coordinate a real commit boundary between prepared assignments and a simultaneous reader-helper call. The reader cannot complete inside the partial write and returns a complete old or new tuple after release. Signals establish ordering; no raw-read atomicity claim or new public test hook is required. |

## Confirmed requirements

| Requirement | Family | Planned evidence |
| --- | --- | --- |
| U01 | F1/F2 | Discover ordinary POCO data classes and map supported values into their retained concrete roots; reject incompatible shapes with type-specific diagnostics. |
| U02 | F1/F5 | Resolve by the actual concrete type with no independent logical-name lookup; reject conflicting concrete registrations and duplicate/colliding identities. |
| U03 | F1/F2 | Distinguish equal full type names from different defining assemblies. Cover the explicitly selected persisted version policy separately from exact runtime `Type` identity. |
| U04 | F1/F4 | Capture the supplied host-project identity once. An EnvironmentOptions name override, library name or later environment change does not redirect the selected application record. |
| U05 | F1/F2/F4 | Prove one live root across consumers/scopes/reload, preserved subscriptions and distinct roots across application hosts; no deserializer/candidate replaces the DI object. |
| U06 | F1/F3/F5 | Expose only the correctly spelled `IConfigurationObject`/`ConfigurationChanged` contract selected for this module. Under P05 prove helper/accessor direct implementations and the optional base both publish without mandatory inheritance. |
| U07 | F1/F2/F4 | Capture constructor defaults once; read both scopes, compose/validate/apply initial values before delivery under P01. Confirmed absence permits valid defaults; read/mapping failure prevents successful preparation. |
| U08 | F2/F3 | Exercise common/application combinations, explicit default-valued overrides and confirmed deletions; demonstrate latest lower-level state after override removal. |
| U09 | F3/F4 | Route notifications and explicit polling through one refresh operation with selected real provider/Heartbeat wiring. No invented interval, backend or environment setting; report adapter gaps honestly. |
| U10 | F2/F3 | Compare deterministic effective values separately from source tokens; equivalent/hidden changes avoid remap/event while real mapped changes commit and notify. |
| U11 | F2/F3/F4 | Shared-lock readers exclude partial commits; accepted checksum/revision and values commit together, then ordered events publish outside the value gate. Stop/cancellation boundaries preserve those facts. |
| U12 | F1/F4/F5 | Use actual `TypeManager.CreateAsync` and `GetImplementations` before the one root container. An initially unused deployed fixture is discovered without Configuration scanning/preloading assemblies itself. |

## Supplied acceptance scenarios

| Acceptance | Family | Planned meaningful evidence |
| --- | --- | --- |
| CFG-A01 | F1/F5 | Real generated inventory includes an initially unused library; direct/inherited marker implementations come from the prepared catalog. Structural assertions preserve no Configuration assembly scan or second Type Manager. |
| CFG-A02 | F1/F4 | Two consumers/two scopes share one concrete root; a separately prepared host has a different root. Resolve again after reload and retain identity. |
| CFG-A03 | F1/F2 | Same full type name in different assemblies remains distinct. Explicitly test chosen P02 assembly-version behavior, culture/token participation and key-collision rejection without fallback guessing. |
| CFG-A04 | F1/F4 | Explicit host identity selects only its application override despite EnvironmentOptions display-name overrides; reserved-common-name collision fails. |
| CFG-A05 | F2/F3 | Table-drive neither record/common-only/application-only/both records and valid captured defaults; distinguish confirmed absence from unsuccessful reads. |
| CFG-A06 | F2 | Explicit false, zero, empty string, empty collection and permitted null override lower values; omitted properties inherit. Invalid null conversion rejects the candidate. |
| CFG-A07 | F2 | Nested member overlays preserve omitted lower members; arrays/lists/dictionaries replace completely and do not accumulate prior refresh items. |
| CFG-A08 | F1/F2/F3 | Remove override property/application record/common record and rebuild from current lower state/captured defaults. Constructor-generated defaults are not recomputed on refresh. |
| CFG-A09 | F2/F5 | Duplicate/ambiguous names and unsupported known values reject; unknown-property handling follows selected policy. Diagnostics contain bounded names/categories without payloads, credentials or secret values. |
| CFG-A10 | F2/F3/F4 | Invalid candidate/read failure leaves live values, accepted checksum/revision and event count unchanged. The corresponding initial failure prevents successful bootstrap/Startup completion. |
| CFG-A11 | F2/F3 | Early masked-common demonstration: retain new successful source state without effective event, then expose it after application override removal. |
| CFG-A12 | F2 | Formatting/property-order/culture variations and equivalent mapped values produce the same effective representation; real typed/nested/collection-order changes produce the selected checksum change. |
| CFG-A13 | F1/F2/F3 | Reload retains root/reference and original event subscriptions; detached candidates do not become new DI values or copy event/runtime bookkeeping into the root. |
| CFG-A14 | F2 | Controlled staged commit and synchronized reader prove no mixed property tuple. Separate raw-read access remains outside the guarantee; reads copy/detach mutable values inside the shared gate. |
| CFG-A15 | F3 | Hold the first provider read while more invalidations arrive. Only one per-entry attempt runs through notification, and the pending-to-idle handoff schedules another reconciliation without an unbounded queue or stale local overwrite. |
| CFG-A16 | F3/F4 | Signal creation/deletion/invalidation between bootstrap read and watch activation, during subscribe-and-reconcile, and after reconnect; required subsequent reads converge without a lost gap signal. |
| CFG-A17 | F3/F4 | Feed selected real notifications and Heartbeat polling into the same serialized refresh path. Startup reconciliation runs directly while recurring callbacks are inert; no timer loop or fake production adapter is substituted. |
| CFG-A18 | F2/F3 | One event follows one accepted effective commit. Initial baseline, unchanged, failed and pre-commit-canceled attempts produce none under P07; accepted local revision is monotonic. |
| CFG-A19 | F3 | A throwing first synchronous subscriber cannot prevent the next subscriber. A handler reads through the shared reader after gate release; a handler trigger coalesces for later work instead of recursive refresh. |
| CFG-A20 | F1/F3/F5 | Direct-interface add/remove forwarding and optional base exercise the same helper publication semantics. Unconnected arbitrary event wiring is rejected usefully; no event-field reflection/proxy appears. |
| CFG-A21 | F1/F4 | Under P01, a real Stage 1 consumer constructor sees populated values; Stage 2 awaits refresh activation/reconciliation before a dependent start. Only the existing Startup coordinator publishes application completion. |
| CFG-A22 | F2/F3/F4 | Cancel at a controlled pre-commit boundary: no live/revision/event change. Cancel after commit: the accepted notification still completes once, and ownership remains until actual settlement. |
| CFG-A23 | F3/F4 | Stop competes with commit admission, rejects new triggers/commits, detaches owned watches/polling and drains active work/notifications. Host-owned adapters and user roots remain undisposed and retain accepted values. |
| CFG-A24 | F2/F3 | Reject one invalid source token, then return conditional Unchanged for it: failure remains until valid candidate validation succeeds. Failure never acts as confirmed deletion or clears the accepted-state distinction. |
| CFG-A25 | F1/F2/F3 | Concurrent independent types/hosts retain separate state and gates; per-entry coalescing remains bounded. Detached collection reads stay stable after later replacement. |
| CFG-A26 | F1/F2/F3 | Conflicting DI registrations, unusable constructors/factory results, unsupported property/event shapes and key collisions fail before delivery with bounded type-specific guidance. Broken live setters never advance a successful revision/event or claim an unproved rollback. |
| CFG-A27 | F5 | Affected existing Core consumer/API/configuration/wire fixtures, actual selected package consumers and architecture checks preserve assembly/dependency policy, Type Manager queries and EnvironmentOptions defaults/overrides. New APIs are identified as additions, not invented historical publication. |
| CFG-A28 | F3/F4/F5 | Completion record separates controlled-provider core results, actual adapter evidence, actual Heartbeat/Startup/Healthcheck integration and unexecuted backend/platform/package/release work. No missing required integration is relabeled as passing. |

## Evidence and completion limits

Record exact native commands, test symbols, passed/failed/skipped observations,
material source/profile/package identities, retained evidence and actual work
accounting in the owning CIS implementation records. This map originally described
unexecuted intake evidence. The separately approved CIS-0008 allocation now covers
Configuration; this file grants no allowance. Actual observations and failures are
recorded in its [verification record](../../docs/changes/CIS-0008/verification.md).

Retain applicable Unit, Integration, Architecture, Backwards Compatibility,
Business Cases and Mutation categories, with the existing coverage/platform/package
and release obligations. Select focused checks at meaningful implementation
boundaries; qualify the actual release candidate at its genuine boundary rather
than launching a replacement campaign for every row. No manual catalogue CSV or
visual UI obligation follows from this in-process library proposal.

Final reporting must name the recorded P disposition, U/CFG-A coverage, actual
runtime/provider and host integrations, changed public/source/dependency surface,
executed and reused evidence, and precise remaining gaps. A successful Type Manager
check, a controlled provider or planning/link validation alone does not establish
Configuration delivery or release readiness.

## Implementation reconciliation

The selected P01–P08 profile is implemented through these bounded ownership groups.
Exact current native counts, package bytes/platform and remaining qualification
limits stay in CIS-0008 rather than being inferred from this mapping.

| Group | Production boundary | Concrete evidence |
| --- | --- | --- |
| F1 — Discovery/retention/bootstrap | `ConfigurationCatalog`, identity/shape/settings capture; `ConfigurationRuntime.PrepareAsync` and `AddCisConfiguration` | `ConfigurationCatalogTests`, `ConfigurationReaderTests`, `ConfigurationHostingContractTests`; real `ConfigurationHostBootstrapTests`; complete direct/inherited marker queries in existing source/package examples. |
| F2 — Values/commit | Fixed `ConfigurationValuePipeline`, scalar/canonical representation and prepared assignments | `ConfigurationMappingTests` and `ConfigurationCommitTests`: literal overlays, independent SHA vector, enum/date/numeric rules, current lower restoration, factory/shared-child isolation, real slowed-copy reader, cancel/revision/poison. `ConfigurationConverterBoundaryTests` closes nullable metadata activation. |
| F3 — Refresh/events/drain | Source caches and `ConfigurationRefreshCoordinator` | `ConfigurationStorageTests`, `ConfigurationRefreshCoordinationTests`, `ConfigurationRefreshLifetimeTests`; separate controlled-provider/value seam. Real runtime/map/store composition is covered by host/source cases. |
| F4 — Actual startup/hosting | First Configuration startable and optional concrete Startup.Hosting closure before HB; actual combined barriers | `ConfigurationHostReadinessTests` and `ConfigurationHostLifetimeTests` use real Kestrel, Type Manager, Startup, Heartbeat and Healthcheck; maintained CountryList story proves same-root masked update/removal and real watch delivery. |
| F5 — Existing contracts/package/architecture | Single Core assembly and approved dependency closure; no coordinator dependency on Configuration/HB | Existing affected Unit/Architecture/Compatibility/Business projects; `ConfigurationDiscoveryBoundaryTests` plus extended `StartupArchitectureTests`; `ConfigurationSourceConsumerTests` and existing actual `TypeManagerPackageConsumerTests`. |
| Qualification boundary | Existing native tools and unchanged release gates | Record the scoped native mutation canary separately from full mutation/coverage/release or unexecuted platforms. The production adapter remains host-supplied; demo/controlled adapters do not establish backend qualification. |

The implementation does not require a second DI container, discovery scan,
periodic timer, generic closing registry, validation language, proxy or test runner.
Independent final acceptance review found no remaining material U/P behavior gap;
it supplies source review, not a substitute for the final native results.
