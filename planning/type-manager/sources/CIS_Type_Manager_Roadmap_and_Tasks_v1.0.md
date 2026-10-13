# CIS Type Manager — Roadmap and Task List

**Version:** 1.0  
**Date:** 13 October 2026  
**Companion contract:** [Implementation definition](CIS_Type_Manager_Definition_v1.0.md)  
**Delivery state:** Planning complete in this pack; all implementation tasks below are **Not started / not verified here**.  
**Scope:** Type Manager runtime, its automatic build inventory integration, examples and release evidence. Configuration and Startup Pipeline implementation are excluded.

## 1. Delivery approach

Deliver in six gated milestones. Each task includes its own tests as it is implemented; M5 completes and runs the full release matrix rather than deferring all testing until the end. The first substantive proof is automatic inventory completeness, because a fast catalog built from the wrong assemblies is not a working module.

These are dependency-ordered milestones, not calendar estimates. The task list does not assign dates or claim that issues have been created in GitHub. Repository inspection is explicitly TM-001, not work already performed for this planning document.

All tasks are required for the declared first release. The default implementing role is **core-library developer / coding agent**, with **maintainer review** at milestone gates and **CI** producing verification evidence. These are responsibility roles, not assignments to named people.

## 2. Milestone roadmap

| Milestone | Outcome | Task range | Entry gate |
|---|---|---|---|
| M1 | Contracts and automatic inventory | TM-001–TM-008 | No prerequisite beyond the existing core repository/toolchain. |
| M2 | Preloading and complete catalog creation | TM-009–TM-014 | M1. |
| M3 | Queries, identity and caches | TM-015–TM-020 | M2. |
| M4 | Host integration and consumer examples | TM-021–TM-025 | M3. |
| M5 | Verification, compatibility and maintainability | TM-026–TM-031 | M4, with tests developed throughout M1–M4. |
| M6 | Package, reproducible evidence and handoff | TM-032–TM-036 | M5. |

**Critical path:** automatic inventory → correct loading → complete catalog → query semantics → supported-host proof → verification → packaged-consumer proof.

The contracts and fixture work can proceed alongside early inventory investigation. Within M5, architecture, compatibility and performance work can run in parallel once M4 is complete. Dependencies below remain authoritative; do not begin adjacent module implementation to bypass a Type Manager gate.

## 3. Detailed tasks

Every task starts unchecked. Mark it complete only when the stated evidence is committed and the referenced acceptance scenarios pass. A task is not complete merely because its classes have been added.

### M1 — Contracts and automatic inventory

**Exit gate:** The generated inventory is demonstrably complete for supported build and publish outputs, including an unused project library. Core public contracts and error semantics are recorded.

**Main risk:** Highest-risk point: using compile references, current runtime loads or stale build outputs instead of actual runtime asset selection.

#### [ ] TM-001 — Inspect the repository and establish module boundaries

**Depends on:** None; first repository task.  
**Acceptance:** A24, A29

**Work:** Read existing core layout, public contracts, build targets, package dependency rules and CI gates. Identify any existing type-discovery code and published API before choosing the module namespace. Record the exact SDK and build conventions; do not modify adjacent modules to make this one fit.

**Complete when:** A short implementation note identifies the chosen existing namespace/project, reused facilities, relevant published APIs and commands that actually exist. No claim that this planning pack already audited the repository.

#### [ ] TM-002 — Add the public contracts and API baseline

**Depends on:** TM-001  
**Acceptance:** A13, A25, A29

**Work:** Define ITypeManager, sealed TypeManager, TypeManagerOptions, the preparation report, initialization exception/failure values and the thin generic extension. Keep options as data. Establish nullability, documented argument behaviour and RequiresUnreferencedCode annotations where needed for reflection entry points; do not claim trimming support.

**Complete when:** The contract/reference build compiles under the pinned net10.0 toolchain. Store an API snapshot and a compiled reference consumer for later compatibility verification; implementations may remain internal work in progress until subsequent tasks.

#### [ ] TM-003 — Create real compiled assembly fixtures and host harnesses

**Depends on:** TM-001  
**Acceptance:** A01, A08, A11, A13, A23

**Work:** Add a tiny executable host, a shared test contract, an unused project dependency containing implementations, a transitive dependency, and fixtures for name collisions, generic inheritance and broken dependency resolution. Tests must not reference discovered fixture classes with typeof before checking initial load state.

**Complete when:** A fresh child process proves the discovery-only library is initially unloaded, yet present in the declared deployment. Test fixtures compile as real assemblies; no production dependency on runtime compilation tooling.

#### [ ] TM-004 — Prove SDK runtime inventory selection

**Depends on:** TM-003  
**Acceptance:** A01, A02, A03

**Work:** On the pinned .NET 10 SDK, inspect resolved build and publish items and identify the correct hook after runtime asset selection/conflict resolution. Use a native asset, resource satellite and runtime-specific dependency to prove classification. Capture the build log and expected selected assembly list.

**Complete when:** The list includes host, unused project library and transitive runtime library; excludes native, reference-only, resource and nonselected RID assets. Record actual target/item names from the tested SDK. Do not implement a runtime deps.json/RID parser as a fallback.

#### [ ] TM-005 — Generate the versioned manifest automatically

**Depends on:** TM-004  
**Acceptance:** A02, A03, A07, A30

**Work:** Implement the focused build target/helper. Support CisTypeManagerEnabled and CisTypeManagerAssembly. Emit schema version 1 with full identities and safe relative paths, deterministic ordering and correct JSON escaping. Derive identities from executable metadata without running application code.

**Complete when:** Normal hosts need one enable flag, not per-configuration or per-dependency lists. A generated manifest contains the expected complete selection and is identical for unchanged assets/inputs. Duplicate and conflicting entries follow the definition.

#### [ ] TM-006 — Read and validate bootstrap inputs and manifests

**Depends on:** TM-002, TM-005  
**Acceptance:** A05, A06, A07, A20, A30

**Work:** Implement input copying, manifest lookup/read, schema validation, duplicate-property rejection, host matching, path containment, required file checks and normalized identity validation. Support only the alternate manifest path, not alternate inventory-provider interfaces.

**Complete when:** Unit/fixture tests distinguish missing, malformed, unsupported, conflicting and valid inputs. Mutating an input list after creation starts cannot alter the captured request. Failure never falls back to currently loaded assemblies or a folder scan.

#### [ ] TM-007 — Integrate build, run and publish output lifecycle

**Depends on:** TM-005  
**Acceptance:** A03, A04, A06

**Work:** Wire generation/copying to supported build and folder-publish workflows, including publish --no-build from valid existing outputs. Handle incremental add/remove references, renamed host outputs and clean/rebuild. Reject unsupported publish settings clearly without silently rewriting them.

**Complete when:** Windows and Linux fixture builds/runs/publishes use their own current manifest; removal of a dependency removes its entry. The target has no recursion, design-time side effects or unnecessary rewrites. Unsupported profile checks are exercised.

#### [ ] TM-008 — Pass the automatic inventory gate

**Depends on:** TM-006, TM-007  
**Acceptance:** A01, A02, A03, A04, A05, A24

**Work:** Run the clean-process inventory fixture matrix and retain expected-versus-actual output. Review the build integration for complexity and separation from the runtime package.

**Complete when:** M1 gate evidence shows automatic completeness across the supported profiles. A hand-written list or already-loaded scan does not satisfy this task. Resolve inventory defects before relying on later catalog tests.

### M2 — Preloading and complete catalog creation

**Exit gate:** One completed manager is returned, or preparation fails with useful diagnostics. No partial catalog or isolated contract assembly is possible.

**Main risk:** Loading context conflicts, incomplete enumeration and cleanup on cancellation/failure.

#### [ ] TM-009 — Prepare the selected assembly map

**Depends on:** TM-008  
**Acceptance:** A07, A11, A12, A26

**Work:** Create the complete identity/path map from validated input. Deduplicate identical entries, identify simple-name/version conflicts and capture which selected assemblies already exist in Default before loading starts. Keep deterministic processing order.

**Complete when:** Fixture tests show stable deduplication, explicit conflict failures and correct selected/reused starting sets. No runtime version-selection framework or filename-prefix filtering appears.

#### [ ] TM-010 — Load selected assemblies in the default context

**Depends on:** TM-009  
**Acceptance:** A01, A06, A12, A15, A17

**Work:** Reuse correct loaded assemblies; load remaining selected assets through AssemblyLoadContext.Default using normal probing and selected paths. Check actual identities. Handle discovery-only dependency ordering without directory searches; any temporary manifest-only resolver is nonrecursive and always removed.

**Complete when:** The initially unloaded fixture loads into the same contract universe as the host. Selected version mismatches fail. Failure and cancellation leave no resolver subscriptions or open files. No isolated LoadFile/byte-load workaround is used.

#### [ ] TM-011 — Enumerate defined types and relationships once

**Depends on:** TM-010  
**Acceptance:** A08, A09, A10, A16, A19

**Work:** Enumerate every selected assembly exactly once. Retain defined runtime types, assembly membership, interfaces/base relationships and open generic definitions. Include nonpublic/nested types and respect forwarding; do not activate instances or attributes.

**Complete when:** Enumeration tests cover direct/inherited markers, forwarded types, nonpublic classes and open definitions. Test instrumentation records one enumeration per selection; constructor/attribute side-effect counters remain unchanged by deliberate discovery actions.

#### [ ] TM-012 — Implement useful operational failure contracts

**Depends on:** TM-002, TM-011  
**Acceptance:** A05, A15, A16, A26

**Work:** Wrap operational inventory/loading/discovery failures using the stable failure kinds. Preserve selected/actual identities, relevant path, inner exceptions and all loader errors from incomplete enumeration. Keep argument errors and expected cancellation separate.

**Complete when:** Broken fixture assemblies cause explicit failure with actionable diagnostics and no published partial catalog. Tests exercise each public failure kind, including the exception contents and protected loader-error collection.

#### [ ] TM-013 — Complete the factory and publication boundary

**Depends on:** TM-006, TM-012  
**Acceptance:** A17, A18, A20, A22, A29

**Work:** Implement the direct CreateAsync sequence with local mutable preparation state, input snapshot, asynchronous manifest read, sequential runtime work and cooperative cancellation. Publish only the finished manager. Do not introduce a background task, public readiness state or global singleton.

**Complete when:** Tests cover cancellation before work, between loading units and at publication; post-completion cancellation cannot revoke results. Awaiters of one task see its same manager/failure, while independent creation calls remain independent. Loaded assemblies are not falsely described as rolled back.

#### [ ] TM-014 — Pass loader and complete-catalog failure tests

**Depends on:** TM-013  
**Acceptance:** A06, A10, A12, A15, A16, A17

**Work:** Run fresh-process scenarios for missing/corrupt assets, incompatible identities, partial type-load errors and discovery side effects. Check resource/resolver cleanup and failure propagation.

**Complete when:** M2 gate produces either a complete valid catalog or a diagnostic failure. No skip-and-continue branch masks a required assembly. Preserve child-process logs and exact fixture identities.

### M3 — Queries, identity and caches

**Exit gate:** Closed implementation queries and exact identity lookup are correct, deterministic, protected and safe under concurrent use; no rescans occur.

**Main risk:** Incorrect assignability, implicit public/constructor filters and mutable shared results.

#### [ ] TM-015 — Build protected catalog and identity maps

**Depends on:** TM-014  
**Acceptance:** A07, A11, A20

**Work:** Publish owned read-only assembly/type collections, assembly-to-types mapping and exact assembly-qualified identity lookup. Implement deterministic sorting, distinct assembly identity and safe handling of entries without textual identity.

**Complete when:** Name-collision fixtures resolve correctly; no distinct type is overwritten. Consumers cannot mutate another caller's data by casting returned collections. Reordered inventory produces the same defined results.

#### [ ] TM-016 — Implement the single assignability matcher

**Depends on:** TM-015  
**Acceptance:** A08, A09, A13

**Work:** Validate query contracts and implement concrete/closed class filtering followed by IsAssignableFrom. Include inherited implementation, concrete self-match and closed generic variance. Do not filter by constructor/public visibility or synthesize generic types.

**Complete when:** Direct/inherited/base-class and variance fixtures pass. Open generic and other unsupported queries receive their documented argument error. Nonpublic/no-default-constructor implementations remain discoverable.

#### [ ] TM-017 — Implement exact lookup and assembly membership APIs

**Depends on:** TM-015  
**Acceptance:** A14, A20

**Work:** Finish GetDefinedTypes and TryGetType with the exact argument and miss semantics. Use stored maps only. Preserve exact ordinal identity matching and nullability guarantees.

**Complete when:** A query for a real but noncataloged assembly/type cannot load or enumerate it. Empty/whitespace inputs, unknown names and external assembly arguments have documented outcomes.

#### [ ] TM-018 — Add thread-safe lifetime query caching

**Depends on:** TM-016, TM-017  
**Acceptance:** A19, A20, A22, A29

**Work:** Cache each valid query by Type, including empty results. Start with one private lock for initial computation/publication and protected result lists. Do not add TTL, eviction, a generic cache service or assembly-load subscriptions.

**Complete when:** Parallel first/repeated queries return identical ordered membership without partial lists or cache corruption. Invalid queries are not cached, and neither cache misses nor hits rescan assemblies.

#### [ ] TM-019 — Prepare requested queries before publication

**Depends on:** TM-018  
**Acceptance:** A13, A19, A20, A26

**Work:** Process PrecomputedContracts through the same validation and matcher as runtime queries. Deduplicate equivalent Type keys, retain their completed results and populate accurate preparation counts.

**Complete when:** A requested discovery query is ready when creation returns. Its result matches the equivalent later generic/non-generic call. Input-list changes after capture do not change prepared contracts.

#### [ ] TM-020 — Pass query, concurrency and immutability gates

**Depends on:** TM-019  
**Acceptance:** A08, A09, A11, A13, A14, A19, A20, A22

**Work:** Run the complete query matrix under concurrent access, culture changes, reversed input order and caller mutation attempts. Add instrumentation assertions against repeated GetTypes or unapproved loading fallbacks.

**Complete when:** M3 gate proves stable queries and exact type identity from both cold and cached query paths. Query tests are committed with the implementation, not postponed to final release.

### M4 — Host integration and consumer examples

**Exit gate:** Console/ASP.NET examples prepare the catalog before container construction and resolve the same prepared instance. Configuration usage is illustrated without implementing it.

**Main risk:** Accidentally starting discovery through DI or freezing postponed module contracts.

#### [ ] TM-021 — Finish the preparation report and host-facing diagnostics

**Depends on:** TM-020  
**Acceptance:** A26, A29

**Work:** Complete report fields, read-only identity lists, monotonic elapsed measurement and consistent loaded/reused classification. Add a short example logging the report after creation and logging exception details on failure without DI dependencies.

**Complete when:** Counts reconcile with the selected set and warm-query preparation. Report mutation attempts fail safely. No per-type log stream, environment dump or configuration dependency is added.

#### [ ] TM-022 — Add the minimal pre-container console example

**Depends on:** TM-021, TM-008  
**Acceptance:** A01, A10, A21, A23, A29

**Work:** Create a small host that explicitly supplies its assembly, awaits Type Manager creation, displays discovered test-contract classes and only then reaches a container-construction boundary. Use a test-only marker, not a newly invented public Configuration contract.

**Complete when:** The example builds/runs from documented commands and discovers the unused library. A preparation failure prevents its container boundary. A reader can follow the entire bootstrap call without a startup framework.

#### [ ] TM-023 — Demonstrate same prepared instance through DI

**Depends on:** TM-022  
**Acceptance:** A21, A23, A24, A29

**Work:** In a separate ASP.NET Core/host integration sample, register the already-created ITypeManager as an instance and resolve it in multiple scopes. Keep framework/DI references in the sample, not in the runtime core.

**Complete when:** All sample consumers receive that exact prepared manager; constructor resolution triggers no scan. Bootstrap preparation precedes container construction. The sample introduces no configuration-object lifetime implementation.

#### [ ] TM-024 — Document the Configuration consumer boundary

**Depends on:** TM-022, TM-023  
**Acceptance:** A08, A21, A29

**Work:** Add an example of querying configuration candidates and explain that construction, singleton retention and storage belong to the later Configuration module. Use the real marker only if its published contract already exists; otherwise compile samples with the dedicated discovery fixture marker.

**Complete when:** Examples show type and defining assembly identity and inherited matches. No public IConfigurationObject/event API or Startup Pipeline contract is created merely to finish this module.

#### [ ] TM-025 — Pass supported-host integration tests

**Depends on:** TM-024  
**Acceptance:** A01, A03, A21, A22, A23

**Work:** Run the console and ASP.NET Core samples in fresh processes on the supported build and publish layouts. Check initial load state, complete discovered selection, failure-before-container and fixed membership after a later unrelated load.

**Complete when:** M4 gate stores reproducible supported-host evidence and sample output. It proves the pre-container ordering requirement without defining the postponed startup orchestration.

### M5 — Verification, compatibility and maintainability

**Exit gate:** All required verification categories pass, performance evidence exists, and the direct implementation passes human readability/scope review.

**Main risk:** Treating test labels as coverage, hiding surviving behavioural mutations, or inventing compatibility/performance evidence.

#### [ ] TM-026 — Complete acceptance-to-test coverage

**Depends on:** TM-020, TM-025  
**Acceptance:** A01, A02, A03, A04, A05, A06, A07, A08, A09, A10, A11, A12, A13, A14, A15, A16, A17, A18, A19, A20, A21, A22, A23, A26, A30

**Work:** Review A01–A30 against actual committed tests and identify gaps. Run unit, integration and business-case suites for every supported profile. Retain original v0.1 acceptance traceability.

**Complete when:** Every applicable acceptance scenario maps to named runnable tests and a result. Missing tests, unexplained skips or ambiguous evidence block completion. Existing repository thresholds remain intact.

#### [ ] TM-027 — Run meaningful mutation tests

**Depends on:** TM-026  
**Acceptance:** A07, A09, A11, A13, A16, A19, A20, A28

**Work:** Apply mutation testing to candidate filters, assignability direction, identity keys, immutable publication, cache insertion and inventory validation. Include focused build-helper logic where applicable. Keep mutation tool dependencies outside the runtime package.

**Complete when:** Mutations that weaken required behaviour are detected. Survivors are fixed or individually explained as equivalent/unreachable with review; do not blanket-exclude loader/error code to improve a score.

#### [ ] TM-028 — Verify architecture and dependency boundaries

**Depends on:** TM-025  
**Acceptance:** A10, A24, A29

**Work:** Inspect the built package and transitive runtime graph, not only source imports. Enforce no external runtime packages, container/Configuration dependency, object activation responsibilities, environment initialization, persistent resolver or background worker. Separate build/test dependencies.

**Complete when:** An automated architecture/dependency report is produced. Runtime package consumers do not inherit MSBuild/test tooling. The core remains usable from a plain net10.0 console application.

#### [ ] TM-029 — Verify source, binary and behaviour compatibility

**Depends on:** TM-002, TM-025  
**Acceptance:** A25, A30

**Work:** Run the frozen reference consumer without recompiling against the candidate; also compile historical source against it. Compare API, defaults, failure values, ordering, queries and manifest schema fixtures. Archive the baseline rather than replacing it.

**Complete when:** Compatibility reports pass. On first release the reference/RC baseline is honestly labelled pre-release; the actual release binary becomes the next historical baseline. No fabricated claim of testing nonexistent old releases.

#### [ ] TM-030 — Measure startup, queries and memory

**Depends on:** TM-025  
**Acceptance:** A19, A23, A27

**Work:** Measure fresh-process preparation, prepared lookup, first uncached matching, repeated matching and retained memory for representative small/large catalogs. Compare with a clearly defined control that defers the same discovery work. Record SDK, OS, architecture, catalog size and sample method.

**Complete when:** Results distinguish shifted startup work from total cost. Enumeration counters show no post-publication rescans. Publish observations, not invented speedups or an unapproved absolute timing target.

#### [ ] TM-031 — Perform maintainability and scope review

**Depends on:** TM-026, TM-027, TM-028, TM-029, TM-030  
**Acceptance:** A24, A25, A27, A28, A29

**Work:** Review the direct creation flow, helpers, public surface and examples. Remove dense code, unnecessary interfaces, giant positional contracts, excessive partial classes and unrelated module changes. Check all release gates and document residual defects honestly.

**Complete when:** M5 gate passes all required suites and maintainer review. No unresolved correctness/compatibility issue is dismissed as a performance optimization or future Configuration task.

### M6 — Package, reproducible evidence and handoff

**Exit gate:** The exact packaged artifacts pass fresh consumer tests and have a complete reproducible verification bundle. Maintainer can review a truthful completion report.

**Main risk:** Project-reference tests masking broken build assets or missing package dependencies.

#### [ ] TM-032 — Package runtime and build integration

**Depends on:** TM-031, TM-008  
**Acceptance:** A02, A24, A28

**Work:** Produce the core package using existing repository conventions, including its required build assets/helper in build-only locations. Verify enablement and discovery-only inclusion instructions work from a package consumer rather than a project reference alone.

**Complete when:** Package inspection shows correct runtime/build separation. A fresh consumer using the packed artifact can generate its manifest automatically without repository-specific imports.

#### [ ] TM-033 — Write final usage and troubleshooting documentation

**Depends on:** TM-031, TM-025  
**Acceptance:** A21, A27, A28, A29

**Work:** Document supported profiles, one-flag build setup, CreateAsync-before-container example, query semantics, identity rules, failure messages, cancellation and non-goals. Include expected manifest contents and debugging steps for missing discovery-only assemblies.

**Complete when:** All code samples compile/run in their sample projects. Readers can follow setup without learning a provider framework. Clearly state that preloading is not full JIT/static/application-resource warm-up.

#### [ ] TM-034 — Assemble the versioned verification bundle

**Depends on:** TM-032, TM-033  
**Acceptance:** A25, A28, A30

**Work:** Bundle test source, compiled assembly fixtures, compatibility binaries, manifest fixtures, pinned tooling metadata, runnable commands and actual reports for all six required suites. Include package/source commit identifiers and file checksums tying evidence to the release candidate.

**Complete when:** Another developer can restore required development tools and rerun the bundle. All six categories contain real relevant verification, not empty folders or placeholder pass reports. Failures/skips are visible and block the applicable gate.

#### [ ] TM-035 — Run clean package-consumer release smoke tests

**Depends on:** TM-034  
**Acceptance:** A01, A02, A03, A21, A23, A24, A25, A28

**Work:** Using the exact packed artifacts, build/run/publish fresh Windows and Linux host consumers outside the repository fixture setup. Recheck automatic unused-library discovery and failure-before-container behaviour; rerun compatibility against the exact candidate binary.

**Complete when:** M6 candidate evidence ties smoke-test logs and compatibility results to the artifact hashes. A dependency or target only available through the development checkout cannot hide a packaging defect.

#### [ ] TM-036 — Close the completion checklist and prepare the handoff

**Depends on:** TM-035  
**Acceptance:** A28, A29

**Work:** Reconcile tasks, acceptance results, public API baseline, package contents, examples and known limitations. Produce the completion report and Configuration handoff using the existing interface. Prepare release artifacts; publication remains a separate authorized maintainer action.

**Complete when:** All mandatory tasks/gates have evidence and the maintainer records implementation readiness. Any remaining defect is reported as blocking rather than silently converted into completed work. Startup Pipeline and Configuration implementation remain out of this delivery.

## 4. Acceptance matrix

These are required test scenarios, not results already obtained. Tests may cover more than one scenario, and one scenario may need multiple tests/profiles. Fill in actual test identifiers and artifact paths during implementation.

| ID | Area | Required evidence |
|---|---|---|
| A01 | Initially unused library | A fresh host begins without loading the discovery fixture library. Its generated inventory includes it, preparation loads it and the implementation query finds its class before the container boundary. |
| A02 | Automatic inventory | One host enablement includes normal project/transitive runtime references without manual class lists. Explicit build inclusion covers additional discovery-only assets; a package consumer receives the build integration. |
| A03 | Correct build/publish assets | Windows/Linux supported build and folder-publish outputs include the selected runtime assemblies and exclude native/reference/resource/wrong-RID assets. Use both console and ASP.NET Core hosts. |
| A04 | Incremental and no-build publishing | Adding/removing a dependency updates the manifest. Clean/rebuild and publish --no-build from valid outputs produce correct target-specific inventory, without stale entries or target recursion. |
| A05 | Invalid inventory fails visibly | Missing/unreadable manifest, malformed JSON, duplicate JSON keys, host mismatch or unsupported schema cannot become a successful empty catalog. |
| A06 | Unsupported inputs/profiles | Path traversal, conflicting identities and selected nonexecutable/dynamic/metadata-only/custom-context assemblies are rejected when applicable. Unsupported enabled publish profiles fail clearly, not by silent scope reduction. |
| A07 | Deduplication and conflicts | Repeated identical selections yield one assembly/type set. Distinct conflicting selections fail; reversing inventory order never selects a different winner. |
| A08 | Direct and inherited matching | Direct interface implementations, derived classes and inherited interface implementations match. A cataloged concrete class can match itself. Every result appears once. |
| A09 | Candidate filters | The complete catalog retains nonpublic/nested/abstract/struct/open-definition entries. The implementation query selects nonabstract closed classes without imposing public visibility or constructor requirements. |
| A10 | No deliberate activation | Discovery does not invoke application constructors, attributes, property values, lifecycle methods or forced type initializers. Tests distinguish this from runtime-controlled module initialization. |
| A11 | Assembly-aware identity | Same namespace/type name in two differently named assemblies remains distinct. Exact assembly-qualified lookup returns the correct actual Type, including observed assembly version. |
| A12 | Correct runtime type universe | Loaded implementations are assignable to the host contract from the default context. Matching assemblies are reused; conflicting selected versions and unsupported contexts cannot be silently substituted. |
| A13 | Generic and argument rules | Closed generic queries follow CLR variance/inheritance. Open generic and other unsupported contracts fail as documented. Generic/non-generic methods agree. |
| A14 | Lookup misses do not load | Unknown nonempty identities return false/null even if the named assembly exists outside the catalog. No Type.GetType fallback, disk search or on-demand enumeration occurs. Validate all null/empty arguments. |
| A15 | Required loading failure | Missing, corrupt or incompatible required runtime assets fail preparation with the selected identity/path and preserved cause. No candidate is silently omitted. |
| A16 | Partial discovery failure | Incomplete type/relationship loading yields useful loader details but no usable manager or successful partial list. |
| A17 | Cooperative cancellation | Cancellation before/during preparation stops at an observable boundary and returns no manager when accepted before publication. Cancellation after success does not revoke the manager; resolver/file cleanup still occurs. |
| A18 | Task/result lifetime | Several awaiters of one factory task receive its same completed result/failure. Separate factory calls are independent; no static reset or process-global merge exists. |
| A19 | Queries and concurrency | Prepared, first uncached, repeated and parallel queries have consistent ordered membership. Every selected assembly is enumerated once per successful preparation; queries never repeat enumeration. |
| A20 | Read-only results and captured inputs | Caller input mutation after capture cannot change preparation. Attempts to mutate exposed result/report collections cannot alter state seen by another reader. |
| A21 | Before-container ordering and DI reuse | Hosts finish preparation before container construction and register that exact instance. Failure prevents construction. Resolution in different scopes does not load or rescan. |
| A22 | Fixed membership | A later unrelated assembly load does not extend the catalog or change existing query membership. No lifetime runtime-load subscription remains. |
| A23 | Fresh-process deployment verification | Supported fixture/sample/package hosts run in fresh processes with an independently known expected catalog; they do not accidentally preload the library whose discovery is being tested. |
| A24 | Runtime dependency boundaries | The packed runtime introduces no external runtime NuGet, DI-container, Configuration, MSBuild or test-tool dependency. Build/test tooling is demonstrably separate. |
| A25 | Compatibility | API/source/binary/behaviour tests verify the frozen initial baseline and subsequently retained historical baselines. Identity/query/error/default semantics are not silently changed. |
| A26 | Useful preparation diagnostics | Report counts/identity lists reconcile and elapsed time is monotonic. Failures preserve relevant inner/loader exceptions; no DI logger or Configuration availability is required. |
| A27 | Honest performance evidence | Measurements identify machine/runtime/catalog and distinguish preloading from JIT/resource initialization. No unmeasured speedup or arbitrary timing guarantee is claimed. |
| A28 | Complete release evidence | The exact candidate is accompanied by all six runnable verification suites, fixtures, instructions, results, API baseline and artifact hashes. Clean consumers validate the packaged build integration. |
| A29 | Small scope and readable implementation | One direct bootstrap flow and focused helpers remain. No Startup Pipeline, Configuration implementation, plugin framework or EnvironmentOptions initialization is added. |
| A30 | Manifest compatibility | Schema-1 fixtures round-trip correctly; unknown additive fields are tolerated, unsupported schema versions/ambiguous duplicate keys fail, and existing released manifest fixtures remain supported. |

### Traceability from the original v0.1 acceptance cases

All 21 earlier scenarios remain represented. A02/A04/A05/A26–A30 expand inventory, diagnostics, performance, scope and release obligations rather than replacing the earlier cases.

| Original | Current |
|---|---|
| AT01 | A01, A02, A21 |
| AT02 | A07 |
| AT03 | A08 |
| AT04 | A09 |
| AT05 | A10 |
| AT06 | A11 |
| AT07 | A12 |
| AT08 | A13 |
| AT09 | A14 |
| AT10 | A15 |
| AT11 | A16 |
| AT12 | A18 |
| AT13 | A17 |
| AT14 | A19 |
| AT15 | A20 |
| AT16 | A21 |
| AT17 | A22 |
| AT18 | A03, A23 |
| AT19 | A25 |
| AT20 | A24 |
| AT21 | A06, A12 |

## 5. Verification suites and release gates

| Suite | Minimum module-specific content | Gate |
|---|---|---|
| Unit | Manifest and argument rules; identity maps; concrete filters; generic matching; deterministic results; failure/report values; protected collections. | All required cases pass; existing repository coverage rules are retained. |
| Integration | Real compiled fixtures, initially unloaded libraries, default loading context, dependency failures, cancellation, build/publish manifests, clean package consumers on both supported OS profiles. | No unexplained missing/partial discovery or unsupported input accepted. |
| Mutation | Filters, IsAssignableFrom direction, assembly-aware keys, failure-on-partial behaviour, query caching/publication and focused manifest validation/helper logic. | Behaviour-weakening mutants are caught; each equivalent/unreachable survivor has a reviewed reason. No blanket exclusions to hide weakness. |
| Architecture | Package dependency graph, build/runtime separation, no DI/Configuration dependency, no activation/lifecycle/env ownership, no persistent background resolver or worker. | Zero prohibited runtime dependencies and no scope violations. |
| Backward compatibility | API snapshot, historical source build, compiled consumer without recompilation, query/error/default/order fixtures and schema-1 inventories. | All retained applicable baselines pass. First-release reference evidence is labelled accurately. |
| Business cases | A host discovers an unused configuration-like library before DI; multiple consumers reuse the catalog; a broken required deployment prevents container construction; a later load does not alter membership. | End-to-end outcomes pass from the packed artifact, not only unit mocks. |

No fabricated universal 95% or 100% line-coverage target is added. Existing repository gates must not be weakened. Coverage metrics supplement the acceptance cases; a high number is not evidence that loader, variance or publication semantics were tested.

A fresh child process is required for load-state assertions because an ordinary test runner can preload dependencies. Keep fixture output outside the test runner's own eager-loading path and explicitly record the relevant assembly's absence before preparation. Use actual compiled fixtures for type identity; mocked Type/Assembly objects are not a replacement for CLR loading tests.

Do not assert that the entire process loads no assembly after bootstrap. Assert that Type Manager does not rescan/extend its selected catalog or deliberately load on a query miss. The runtime and unrelated application work can legitimately load other assemblies.

### Supported-profile test grid

Run the relevant host tests on Windows x64 and Linux x64 using the pinned .NET 10 SDK/runtime and framework-dependent untrimmed output. Cover normal build/run, folder publish, and publish --no-build from valid preceding build outputs. Exercise console and ASP.NET Core consumers. Include debug/release coverage for inventory generation; release-package evidence uses the actual release configuration.

Single-file, AOT, trimming, self-contained and custom-context/plugin scenarios test explicit rejection/scope behaviour where applicable, not advertised successful deployment support. Additional platform support requires an explicit later scope change and its own evidence.

## 6. Required examples and documentation

Ship a minimal console example, a separate DI/ASP.NET Core integration example and a broken-deployment diagnostic example. A compile-time reference to the shared marker is allowed; do not reference a discovered implementation merely to cause it to load before the discovery assertion.

Examples show one enablement property, the generated inventory, explicit host-assembly input, awaited creation before the container boundary, implementation lookup and exact identity lookup. Show an inherited marker implementation and a class without a default constructor so the discovery/construction distinction is visible.

Document that the prepared Type Manager instance is reused by DI. This does not implement Configuration singleton object retention. Describe Configuration's future usage without defining its event arguments, persistence key/version policy, mapping or reload synchronization.

The troubleshooting page should distinguish missing manifest, excluded undeployed library, unreadable asset, identity conflict and incomplete type enumeration. It should point to actual report/exception fields and avoid a generic recommendation to swallow loader errors.

## 7. Release verification bundle

The versioned bundle must contain the exact runtime package and separate build integration assets, source commit and package hashes, SDK/tool versions, all six test categories, source and compiled assembly fixtures, valid/invalid manifest fixtures, frozen compatibility consumers and API baseline, runnable commands, and actual results.

Performance reports include machine/runtime/catalog details, process-fresh preparation cost, prepared query cost, uncached matching cost, repeated-query behaviour, retained managed memory and the defined comparison method. They do not promise that JIT, static constructors or application initialization have been pre-executed.

Test tooling may need package restore/network access; document those prerequisites and the pinned tool versions. Do not claim the bundle is offline/self-contained unless that has actually been delivered and verified. Never distribute placeholder reports as executed results.

## 8. Completion checklist

- [ ] All 36 tasks have implementation/review evidence, or the delivery is explicitly marked incomplete.
- [ ] A01–A30 map to real tests and the original AT01–AT21 trace is preserved.
- [ ] Automatic inventory works from packed build integration on supported fresh consumers.
- [ ] Preparation completes before container construction; broken required input stops that path.
- [ ] Correct default-context CLR types, exact identity and inherited/closed-generic queries are verified.
- [ ] No required assembly/type is silently skipped; loader details survive failure.
- [ ] Captured inputs, fixed membership, read-only results and concurrent caches are verified.
- [ ] All six verification categories and supported profile tests have actual passing results.
- [ ] Source/binary/behaviour/manifest compatibility evidence is retained for future releases.
- [ ] Runtime package dependency boundaries and direct human-readable implementation are reviewed.
- [ ] Examples, setup/troubleshooting docs and measured performance evidence match the exact artifacts.
- [ ] Release verification bundle has hashes, fixtures, reproducible instructions and truthful status.
- [ ] Configuration handoff uses the completed query contract without implementing postponed modules.

## 9. Completion report template

Record: source commit, package/version/hash, SDK/runtime, supported profile results, completed task IDs, acceptance-to-test map, six suite results, reviewed mutation survivors, compatibility baseline versions, performance report location, sample commands, remaining defects and maintainer review.

Allowed status is `Complete` only after all required gates pass. Otherwise use `Blocked` or `In progress`, identify the missing evidence and do not present the module as production-ready. Preparing this roadmap closes the design/planning work; it does not execute these tasks or publish a software release.
