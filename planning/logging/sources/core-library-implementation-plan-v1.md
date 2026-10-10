# Core Library Implementation Plan

**Plan version:** 1.0  
**Date:** 9 October 2026  
**Baseline:** Core Library Implementation Guide, specification 1.1  
**Delivery status:** Planned

Build the standalone C# environment and logging core, verify its contracts, create examples that consume its packaged API, and deliver the first release with its complete verification bundle. This backlog translates the guide into assignable stories and implementation tasks. The guide remains authoritative for exact API semantics, environment-variable defaults, value bounds, and release gates.

The initial delivery covers WP-01 through WP-09. TypeScript work is a separate follow-on that starts after the C# release and its examples establish the shared contract. Tests accompany implementation throughout; the verification-completion package assembles and checks the full evidence rather than starting testing for the first time.

## 1 Scope and working rules

### Required first release

- The exact static entry point `EnviormentsOptions`, explicit startup initialization, immutable resolved configuration, and automatic application/build/environment/host/OS context.
- A small structured logging API, copied event values, bounded exception capture, operation scopes, correlation, and correctly deferred event construction.
- Human and JSONL rendering, independent console/file filtering, bounded per-output queues, rotating files, diagnostics, flush, and shutdown.
- A separately packaged Microsoft logging bridge. It is optional for an application to install and remains part of this release's planned implementation and verification scope.
- Unit, integration, mutation, architecture, backwards-compatibility, and business-case verification, all required with every published version.
- Eight runnable reference examples, language-neutral fixtures, and a complete release verification bundle.

Keep the shipping core at zero explicit `PackageReference` items and free of third-party runtime dependencies. Test tools and the optional bridge remain outside that dependency graph. Use `Core` and `Core.MicrosoftLogging` as working names until WP-01 establishes the actual package, assembly, and namespace identities.

Preserve the guide's indefinite compatibility commitment from the first published contract, including prereleases. Source, binary, configuration, wire format, documented behavior, and retained target assets remain covered. Publishing a new major version does not waive that commitment.

### Boundaries

The GMCore attachment informs the new design. Godot integration, GMCore compatibility adapters, duplicate suppression, durable disk spooling, direct collector exporters, unrelated core modules, and consuming-application migrations are outside this first release. Collector-oriented JSON output is required; actual hosted collector-ingestion claims require their own exercised integration evidence. The plan does not depend on such an external hosted service for basic verification.

The TypeScript follow-on preserves the shared contract through runtime-appropriate implementations. Runtime support and the language-specific test toolchain are selected in that follow-on before implementation promises are made.

### How to use story and task identifiers

Each work package is an epic. Story IDs have the form `WP03-S01`; task IDs append `-T01`, and acceptance criteria append `-AC01`. `TS-S01` identifies a later TypeScript story. These IDs remain stable when titles, owners, or scheduling change. Requirement IDs `REQ-001` through `REQ-010` and business-case IDs `BC-001` through `BC-016` retain their meanings from the guide.

Every story and task starts as **Planned** and unassigned. Initial C# stories have priority **Required for v1**. TypeScript stories have priority **Follow-on**. Priority does not make a required test family or release gate optional.

`Depends on` lists direct story prerequisites. Dependencies inherited through other stories also apply. The structured backlog expands work-package gates into explicit story IDs and contains no calendar commitments or invented effort estimates. Task ordering is advisory where tasks can proceed independently; the story's dependencies and acceptance criteria govern completion.

## 2 Delivery order and milestones

Work proceeds through eight milestones across the nine initial work packages. A milestone closes only when all of its stories are Done and its exit evidence is available. A later story may prepare independent fixtures or contracts once its explicit prerequisites are satisfied; the package-level milestone order still governs readiness and release claims.

| Milestone | Work package | Observable exit |
| --- | --- | --- |
| M1 Foundation | WP-01 | The solution builds, dependencies are constrained, tooling is pinned, and verification/discovery negative controls work. |
| M2 Contracts | WP-02 | Public shapes, configuration semantics, event schema, lifecycle results, compatibility policy, and initial fixtures are coherent and reviewed. |
| M3 Environment | WP-03 | Startup resolution and immutable publication pass deterministic and process-isolated tests, including relative-path capture. |
| M4 Core logging | WP-04 and WP-05 | Event capture, both output formats, independent outputs, file policy, bounded delivery, diagnostics, flush, and stop pass their relevant tests. |
| M5 Host integration | WP-06 | Microsoft logging maps into the core without dependency leakage, duplicate records, or incorrect runtime ownership. |
| M6 Verified core | WP-07 | All six verification families and their harnesses are complete; discovery, coverage, mutation, historical replay, and architecture checks work. |
| M7 Reference examples | WP-08 | All eight examples run against candidate packages and assert the documented events and failure behavior. |
| M8 First release | WP-09 | The final candidate passes the complete release run, its replayable bundle is retained, and promotion uses the same tested payload. |

The first useful end-to-end demonstration is a small test consumer that initializes the core, emits a contextual event to console JSONL, and reports a bounded stop result. Build that as an integration fixture after the relevant environment, event, queue, console, diagnostics, and lifecycle stories exist. Its success is an intermediate implementation result; the remaining file, bridge, example, and release obligations remain part of v1.

### Parallel work

After the contracts are stable, output rendering, controlled I/O fixtures, and verification tooling can advance independently of parts of the environment and event implementation. Once immutable event capture is available, human/JSONL formatting and file work can proceed alongside queue and lifecycle work. Integrate against the same contract revision and keep shared result/ownership changes with one owner.

Use three practical lanes when more than one developer or agent is available:

| Lane | Main responsibility | Integration constraint |
| --- | --- | --- |
| Runtime | Environment, capture, context, admission, accounting, flush, and stop. | One reviewed contract for event ownership, output state, and completion boundaries. |
| Outputs and integration | Renderers, console/files, Microsoft bridge, and later examples. | Consume immutable captured events and the agreed lifecycle; do not create an alternative pipeline. |
| Verification | Test harnesses, negative fixtures, architecture rules, compatibility, mutation, packaging, and replay. | Exercise the same candidate/source inputs and make missing evidence fail visibly. |

Within a story, use small commits for a coherent behavior and its tests. Avoid splitting one responsibility into many partial files or introducing public abstractions solely to make tests easier.

## 3 Readiness and completion rules

### A story is ready to start when

1. Its dependencies are Done, or the selected task is demonstrably independent preparation that does not claim the story's behavior is complete.
2. The implementer has read the cited guide sections and relevant existing public contracts.
3. Any repository-specific naming, platform, or toolchain decision required by that story has been recorded by its owning foundation/contract task.
4. The intended acceptance observations and test seam or real integration fixture are identified.

Use the provided `net10.0` baseline unless the initial consumer review establishes a concrete earlier-target requirement. Record exact serviced SDK, package, tool, OS, and architecture versions during setup. The plan assigns that decision to a task; it does not invent patch versions or require a separate approval for routine setup choices.

### A story is Done when

1. Its implementation exists and every acceptance criterion has passing, identifiable evidence.
2. The relevant unit, business, integration, architecture, and compatibility checks have run; concurrency/failure cases use controlled coordination rather than arbitrary sleeps.
3. The change passes applicable build, nullable, analyzer, formatting, documentation, dependency, and readability checks.
4. Configuration and public-contract changes are reflected in current documentation and fixtures. Historical fixtures remain immutable.
5. The review explains responsibility, behavior, ownership, compatibility impact, and the actual verification results.
6. Evidence identifies the source revision and, where applicable, the actual package and loaded assembly. Missing tests, skipped required cases, and placeholder reports do not count as completion.

Day-to-day story verification uses the relevant fast checks. The full six-family suite is mandatory for every published version, including prereleases and hotfixes; a previous development run cannot substitute for the final candidate's release evidence.

### Execution states

Use Planned, Ready, In progress, In review, Done, and Blocked. Record a concrete reason and dependency for Blocked. A planning document, a compiling stub, or a generated test-project shell cannot by itself move a functional story to Done. Owners and target iterations are assigned when work is pulled.

## 4 Epic and story index

| Epic | Scope | Stories | Tasks |
| --- | --- | --- | --- |
| WP-01 Repository foundation | C# v1 | 4 | 16 |
| WP-02 Public contracts | C# v1 | 4 | 16 |
| WP-03 Environment resolution | C# v1 | 4 | 16 |
| WP-04 Event capture and logging API | C# v1 | 5 | 21 |
| WP-05 Outputs and lifecycle | C# v1 | 10 | 41 |
| WP-06 Microsoft logging bridge | C# v1 | 3 | 15 |
| WP-07 Verification completion | C# v1 | 6 | 33 |
| WP-08 Reference examples and agent guidance | C# v1 | 4 | 21 |
| WP-09 First release | C# v1 | 4 | 20 |
| TS TypeScript follow-on | Later TypeScript | 5 | 25 |

The numbers in story IDs identify work; dependency order determines when it can start. Every item is Planned, with no task or acceptance evidence yet marked complete.

| Story | Outcome | Direct prerequisites | Tasks |
| --- | --- | --- | --- |
| WP01-S01 | Establish package identities and supported framework contracts | None | 4 |
| WP01-S02 | Create the minimal repository and enforce shipping dependency boundaries | WP01-S01 | 4 |
| WP01-S03 | Pin and prove the complete test toolchain early | WP01-S02 | 4 |
| WP01-S04 | Bootstrap verification commands, test inventories, and deterministic support | WP01-S03 | 4 |
| WP02-S01 | Define immutable startup, identity, and configuration contracts | WP01-S04 | 4 |
| WP02-S02 | Define event schema, supported values, and portable fixtures | WP02-S01 | 4 |
| WP02-S03 | Define logging, admission, lifecycle, and ownership results | WP02-S02 | 4 |
| WP02-S04 | Establish permanent contract and cumulative baseline governance | WP02-S03 | 4 |
| WP03-S01 | Resolve application and runtime identity | WP02-S04 | 4 |
| WP03-S02 | Resolve documented environment settings and output defaults | WP02-S04 | 4 |
| WP03-S03 | Validate configuration and resolve file paths without side effects | WP03-S01, WP03-S02 | 4 |
| WP03-S04 | Publish the immutable EnviormentsOptions snapshot once | WP03-S03 | 4 |
| WP04-S01 | Capture supported structured values within fixed bounds | WP02-S04 | 4 |
| WP04-S02 | Project exceptions into bounded immutable event data | WP04-S01 | 4 |
| WP04-S03 | Implement operation scopes across asynchronous work | WP04-S01 | 4 |
| WP04-S04 | Create complete immutable events with metadata and W3C correlation | WP03-S04, WP04-S02, WP04-S03 | 5 |
| WP04-S05 | Implement logger filtering, deferred capture, and write outcomes | WP04-S04 | 4 |
| WP05-S01 | Render the same event as JSONL or readable human output | WP04-S04 | 4 |
| WP05-S02 | Admit events through independent bounded output queues | WP04-S05 | 4 |
| WP05-S03 | Account for delivery and isolate terminal output failures | WP05-S02 | 4 |
| WP05-S04 | Integrate configured console output with runtime ownership | WP05-S01, WP05-S03 | 4 |
| WP05-S05 | Initialize an exclusively owned file output and managed inventory | WP05-S04 | 4 |
| WP05-S06 | Rotate files by encoded bytes and retain only owned segments | WP05-S05 | 4 |
| WP05-S07 | Recover managed JSONL files after interrupted writes or rotation | WP05-S06 | 4 |
| WP05-S08 | Flush accepted boundaries without unbounded coordination | WP05-S03 | 4 |
| WP05-S09 | Flush file buffers after idle intervals | WP05-S05, WP05-S08 | 4 |
| WP05-S10 | Stop and dispose through one bounded shutdown operation | WP05-S07, WP05-S09 | 5 |
| WP06-S01 | Map Microsoft logging into the core event contract | WP05-S10 | 5 |
| WP06-S02 | Preserve host scopes, independent filters, and runtime ownership | WP06-S01 | 5 |
| WP06-S03 | Make the bridge a verified optional package | WP06-S02 | 5 |
| WP07-S01 | Complete functional families and per-assembly coverage | WP06-S03 | 5 |
| WP07-S02 | Complete multi-level architecture inspection and negative controls | WP06-S03 | 4 |
| WP07-S03 | Implement full mutation qualification and conservative result validation | WP07-S01 | 6 |
| WP07-S04 | Implement all four cumulative compatibility layers | WP06-S03 | 6 |
| WP07-S05 | Implement the exact-candidate release pipeline | WP07-S02, WP07-S03, WP07-S04 | 6 |
| WP07-S06 | Retain replay prerequisites and validate complete release evidence | WP07-S05 | 6 |
| WP08-S01 | Teach human and agent logging with runnable entry examples | WP07-S06 | 6 |
| WP08-S02 | Demonstrate file collection and independent destination levels | WP08-S01 | 5 |
| WP08-S03 | Teach concurrent context and observable failure handling | WP08-S01 | 5 |
| WP08-S04 | Demonstrate host integration and prepare a preserved consumer | WP08-S01 | 5 |
| WP09-S01 | Freeze release inputs and prepare the first historical baseline | WP08-S02, WP08-S03, WP08-S04 | 5 |
| WP09-S02 | Run final release qualification from clean frozen inputs | WP09-S01 | 5 |
| WP09-S03 | Assemble and validate the complete release bundle | WP09-S02 | 5 |
| WP09-S04 | Publish the verified release and register its historical baseline | WP09-S03 | 5 |

## 5 Detailed stories and tasks

### WP-01 Repository foundation

4 required stories and 16 tasks. The guide's corresponding work package defines the completion boundary.

#### WP01-S01 Establish package identities and supported framework contracts

As a library maintainer, I want identities and supported targets fixed before publication so consumers can depend on a stable upgrade contract.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Platform maintainer

**Depends on:** None

**Guide sections:** 1, 3, 4, 13, 20  
**Requirements:** REQ-001, REQ-005, REQ-006, REQ-009  
**Business cases:** BC-009, BC-010

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP01-S01-T01 | Inspect the first intended consumer's project target without migrating or changing that application; record whether net10.0 satisfies it and add any necessary older target before publication. |
| WP01-S01-T02 | Choose final package IDs, assembly names, namespaces, and bridge identity using the repository convention; retain the exact EnviormentsOptions type name. |
| WP01-S01-T03 | Record the guide's assembly-version/signing choice and assess intended loader scenarios; distinguish assembly identity from advancing package, file, and informational versions. |
| WP01-S01-T04 | Create separate contract-target and actual execution-matrix records for Windows and Linux, with explicit historical-runtime and roll-forward treatment. |

**Acceptance criteria**

- **WP01-S01-AC01** — The identity record names both shipping packages, their namespaces, signing policy, and all initial target assets without relying on Core shorthand.
- **WP01-S01-AC02** — The inspected consumer target and its source location are recorded; any additional target is in the initial contract before publication.
- **WP01-S01-AC03** — Execution claims cover only recorded Windows/Linux lanes; optional platforms or modes have no unsupported success claim.
- **WP01-S01-AC04** — The target-retention policy prohibits dropping published assets, raising their minimum framework, or rewriting old consumers to satisfy an upgrade.

#### WP01-S02 Create the minimal repository and enforce shipping dependency boundaries

As a core consumer, I want shipping dependencies and responsibility boundaries enforced so the library remains independent of application infrastructure.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Platform maintainer

**Depends on:** WP01-S01

**Guide sections:** 3, 5, 6, 14, 20  
**Requirements:** REQ-005, REQ-008, REQ-009  
**Business cases:** None

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP01-S02-T01 | Create the Core, optional Core.MicrosoftLogging, five functional test-family projects, TestSupport, contracts, compatibility, verification/mutation, examples, and eng structure from section 5. |
| WP01-S02-T02 | Create explicit dependency-direction and approved-package manifests, separating shared-framework core dependencies, the audited bridge closure, and development-only tools. |
| WP01-S02-T03 | Apply nullable/compiler warnings as errors, SDK analyzers, documentation output, deterministic Release builds, formatting rules, and the documented decomposition review triggers. |
| WP01-S02-T04 | Pack the minimal core and inspect its dependency groups/assets and assembly references; add a deliberately forbidden dependency fixture that the initial gate must reject. |

**Acceptance criteria**

- **WP01-S02-AC01** — The minimal core has zero explicit PackageReference items, no larger convenience framework reference, and no third-party runtime assets.
- **WP01-S02-AC02** — The bridge and test/tool dependencies are isolated and have an explicit allowlist; neither can flow back into the core.
- **WP01-S02-AC03** — Release build, documentation/analyzer checks, and dotnet format --verify-no-changes succeed for the legitimate scaffold.
- **WP01-S02-AC04** — The forbidden-dependency fixture makes verification fail with the violated rule identified; no broad suppression is used.

#### WP01-S03 Pin and prove the complete test toolchain early

As a maintainer, I want the pinned toolchain proved against real core behavior early so full verification is feasible and reproducible.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Platform maintainer

**Depends on:** WP01-S02

**Guide sections:** 3, 4, 14, 16, 17, 20  
**Requirements:** REQ-005, REQ-007, REQ-008  
**Business cases:** None

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP01-S03-T01 | Select and commit exact serviced SDK/runtime, stable MSTest/VSTest, Microsoft coverage, API compatibility, and Stryker versions; declare runner selection explicitly. |
| WP01-S03-T02 | Commit global.json, central versions, project lockfiles, local tool manifest, NuGet restore configuration/source mapping, and approved dependency inventory. |
| WP01-S03-T03 | Implement and exercise one small genuine internal core configuration behavior, such as parsing the guide-defined severity tokens, with MSTest, Microsoft line/branch coverage, and isolated Stryker execution; preserve this implementation and its tests for the later resolver story. |
| WP01-S03-T04 | Record nonzero branch and mutation denominators, real tool reports, version output, source identity, and the prerequisites/retention inventory needed to reproduce the smoke run. |

**Acceptance criteria**

- **WP01-S03-AC01** — A fresh restore uses exact committed inputs and the intended VSTest runner; no floating latest or preview runner is required.
- **WP01-S03-AC02** — The known branching behavior produces a nonzero branch denominator with Microsoft's collector/tool rather than an accidentally selected Coverlet collector.
- **WP01-S03-AC03** — Stryker discovers the intended tests and produces a nonempty population and actual mutation report for the genuine core behavior.
- **WP01-S03-AC04** — Stryker exists only as a pinned development CLI and does not appear in shipping project dependencies or runtime assets.

#### WP01-S04 Bootstrap verification commands, test inventories, and deterministic support

As a reviewer, I want one driver to expose discovery and missing work so incomplete scaffolding cannot appear release-ready.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Platform maintainer

**Depends on:** WP01-S03

**Guide sections:** 5, 14, 15, 16, 17, 19, 20  
**Requirements:** REQ-007, REQ-008  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP01-S04-T01 | Create repository-owned entry points for eng/verify developer and release profiles, eng/replay, and eng/check-release with structured output, documented prerequisites, explicit stage exits, and stable report paths. |
| WP01-S04-T02 | Create the six-family manifest, required case-ID inventory, requirement-to-case map, and lane applicability records without marking placeholder cases as passed. |
| WP01-S04-T03 | Add test-only clocks, explicit metadata/UUID inputs, barriers, controllable writers, isolated child-process helpers, and temporary-directory support. |
| WP01-S04-T04 | Add negative controls for missing family/report, missing required case, failed case, unexpected skip, and unrecognized report schema; keep the incomplete release profile nonzero. |

**Acceptance criteria**

- **WP01-S04-AC01** — Developer verification runs the available real checks; release verification reports required missing work and cannot return success for empty scaffolding.
- **WP01-S04-AC02** — Every family has an independently identifiable discovery/execution/report slot, including mutation without inventing a sixth C# test project.
- **WP01-S04-AC03** — Each negative control yields a nonzero result and identifies the absent or invalid evidence.
- **WP01-S04-AC04** — Process support captures stdout/stderr independently and drains them concurrently; concurrency helpers use finite deadlines and signals rather than arbitrary sleeps.

### WP-02 Public contracts

4 required stories and 16 tasks. The guide's corresponding work package defines the completion boundary.

#### WP02-S01 Define immutable startup, identity, and configuration contracts

As an application developer, I want a stable startup and configuration contract so applications initialize the core consistently.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Library maintainer

**Depends on:** WP01-S04

**Guide sections:** 7, 8, 13, 22, 20  
**Requirements:** REQ-002, REQ-003, REQ-004, REQ-006, REQ-010  
**Business cases:** BC-003, BC-006, BC-009, BC-012, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP02-S01-T01 | Define EnvironmentSetup, immutable EnvironmentSnapshot/resolved logging/EventMetadata, EnvironmentKind values, configuration issue types, and EnviormentsOptions signatures/convenience getters. |
| WP02-S01-T02 | Record pre-initialization, failed validation, single successful publication, concurrent initialization, repeated initialization, and no-production-Reset semantics. |
| WP02-S01-T03 | Encode all section 8 keys, aliases, defaults, units, accepted tokens/bounds, precedence, blank handling, invalid higher-priority behavior, and one-time path resolution in machine-readable fixtures. |
| WP02-S01-T04 | Define metadata field types, discovery fallbacks, application-assembly identity, BuildVersion versus ServiceVersion, safe diagnostics, and immutable/copying ownership rules. |

**Acceptance criteria**

- **WP02-S01-AC01** — The public contract retains the requested spelling, named setup properties, immutable resolved outputs, and stable enum values. Narrow atomic-publication/synchronization fields are permitted; mutable static option values or operation context are not.
- **WP02-S01-AC02** — Every documented configuration key has explicit valid, absent/blank, invalid, precedence, and applicable boundary expectations.
- **WP02-S01-AC03** — Fixtures preserve informational build strings, optional unavailable facts, actual process start versus initialization time, and the distinct telemetry release label.
- **WP02-S01-AC04** — Initialization and error contracts are documented and compile-time consumer fixtures cover their public shape; implementation completion is not claimed by those contract checks.

#### WP02-S02 Define event schema, supported values, and portable fixtures

As a structured-log consumer, I want exact event and value contracts so records retain their meaning across applications, upgrades, and later language ports.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Library maintainer

**Depends on:** WP02-S01

**Guide sections:** 9, 13, 22, 20  
**Requirements:** REQ-003, REQ-004, REQ-006, REQ-010  
**Business cases:** BC-001, BC-002, BC-005, BC-012, BC-014, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP02-S02-T01 | Define event input/captured-value/scope data contracts and schema v1 with fixed severity, resource projection, timestamps, required/optional fields, and null/absence rules. |
| WP02-S02-T02 | Define explicit safe integer, integer-string, decimal-string, finite-number, map/array, UUID, and timestamp representations; specify rejection of arbitrary objects. |
| WP02-S02-T03 | Encode every section 9 record, Unicode, node, depth, text, exception, and scope bound, including aggregate encoded-byte accounting and explicit exception truncation. |
| WP02-S02-T04 | Create language-neutral valid/rejected fixture pairs for identity projection, correlation/override rules, nested copying, numeric boundaries, escaping, and schema field evolution. |

**Acceptance criteria**

- **WP02-S02-AC01** — Schema fixtures include all required service/env/version and captured resource fields without serializing configuration or destination objects.
- **WP02-S02-AC02** — Representations are unambiguous across languages: large integers and decimals do not silently switch field types.
- **WP02-S02-AC03** — Boundary fixtures cover valid Unicode and post-escaping record size, with unsupported/oversized events rejected as specified.
- **WP02-S02-AC04** — C# contract validation accepts the valid fixture catalog and rejects the invalid catalog; no TypeScript runtime implementation or hosted collector ingestion is marked as tested.

#### WP02-S03 Define logging, admission, lifecycle, and ownership results

As an application developer, I want precise logging and lifecycle results so I can distinguish filtering, admission, loss, successful drain, and unresolved work.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Library maintainer

**Depends on:** WP02-S02

**Guide sections:** 10, 11, 12, 13, 20  
**Requirements:** REQ-004, REQ-006, REQ-008, REQ-009  
**Business cases:** BC-004, BC-007, BC-008, BC-013, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP02-S03-T01 | Specify the complete CoreLogRuntime/CoreLogger method signatures, named arguments, exceptions, diagnostics, scope, provider ownership, and IAsyncDisposable surface. |
| WP02-S03-T02 | Define immutable write/flush/stop/output result types, frozen enum values, accepted-only sequence numbers, observed timestamps, rejection snapshots, and global invalid-event accounting. |
| WP02-S03-T03 | Encode the argument-validation -> Stopped -> Filtered -> all-faulted rejection -> capture/admission order and the single authoritative severity/deferred-factory rules. |
| WP02-S03-T04 | Document per-output ordering, bounded coordination, flush boundaries, cancellation-of-wait, shared stop deadline, loss observations, accounting identity, and accepted/written/flushed/durable distinctions. |

**Acceptance criteria**

- **WP02-S03-AC01** — Contract samples compile without ambiguous nullable overloads, expanding positional options, or multiple authorities for severity.
- **WP02-S03-AC02** — Decision-table fixtures distinguish all outcomes, including None after closure, partial acceptance, invalid capture, and all-interested-outputs faulted.
- **WP02-S03-AC03** — Result definitions permit Completed with Succeeded=false and Succeeded=true with LossObserved=true; their meanings and observation boundaries are explicit.
- **WP02-S03-AC04** — Cancellation and disposal cannot reopen admission or restart the shared shutdown deadline; no API promises disk durability, collector acknowledgement, or cross-output total order.

#### WP02-S04 Establish permanent contract and cumulative baseline governance

As an upgrading consumer, I want every published contract retained and represented in immutable history so new releases preserve supported use.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Library maintainer

**Depends on:** WP02-S03

**Guide sections:** 1, 4, 13, 18, 22, 20  
**Requirements:** REQ-005, REQ-006, REQ-007, REQ-010  
**Business cases:** BC-003, BC-009, BC-010, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP02-S04-T01 | Review and record the public API, parameter names, constants/enums, construction paths, defaults, target assets, ownership, and behavior for indefinite preservation. |
| WP02-S04-T02 | Create the append-only publication/baseline manifest format with package identity membership, version including prerelease, hashes, API metadata, source/binary fixtures, and execution/toolchain records. |
| WP02-S04-T03 | Define all four compatibility layers, historical source substitution rules, unchanged binary replay rules, allowed nondeterministic normalization, and zero-history behavior. |
| WP02-S04-T04 | Create initial compiling source patterns and deliberately incompatible API/wire samples to qualify the later harness; record which contracts each sample covers. |

**Acceptance criteria**

- **WP02-S04-AC01** — The compatibility policy gives no waiver for a major version, newly added obsolete warning, rewritten baseline, or removed target.
- **WP02-S04-AC02** — Historical membership comes from the immutable publication record and includes intermediate releases and published prereleases.
- **WP02-S04-AC03** — The initial manifest accurately represents actual publication history; empty history does not bypass consumer, fixture, or negative-control requirements.
- **WP02-S04-AC04** — The contract fixture set covers named arguments, null/conversion/extension call patterns, supported implementations, configuration defaults, and wire meanings.

### WP-03 Environment resolution

4 required stories and 16 tasks. The guide's corresponding work package defines the completion boundary.

#### WP03-S01 Resolve application and runtime identity

As an application developer, I want the core to identify my application and runtime accurately so that each event can be attributed to the binary and instance that produced it.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP02-S04

**Guide sections:** 4, 7 Captured metadata, 9 Required schema fields, 14 Unit tests  
**Requirements:** REQ-002, REQ-003  
**Business cases:** BC-003, BC-011, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP03-S01-T01 | Implement identity discovery using supplied application metadata and assembly, with entry-assembly fallback only where available; preserve informational-version strings and explicit commit/build IDs. |
| WP03-S01-T02 | Capture process start, host, OS family and description, OS/process architecture, runtime description, and process ID through internal discovery seams. |
| WP03-S01-T03 | Keep actual BuildVersion separate from configurable ServiceVersion and generate initialization identity/time through deterministic test seams. |
| WP03-S01-T04 | Add table-driven discovery tests and small Windows/Linux process fixtures, including missing optional metadata and application/core assemblies with different versions. |

**Acceptance criteria**

- **WP03-S01-AC01** — Fallback identity identifies the application assembly, never the core assembly; prerelease and build suffixes survive unchanged.
- **WP03-S01-AC02** — Unavailable optional metadata stays absent; BuildVersion uses its specified unknown fallback, and process start is never replaced with initialization time.
- **WP03-S01-AC03** — HostName describes the application-visible host; DD_AGENT_HOST does not replace it, and changing a telemetry version override does not change BuildVersion.
- **WP03-S01-AC04** — Tests prove the expected Windows/Linux projection and macOS family recognition without advertising an untested macOS execution lane.

#### WP03-S02 Resolve documented environment settings and output defaults

As an operator, I want environment settings to select output behavior consistently so that the same application build can serve human, agent, and collector use cases.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP02-S04

**Guide sections:** 7 Configuration precedence, 7 Validation and diagnostics, 8, 14 Unit tests  
**Requirements:** REQ-002, REQ-004, REQ-010  
**Business cases:** BC-003, BC-004, BC-006, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP03-S02-T01 | Read only recognized process variables once per resolution, and apply CORE variable, supported DD alias, explicit setup, discovery, and default precedence in that order. |
| WP03-S02-T02 | Reuse the proven configuration-token behavior from WP01-S03 and resolve every setting in the section 8 table, including independent console/file level inheritance, output enablement, explicit format/stream, queue size, flush interval, shutdown timeout, rotation, and schema selection. |
| WP03-S02-T03 | Treat blank values as absent, normalize configuration tokens case-insensitively, and preserve trimmed identity value case; ignore unknown CORE names without dumping process variables. |
| WP03-S02-T04 | Add shared input/output configuration fixtures for defaults, aliases, overrides, disabled outputs, and host environment labels; do not mutate the host's resolved framework environment. |

**Acceptance criteria**

- **WP03-S02-AC01** — Fixtures reproduce every documented section 8 default and precedence step, including CORE overrides of DD identity aliases.
- **WP03-S02-AC02** — A console Warning threshold and file Debug threshold retain the file's Debug interest even when CORE_LOG_LEVEL is Information.
- **WP03-S02-AC03** — Absent file path or file level None disables file output; console level None disables console output; terminal/debugger state never changes format or stream.
- **WP03-S02-AC04** — A telemetry environment label can differ from the supplied host framework environment without changing the latter or adding unrelated runtime settings.

#### WP03-S03 Validate configuration and resolve file paths without side effects

As an application developer, I want complete safe startup validation and stable file paths so that configuration errors are understood before output workers start.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP03-S01, WP03-S02

**Guide sections:** 7 Validation and diagnostics, 8, 9 Values and bounds, 11 File lifecycle and bounds  
**Requirements:** REQ-002, REQ-004, REQ-010  
**Business cases:** BC-003, BC-006, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP03-S03-T01 | Validate enums, explicit tokens, invariant numeric parsing, all section 8 ranges, supported schema selection, and relevant cross-option relationships. |
| WP03-S03-T02 | Validate identity string lengths and control-character rules; collect issues into the immutable CoreConfigurationException issue collection with stable codes and keys. |
| WP03-S03-T03 | Capture the initialization working directory and resolve relative CORE_LOG_FILE_PATH values against it once; validate path syntax without creating directories or opening files. |
| WP03-S03-T04 | Add boundary and combined-error fixtures, including invalid high-priority input, provided settings for disabled outputs, Unicode, changed working directory, and safe error text. |

**Acceptance criteria**

- **WP03-S03-AC01** — Invalid selected high-priority input fails instead of falling through to a lower-priority value, and recognized supplied output settings are validated even for a disabled output.
- **WP03-S03-AC02** — Multiple configuration problems appear in one immutable issue collection with stable codes and keys; diagnostics do not expose unrelated variables or credentials.
- **WP03-S03-AC03** — A relative path is exposed as the expected absolute path, and changing the process working directory after resolution cannot redirect it.
- **WP03-S03-AC04** — Validation and environment property reads create no directories, open no output, start no worker, and contact no infrastructure.

#### WP03-S04 Publish the immutable EnviormentsOptions snapshot once

As a core consumer, I want one immutable startup snapshot so that every component observes the same validated environment and identity.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP03-S03

**Guide sections:** 7 Public shape, 7 Initialization semantics, 7 Captured metadata, 14 Unit tests  
**Requirements:** REQ-002, REQ-003  
**Business cases:** BC-006, BC-011, BC-012

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP03-S04-T01 | Implement EnviormentsOptions.Initialize, Current, IsInitialized, and the documented convenience getters with a trivial CLR static constructor. |
| WP03-S04-T02 | Copy startup input and nested mutable collections into owned immutable resolved options and EventMetadata, then publish only the fully validated snapshot. |
| WP03-S04-T03 | Synchronize concurrent initialization so one successful publisher wins, preserve the snapshot after later calls, and allow a failed explicit validation attempt to publish nothing. |
| WP03-S04-T04 | Add isolated child-process tests for pre-initialization reads, failed-then-valid initialization, concurrent calls, mutation of setup after initialization, and separate application runs. |

**Acceptance criteria**

- **WP03-S04-AC01** — Current and convenience getters throw the documented InvalidOperationException before successful initialization; failure leaves IsInitialized false.
- **WP03-S04-AC02** — Concurrent valid initialization attempts produce exactly one successful publication; later attempts cannot replace it.
- **WP03-S04-AC03** — Current and all getters remain consistent after caller input mutation; InstanceId is stable during a run and differs between independently initialized application processes.
- **WP03-S04-AC04** — No production Reset API, mutable global operation context, global exception subscription, or output lifecycle side effect is introduced.

### WP-04 Event capture and logging API

5 required stories and 21 tasks. The guide's corresponding work package defines the completion boundary.

#### WP04-S01 Capture supported structured values within fixed bounds

As an event producer, I want supported structured values to be copied and bounded so that later caller mutations cannot alter recorded facts.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP02-S04

**Guide sections:** 9 Values and bounds, 10 Small core surface, 14 Unit tests, 22  
**Requirements:** REQ-003, REQ-010  
**Business cases:** BC-012, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP04-S01-T01 | Implement null, boolean, string, finite floating-point, safe-range integer, explicit large-integer string, invariant decimal-string, array, and string-keyed map representations. |
| WP04-S01-T02 | Normalize supported UUID/time scalars to the contract representation; reject arbitrary objects without reflection serialization or arbitrary ToString calls. |
| WP04-S01-T03 | Recursively copy input maps and arrays while enforcing depth 8, 1024 total attribute nodes, key/control rules, and shared Unicode-scalar validation helpers. |
| WP04-S01-T04 | Add language-neutral positive/negative fixtures for numeric boundaries, nonfinite numbers, decimals, nested mutation, oversized structures, and Unicode/control input. |

**Acceptance criteria**

- **WP04-S01-AC01** — Integer-number input outside inclusive -9007199254740991 through 9007199254740991 is rejected; explicit integer strings and decimal strings preserve their documented type.
- **WP04-S01-AC02** — Mutating any original nested collection after capture cannot alter owned values; unsupported or cyclic/excessively nested input terminates with a bounded rejection.
- **WP04-S01-AC03** — The attribute root counts as depth 1 and one node, and boundary fixtures distinguish exactly-at-limit from over-limit input.
- **WP04-S01-AC04** — No input automatically changes JSON type as its magnitude grows, and rejected input is available to the caller as a stable capture error for InvalidEvent accounting.

#### WP04-S02 Project exceptions into bounded immutable event data

As an operator, I want useful bounded exception detail so that a failure is understandable without retaining arbitrary mutable exception data.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP04-S01

**Guide sections:** 9 Required schema fields, 9 Values and bounds, 9 Correlation and exceptions, 14 Unit tests  
**Requirements:** REQ-003, REQ-010  
**Business cases:** BC-001, BC-014, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP04-S02-T01 | Project exception type, message, optional stack trace, and inner exceptions into the section 9 shape at capture time. |
| WP04-S02-T02 | Preserve AggregateException child order and enforce 16 nodes, inner depth 4, 4096 scalar values per exception message, and 16384 per stack trace. |
| WP04-S02-T03 | Mark projections with truncated=true only where exception content was clipped; never retain Exception.Data or arbitrary exception properties. |
| WP04-S02-T04 | Add deterministic fixtures for single/aggregate/nested exceptions, all limits, Unicode clipping, and exclusion of mutable data. |

**Acceptance criteria**

- **WP04-S02-AC01** — Single inner exceptions use an array of one; aggregate children retain their original order and the required type/message fields.
- **WP04-S02-AC02** — Every exception-specific limit is enforced during traversal, with a truncation marker where required rather than malformed or incomplete JSON.
- **WP04-S02-AC03** — Changing Exception.Data after capture has no effect, and projected records contain no automatic HTTP body/header or environment-variable capture.
- **WP04-S02-AC04** — The result is an owned immutable projection with no retained mutable exception graph; its truncation and size information is available to the later aggregate event capture check in WP04-S04.

#### WP04-S03 Implement operation scopes across asynchronous work

As an application developer, I want operation context to follow asynchronous work so that concurrent jobs keep distinct identifiers and attributes.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP04-S01

**Guide sections:** 9 Correlation and exceptions, 10 Small core surface, 14 Unit tests  
**Requirements:** REQ-003, REQ-010  
**Business cases:** BC-005, BC-012, BC-013

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP04-S03-T01 | Implement runtime-specific asynchronous scope storage and BeginScope using copied OperationContextData and the supported value model. |
| WP04-S03-T02 | Apply outer-to-inner precedence, enforce maximum scope depth 32, and expose a captured merged scope view for event creation. |
| WP04-S03-T03 | Implement LIFO disposal in the owning logical flow, harmless repeated disposal, and an out-of-order error that leaves scope state unchanged. |
| WP04-S03-T04 | Add barrier-driven tests for nested scopes, child-task inheritance, two concurrent operations, independent runtimes, cancellation, input mutation, and invalid disposal order. |

**Acceptance criteria**

- **WP04-S03-AC01** — Concurrent asynchronous operations do not exchange operation IDs or attributes; separate runtime instances keep separate scope chains.
- **WP04-S03-AC02** — Inner values override outer values, scope input is immutable after BeginScope, and the 33rd nested scope is rejected as specified.
- **WP04-S03-AC03** — Out-of-order disposal throws InvalidOperationException without changing the active scope; repeated correct-flow disposal is harmless.
- **WP04-S03-AC04** — Documentation states normal execution-context inheritance and prohibits passing scope handles to another concurrent flow for disposal.

#### WP04-S04 Create complete immutable events with metadata and W3C correlation

As an event consumer, I want complete immutable event records so that identity, context, and trace correlation describe the actual logging call.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP03-S04, WP04-S02, WP04-S03

**Guide sections:** 9, 10 Small core surface, 11 JSONL output, 14 Unit tests  
**Requirements:** REQ-003, REQ-010  
**Business cases:** BC-005, BC-012, BC-014, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP04-S04-T01 | Implement the allowlisted event/resource projection from EnvironmentSnapshot, scope context, method severity, and LogEventData; keep configuration and destinations out of event metadata. |
| WP04-S04-T02 | Apply explicit-event-over-scope attribute and OperationId precedence, trim optional identifiers, and reserve identity and trace/span fields against free-form overrides. |
| WP04-S04-T03 | Capture valid W3C Activity.Current IDs and UTC time before admission; omit both IDs for absent/invalid/non-W3C activity and preserve the separate operation ID. |
| WP04-S04-T04 | Implement the shared bounded contract encoder/size validation, including UTF-8 escaping, exact timestamp representation, field length/control rules, and the 64 KiB JSONL cap including LF. |
| WP04-S04-T05 | Add structural fixtures for every field/null/severity mapping, delayed consumption after scope/activity changes, child activities, concurrent operations, and aggregate-size rejection. |

**Acceptance criteria**

- **WP04-S04-AC01** — All required identity fields and every available allowlisted resource field appear; optional unavailable values are omitted and explicit attribute null remains JSON null.
- **WP04-S04-AC02** — Captured trace IDs have 32 lowercase hexadecimal characters and span IDs have 16, neither all zero; later Activity or scope changes cannot change queued evidence.
- **WP04-S04-AC03** — Explicit attributes and nonblank event OperationId win the documented scope precedence, while reserved identity/correlation stays unchanged.
- **WP04-S04-AC04** — Timestamp and numeric/string types match the shared schema; record size is checked after escaping, and an oversized event is rejected globally before any output admission.
- **WP04-S04-AC05** — Messages/templates retain supplied text within their limits, ordinary oversized input is rejected, and only the documented exception projection may clip content.

#### WP04-S05 Implement logger filtering, deferred capture, and write outcomes

As an application developer, I want predictable filtering and write outcomes so that expensive diagnostics are deferred and admission results are truthful.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP04-S04

**Guide sections:** 8, 10 Small core surface, 12 Admission and independent workers, 15  
**Requirements:** REQ-003, REQ-004  
**Business cases:** BC-004, BC-007, BC-015, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP04-S05-T01 | Implement runtime-bound CreateLogger, IsEnabled, Write, and WriteLazy against an internal output-admission seam; use method severity as the sole level authority. |
| WP04-S05-T02 | Implement the exact argument-validation, Stopped, Filtered, all-interested-faulted Rejected, capture, and admission decision order. |
| WP04-S05-T03 | Evaluate an interested lazy factory at most once, return global InvalidEvent for capture rejection, and map per-output outcomes into Accepted/PartiallyAccepted/Rejected without inventing sequence numbers. |
| WP04-S05-T04 | Add outcome-table tests with controllable output-interest/admission doubles, including None after stop, disabled outputs, differing thresholds, faults, queue-full results, and throwing factories. |

**Acceptance criteria**

- **WP04-S05-AC01** — Stopped, Filtered, and all-faulted early rejection do not traverse content or invoke the factory; a valid None call after admission closes returns Stopped.
- **WP04-S05-AC02** — A file interested in Debug causes one lazy evaluation even when console is Warning; full queues never trigger a second factory invocation.
- **WP04-S05-AC03** — Accepted/PartiallyAccepted/Rejected and output statuses match actual admission attempts; only successful admissions receive sequence numbers and InvalidEvent has no output admission entries.
- **WP04-S05-AC04** — IsEnabled is not represented as a capacity reservation; caller factory exceptions retain their own failure, and normal output I/O failures cannot escape through logging calls.

### WP-05 Outputs and lifecycle

10 required stories and 41 tasks. The guide's corresponding work package defines the completion boundary.

#### WP05-S01 Render the same event as JSONL or readable human output

As a person or machine consuming logs, I want selectable human and JSONL representations of the same event so that presentation changes preserve its meaning.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP04-S04

**Guide sections:** 9, 11 Human output, 11 JSONL output, 14 Unit tests  
**Requirements:** REQ-003, REQ-004, REQ-010  
**Business cases:** BC-001, BC-002, BC-003, BC-014, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S01-T01 | Reuse the bounded schema encoder for UTF-8 JSONL with no BOM and exactly one LF-terminated physical line per record. |
| WP05-S01-T02 | Implement normal and verbose human renderers with the specified fields, available context, exception detail, and control-sequence escaping. |
| WP05-S01-T03 | Keep rendering separate from threshold and destination selection; do not infer format from terminal/debugger state or add a message-template parser. |
| WP05-S01-T04 | Add structural JSON fixtures and human-information assertions covering Unicode, newlines, exceptions, empty optional fields, and independent presentation settings. |

**Acceptance criteria**

- **WP05-S01-AC01** — Each JSONL line independently parses and preserves required identity, severity, field types, optional omission, and metadata without banners, ANSI, raw multiline text, or a BOM.
- **WP05-S01-AC02** — Verbose human output includes available application/build/environment, operation, attributes, and exception detail; normal output includes timestamp, severity, component, event name, and message.
- **WP05-S01-AC03** — Changing detail or format never changes filtering or captured facts; untrusted text cannot inject terminal control commands or fake record boundaries.
- **WP05-S01-AC04** — Comparers normalize only documented nondeterministic fields and do not treat JSON property order or human spacing as an accidental permanent contract.

#### WP05-S02 Admit events through independent bounded output queues

As an application developer, I want independent bounded output admission so that slow I/O cannot block producer calls or silently evict accepted work.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP04-S05

**Guide sections:** 8, 10 Small core surface, 12 Admission and independent workers, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-004, BC-007, BC-012

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S02-T01 | Create one bounded Channel and sequential worker per enabled output, using Wait full mode with nonblocking TryWrite, SingleReader=true, SingleWriter=false, and AllowSynchronousContinuations=false. |
| WP05-S02-T02 | Fan out one captured immutable event and synchronize successful admission, accepted tickets, and lifecycle checks without waiting for queue capacity or performing writer I/O under the admission lock. |
| WP05-S02-T03 | Reject incoming events at a full output, preserve previously admitted work, and produce independent output results without promising a total order across destinations. |
| WP05-S02-T04 | Add barrier-driven multi-producer, saturation, blocked-writer, and output-threshold tests using controllable writers and bounded worker bookkeeping. |

**Acceptance criteria**

- **WP05-S02-AC01** — A saturated output returns QueueFull for the incoming event and never evicts an older accepted event; accepted tickets exist only for successful admission.
- **WP05-S02-AC02** — A blocked file output does not block console admission or reorder console delivery; accepted records retain each output's admission order.
- **WP05-S02-AC03** — Queue capacity and outstanding worker bookkeeping remain within the documented bounds under bursts and concurrent producers.
- **WP05-S02-AC04** — Mixed destination outcomes produce the correct partial-acceptance result, and no enqueue result is described as a delivery or durability acknowledgment.

#### WP05-S03 Account for delivery and isolate terminal output failures

As an operator, I want truthful delivery accounting and output fault isolation so that loss and unresolved work remain visible.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S02

**Guide sections:** 10 Small core surface, 12 Delivery accounting, 12 Stop and disposal, 14 Unit tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-008, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S03-T01 | Maintain cumulative per-output filtered/attempted/accepted/rejected/written/failed/abandoned counters, nonnegative pending/in-flight gauges, and the runtime-wide InvalidEvent count. |
| WP05-S03-T02 | Update delivery only after complete success is confirmed, capture consistent observation boundaries, and retain partial/uncertain writes as failed or pending according to their actual terminal state. |
| WP05-S03-T03 | Implement Running/Faulted/Stopping/Stopped output transitions; terminal I/O failure closes admission, records the in-flight outcome, abandons unattempted queued work, and retains fault information. |
| WP05-S03-T04 | Expose immutable GetDiagnostics snapshots separately from normal routing, without recursive retries; add fault/partial-write/blocked-write and counter-invariant tests with controllable writers. |

**Acceptance criteria**

- **WP05-S03-AC01** — Every consistently captured boundary satisfies Accepted = Written + Failed + Abandoned + Pending; dequeuing alone never increments Written.
- **WP05-S03-AC02** — InvalidEvent increments the global counter without an output admission or sequence; pending gauges can decrease while cumulative counters never reset or decrease.
- **WP05-S03-AC03** — One terminally failed output rejects new work and abandons unattempted accepted events while other outputs continue; v1 does not automatically retry a possibly partial write.
- **WP05-S03-AC04** — Diagnostics remain available after fault/stop, preserve the last fault, and cannot create a recursive log loop, unbounded retry queue, or unbounded diagnostic tasks.

#### WP05-S04 Integrate configured console output with runtime ownership

As a CLI or service developer, I want configured console logging so that diagnostics use the intended stream and preserve application output.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S01, WP05-S03

**Guide sections:** 8, 10 Small core surface, 11 JSONL output, 11 File lifecycle and bounds, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-001, BC-002, BC-003, BC-007, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S04-T01 | Wire console enablement, format, detail, and stream from the immutable snapshot into runtime composition and the independent output worker. |
| WP05-S04-T02 | Implement borrowed stream ownership and serialized writes for the console output; preserve default stderr and explicit stdout selection. |
| WP05-S04-T03 | Implement cleanup of acquired runtime resources if console startup fails, without disposing borrowed streams; file startup joins this composition in WP05-S05. |
| WP05-S04-T04 | Add test consumer processes that capture and concurrently drain stdout/stderr, verify all-disabled startup, and exercise a blocked or failed console seam. |

**Acceptance criteria**

- **WP05-S04-AC01** — Default JSONL CLI diagnostics appear on stderr while stdout remains a valid command result; explicit stdout selection is respected without format guessing.
- **WP05-S04-AC02** — No ordinary records appear when all outputs are disabled, and logger early-exit outcomes remain those defined in section 10.
- **WP05-S04-AC03** — Runtime cleanup does not dispose borrowed standard streams, and one blocked console writer does not block an independently enabled output worker.
- **WP05-S04-AC04** — The same compiled test consumer switches among configured formats/streams through environment settings and preserves event identity.

#### WP05-S05 Initialize an exclusively owned file output and managed inventory

As an operator, I want exclusive file ownership and safe startup so that concurrent instances and configuration changes cannot corrupt existing records.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S04

**Guide sections:** 8, 11 File lifecycle and bounds, 12 Admission and independent workers, 14 Integration tests  
**Requirements:** REQ-002, REQ-004, REQ-009  
**Business cases:** BC-003, BC-007, BC-011, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S05-T01 | Implement file output creation from the captured absolute path; create directories and acquire exclusive active-file/rotation-group ownership only during output initialization. |
| WP05-S05-T02 | Create or inspect managed inventory containing segment/group identity, format, and encoding; reject nonempty existing segments with missing or incompatible metadata before modifying content. |
| WP05-S05-T03 | Wire file and console outputs into runtime startup so any enabled-file initialization failure stops newly created workers and releases handles/ownership without deleting pre-existing segments. |
| WP05-S05-T04 | Add two-process Windows/Linux ownership tests and startup cases for relative-path capture, Unicode paths, missing directories, permissions, format switches, missing inventory, and rollback. |

**Acceptance criteria**

- **WP05-S05-AC01** — A second process targeting the same group fails initialization clearly; processes with distinct paths do not interleave or corrupt each other's output.
- **WP05-S05-AC02** — Existing nonempty data with missing/incompatible format inventory remains unchanged and fails clearly; a newly created empty group records its format before use.
- **WP05-S05-AC03** — Later working-directory changes cannot redirect the writer, and files/directories are untouched until runtime output initialization begins.
- **WP05-S05-AC04** — Failed enabled-file startup fails runtime creation, releases newly acquired resources, preserves pre-existing active/retained segments and borrowed streams, and never redirects silently to another directory.

#### WP05-S06 Rotate files by encoded bytes and retain only owned segments

As an operator, I want bounded file rotation and retention so that complete records are preserved within the owned file group.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S05

**Guide sections:** 8, 11 File lifecycle and bounds, 12 Delivery accounting, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-011, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S06-T01 | Track actual encoded record and active-segment byte counts; rotate before the next complete record would exceed FileMaxBytes. |
| WP05-S06-T02 | Maintain deterministic segment identity/order in the managed inventory across restarts and enforce FileRetainedFiles closed segments plus the active segment. |
| WP05-S06-T03 | Delete only exact owned-group closed segments, fault/pause before further growth when required retention deletion fails, and report oversized human records as terminal render-too-large outcomes without repeated rotation. |
| WP05-S06-T04 | Add real-filesystem and controlled-failure tests for exact byte boundaries, multibyte text, retention limits, naming collisions, permissions, and unrelated neighboring files. |

**Acceptance criteria**

- **WP05-S06-AC01** — No record is split between segments; exact-limit and one-byte-over cases rotate correctly using encoded bytes, including line termination.
- **WP05-S06-AC02** — Normal operation retains at most the configured number of closed segments plus active, and deterministic ordering survives a restart.
- **WP05-S06-AC03** — A failed required deletion prevents creation of additional segments that would violate retention; other outputs continue and the fault remains observable.
- **WP05-S06-AC04** — Retention never deletes another instance's files, unrelated files, or archives outside the exact rotation group; a too-large human record does not create repeated empty segments.

#### WP05-S07 Recover managed JSONL files after interrupted writes or rotation

As an operator, I want deterministic restart recovery so that damaged records are preserved separately and fresh JSONL remains valid.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S06

**Guide sections:** 11 File lifecycle and bounds, 12 Delivery accounting, 12 Stop and disposal, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-011, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S07-T01 | Inspect the owned active segment's final record boundary after validating its managed format/encoding identity. |
| WP05-S07-T02 | Move a partial-record segment into a uniquely identified damaged segment and start a fresh active segment; retain damage status in inventory and safe diagnostics. |
| WP05-S07-T03 | Include damaged segments in retention and define deterministic handling for interruption points in rotation/inventory updates, failing clearly when safe reconciliation is impossible. |
| WP05-S07-T04 | Add restart fixtures for intact JSONL, partial UTF-8/JSON records, interrupted rotation stages, name collisions, deletion failure, and collector selection that excludes damaged segments. |

**Acceptance criteria**

- **WP05-S07-AC01** — Startup never appends a fresh JSON object directly after a partial record; the damaged segment survives with a unique recorded identity.
- **WP05-S07-AC02** — New active output consists of complete parseable JSONL records, while damaged segments are distinguishable from normal collector input.
- **WP05-S07-AC03** — Damaged segments count toward the same retention limit, and required retention failure prevents additional segment growth.
- **WP05-S07-AC04** — Interruption fixtures either recover to the documented consistent state or fail clearly without changing unrelated/pre-existing evidence; no crash-durable or exactly-once ingestion guarantee is claimed.

#### WP05-S08 Flush accepted boundaries without unbounded coordination

As an application developer, I want a bounded flush observation so that later traffic or blocked output cannot extend the requested completion boundary indefinitely.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S03

**Guide sections:** 10 Small core surface, 12 Flush semantics, 12 Delivery accounting, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-008, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S08-T01 | Capture per-output accepted boundaries and initial rejection/global-invalid observations under the admission coordination used to register nondroppable flush requests. |
| WP05-S08-T02 | Serialize flush with writes, coalesce the same boundary, retain at most one coordination record per outstanding boundary plus the completed boundary being flushed, and detach caller waits on completion/timeout/cancellation. |
| WP05-S08-T03 | Implement positive finite timeout validation, monotonic TimeProvider deadlines, cancellation of caller waits, immutable ObservedAtUtc results, and truthful completion/success/loss semantics. |
| WP05-S08-T04 | Add concurrent-admission, saturated-queue, clock-correction, failed-flush, and blocked-writer stress tests that assert coordination bounds as well as event counts. |

**Acceptance criteria**

- **WP05-S08-AC01** — Events admitted after a flush boundary do not extend its target; a full event queue cannot drop its flush request or prevent a finite timeout result.
- **WP05-S08-AC02** — Cancelled waits throw OperationCanceledException without erasing shared coordination; repeated cancelled/timed-out calls do not accumulate unbounded worker controls or retained waiters.
- **WP05-S08-AC03** — Results accurately distinguish Completed/TimedOut, Succeeded, LossObserved, terminal counts, pending gauges, and initial global-invalid/rejection observations; successful drain can coexist with earlier observed rejection.
- **WP05-S08-AC04** — UTC clock changes do not alter the elapsed deadline; writer flush remains serialized with writes and is documented as userspace flushing, not fsync or collector acknowledgment.

#### WP05-S09 Flush file buffers after idle intervals

As an operator, I want periodic flushing during idle traffic so that the final buffered record is scheduled for output without another event arriving.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S05, WP05-S08

**Guide sections:** 8, 12 Flush semantics, 12 Stop and disposal, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-008, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S09-T01 | Schedule regular file-buffer flushes from CORE_LOG_FLUSH_INTERVAL_MS using the internal TimeProvider seam, including periods with no new events. |
| WP05-S09-T02 | Integrate timer requests with the bounded serialized flush coordinator rather than launching independent overlapping I/O tasks. |
| WP05-S09-T03 | Carry timer flush failures into the same output lifecycle and diagnostics, and coordinate timer cancellation with the shared stop operation. |
| WP05-S09-T04 | Add controllable-time tests for one event followed by idle time, continuous traffic, slow/blocked flush, timer failure, and no duplicate work after stopping. |

**Acceptance criteria**

- **WP05-S09-AC01** — After one written event followed by idle time, advancing the configured interval schedules a file-buffer flush without requiring another log event.
- **WP05-S09-AC02** — Timer, explicit flush, and writes never execute concurrently on the same writer; blocked I/O delays completion but does not spawn accumulating timer tasks.
- **WP05-S09-AC03** — The interval is reported as a scheduling target, not a completion/durability guarantee; file flush failure remains visible and other outputs continue.
- **WP05-S09-AC04** — Stopped/faulted outputs do not restart periodic work, and timer behavior is verified without arbitrary sleeps as proof of correctness.

#### WP05-S10 Stop and dispose through one bounded shutdown operation

As an application developer, I want one bounded shared shutdown so that repeated stop and disposal calls report an honest outcome without restarting the deadline.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Core developer

**Depends on:** WP05-S07, WP05-S09

**Guide sections:** 10 Small core surface, 11 File lifecycle and bounds, 12 Flush semantics, 12 Stop and disposal, 14 Integration tests  
**Requirements:** REQ-004, REQ-009  
**Business cases:** BC-007, BC-008, BC-011, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP05-S10-T01 | Atomically close admission and capture stop boundaries on the first StopAsync call, start one operation with the configured monotonic overall deadline, and make later calls join it. |
| WP05-S10-T02 | Drain and flush admitted work, cancel periodic scheduling, and at the deadline abandon unstarted queued work, request cancellation of in-flight I/O, and report unresolved writes as pending. |
| WP05-S10-T03 | Implement caller-only wait cancellation, including starting shutdown for an already-cancelled caller, and make DisposeAsync join the same operation without a new drain window. |
| WP05-S10-T04 | Coordinate final handle/ownership release with eventual I/O completion, preserve immutable results and later diagnostics, and avoid disposal beneath unresolved writes or replacement-write loops. |
| WP05-S10-T05 | Add process and barrier-driven tests for normal drain, racing producers/stoppers, repeated disposal, cancelled callers, blocked OS-like I/O, late completion, and abrupt process termination loss boundaries. |

**Acceptance criteria**

- **WP05-S10-AC01** — After admission closes, valid new writes return Stopped, including None; concurrent stop/dispose calls share the original deadline and cannot reopen outputs.
- **WP05-S10-AC02** — Caller cancellation affects only its wait; even an already-cancelled first caller initiates shutdown, and uncancelled joiners can observe its result.
- **WP05-S10-AC03** — Deadline results distinguish abandoned unstarted records from unresolved in-flight work; cancellation is never asserted to prove an OS write stopped.
- **WP05-S10-AC04** — DisposeAsync is idempotent and joins the same operation; unsuccessful drain remains visible through stop results/diagnostics, with no second drain window or deletion of pre-existing log evidence.
- **WP05-S10-AC05** — Returned observations remain immutable if a blocked write later completes; ownership is released only through the documented safe coordination, and tests make no exactly-once/crash-durable promise.

### WP-06 Microsoft logging bridge

3 required stories and 15 tasks. The guide's corresponding work package defines the completion boundary.

#### WP06-S01 Map Microsoft logging into the core event contract

As an application developer using ILogger<T>, I want my existing logging calls to enter the core event pipeline so that structured events and output behavior remain consistent with direct core usage.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Integration developer

**Depends on:** WP05-S10

**Guide sections:** 3, 9, 10, 14, 15  
**Requirements:** REQ-003, REQ-005, REQ-008, REQ-009  
**Business cases:** BC-013, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP06-S01-T01 | Implement CoreLoggerProvider and category loggers in Core.MicrosoftLogging using the core capture/admission path. |
| WP06-S01-T02 | Map levels, categories, EventId.Name, the numeric event ID, the supplied formatted message, and the supplied exception; use log.message when the event name is absent. |
| WP06-S01-T03 | Preserve {OriginalFormat} as message_template and normalize supported structured state through core value handling without a second template parser. |
| WP06-S01-T04 | Apply core interest checks before invoking a supplied formatter or copying state; specify rejection and fault behavior through the approved public contract. |
| WP06-S01-T05 | Add bridge unit tests and process integration fixtures for mappings, absent names, templates, unsupported values, exceptions, and deferred formatter invocation. |

**Acceptance criteria**

- **WP06-S01-AC01** — A standard ILogger event yields the same schema, automatic identity, value encodings, and exception projection as an equivalent direct core event.
- **WP06-S01-AC02** — A named EventId preserves its name; an unnamed EventId yields log.message and retains the supplied numeric event identifier in its documented attribute.
- **WP06-S01-AC03** — Filtered events do not call the formatter or traverse structured state; accepted events preserve the supplied rendered message and available original template.
- **WP06-S01-AC04** — Unsupported or oversized state follows core rejection/accounting rules, with no partial event admitted to an output.
- **WP06-S01-AC05** — The new bridge references the core; the core's public API and compiled dependency graph remain independent of Microsoft.Extensions types.

**Completion artifacts:** Bridge mapping test reports, representative structured fixtures, and inspected project/compiled dependency graphs.

#### WP06-S02 Preserve host scopes, independent filters, and runtime ownership

As a host application developer, I want provider integration to preserve my operation context and the runtime's lifetime so that host logging cannot silently suppress required file diagnostics or stop a shared pipeline.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Integration developer

**Depends on:** WP06-S01

**Guide sections:** 9, 10, 12, 15  
**Requirements:** REQ-003, REQ-004, REQ-008, REQ-009  
**Business cases:** BC-004, BC-005, BC-008, BC-013

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP06-S02-T01 | Integrate provider-created scopes and the host external scope provider using the core's copied, bounded scope representation. |
| WP06-S02-T02 | Map recognized string operation_id scope state into the typed operation ID and enforce nested/event precedence while protecting identity and Activity-derived correlation fields. |
| WP06-S02-T03 | Document and exercise host filter configuration broad enough for the most verbose enabled core destination. |
| WP06-S02-T04 | Implement the documented non-owning default so provider disposal releases its own resources without stopping the shared runtime; keep explicit core stop/disposal with the host application. |
| WP06-S02-T05 | Add concurrent asynchronous scope tests, Debug-file/Warning-console regression tests, shared-runtime disposal tests, and explicit host shutdown integration tests. |

**Acceptance criteria**

- **WP06-S02-AC01** — Two asynchronous operations keep distinct operation attributes through both provider-created and externally supplied scopes.
- **WP06-S02-AC02** — Event-specific and nested scope precedence matches the core contract; free-form state cannot overwrite automatic identity or Activity trace/span IDs.
- **WP06-S02-AC03** — With the documented host configuration, Debug events reach the file while the console emits only Warning and above; unwanted formatter work remains skipped.
- **WP06-S02-AC04** — Disposing the default provider leaves direct core logging and other runtime consumers usable until the application explicitly stops the runtime.
- **WP06-S02-AC05** — Host shutdown finishes application work, stops the runtime, and observes the actual drain result; repeated stop/disposal follows the core's idempotent lifecycle rules.

**Completion artifacts:** Host integration reports covering external scopes, filtering, concurrent flows, ownership, and shutdown.

#### WP06-S03 Make the bridge a verified optional package

As a core consumer and release maintainer, I want the Microsoft integration distributed independently and fully verified so that consumers choose it deliberately without weakening release assurance.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Integration developer

**Depends on:** WP06-S02

**Guide sections:** 3, 13, 14, 16, 18, 19, 23  
**Requirements:** REQ-005, REQ-006, REQ-007, REQ-008, REQ-009  
**Business cases:** BC-009, BC-010, BC-013

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP06-S03-T01 | Package Core.MicrosoftLogging separately and pin its approved Microsoft logging abstraction dependency closure using the framework and versions selected in WP-01. |
| WP06-S03-T02 | Inspect the bridge restore graph, compiled references, and package contents; add rejection controls for an unapproved transitive dependency and dependency leakage into Core. |
| WP06-S03-T03 | Wire bridge unit/integration coverage and a separate Stryker mutation invocation into the shipping-project verification matrix; complete full matrix execution in WP-07. |
| WP06-S03-T04 | Add bridge API/source/binary contract fixtures to the compatibility inventory, including old bridge plus candidate core and full bridge/core upgrade scenarios for historical releases. |
| WP06-S03-T05 | Include bridge API documentation, packages, dependency inventory, reports, and hashes in the first-release verification bundle manifest. |

**Acceptance criteria**

- **WP06-S03-AC01** — An application restoring only Core acquires no bridge, logging abstraction package, or bridge transitive dependency.
- **WP06-S03-AC02** — The bridge's resolved and packed dependency closure matches the explicit allowlist; its negative controls reject prohibited changes.
- **WP06-S03-AC03** — WP-07 discovers the bridge as a separate shipping assembly for coverage and mutation, with no reliance on the Core-only report as bridge evidence.
- **WP06-S03-AC04** — Compatibility manifests include bridge consumers and both package-replacement upgrade paths; no first-release result is mislabeled as an existing historical-release replay.
- **WP06-S03-AC05** — The verification bundle inventory includes the exact bridge candidate and all applicable validation material; consumer optionality does not omit bridge verification.

**Completion artifacts:** Inspected package manifests, dependency negative controls, verification matrix entries, and compatibility/bundle inventories.

### WP-07 Verification completion

6 required stories and 33 tasks. The guide's corresponding work package defines the completion boundary.

#### WP07-S01 Complete functional families and per-assembly coverage

As a maintainer, I want observable functional cases and per-assembly coverage completed so all promised behaviors have current evidence.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP06-S03

**Guide sections:** 7, 8, 9, 10, 11, 12, 14, 15, 16, 17, 20  
**Requirements:** REQ-001 through REQ-010  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S01-T01 | Reconcile behavior-built tests with every section 14 area and BC-001 through BC-016; add missing observable cases rather than postponing basic tests until this work package. |
| WP07-S01-T02 | Complete real candidate-consumer process/stream/file tests for output, restart, failure isolation, exclusive ownership, rotation recovery, burst load, and lifecycle boundaries. |
| WP07-S01-T03 | Complete deterministic concurrency tests for blocked I/O, bounded flush coordination, wall-clock corrections, stop races, already-cancelled waits, pending gauges, and invalid-event loss. |
| WP07-S01-T04 | Collect Microsoft line/branch coverage per handwritten shipping assembly and in aggregate; publish source inventories, denominators, and only permitted exclusions. |
| WP07-S01-T05 | Reconcile required discovery, lane applicability, outcomes, and supporting technical tests against the requirement/case inventory. |

**Acceptance criteria**

- **WP07-S01-AC01** — All required applicable unit/integration/business cases execute and pass, including every BC ID; an unexpected skip or undiscovered required case fails verification.
- **WP07-S01-AC02** — Core and the shipping Microsoft bridge each reach at least 95% line and 90% branch coverage, with nonzero appropriate denominators and published aggregate results.
- **WP07-S01-AC03** — Integration consumers restore actual candidate packages, and process logs record the loaded package/assembly identity used by the case.
- **WP07-S01-AC04** — Concurrency/failure cases prove accounting and resource bounds with controlled signals and deadlines; JSONL assertions do not claim hosted ingestion.

#### WP07-S02 Complete multi-level architecture inspection and negative controls

As a core consumer, I want dependency and architecture rules checked against actual packages and binaries so runtime dependencies cannot leak through packaging.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP06-S03

**Guide sections:** 3, 5, 6, 14, 16, 19, 20  
**Requirements:** REQ-002, REQ-005, REQ-007, REQ-008, REQ-009  
**Business cases:** BC-012, BC-013

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S02-T01 | Inspect evaluated project/framework references, each target's locked dependency graph, package dependency groups/assets, compiled references/public APIs, and internal type/namespace dependencies. |
| WP07-S02-T02 | Assert core-only framework dependencies and the exact bridge/tool allowlists, with no dependency on examples, tests, host orchestration, persistence, engines, or collector SDKs. |
| WP07-S02-T03 | Check immutable resolved configuration and caller-collection ownership, allowing only the narrow synchronization/publication state required by EnviormentsOptions; enforce contract-to-output separation and reject unwanted content, build-transitive scripts, or test binaries in packages. |
| WP07-S02-T04 | Add isolated fixtures that violate each material checker level and verify failure reporting; include controls whenever the architecture harness changes. |

**Acceptance criteria**

- **WP07-S02-AC01** — Every shipping target and assembly is enumerated independently of candidate self-reporting; all five inspection levels return zero real violations.
- **WP07-S02-AC02** — The core remains free of explicit NuGet references and third-party runtime dependencies, and the bridge's actual closure matches the versioned allowlist.
- **WP07-S02-AC03** — Deliberate dependency, package-asset, compiled-reference, and type-boundary violations are rejected with specific diagnostics.
- **WP07-S02-AC04** — Normal verification cannot pass by checking filenames alone, hiding a PrivateAssets runtime reference, or suppressing a real violation.

#### WP07-S03 Implement full mutation qualification and conservative result validation

As a reviewer, I want full-scope mutation evidence for each shipping implementation so missing assertions and unresolved critical behavior are exposed.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP07-S01

**Guide sections:** 14, 16, 17, 19, 20  
**Requirements:** REQ-005, REQ-007, REQ-008  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S03-T01 | Create one explicit mutation matrix entry/invocation/report per shipping project and materially distinct target/conditional implementation, including the bridge. |
| WP07-S03-T02 | Run Stryker over all handwritten shipping bodies in isolated source/output copies with changed-files-only and stale incremental shortcuts disabled. |
| WP07-S03-T03 | Preserve raw JSON/HTML, tool/config/source identities, all mutant classifications, test discovery, exclusions, and the native score. |
| WP07-S03-T04 | Calculate Killed/(Killed+Timeout+Survived+NoCoverage) per implementation and validate the 90% conservative gate plus the unresolved-critical-invariant rule. |
| WP07-S03-T05 | Rerun timeout mutants in isolation with relaxed settings; retain immutable originals and separate source-bound, hashed reclassification attestations. |
| WP07-S03-T06 | Disposition all survivors/uncovered mutants and any equivalent exclusions; reject pending work, missing projects/reports, unexplained errors/timeouts, and invalid zero populations. |

**Acceptance criteria**

- **WP07-S03-AC01** — Every required matrix entry executes fully and reaches at least 90% conservative score; native score and raw counts are published separately.
- **WP07-S03-AC02** — Identity immutability, precedence, output separation, escaping, filtering, and delivery accounting have no unresolved meaningful surviving or uncovered mutants.
- **WP07-S03-AC03** — Confirmed nontermination receives no conservative numerator credit; any reclassified kill has reproducible assertion-failure evidence.
- **WP07-S03-AC04** — Mutation outputs never modify candidate staging, and their source/config/tool identities link to the same final release inputs.
- **WP07-S03-AC05** — No broad class/method exclusions, stale incremental results, empty populations, or unaccounted errors can produce a passing release.

#### WP07-S04 Implement all four cumulative compatibility layers

As an existing consumer, I want my historical source, compiled binaries, configuration, and event contracts replayed unchanged against each candidate.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP06-S03

**Guide sections:** 4, 13, 14, 16, 17, 18, 19, 20  
**Requirements:** REQ-006, REQ-007, REQ-010  
**Business cases:** BC-003, BC-009, BC-010, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S04-T01 | Enumerate all members from the immutable historical manifest, including intermediate versions and published prereleases, and validate every package/source/binary/fixture/toolchain hash. |
| WP07-S04-T02 | Run layer A API/package/asset checks against each package with relevant parameter-name/attribute checks, allowing compatible additions without blind strict-equality mode. |
| WP07-S04-T03 | Run layer B unchanged historical source/settings against candidate substitutions and temporary lockfiles only in replay workspaces; preserve declared targets, language, nullable, and warnings-as-errors settings. |
| WP07-S04-T04 | Run layer C frozen binaries for candidate-core/old-bridge replacement and complete candidate-core/bridge upgrade; preserve consumer hashes and record actual loaded paths/hashes and manifest/probing changes. |
| WP07-S04-T05 | Run layer D all historical configuration, schema, defaults, errors, stream separation, lifecycle, and behavior fixtures with only explicitly authorized nondeterministic normalization. |
| WP07-S04-T06 | Qualify all layers using incompatible samples, explicit same-runtime versus recorded current-runtime roll-forward cases, and the honest empty-history path. |

**Acceptance criteria**

- **WP07-S04-AC01** — Results identify every baseline, target, and layer; 100% of applicable historical cases pass, and missing baseline work cannot be hidden in aggregate counts.
- **WP07-S04-AC02** — Historical source settings and frozen consumer DLL hashes remain unchanged; actual candidate core/bridge loads are proved for both upgrade cases.
- **WP07-S04-AC03** — API removal/parameter or field-type break controls fail their intended layers; real breaks cannot be suppressed or hidden by regenerated expected output.
- **WP07-S04-AC04** — Empty-history handling applies only to an empty publication manifest and still exercises consumers/contracts/negative controls; prior prereleases remove that exception.
- **WP07-S04-AC05** — Historical target assets remain represented, and explicit current-runtime replay supplements rather than replaces original-target coverage.

#### WP07-S05 Implement the exact-candidate release pipeline

As a release maintainer, I want one exact candidate bound to all verification inputs so a different build or cached package cannot inherit green evidence.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP07-S02, WP07-S03, WP07-S04

**Guide sections:** 14, 16, 17, 19, 20  
**Requirements:** REQ-005, REQ-006, REQ-007, REQ-008  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S05-T01 | Implement ordered restore, static checks, fast correctness, full isolated mutation, fresh unmutated Release build, one-time pack/sign, package verification, full package suites, and evidence validation. |
| WP07-S05-T02 | Freeze package and uninstrumented assembly hashes after all configured signing/versioning operations; stage those packages in an isolated candidate-only feed/cache. |
| WP07-S05-T03 | Make source-based unit builds prove that their uninstrumented shipping assembly matches the candidate; record separate coverage/mutation instrumented copies. |
| WP07-S05-T04 | Run candidate integration/business/compatibility consumers across the recorded Windows/Linux matrix and capture loaded core/bridge paths and hashes. |
| WP07-S05-T05 | Fingerprint source, tests, harness, compiler inputs, dependency closure, verification settings, and package bytes; reject stale evidence after any change. |
| WP07-S05-T06 | Exercise negative controls for same-version cache contamination, wrong loaded DLL, missing package, mutated staging, and attempted hidden rebuild/repack. |

**Acceptance criteria**

- **WP07-S05-AC01** — The pipeline's qualified package passes inspections and suites with one fixed final package hash and matching uninstrumented shipping DLL hashes.
- **WP07-S05-AC02** — Candidate consumers cannot silently restore the same version from another feed or a developer cache; wrong-assembly controls fail.
- **WP07-S05-AC03** — All six families have evidence linked to the same input set; mutation is explicitly source-linked and never misrepresented as executing unchanged package bytes.
- **WP07-S05-AC04** — The release profile fails on stale input fingerprints or a package change and does not repair discrepancies through a hidden rebuild.
- **WP07-S05-AC05** — The promotion command accepts the staged package path without pack/build steps; the final publication story uses this verified path after the repository release conditions are met.

#### WP07-S06 Retain replay prerequisites and validate complete release evidence

As a future maintainer, I want retained prerequisites and complete clean replay so verification remains possible after transient feeds and caches disappear.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Verification maintainer

**Depends on:** WP07-S05

**Guide sections:** 13, 16, 17, 18, 19, 20  
**Requirements:** REQ-005, REQ-006, REQ-007  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP07-S06-T01 | Retain exact permitted tool, SDK/runtime, test-package, bridge-dependency, and historical prerequisite bytes in immutable content-addressed storage, with hashes, licensing inventory, and a materialization script. |
| WP07-S06-T02 | Implement the full release manifest: input/package/assembly identities; suite/tool/lane versions; per-family counts; historical IDs; coverage denominators; mutation classifications/exclusions; and evidence paths/hashes. |
| WP07-S06-T03 | Implement eng/check-release to reconcile all manifests and reject missing/unknown-schema reports, empty discovery, unexpected skips, incomplete baselines/mutation entries, broken hashes, and failed gates. |
| WP07-S06-T04 | Implement eng/replay using read-only extracted inputs, separate generated work/output directories, pinned prerequisites, and all six families including the selected lane's mutation entries. |
| WP07-S06-T05 | Check input hashes before/after replay and produce a separate replay attestation/external index without embedding an archive's own checksum inside itself. |
| WP07-S06-T06 | Qualify clean replay and checker rejection using corrupt/missing prerequisite, evidence, case, baseline, and input-hash fixtures. |

**Acceptance criteria**

- **WP07-S06-AC01** — Replay materializes retained bytes by content/hash and succeeds without a developer cache, live production service, undisclosed credentials, or paid collector account.
- **WP07-S06-AC02** — A selected lane replay executes all six families and reports its actual environment; one lane cannot claim completion of the entire platform matrix.
- **WP07-S06-AC03** — Input checksums are unchanged after replay and output/evidence writes occur only outside the immutable input bundle.
- **WP07-S06-AC04** — The validator rejects every deliberate omission/corruption and never treats missing evidence or zero required discoveries as success.
- **WP07-S06-AC05** — Retention covers the historical cumulative inventory and required toolchains; a public-feed URL or expiring CI attachment is not the sole retained copy.

### WP-08 Reference examples and agent guidance

4 required stories and 21 tasks. The guide's corresponding work package defines the completion boundary.

#### WP08-S01 Teach human and agent logging with runnable entry examples

As a developer or coding agent, I want clear ConsoleHuman and AgentCli examples so that I can initialize the library correctly and select useful events without contaminating command results.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Example maintainer

**Depends on:** WP07-S06

**Guide sections:** 6, 7, 8, 9, 10, 11, 19, 21  
**Requirements:** REQ-001, REQ-002, REQ-003, REQ-004, REQ-008, REQ-010  
**Business cases:** BC-001, BC-002, BC-003, BC-006

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP08-S01-T01 | Create the reference-example run convention using isolated candidate restore, explicit environment inputs, captured stdout/stderr, and loaded-package identity evidence. |
| WP08-S01-T02 | Implement examples/ConsoleHuman with one initialization, normal and verbose presentation, and startup, completion, and failure cases. |
| WP08-S01-T03 | Implement examples/AgentCli with a parseable stdout result, independently parseable stderr JSONL diagnostics, operation IDs, and documented command exit behavior. |
| WP08-S01-T04 | Write shared developer/agent guidance for when to log, level choice, stable event names, business attributes, automatic identity, and configuration; include all six example event types from section 21. |
| WP08-S01-T05 | Explain exception ownership at a handling/context boundary, safe message content, expected cancellation, retry events, and bounded progress reporting; avoid catch-log-rethrow duplication in the examples. |
| WP08-S01-T06 | Add per-example exact run commands, configuration, asserted observations, boundary cases, and CI checks using shared fixtures for portable event/configuration behavior. |

**Acceptance criteria**

- **WP08-S01-AC01** — Both examples run against the recorded candidate package without source project references; a clean replay reproduces their asserted observations.
- **WP08-S01-AC02** — ConsoleHuman visibly distinguishes normal and verbose output, and its failure event supplies permitted exception/context detail plus automatic application attribution.
- **WP08-S01-AC03** — AgentCli stdout parses as the command's documented result with no log contamination; each stderr diagnostic is valid standalone JSON with operation and application identity.
- **WP08-S01-AC04** — Changing documented environment inputs selects supported output behavior without recompilation; a deliberately invalid setting fails with a safe, useful diagnostic.
- **WP08-S01-AC05** — The guidance explains when, what, and how to log, with reasons for each example level and attribute; the examples do not manually repeat resource identity or log one exception at every layer.

**Completion artifacts:** Two runnable examples, example/agent usage guide, fixture assertions, candidate identity records, and clean CI output.

#### WP08-S02 Demonstrate file collection and independent destination levels

As an operator or application developer, I want WorkerWithFileCapture and IndependentOutputLevels examples so that file collection, diagnostic selection, and deferred work are configured correctly.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Example maintainer

**Depends on:** WP08-S01

**Guide sections:** 8, 10, 11, 12, 15, 21  
**Requirements:** REQ-001, REQ-003, REQ-004, REQ-008, REQ-009  
**Business cases:** BC-003, BC-004, BC-008, BC-011, BC-014, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP08-S02-T01 | Implement examples/WorkerWithFileCapture using configured identity, owned rotating JSONL files, and explicit bounded graceful stop. |
| WP08-S02-T02 | Provide deployment-owned collector configuration examples with file matching that excludes damaged segments and retains the documented service/env/version metadata. |
| WP08-S02-T03 | Demonstrate restart and retention behavior, including the preserved boundary between the example's owned files and unrelated files. |
| WP08-S02-T04 | Implement examples/IndependentOutputLevels with Debug file output, Warning console output, and a counted expensive deferred factory. |
| WP08-S02-T05 | Document run/configuration commands, normal and boundary observations, admission versus delivery, flush versus durable storage/collector acknowledgment, and an all-disabled output case; run assertions through the candidate-package harness. |

**Acceptance criteria**

- **WP08-S02-AC01** — WorkerWithFileCapture produces one valid JSON object per physical record, demonstrates rotation/restart, and leaves unrelated files intact within its replay scenario.
- **WP08-S02-AC02** — Collector sample patterns exclude marked damaged segments; record assertions verify service/env/version and clean JSONL without claiming hosted collector ingestion.
- **WP08-S02-AC03** — Debug events reach the file and do not reach the Warning console even with Information as the fallback; the deferred factory executes only when a running destination is interested.
- **WP08-S02-AC04** — The all-disabled case performs no deferred work or ordinary output, and the worker inspects graceful-stop results visibly.
- **WP08-S02-AC05** — Both examples run against the exact candidate and explain their logging choices, excluded data, and the documented flush/admission limitations.

**Completion artifacts:** Two runnable examples, collector configuration samples, file/restart fixture outputs, deferred factory observations, and recorded candidate identities.

#### WP08-S03 Teach concurrent context and observable failure handling

As an application developer, I want ConcurrentOperations and FailureAndShutdown examples so that I can preserve operation identity and respond accurately to overload, writer failure, and incomplete shutdown.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Example maintainer

**Depends on:** WP08-S01

**Guide sections:** 9, 10, 12, 15, 21  
**Requirements:** REQ-001, REQ-003, REQ-007, REQ-008  
**Business cases:** BC-005, BC-007, BC-008, BC-012

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP08-S03-T01 | Implement examples/ConcurrentOperations with two small jobs/experiments, asynchronous work, nested scopes, and distinct operation identifiers. |
| WP08-S03-T02 | Demonstrate capture-time copying with a mutated caller-owned map and show expected cancellation separately from failure. |
| WP08-S03-T03 | Implement examples/FailureAndShutdown with a controlling process that, after successful runtime startup, stalls/closes a child console pipe or triggers a controlled temporary-file failure to produce blocked/faulted output and queue saturation. Add a separate enabled-file startup failure case; use the actual candidate package without a public production fault-injection API. |
| WP08-S03-T04 | Print or assert immutable write/diagnostic/flush/stop results that distinguish admission, loss, pending work, deadline expiry, and output isolation. |
| WP08-S03-T05 | Document exception ownership, useful completion/failure counts, bounded progress messages, and safe failure observations; integrate repeatable candidate-package CI scenarios with explicit deadlines. |

**Acceptance criteria**

- **WP08-S03-AC01** — Interleaved events retain their own operation/experiment context across asynchronous boundaries and share only the intended process metadata.
- **WP08-S03-AC02** — Changes to a caller map after scope/event capture do not alter emitted values; cancellation is represented with its intended event and level.
- **WP08-S03-AC03** — After successful startup, a writer that faults or saturates leaves the other destination functioning, with truthful rejection/failure diagnostics. A separate enabled-file initialization failure rejects runtime creation and releases startup resources without deleting pre-existing log segments.
- **WP08-S03-AC04** — Flush and stop examples distinguish Completed from Succeeded, expose loss and unresolved work, and end within the stated bounded scenario deadline.
- **WP08-S03-AC05** — Both examples are repeatable against the candidate package and teach a single useful exception-handling boundary instead of duplicate logs from each layer.

**Completion artifacts:** Two runnable examples, interleaving/capture fixtures, deterministic failure observations, bounded replay reports, and candidate identity evidence.

#### WP08-S04 Demonstrate host integration and prepare a preserved consumer

As a consuming developer and future maintainer, I want MicrosoftLogging and UpgradeConsumer examples so that I can integrate standard logging and verify later upgrades against a real small application.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Example maintainer

**Depends on:** WP08-S01

**Guide sections:** 3, 10, 13, 18, 19, 21  
**Requirements:** REQ-001, REQ-005, REQ-006, REQ-007, REQ-008, REQ-009  
**Business cases:** BC-004, BC-009, BC-010, BC-013

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP08-S04-T01 | Implement examples/MicrosoftLogging with ILogger categories, named/unnamed events, structured state, host scopes, and the correct host filter for independent destination thresholds. |
| WP08-S04-T02 | Demonstrate the default non-owning provider and an application-owned stop/disposal sequence that examines unsuccessful drain results. |
| WP08-S04-T03 | Implement examples/UpgradeConsumer as a small reproducible consumer with representative API/configuration/output assertions and preserved compiler/project settings. |
| WP08-S04-T04 | Connect UpgradeConsumer outputs and build manifest to the compatibility harness; prepare source and already-built consumer artifacts for WP-09 baseline freezing without recompiling them during later binary replay. |
| WP08-S04-T05 | Complete each example's exact configuration, run instructions, expected results, ownership explanation, and candidate identity evidence; verify the complete eight-example catalog and guidance are included in the bundle inventory. |

**Acceptance criteria**

- **WP08-S04-AC01** — MicrosoftLogging reproduces documented category/event/template/scope mappings and the Debug-file/Warning-console behavior through the separately restored candidate bridge package.
- **WP08-S04-AC02** — Provider disposal leaves the shared runtime available; the application owns its final bounded stop and reports its actual result.
- **WP08-S04-AC03** — UpgradeConsumer builds and runs against the candidate with recorded toolchain/settings, source identity, and actual loaded core/bridge identity where applicable.
- **WP08-S04-AC04** — The consumer is ready for WP-09 to freeze as the first historical baseline; first-release checks do not claim an upgrade from a historical release that does not exist.
- **WP08-S04-AC05** — All eight named reference examples have runnable candidate-package evidence, exact configuration, useful logging explanations, and entries in the release verification bundle.

**Completion artifacts:** Two runnable examples, preserved-consumer preparation manifest, host ownership/filter reports, and complete example/bundle inventory.

### WP-09 First release

4 required stories and 20 tasks. The guide's corresponding work package defines the completion boundary.

#### WP09-S01 Freeze release inputs and prepare the first historical baseline

As a release maintainer, I want final contracts and prospective historical fixtures frozen before qualification so the published baseline is explicit and reproducible.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Release maintainer

**Depends on:** WP08-S02, WP08-S03, WP08-S04

**Guide sections:** 1, 4, 7, 8, 9, 10, 11, 12, 13, 18, 20, 23  
**Requirements:** REQ-001 through REQ-010  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP09-S01-T01 | Review and freeze final identities/target assets, public signatures, configuration defaults/aliases, schema, value bounds, result/error semantics, file rules, and ownership against specification 1.1. |
| WP09-S01-T02 | Review measured burst/file/queue evidence for the stated defaults; any pre-publication adjustment must be explicit and becomes a new final input set requiring the complete run. |
| WP09-S01-T03 | Capture the prospective baseline's representative source/build settings, compiled consumer/bridge fixtures, dependencies/runtime configurations, contract fixtures, and hashes from the final inputs. |
| WP09-S01-T04 | Reconcile the immutable publication history, including prereleases; stage the new entry as prospective/unpublished until the release promotion actually succeeds. |
| WP09-S01-T05 | Freeze source/test/example/harness/tool/dependency manifests and the verification input kit; identify any prior evidence invalidated by these final inputs. |

**Acceptance criteria**

- **WP09-S01-AC01** — A recorded freeze review covers every permanent contract surface and contains no unresolved discrepancy with the guide.
- **WP09-S01-AC02** — Prospective source/binary/configuration/wire/behavior fixtures and required toolchains are retained with immutable identities; no candidate is falsely described as a historical publication.
- **WP09-S01-AC03** — If any published prerelease exists, it remains in every applicable historical layer; an empty-history declaration is supported by the actual publication manifest.
- **WP09-S01-AC04** — The final input identity includes tests, examples, harnesses, tools, dependencies, and settings; earlier green results are not reused after an input change.

#### WP09-S02 Run final release qualification from clean frozen inputs

As a consumer, I want the final candidate to pass the complete suite on the recorded platform matrix so release claims correspond to actual tested bytes.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Release maintainer

**Depends on:** WP09-S01

**Guide sections:** 14, 15, 16, 17, 18, 19, 20, 21, 23  
**Requirements:** REQ-001 through REQ-010  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP09-S02-T01 | Materialize the frozen input kit and retained prerequisites in clean recorded Windows/Linux verification lanes with isolated candidate caches. |
| WP09-S02-T02 | Run eng/verify --profile release for the final version through all stages, including full source-linked mutation followed by a fresh unmutated build and one-time pack/sign/hash. |
| WP09-S02-T03 | Execute every normal family, all historical compatibility layers, and all eight reference examples against the exact candidate packages; preserve loaded assembly proof. |
| WP09-S02-T04 | Validate discovery/skip completeness, 95% line/90% branch per shipping assembly, 90% conservative mutation per distinct implementation, critical-invariant dispositions, and zero functional/architecture/compatibility failures. |
| WP09-S02-T05 | Retain immutable raw reports, rerun attestations, lane identities, candidate hashes, input linkage, and clean-input before/after checksums. |

**Acceptance criteria**

- **WP09-S02-AC01** — All six families execute for their complete prescribed matrix, every applicable BC passes, and each reference example runs against the candidate package.
- **WP09-S02-AC02** — All coverage/mutation/critical-invariant and cumulative compatibility gates pass with complete denominators, raw statuses, and no unexpected skips.
- **WP09-S02-AC03** — Final package and uninstrumented assembly hashes match the assets actually verified; mutation and instrumentation copies remain separately identified.
- **WP09-S02-AC04** — The final evidence is attributable to the frozen input set and actual Windows/Linux lanes; failures or changed inputs trigger a new complete qualification rather than a waiver.
- **WP09-S02-AC05** — The input-kit execution provides clean replay evidence without changing archived inputs or relying on hidden prerequisite state.

#### WP09-S03 Assemble and validate the complete release bundle

As a future maintainer, I want the complete package, source, history, tools, and evidence bundle ready together so every published version remains reviewable and replayable.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Release maintainer

**Depends on:** WP09-S02

**Guide sections:** 18, 19, 20, 21, 22, 23  
**Requirements:** REQ-001, REQ-005, REQ-006, REQ-007, REQ-008, REQ-009, REQ-010  
**Business cases:** BC-001 through BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP09-S03-T01 | Assemble exact products/symbols/docs, matching source, all tests and mutation harness, contracts, cumulative history, examples, tooling/materialization references, evidence, inventories, licenses, manifests, and checksums. |
| WP09-S03-T02 | Run eng/check-release against the completed bundle and reconcile every required content area and evidence hash with the successful final qualification. |
| WP09-S03-T03 | Exercise the documented extracted-bundle replay path or attach the qualifying clean input-kit replay evidence permitted by section 19; validate unchanged inputs and the external release index. |
| WP09-S03-T04 | Write release notes and replay instructions identifying actual supported behavior, execution lanes, historical coverage, and durability/collector/port scope. |
| WP09-S03-T05 | Prepare the promotion and retrieval-verification procedure to use identical staged bytes, preserve repository-signature envelope hashes, and compare all non-signature payload/assembly bytes; provide it to WP09-S04. |

**Acceptance criteria**

- **WP09-S03-AC01** — The bundle contains every section 18 content area or an exact retained content-addressed prerequisite archive plus materialization instructions; no missing artifact is replaced by a checklist.
- **WP09-S03-AC02** — The external release index identifies input, evidence, and final bundle digests, and eng/check-release succeeds only for that exact complete artifact set.
- **WP09-S03-AC03** — Clean replay evidence covers all six families for its recorded lane and preserves input checksums; the full required lane matrix is separately complete.
- **WP09-S03-AC04** — Release notes make no untested platform, TypeScript, hosted-ingestion, durability, or consuming-application migration claim.
- **WP09-S03-AC05** — The verified packages, prospective baseline payload, and complete verification bundle are ready for the repository release process; promotion is executed by WP09-S04.

#### WP09-S04 Publish the verified release and register its historical baseline

As a library consumer, I want the verified package and its complete test bundle distributed together so that upgrades and their evidence remain available for future replay.

**Status:** Planned · **Priority:** Required for v1 · **Owner role:** Release maintainer

**Depends on:** WP09-S03

**Guide sections:** 13, 18, 19, 20, 23  
**Requirements:** REQ-001, REQ-005, REQ-006, REQ-007, REQ-010  
**Business cases:** BC-009, BC-010

**Tasks**

| Task | Implementation work |
| --- | --- |
| WP09-S04-T01 | Apply the repository release conditions, make the complete retained verification bundle and instructions retrievable before exposing runtime packages, and promote the exact staged core and bridge packages using the prepared process without rebuilding or repacking. |
| WP09-S04-T02 | Retrieve the distributed artifacts, verify signatures where applicable, retain pre- and post-feed package hashes, and compare all non-signature payload and loaded-assembly identities with the verified candidate. |
| WP09-S04-T03 | Append each actually published package/version to the immutable historical manifest with publication identity, source/binary fixtures, contracts, dependencies, and retained toolchain/evidence hashes, even if another part of the release promotion fails. |
| WP09-S04-T04 | Verify that package, release notes, verification bundle, retained prerequisite materialization, and replay instructions are available through the documented release locations. |
| WP09-S04-T05 | Close the release milestone only after both runtime packages and complete verification material are retrievable. Keep partially published releases explicitly incomplete, preserve the history of every exposed package, and resume only missing artifact promotions using the same verified staged bytes. |

**Acceptance criteria**

- **WP09-S04-AC01** — The distributed core and bridge payloads match the tested candidate; any feed-added signature envelope is separately recorded and does not conceal a changed runtime payload.
- **WP09-S04-AC02** — The same published version has the complete required verification bundle and recorded replay evidence, with hashes that validate after retrieval.
- **WP09-S04-AC03** — The append-only history contains every actually published package/version and its frozen baselines, including packages exposed during a partially failed release. Unpublished candidates are not historical releases; aggregate success remains false until the whole release is complete.
- **WP09-S04-AC04** — A maintainer can retrieve the documented release artifacts and retained replay prerequisites without relying on expiring CI artifacts or an unstated developer cache.

**Completion artifacts:** Published core and bridge packages, complete verification bundle, retrieval attestation, and append-only historical publication entry.

## 6 Requirement and acceptance coverage

Each requirement maps to implementation and verification stories. Contract or tooling support is not itself a passing business-case result; WP07-S01 reconciles the acceptance inventory, and WP09-S02 executes it against the final candidate.

| Requirement | Required outcome | Owning epics |
| --- | --- | --- |
| REQ-001 | Implement the core functionality before creating the reference examples and language ports. | WP-01, WP-07, WP-08, WP-09, TS |
| REQ-002 | Provide the static `EnviormentsOptions` entry point for validated startup configuration and application/runtime identity. | WP-02, WP-03, WP-05, WP-07, WP-08, WP-09 |
| REQ-003 | Automatically attach application name, build version, deployment environment, instance, host, OS, and available runtime context to events. | WP-02, WP-03, WP-04, WP-05, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-004 | Configure human, coding-agent, and log-collector output through documented environment variables. | WP-02, WP-03, WP-04, WP-05, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-005 | Keep all shipping core code free of third-party NuGet dependencies, including transitive runtime dependencies. Prefer zero explicit package references in the core itself. | WP-01, WP-02, WP-06, WP-07, WP-08, WP-09 |
| REQ-006 | Preserve published backwards compatibility indefinitely, including source, binary, configuration, wire format, and documented behavior. | WP-01, WP-02, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-007 | Execute and distribute the complete unit, integration, mutation, architecture, backwards-compatibility, and business-case verification material with every published version. | WP-01, WP-02, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-008 | Demonstrate readable C# and enforce responsibility and dependency boundaries automatically. | WP-01, WP-02, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-009 | Keep the core usable across applications without a dependency on CIS orchestration, CTEP persistence, a game engine, or a collector SDK. | WP-01, WP-02, WP-05, WP-06, WP-07, WP-08, WP-09, TS |
| REQ-010 | Preserve a language-neutral contract and fixtures that a later TypeScript implementation can pass. | WP-02, WP-03, WP-04, WP-05, WP-07, WP-08, WP-09, TS |

| Case | Scenario and required observation | Primary story owners |
| --- | --- | --- |
| BC-001 | A developer selects verbose human output and an operation fails. Output identifies application/build/environment, explains the event, and includes the permitted exception/context detail. | WP04-S02, WP05-S01, WP08-S01 |
| BC-002 | An agent invokes a CLI command configured for JSONL diagnostics. Stdout is a valid command result; every stderr log line is independently parseable JSON with stable identity. | WP05-S04, WP08-S01 |
| BC-003 | An operator changes output settings through environment variables. The same application build selects the configured stream/file/format without source changes, preserving event meanings. | WP03-S02, WP03-S03, WP08-S02 |
| BC-004 | Console is Warning and file is Debug. Debug events reach the file and are absent from console; expensive work is skipped only when no output is interested. | WP04-S05, WP05-S02, WP06-S02, WP08-S02 |
| BC-005 | Two experiments execute concurrently across asynchronous boundaries. Each event retains its own operation/experiment identifiers and the shared process identity. | WP04-S03, WP04-S04, WP08-S03 |
| BC-006 | A recognized setting contains an invalid value. Startup fails with the setting name and accepted values, without dumping unrelated environment data. | WP03-S03, WP03-S04 |
| BC-007 | A queue fills or a writer fails. Resource use remains bounded, unaffected outputs continue, and rejection/failure counters are truthful. | WP05-S02, WP05-S03, WP05-S08, WP05-S10, WP08-S03 |
| BC-008 | An application stops gracefully with queued events. Accepted events reach the documented boundary or the stop result explicitly reports unresolved work and deadline/cancellation. | WP05-S08, WP05-S09, WP05-S10, WP08-S03 |
| BC-009 | An unchanged older source consumer upgrades its package reference. It builds under its preserved settings and retains its supported behavior. | WP07-S04, WP08-S04 |
| BC-010 | An already-compiled older consumer receives the candidate core asset. It runs without recompilation and proves that the candidate assembly was loaded. | WP07-S04, WP08-S04 |
| BC-011 | The application restarts and file rotation continues. Instance identity changes, existing owned segments are handled consistently, retention is bounded, and unrelated files survive. | WP03-S04, WP05-S05, WP05-S06, WP05-S07 |
| BC-012 | A caller mutates a dictionary immediately after logging. Queued events retain the values captured at the logging call. | WP04-S01, WP04-S03, WP04-S04 |
| BC-013 | An application uses the Microsoft provider and its scopes. Category, event name, structured state, exception, and correlation map into the core contract; provider ownership rules hold. | WP06-S01, WP06-S02, WP06-S03, WP08-S04 |
| BC-014 | A collector reads generated structured records. Records retain the specified service/env/version and metadata fields, with no raw multiline or ANSI contamination. This proves the output contract, not hosted ingestion. | WP04-S04, WP05-S01, WP05-S07, WP08-S02 |
| BC-015 | An event contains boundary-size Unicode, a decimal value, and an unsupported object. Supported values follow the exact type/encoding rules; unsupported/oversized input is explicitly rejected and accounted for. | WP03-S03, WP04-S01, WP04-S02, WP04-S04, WP05-S06 |
| BC-016 | All normal outputs are disabled. Logging calls return filtered/stopped outcomes as specified, do not invoke deferred factories, and produce no ordinary log records. | WP04-S05, WP05-S04, WP05-S10, WP08-S02 |

BC-009 and BC-010 use real historical releases when such history exists. The first release still qualifies the harness, consumer shapes, fixtures, and negative controls; empty history is recorded honestly, and publication creates the baseline used by later upgrades.

## 7 Release gates and evidence

Every initial-release story is required. The final release run must additionally establish the following cross-cutting gates from guide sections 13 through 19.

| Gate | Required result |
| --- | --- |
| Test completeness | All six families execute, every applicable required case is discovered, and no unexpected skips remain. |
| Functional acceptance | All applicable business cases and all required unit/integration checks pass. |
| Compatibility | Every applicable historical API, source, unchanged-binary, configuration, and behavior fixture passes, including prior published prereleases. |
| Dependency and architecture | Zero violations in evaluated projects, resolved graphs, package assets, compiled references, and contract boundaries. |
| Coverage | At least 95% line and 90% branch coverage per handwritten shipping assembly, with aggregate results and disclosed denominators. |
| Mutation | At least 90% conservative full-scope score for each materially distinct shipping implementation, alongside native results and all raw mutant classifications. |
| Critical invariants | No unresolved meaningful surviving/no-coverage mutants for identity immutability, precedence, output separation, escaping, filtering, or delivery accounting. |
| Build and readability | Release build, formatting, SDK analyzers, public documentation, and responsibility-focused review pass. |
| Distribution and replay | Candidate/package/assembly identities agree with evidence; a complete bundle and recorded clean replay exist for the promised lane matrix. |

The coverage and mutation thresholds are the guide's starting project gates. They are carried into this plan unchanged; adjusting a gate is a design change rather than an ordinary way to resolve a failing implementation task.

The conservative mutation score is `Killed / (Killed + Timeout + Survived + NoCoverage)`. Retain native Stryker reports unchanged. Account for compile/runtime errors, exclusions, pending work, empty populations, and any source-bound rerun attestations explicitly. Mutation uses modified builds from the same frozen source/test/tool inputs; package-consumer tests establish behavior of the actual unmodified candidate payload.

Keep the release dependency direction clear: WP-07 qualifies the verification machinery, WP-08 supplies completed reference examples, and WP-09 freezes the final inputs and executes the full release run. WP-09 cannot reuse stale results after source, tests, tooling, configuration, dependencies, or package bytes change.

The verification bundle includes the exact packages and source, all test projects and support, mutation setup, schema/configuration fixtures, cumulative historical packages and source/binary consumers, runnable examples, pinned toolchains/dependencies or their retained materialization archive, reports, inventories, and hashes. A public feed URL alone is not sufficient retention for indefinite replay.

For a first publication with an empty historical manifest, qualify the compatibility harness using clearly identified synthetic incompatible fixtures and preserve the new release as the first real baseline after publication. Do not manufacture historical success. Existing prereleases are historical releases and must be compared.

Make the complete verification material retrievable before exposing runtime packages. Every package/version that actually becomes available establishes a historical compatibility obligation, even if the overall multi-artifact promotion fails. Keep that aggregate release incomplete, preserve each published package in history, and resume missing promotions using the same verified staged bytes.

## 8 Initial work queue

Start with the four foundation stories in dependency order. Their output makes later feature work reproducible and makes incomplete verification visible from the beginning.

| Order | Story | Immediate result |
| --- | --- | --- |
| 1 | WP01-S01 | Establish package identities and supported framework contracts |
| 2 | WP01-S02 | Create the minimal repository and enforce shipping dependency boundaries |
| 3 | WP01-S03 | Pin and prove the complete test toolchain early |
| 4 | WP01-S04 | Bootstrap verification commands, test inventories, and deterministic support |

The first implementation batch is complete when the identities/target policy, minimal dependency-clean package, real toolchain smoke evidence, and verification/discovery harness all exist. Follow it with WP02-S01 through WP02-S04 to establish the public contract before the implementation spreads.

After WP-02, the first independent implementation branches are application identity (WP03-S01), configuration resolution (WP03-S02), and supported event values (WP04-S01). Exception projection and asynchronous scopes can then advance independently. Full metadata/event capture joins these branches before the logger API and output workers.

Within WP-05, formatting and queue work can progress in parallel. Delivery accounting follows queues; console integration then enables the real-process path. File ownership precedes rotation and restart recovery. Explicit flush coordination can proceed alongside file work; periodic flushing and the final shared stop integrate the completed behavior.

WP07-S02 architecture completion and WP07-S04 compatibility qualification can run alongside functional-case reconciliation. Full mutation follows the completed functional suite. WP07-S05 joins all four evidence streams before retention/replay completion.

The first implementation handoff should identify the selected story and tasks, cite the guide sections, state the planned responsibility boundary, and list the exact checks to run. Pull the next dependency-ready story after review. Keep new application migrations and TypeScript implementation behind their stated follow-on boundaries.

## 9 Later TypeScript backlog

The five TypeScript stories below are Follow-on work. TS-S01 depends on the completed C# publication story WP09-S04; the released contract and examples are the input baseline.

| Story | Outcome | Direct prerequisites | Tasks |
| --- | --- | --- | --- |
| TS-S01 | Select the first TypeScript runtime and applicability boundary | WP09-S04 | 5 |
| TS-S02 | Implement portable configuration and immutable event values | TS-S01 | 5 |
| TS-S03 | Implement logging, context, outputs, and lifecycle for the selected runtime | TS-S02 | 5 |
| TS-S04 | Create TypeScript usage examples after port functionality passes | TS-S03 | 5 |
| TS-S05 | Verify and package the first TypeScript release | TS-S04 | 5 |

#### TS-S01 Select the first TypeScript runtime and applicability boundary

As a cross-language consumer, I want an explicit runtime and capability scope for the first port so that identical contracts and runtime-specific behavior are clearly distinguishable.

**Status:** Planned · **Priority:** Follow-on · **Owner role:** Port maintainer

**Depends on:** WP09-S04

**Guide sections:** 1, 15, 22  
**Requirements:** REQ-001, REQ-009, REQ-010  
**Business cases:** None

**Tasks**

| Task | Implementation work |
| --- | --- |
| TS-S01-T01 | Select the initial runtime/application use case and record the supported runtime matrix before choosing implementation tools or packages. |
| TS-S01-T02 | Map every shared configuration/event/business fixture to applicable, host-specific, or out-of-scope capability with a concrete reason. |
| TS-S01-T03 | Define metadata discovery versus supplied public startup identity for the selected runtime; keep process identity separate from operation context. |
| TS-S01-T04 | Document output, asynchronous-context, timeout, and shutdown capabilities and the observable differences that require runtime-specific tests. |
| TS-S01-T05 | Review the port scope, dependency policy, package identity, and verification tool selection as a separate implementation decision against the released contract. |

**Acceptance criteria**

- **TS-S01-AC01** — An explicit first-runtime decision and capability table exist; the plan does not silently promise both Node and browser implementations.
- **TS-S01-AC02** — Every shared fixture has a justified applicability status, with unavailable host/process fields treated according to the guide.
- **TS-S01-AC03** — Any browser scope uses deliberately supplied public identity and excludes server environment dumps from the bundle/configuration.
- **TS-S01-AC04** — Runtime differences are documented as capabilities and tested outcomes rather than described as identical internal behavior.

**Completion artifacts:** Port scope decision, runtime capability matrix, and fixture applicability catalog.

#### TS-S02 Implement portable configuration and immutable event values

As a TypeScript consumer, I want the same observable configuration and structured value contract so that operators and downstream tools can use the established settings and records.

**Status:** Planned · **Priority:** Follow-on · **Owner role:** Port maintainer

**Depends on:** TS-S01

**Guide sections:** 7, 8, 9, 22  
**Requirements:** REQ-003, REQ-004, REQ-010  
**Business cases:** BC-003, BC-006, BC-012, BC-015

**Tasks**

| Task | Implementation work |
| --- | --- |
| TS-S02-T01 | Implement the selected runtime's input-resolution surface while preserving applicable environment names, precedence, defaults, and validation behavior. |
| TS-S02-T02 | Implement immutable process metadata and its allowlisted structured projection using the selected runtime's documented identity capabilities. |
| TS-S02-T03 | Implement copied supported values, scalar/node/depth/record bounds, and stable field types using the language-neutral fixtures. |
| TS-S02-T04 | Preserve explicit decimal and large-integer string encodings and the released timestamp/identifier representation without relying on lossy JavaScript-number conversion. |
| TS-S02-T05 | Run applicable configuration, metadata, mutation-after-capture, Unicode, and numeric-boundary fixtures against the port. |

**Acceptance criteria**

- **TS-S02-AC01** — Applicable configuration fixtures resolve to the same documented values or safe validation outcomes as the released C# implementation.
- **TS-S02-AC02** — Structured records retain required fields/types and omit unavailable optional metadata as specified by the selected runtime capability table.
- **TS-S02-AC03** — Decimal, large-integer, safe-integer, Unicode, timestamp, and identifier fixtures preserve the exact encoding contract.
- **TS-S02-AC04** — Caller mutations cannot change captured metadata or event values, and unsupported/oversized values produce the documented rejection behavior.

**Completion artifacts:** Shared configuration/event fixture reports and selected-runtime identity/value tests.

#### TS-S03 Implement logging, context, outputs, and lifecycle for the selected runtime

As a TypeScript developer, I want logging behavior consistent with the shared contract so that operation context, filtering, and shutdown remain understandable across languages.

**Status:** Planned · **Priority:** Follow-on · **Owner role:** Port maintainer

**Depends on:** TS-S02

**Guide sections:** 9, 10, 11, 12, 22  
**Requirements:** REQ-003, REQ-004, REQ-008, REQ-010  
**Business cases:** BC-004, BC-005, BC-007, BC-008, BC-016

**Tasks**

| Task | Implementation work |
| --- | --- |
| TS-S03-T01 | Implement interest checks, deferred event creation, capture-time context, and the shared write outcomes for selected-runtime capabilities. |
| TS-S03-T02 | Implement bounded operation context and precedence with the runtime-specific asynchronous propagation mechanism selected in TS-S01. |
| TS-S03-T03 | Implement supported human/JSONL outputs and independent destination filtering while preserving stable event meaning and record bounds. |
| TS-S03-T04 | Implement supported admission, output isolation, diagnostics, flush, and stop semantics; make any host-specific limitations explicit in the capability contract. |
| TS-S03-T05 | Add applicable shared and runtime-specific concurrency, overload, blocked/failing output, cancellation, and shutdown tests. |

**Acceptance criteria**

- **TS-S03-AC01** — Applicable filtering and deferred-work fixtures pass, including independent levels and all-disabled outputs where those destinations are supported.
- **TS-S03-AC02** — Concurrent operations preserve their context and shared identity without cross-operation mutation.
- **TS-S03-AC03** — Supported output/lifecycle cases distinguish accepted work from delivered work and expose failure, loss, and unresolved work accurately.
- **TS-S03-AC04** — Any capability outside the selected runtime is explicitly absent with a documented reason; the port does not silently substitute a different behavior or claim an unrun case passed.

**Completion artifacts:** Applicable shared behavior reports, runtime-specific concurrency/lifecycle tests, and updated capability documentation.

#### TS-S04 Create TypeScript usage examples after port functionality passes

As a developer or coding agent working in TypeScript, I want examples aligned with the C# event and configuration conventions so that language changes do not require inventing new logging practices.

**Status:** Planned · **Priority:** Follow-on · **Owner role:** Port maintainer

**Depends on:** TS-S03

**Guide sections:** 6, 21, 22  
**Requirements:** REQ-001, REQ-008, REQ-010  
**Business cases:** BC-001, BC-002, BC-003, BC-005, BC-008, BC-014

**Tasks**

| Task | Implementation work |
| --- | --- |
| TS-S04-T01 | Select examples corresponding to supported first-runtime capabilities and explicitly map which C# teaching outcomes each one reproduces. |
| TS-S04-T02 | Build examples against the TypeScript candidate package after implementation tests pass, without copying logging infrastructure into applications. |
| TS-S04-T03 | Reuse event selection, exception ownership, cancellation/retry, bounded progress, and metadata/context guidance from the shared teaching contract. |
| TS-S04-T04 | Provide exact runtime configuration, commands, expected structured records, and failure/boundary observations for each example. |
| TS-S04-T05 | Run examples with shared portable fixtures and runtime-specific assertions; record the candidate package identity and supported capability set. |

**Acceptance criteria**

- **TS-S04-AC01** — Each selected example runs against the TypeScript candidate package and reproduces its applicable shared configuration/event observations.
- **TS-S04-AC02** — Examples explain when, what, and how to log using the shared event meanings without copying the C# internal class structure as a requirement.
- **TS-S04-AC03** — Useful business/operation fields are supplied by the example and identity is attached by the port; exception and progress logging follow the shared guidance.
- **TS-S04-AC04** — Runtime-specific examples identify unsupported capabilities plainly, and only actually executed applicable business cases are reported as passing.

**Completion artifacts:** Runnable selected-runtime examples, teaching-outcome mapping, shared fixture assertions, and candidate identity records.

#### TS-S05 Verify and package the first TypeScript release

As a cross-language maintainer, I want a reproducible port release and compatibility baseline so that later changes can prove both local correctness and continued shared-contract conformance.

**Status:** Planned · **Priority:** Follow-on · **Owner role:** Port maintainer

**Depends on:** TS-S04

**Guide sections:** 13, 14, 15, 16, 18, 19, 22  
**Requirements:** REQ-006, REQ-007, REQ-008, REQ-010  
**Business cases:** None

**Tasks**

| Task | Implementation work |
| --- | --- |
| TS-S05-T01 | Complete the selected runtime's unit, integration, mutation, architecture, compatibility, and business-case verification with declared applicability and tooling. |
| TS-S05-T02 | Run the cumulative shared fixture catalog and compare normalized observable outputs with the released contract, preserving expected host-specific differences. |
| TS-S05-T03 | Inspect the actual candidate package, dependency graph, public declarations, runtime behavior, and examples; apply the agreed port quality gates. |
| TS-S05-T04 | Freeze first port API/type/configuration/event/behavior consumer baselines using compatibility methods applicable to the selected runtime; document why CLR binary-replay mechanics do not directly transfer. |
| TS-S05-T05 | Produce a reproducible verification bundle with exact candidate bytes, tooling, fixtures, reports, examples, and hashes, ready for the port's release workflow. |

**Acceptance criteria**

- **TS-S05-AC01** — All applicable shared fixtures pass and every excluded host-specific case is justified; the report makes no retroactive claim that C# release verification executed TypeScript.
- **TS-S05-AC02** — All required applicable verification families and agreed quality gates have evidence tied to the exact TypeScript candidate package.
- **TS-S05-AC03** — A preserved first-release port baseline exists for future compatible evolution, with explicit source/type/runtime/behavior compatibility coverage.
- **TS-S05-AC04** — A clean replay validates the delivered verification bundle and examples against the same candidate bytes.

**Completion artifacts:** Port verification manifest, complete reports, preserved consumer baselines, shared conformance report, and clean bundle replay.

The later backlog does not delay or replace any C# release obligation. Its stories are refined against the actual released C# contract and selected TypeScript runtime matrix before execution. A later consuming-application migration is a separate adoption task with its own existing-behavior comparison.

## 10 Agent handoff template

```text
Implement <story ID> and its selected tasks from this implementation plan.
Read the cited sections of core-library-implementation-guide-v1.md, spec 1.1,
and the current repository instructions before editing.

Confirm completed prerequisites and identify the responsibility boundary,
existing types to reuse, public-contract impact, and required verification.
Preserve EnviormentsOptions exactly. Keep the core free of explicit NuGet
references and third-party runtime dependencies. Use internal test seams
where possible and keep application examples as consumers of the package.

Implement the behavior and its tests in small coherent changes. Satisfy every
acceptance criterion with actual evidence; report discovered/executed tests,
commands, source revision, and package/assembly identity where relevant.
Do not rewrite historical expected outputs, weaken gates, or claim success
from missing reports, stubs, or tests that were never executed.

Update task and story status only when the stated completion rules are met.
Keep a concrete unresolved issue attached to its story. Follow existing
repository rules when executing a later publication task.
```

## 11 Source and backlog format

This plan derives from `core-library-implementation-guide-v1.md`, specification 1.1, dated 8 October 2026, SHA-256 `01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675`. Section references and REQ/BC IDs refer to that baseline. The implementation guide remains the source for detailed behavior; the plan owns decomposition, dependency order, and completion evidence.

The companion `core-library-backlog-v1.json` contains the same story/task identifiers, dependencies, acceptance criteria, traceability, and initial statuses. It is a neutral structured backlog, without an assumed issue-tracker import schema. It can be transformed into the chosen tracker format or used directly for agent task selection.
