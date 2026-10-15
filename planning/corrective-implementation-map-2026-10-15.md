# CISLIB corrective implementation map

15 October 2026. Draft execution map for the supplied corrective review, against `main` at `c1f37a2ee0bbc4772797aa54e44ca3b13c138132`.

Start with a reliable ordinary build and a small native C# feedback loop. Recover the intended defaults before changing public behavior, then introduce the incoming logging spool and common writer mechanism, restore extension features, and qualify the corrected candidate. Preserve existing capabilities and release obligations throughout.

The subsequent user instruction authorizes Batch A and the first focused native-test slice of B, coordinated with the logging chat. Those changes are implemented with Windows/Linux ordinary-build and focused native evidence; see [Batch A execution evidence](evidence/corrective-batch-a-2026-10-15.md). The user's follow-up, “Lets keep going,” continues B with the [discovery-driver migration](evidence/corrective-discovery-migration-2026-10-15.md): native observation equivalence, five corruption controls and retirement of `verify-process.py`. Supported Visual Studio IDE build/Test Explorer/design-time acceptance remains open because this machine only has VS2022 Build Tools. The explicitly approved Claude review completed, its actionable findings were addressed locally, and the user subsequently directed that further Claude reviews stop. The remaining B migration and C–F remain open. Publication and the old mutation queue remain separate.

## Review disposition

The local HEAD matches the review's pinned revision. The review is a sound corrective direction, with the following distinctions that matter for implementation.

| Finding | Assessment at this checkout | Implementation consequence |
| --- | --- | --- |
| C01 Broad Developer loop | Confirmed. Both verification entry points expose profile, lane and build-node selection, without a test/module filter. The retained Windows run totals 1,319.3 seconds; its architecture stage takes 574.3 seconds and unit stage 47.8 seconds. | Introduce focused execution in B; retain the comprehensive checkpoint. Optimize the expensive responsibilities and their cadence, not just unit execution. These timings are retained evidence, not a new benchmark. |
| C02 Substantive Python verification | Confirmed. The current engineering/test directories contain 92 Python files: 81 DiscoveryHost readers, eight engineering utilities and three Type Manager fixture utilities. Presence does not prove each remains an active path. `DiscoveryProcessTests.cs` already overlaps substantially with `verify-process.py`. | Inventory active invocations and map observations to native cases before choosing migration versus retirement. Include substantive PowerShell policy in the native-tooling endpoint too. |
| C03 Build-host-dependent references | Confirmed source assumption, not a reproduced failure. Discovery TestLogger and TypeManager.Build resolve assemblies through `MSBuildBinPath`; the Type Manager targets enforce a specific helper path. | Reproduce and classify the failure in A before selecting a dependency/path fix. Validate CLI, executable Visual Studio builds and design-time behavior separately. |
| C04 Valid dependencies and runtime pins | Confirmed. Shared test targets provide MSTest/VSTest and pin runtime 10.0.12. The helper reference is build-only. | Preserve the one-Core package and approved Microsoft closure. Diagnose SDK, reference, task-host and test-host problems separately; do not loosen runtime pins or remove legitimate projects. |
| C05 Reported Type Manager default regression | Still unresolved. Current options require HostAssembly, default the manifest to the host name under AppContext.BaseDirectory, and use an empty precomputed-contract list. These behaviors are in the v1 definition and existing tests. | Recover the missing use case in C. Agreement with the latest specification does not dismiss the report; neither does it establish a parameterless API or scanning fallback. |
| C06 Logging architecture and missing features | Confirmed. CoreLogger exposes IsEnabled/Write/WriteLazy. OutputQueues has paired console/file composition. CoreLogRuntime.Write captures, encodes and prepares output before admission. Earlier recovery, provider and deadline corrections are already present. | Preserve those fixes while moving substantial processing behind an incoming boundary in D and restoring the extension surface in E. |
| C07 Documentation and prerequisites | Confirmed. The test README still describes business tests as empty and logging integration as pending; AGENTS retains scaffold wording. Verification output/package roots are shared. | Fix prerequisites and direct test execution in A/B; update guidance alongside code rather than waiting until F. |

Two additional planning risks deserve explicit treatment:

- Moving processing behind a spool changes when failures, truncation and per-writer tickets become observable. The current synchronous `LogWriteResult` cannot automatically keep all its present meanings. Decide that transition before implementing the spool.
- A successful Developer run is not a substitute for cold normal builds, focused test usability, or final release qualification. Each boundary needs its own acceptance result.

The local publication ledger currently has no publication rows. Recheck publication state before any public-contract change; unpublished prototypes may be corrected without rewriting preserved historical evidence.

## Dependency and ownership map

| Batch | Can begin after | Main scope and proposed coordinator | Completion boundary |
| --- | --- | --- | --- |
| A Build reliability | Implementation is authorized | Build/tooling owner, coordinated with Type Manager helper owner | Fresh normal Debug/Release build and native discovery on declared hosts |
| B Native verification and focused loop | The first buildable A slice; inventory can start alongside A | Verification owner; module owners review observation mappings | Fast selected tests plus no Python dependency in maintained C# verification |
| C Defaults and behavior recovery | Source/history reconciliation can start alongside A/B | Type Manager owner and logging/configuration owner | Every discrepancy has a disposition and minimal-use regression coverage |
| D Incoming spool and common writers | A, a usable focused B path, and relevant C decisions | Logging owner | One asynchronous pipeline with bounded ownership and end-to-end flush/stop accounting |
| E Filters, interceptors, duplicates and convenience | D's extension/lifecycle contract; decision recovery can start earlier | Logging owner, including Microsoft provider integration | Restored capabilities use the same pipeline and preserve required semantics |
| F Consolidation and qualification | A–E acceptance complete | Coordinated package/release verification | Exact corrected candidate passes all required release families and platforms |

The full B migration can proceed incrementally with C–E; it must not hold up every focused correction. It must finish before F. Do not start full mutation qualification against a pipeline still awaiting D/E changes. Focused mutation controls remain useful where they establish a particular regression test.

The existing logging chat confirmed no active operations and remains paused at `c1f37a2e`. This chat reserved the Batch A tooling, shared build/test and graph window; the logging chat received progress and the stale compiler-server cleanup notice. No logging production behavior was changed or dispatched. Concurrent Windows/Linux checks use separate checkouts. Keep reserving shared package/report roots and the Git index before use.

## Batch A Restore the normal build

**Change locations:** `implementations/csharp/CisLib.slnx`, `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `NuGet.config`, project lockfiles, `eng/CIS.Discovery.TestLogger/CIS.Discovery.TestLogger.csproj`, `eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj`, `build/type-manager/CIS.TypeManager.targets`, and the affected test/fixture project references. Paths in this and subsequent batches are relative to `implementations/csharp` unless prefixed otherwise.

1. Establish a clean checkout and declared SDK/runtime/Visual Studio prerequisites. Inventory every solution project, evaluated target/RID and reference origin. Capture actual compiler/task-host diagnostics and resolved paths, beginning with TestLogger's ObjectModel reference and the helper's MSBuild references.
2. Reproduce Debug and Release through ordinary CLI restore/build. Exercise the supported Visual Studio executable build and Test Explorer, then design-time loading separately. An unavailable IDE environment is a recorded gap, not a passing result.
3. Choose the narrow reference/task-loading fix from the reproduced failure. Prefer explicit approved development dependencies or the correct selected SDK location; do not add ambient DLL search or shipping runtime dependencies. Check the packaged helper as well as source-project use.
4. Make direct test project builds produce their ordinary fixture/logger prerequisites. Keep compiler-negative and damaged-asset cases in owned test directories. Make expensive architecture/package prerequisites explicit in their fixtures/checkpoint.

**Acceptance:** fresh Debug/Release solution results, normal native discovery, a direct ordinary unit test without prior pack/checkpoint, and evaluated/packed/loaded boundary evidence for the changed tooling. Keep exact host/version/command/diagnostic facts. Source build, package build, executable IDE build and design-time behavior have distinct outcomes. Unsupported custom layouts remain explicit unless deliberately added to scope.

**Traceability:** C03/C04/C07; WP01-S02/S03; relevant WP07-S02 checks; Type Manager TM-001, TM-004–TM-007 and TM-032. Prior Done entries retain their historical scope; fresh-build usability is new acceptance work.

## Batch B Native tests and a useful fast loop

**Change locations:** existing `tests/CIS.Core.*` family projects; `tests/CIS.Core.TestSupport`; `tests/fixtures`; `eng/CIS.Discovery.TestLogger`; `eng/CIS.DependencyChecker`; `eng/verify.ps1`, `Evidence.psm1`, `Verification.psm1`, `Publications.psm1`; `verification/families.json` and runsettings; repository `eng/verify.ps1`, `AGENTS.md`, test README and verification guidance.

First deliver a thin focused launcher selecting an existing project/class/category through ordinary .NET tooling. Incrementally build its dependencies, retain native results and reconcile what the filter actually selected. Reject zero discovered/executed cases, unexpected skips, stale reports and tool failures. A focused result identifies omitted families rather than crediting them as passed. Do not build a dependency-selection framework.

Then migrate by observation, using this starting map:

| Existing verification | Native destination or reuse | Required migration observations |
| --- | --- | --- |
| `tests/fixtures/DiscoveryHost/verify-process.py` (retired in first migration slice) | Existing `DiscoveryProcessTests` and discovery fixture, with TestSupport.ProcessRunner | All seven scenario observations mapped to native checks, including exact runtime, binding, unique IDs across all children, canonical environment isolation and complete parent environment comparison. Twelve native cases pass on Windows/Linux, including five corruption controls. See the [migration record](evidence/corrective-discovery-migration-2026-10-15.md). |
| Other DiscoveryHost readers: configuration, capture, scopes, outputs/files, flush/stop and provider families | Corresponding existing unit/integration/business classes and C# fixture validators | Retain physical-child facts, exact failure reasons, accounting, negative/corruption controls and platform applicability; remove duplicated assertion paths only after reconciliation. |
| `eng/verify-type-manager-behavior.py` | CompatibilityTests for API/source/binary/behavior controls; existing Type Manager unit/business cases for ordinary behavior | Exact candidate identity, frozen consumer behavior, defaults/overrides, error/schema observations and intentionally incompatible controls. |
| `eng/verify-type-manager-examples.py`, `verify-type-manager-package.py`, fixture inventory/runtime matrix utilities | IntegrationTests and architecture/package controls with actual console/Web consumers | Automatic inventory, unused dependency, publish/layout cases, disabled/removal/rename behavior, loaded package identity and supported-profile failures. |
| `eng/verify-type-manager-instrumentation.py` and fixture provenance controls | C# verification fixtures with explicit instrumentation labels | Enumeration/publication observations, actual-source provenance, rejected wrong inputs and separation from unmodified candidate evidence. |
| `eng/measure-type-manager.py`, `summarize-shipping-coverage.py` | Small C# measurement/coverage utilities using existing native reports | Same workloads, warm/cold distinctions, hashes/runtime identity, allocation/timing calculations, coverage denominators and unchanged gate policy. |
| `eng/prepare-type-manager-mutation.py`, `replay-type-manager-mutants.py` | C# preparation/replay utilities around the pinned mutation tool | Exact source/report/mutant binding, compiler outcomes, controls, timeout handling and complete restoration. Keep historical classifications unchanged. |
| Policy-bearing PowerShell modules | Reuse appropriate existing C# verification code or narrowly scoped C# utilities | TRX parsing, discovery reconciliation, publication integrity, coverage calculations and pass/fail policy. PowerShell keeps argument/environment/process launching only. |

Record each active assertion group's current observation, native case or gap, input/candidate kind, OS applicability, control and retirement decision in the existing conformance/acceptance maps. An equal test count is not proof of equivalence. Dormant scripts may be retired after demonstrating all required observations are covered; historical reports and source snapshots stay historical.

**Acceptance:** run selected native tests from CLI and IDE without Python or a prior full checkpoint; verify a deliberately empty filter fails; demonstrate the same acceptance observations before/after migration. Measure cold setup, incremental build, discovery, execution and report overhead separately on the same host. Preserve finite timeouts and test isolation. Do not loosen release thresholds or globally enable concurrent tests.

**Traceability:** C01/C02/C07; WP01-S03/S04, WP07-S01 and WP07-S02; Type Manager TM-026–TM-031. Finish the smallest useful path early, then migrate the remaining active logic in coherent module groups.

## Batch C Recover defaults before refactoring

**Change locations:** `src/CIS.Core/TypeManagement/TypeManagerOptions.cs`, `PreparationInputs.cs`, inventory build targets/helper, relevant Environment/Logging setup and defaults, Type Manager examples, `TypeManagerInputTests`, `TypeManagerStartupStories`, logging configuration/business cases and existing contracts.

Create one before/after behavior table linking original use case, explicit user decision, current contract/code and regression test. Reuse the existing planning/contract documents. Distinguish intended restoration from a newly selected API convenience.

| Behavior to protect or resolve | Current fact | Required case or disposition |
| --- | --- | --- |
| Omitted inventory path | Host-name `.types.json` under AppContext.BaseDirectory | Minimal generated-host startup works without manually assigning a manifest path. |
| Explicit inventory path | Relative paths use the application directory; blank/invalid values fail | Absolute/relative override, changed working directory, null versus blank, invalid path, missing/malformed inventory. |
| Omitted precomputed contracts | Empty list | Preparation works; later queries work without eager query registration. |
| Host selection | HostAssembly is a required options constructor argument | Recover the specifically expected earlier call/use case; do not silently invent entry/calling-assembly inference. |
| Deployment inventory | Build/publish selects deployed runtime assets | Initially unused dependency appears without per-class registration; preserve supported exclusions and deterministic failures. |
| Logging setup and precedence | Public Create and current captured configuration exist | Minimal setup, explicit setup/environment overrides, blank/invalid values, independent destinations and unchanged capture ownership. |
| Configuration object defaults | Separate from assembly discovery | Test property construction/default values independently; do not use Type Manager changes to mask a configuration issue. |

**Decision needed:** a concrete example of the Type Manager startup that should work but no longer does, plus its intended outcome. If original source/decisions recover it, use them; otherwise obtain the user's selection before changing that behavior. No recursive DLL scan, missing-manifest fallback or parameterless API is approved by the review alone.

**Acceptance:** runnable minimal examples, default/override/invalid-input native cases and a visible disposition for every discrepancy. An unresolved historical default blocks its own behavior change, not unrelated build repairs.

**Traceability:** C05; Type Manager TM-002, TM-004–TM-014 and TM-021–TM-026; logging WP03 and WP07-S01.

## Batch D Restore the asynchronous logging pipeline

**Change locations:** `src/CIS.Core/Logging/CoreLogRuntime.cs`, `Runtime/QueuedRuntimeFactory.cs`, `OutputQueues.cs`, `OutputEventQueue.cs`, captured event/encoding/rendering responsibilities, console/file adapters, result/diagnostic types and MicrosoftLogger integration. Reuse the existing scopes, clocks, controlled writers and lifecycle cases.

The proposed execution boundary is: caller-owned capture and bounded incoming admission; background processing; bounded independent writer admission and delivery. Capture mutable values, scope/correlation, occurrence time and caller facts before returning. Never retain a caller-owned object for later processing. Move encoding/rendering and selected extension processing behind the spool, while retaining bounded capture validation.

Introduce one supported writer registration/contract, with console and file as adapters to it. Keep each writer's ownership, ordering, queue capacity, failures and flush obligations explicit. Preserve file-group acquisition, intent-first retirement/recovery and borrowed console ownership.

Before coding, decide how incoming acceptance relates to eventual writer admission, tickets, truncation and failures. Carry a captured flush/stop boundary across both the incoming processor and all resulting writer work; draining only today's writer queues is insufficient. Preserve one shutdown deadline, caller cancellation semantics, immutable completed observations and late cleanup accounting.

**Acceptance:** a test writer implemented separately registers without changing Core orchestration; a blocked processor proves accepted calls return before processing; mutated caller data cannot change captured output; one blocked/faulted writer does not prevent independent writers; flush includes pre-boundary events still in the spool and excludes later events; stop closes admission and accounts for accepted, suppressed, rejected, written, failed and abandoned work under the selected rules. Existing recovery/provider/deadline regressions must continue to pass.

**Traceability:** C06; logging WP04-S04/S05, WP05-S01–S10, WP06 and WP07-S01. Generalization may require deliberate corrections to unpublished result APIs, not compatibility with every prototype shape.

## Batch E Restore extension behavior and convenient calls

**Change locations:** the same logging pipeline, `CoreLogger.cs`, event/result contracts, writer registration, Microsoft provider mapping, shared logging conformance cases and ordinary application examples. Add specific extension types only after their responsibilities are selected.

| Capability | Decisions required before its implementation | Acceptance observations |
| --- | --- | --- |
| Global and destination filtering/routing | Ordering, configuration precedence, destination identity and suppression accounting | Global exclusion, destination-only exclusion, overrides and independent writer progress. |
| Interceptors | Hook order, allowed mutation, failure policy, redaction boundary, recursion rules and registration lifetime | Failures cannot bypass mandatory redaction; recursive logging is bounded; no user callback runs under an admission lock. |
| Duplicate checking | Opt-in/default behavior, occurrence key, scope/window, expiry, memory bound, clock source and suppression counts | Exact repeats suppress as selected; different customer IDs, severity or relevant exception facts remain distinct; window expiry and bounded state work. Template identity alone is insufficient. |
| Info-style convenience calls | Selected overloads, captured values, lazy evaluation and event-identity policy | Real application calls reach the same pipeline, preserve typed values/correlation and avoid disabled-call work under the selected contract. |
| Custom writers | Startup-only versus live registration, ownership/removal and outstanding-work obligations | Separately implemented writer receives correct routed events and participates in flush/stop without special Core branches. |

Memory versus disk spool, capacity/overflow policy and live registration remain explicit product decisions. Do not infer durability, exactly-once delivery, a default deduplication key, specific handler APIs or `template_id` from the attachment. Godot remains outside this proposed corrective scope.

**Traceability:** C06 and the original capabilities identified by the review. Where the current reduced backlog lacks a matching requirement, add a restoration acceptance entry under the existing logging plan instead of claiming an unrelated old task already covers it. WP04-S05, WP05, WP06 and WP08 remain related implementation/consumer anchors.

## Batch F Consolidate and qualify the corrected candidate

Update examples, AGENTS, test guidance, feature status, behavior contracts and existing task/acceptance maps to describe the actual corrected API. Retain historical completion/evidence records with their original scope. Reconcile original capabilities and defaults before freezing the candidate.

Run the normal build matrix and all six verification families against final inputs: unit, integration, business, architecture, mutation and compatibility. Include every shipping implementation and packaged build helper, all supported native lanes, coverage gates, conservative mutation/dispositions, first-release zero-history handling, exact-package consumers, and complete bundle/clean replay. Earlier partial development runs do not qualify a changed candidate. Publication choices and permission remain a separate final action.

**Traceability:** WP07-S01–S06, WP08, WP09 and Type Manager TM-026–TM-036, with formal milestone reconciliation. The old next-task pointer WP07-S03-T01 becomes a qualification dependency of this corrected design, not an automatic instruction to run it now.

## Verification cadence and first execution slice

| Boundary | Run | Omit unless the change requires it |
| --- | --- | --- |
| Edit | Affected incremental project build and selected native unit/fast behavior cases | Full pack, architecture fault matrix, both-platform replay and mutation |
| Coherent batch | Normal solution build, affected and dependent suites, relevant integration/dependency checks; comprehensive combined checkpoint at an intentional integration boundary | Repeating every expensive checker negative after unrelated changes |
| Platform or build-sensitive batch | Applicable CLI/IDE and Windows/Ubuntu checks with isolated outputs | Unrelated full release bundle reconstruction |
| Final candidate | Complete unchanged release obligations and exact artifacts | Nothing required by the final gate |

After implementation is authorized, the first slice should:

1. Identify the branch/revision and preserve unrelated changes; recheck the publication ledger and coordinate shared paths.
2. Record prerequisites and reproduce the discovery-logger/helper build issue from a clean checkout using ordinary Debug/Release builds. Do not choose a package fix before seeing the failing host/path.
3. Repair only the demonstrated cause and run the affected native checks.
4. Deliver B's basic focused invocation and zero-match rejection, then prove it with a Type Manager input class and a logging behavior class.
5. Record the cold/incremental timings and the unresolved C/D/E decisions. Review that coherent slice before broader migration or pipeline work.

Each batch reports behavior preserved/restored, files changed, native checks actually run, checks omitted and remaining issues. Use existing reports and conformance maps; this map introduces no receipt framework, new test framework or second specification hierarchy.

## Source and review record

- Supplied review: `CISLIB-corrective-review-and-handoff-2026-10-15.md`, SHA-256 `f31fd256c2a8478ae21ffc1d1b924efc53093c265a5bbacbdbb4c6972745de19`.
- Current authority and historical boundaries: [decisions](decisions.md), [logging amendments](logging/plan-amendments.md), [feature status](feature-status-2026-10-15.md), [Type Manager status](type-manager/implementation-status.md) and [publication ledger](../compatibility/csharp/publications/ledger.json).
- Concrete source anchors: [verification driver](../implementations/csharp/eng/verify.ps1), [test guidance](../implementations/csharp/tests/README.md), [discovery logger](../implementations/csharp/eng/CIS.Discovery.TestLogger/CIS.Discovery.TestLogger.csproj), [helper targets](../implementations/csharp/build/type-manager/CIS.TypeManager.targets), [Type Manager inputs](../implementations/csharp/src/CIS.Core/TypeManagement/PreparationInputs.cs), [logger API](../implementations/csharp/src/CIS.Core/Logging/CoreLogger.cs) and [runtime](../implementations/csharp/src/CIS.Core/Logging/CoreLogRuntime.cs).
- Retained timing source: [Windows T04 summary](logging/evidence/wp07-s02-t04/windows/summary.json). No build or C# test was run to create this map.
- Current graph navigation reports fresh, 10,106 nodes, 47,016 edges, zero diagnostics, build `3b5c6e1bc9a8552725f74e4b35982f7c03853f82c004928b267e62dc340702b4`. The bounded Write search was truncated; Capture matched uniquely. Direct source inspection supports the findings; a clean graph does not establish complete semantic/test coverage. The feature report's older graph-cache warning is historical rather than a newly observed blocker.

Existing unrelated graph/profile changes and the Web example Properties directory are outside this map's changes. No implementation, test, dependency, original review/source, roadmap completion state or publication record has been edited.
