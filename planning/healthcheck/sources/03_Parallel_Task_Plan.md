# Parallel implementation task plan — Healthcheck pilot

**Revision:** 0.1 · **Status:** Proposed assignments; none dispatched or executed.

## 1. Operating rule

The Technical Intent and contract catalog settle shared behavior. This plan gives each active task exclusive write ownership, true start prerequisites, and separate verification requirements. A file's implementation owner is not the owner of every test and document associated with that feature.

The manifest `tasks.json` is a draft planning format, not a claim about an existing CIS import endpoint. Use the current CIS schema only after checking it in a separately authorized tool-integration task. The pilot can be run with the repository's existing task/branch workflow; building a new scheduler is not a prerequisite.

### Status vocabulary

**Proposed → Ready for authoring → In progress → Authored → Implemented/compilable → Locally verified → Integrated → Accepted.** Tests and documentation do not pretend to have runtime implementation status: they move from authored/compilable to verified against the actual candidate. Use Blocked with a concrete reason where needed. Accepted is scoped to each task's stated deliverable; it does not imply overall release qualification.

A reference-only compilation validates shape. A mock validates its seam. A passing native test validates the actual code/revision/environment it executed. None automatically validates the combined candidate or an unavailable external integration.

## 2. Shared-file coordination

### 2.1 Read freely from stable revisions; serialize mutations

Use separate Git worktrees and normal supported output layouts. Every task pins the HC00 receipt. Ordinary reading needs no long-lived lock because the input revision is stable. Do not hold a repository read lock for a feature, restore, or full test suite.

For a genuinely shared mutable checkout, take a short read lease to capture a consistent set of build inputs, or use a writer-excluding resource lease for an operation that really reads the live mutable files. A separate worktree is preferred. Git worktrees share repository metadata, so ref updates/integration still require coordination. Build outputs, fixed ports, pipe names, process-wide test resources, generated files, and local graph caches are also resources; file edit ownership alone does not isolate them.

The current Type Manager/build tooling has layout constraints. Do not introduce arbitrary shared OutputPath/obj redirections merely to parallelize tests. Use isolated standard worktree paths, unique local fixtures/endpoints, and separate artifact receipts first. A focused run and comprehensive checkpoint using the same physical outputs must not overlap.

### 2.2 One build-graph transaction queue

The protected set includes solution/project files, project references, central package versions, NuGet configuration, package lockfiles, SDK pins, common imports/targets, assembly attributes/global usings, relevant verification dispatch files, and dependency allowlists. It also covers renamed/deleted versions of protected files. The coordinator is the only writer while applying a transaction. Agents submit **desired changes**, not stale replacement copies.

Each request records requester/task, exact affected paths, reason, expected baseline revision/hashes, desired semantic edit, dependent tasks, and the minimum validating restore/build/checks. Related project/reference/package/lockfile edits are one coherent transaction. Conflicting requested package versions are a decision to resolve, not a text merge to guess.

The coordinator processes ready requests FIFO unless a documented dependency prevents application. It validates preconditions, applies the coherent change to the latest integration baseline, performs the necessary focused build-graph validation in an isolated candidate, then serializes promotion. It publishes the new revision and requests acknowledgements from affected workers. Expensive unrelated test suites run outside the mutation lease. Short promotion leases protect the shared ref; an isolated candidate's restore/build need not hold a repo-wide editing lock.

Use a bounded lease/explicit owner and a generation token if automated. A heartbeat alone does not fence a stale writer: reject stale generation/base revisions at promotion, and confirm the old writer cannot still commit before takeover. Never blindly delete a timed-out lock and permit both writers. The initial pilot can use one human/coordinator-controlled queue without a distributed lock service.

### 2.3 Change contracts through the same discipline, not the same giant lock

The contract owner revises one canonical artifact and records changed C01..C09 sections, impacted tests/tasks, and the new hash. Only affected workers pause their dependent work and acknowledge the new baseline. Documentation and tests change with the decision. Unrelated implementation continues. No agent silently expands its write scope to change a shared interface.

Locking a file prevents simultaneous edits; it does not automatically prevent two implementations from assuming different behavior. Baseline pins and targeted contract updates address the latter.

## 3. Foundation and milestones

| Milestone | Content | What it does not prove |
| --- | --- | --- |
| M0 — HC00 foundation | Reconciled approved draft choices, compiled contract surface, verified build graph, concrete ownership, common fixture primitives. | Runtime correctness or passing unimplemented tests. |
| M1 — first integrated proof | Real critical/noncritical monitors, threshold/recovery/disposal, readiness, immutable snapshot, serializer and HTTP branch; matching native tests. | Async collection, pipe/platform behavior, real Heartbeat, or release qualification. |
| M2 — Healthcheck feature candidate | Actual owner-safe collection, pipe/client, fact binding, full local business proof and corresponding native tests/docs. | A real external coordinator/scheduler if it is absent, or historical compatibility that was not run. |
| M3 — external integration evidence | Real Heartbeat activation/retirement/OnError/final handoff and eventual IStartable coordinator cases at their actual integration boundary. | Publication authorization. |
| M4 — accepted release candidate | All required applicable source cases, six verification families, claimed platform lanes, exact package/example evidence, with genuine publication-history treatment. | Permission to publish without authorization. |

These are integration checkpoints, not mandatory serial authoring waves. After M0 all task write sets below are available for parallel authoring. HTTP can be authored against the frozen source/formatter surface while Core is implemented; wire tests can be authored before either adapter; docs/examples can be authored before execution. Verification waits for the listed real artifacts.

### Practical capacity allocation

Start with a modest number of active contributors and adjust from measured constraints rather than launching all fourteen task IDs at once. One possible four-contributor start is core/state, reporting/HTTP, independent core/transport tests, and documentation/remaining contract fixtures. The coordinator completes shared setup then spends most time on implementation/integration, not a full-time approval bureaucracy. Collection and binding work can start earlier when capacity permits; the contract—not an artificial “provider complete” dependency—allows it. A contributor can own sequential tasks without granting concurrent writers access to the same files.

The mutable state coordinator is deliberately one implementation task. Splitting each monitor into several writers sharing one registry would recreate the conflict this pilot is testing. The catalog and runner/binding/report seams create useful independent work without creating a project per agent.

## 4. Task index

| ID | Outcome | Role | Start for authoring |
| --- | --- | --- | --- |
| HC00 | Reconcile, freeze contracts, and establish the shared build baseline | Contract/build coordinator | Source/repository review |
| HC01 | Implement monitor policies, coherent state, snapshots, and withdrawal | Core runtime owner | HC00 |
| HC02 | Implement owner-safe bounded asynchronous collection | Collection implementation owner | HC00 |
| HC03 | Implement shared bounded snapshot and pipe-protocol serialization | Reporting codec owner | HC00 |
| HC04 | Implement the reserved ASP.NET health branch | HTTP implementation owner | HC00 |
| HC05 | Implement local pipe server and the matching probe executable | IPC implementation owner | HC00 |
| HC06 | Implement execution-fact binding and prepare the real Heartbeat handoff | Execution binding owner | HC00 |
| HC07 | Author and execute native core policy and snapshot tests | Core test owner | HC00 |
| HC08 | Author and execute native collection, race, and lifetime tests | Collection test owner | HC00 |
| HC09 | Author and execute native HTTP/pipe/wire tests | Transport test owner | HC00 |
| HC10 | Author execution-binding and business lifecycle integration cases | Execution/business test owner | HC00 |
| HC11 | Write API documentation, usage examples, and deployment guidance | Documentation/example owner | HC00 |
| HC12 | Qualify architecture, mutation, compatibility, and release evidence | Qualification owner | HC00 |
| HC13 | Integrate candidates, reconcile acceptance, and evaluate the pilot | Integration coordinator | HC00 |

## 5. Detailed assignments

All paths are repository-relative **proposals for new Healthcheck work**, aligned to inspected existing roots. HC00 must check for existing/uncommitted files and establish ownership before using them. Shared protected patterns are excluded from every normal task claim. Evidence goes only to that task's dedicated directory. Reading another task's code is allowed; modifying it requires a recorded handoff or a request to its owner.

### HC00 — Reconcile, freeze contracts, and establish the shared build baseline

**Role:** Contract/build coordinator  
**Start prerequisites:** Existing definition and current repository access.  
**Contract inputs:** C01, C02, C03, C04, C05, C06, C07, C08, C09, C10.

**Exclusive write set:**

```text
contracts/healthcheck/v1/**
implementations/csharp/src/CIS.Core/Healthcheck/Contracts/**
implementations/csharp/src/CIS.Core/Healthcheck/Internal/Contracts/**
implementations/csharp/verification/contracts/healthcheck/**
implementations/csharp/tests/CIS.Core.TestSupport/Healthcheck/**
conformance/healthcheck/common/**
planning/healthcheck/technical-intent.md
planning/healthcheck/contract-catalog.md
planning/healthcheck/ownership.json
planning/healthcheck/baseline.json
planning/healthcheck/decisions.md
planning/healthcheck/evidence/HC00/**
```

**Deliverable:** Approved/reconciled API and wire/configuration baseline; Real interface/enum/detached DTO/default/options-resolution declarations; nonshipping reference facade for not-yet-implemented behavior; Bounded native test support and project/fixture/probe scaffolding; Atomic shared build-change receipt and all task input revisions.

**Verification prerequisites (not authoring blockers):** Actual foundation restore/build and nonshipping reference compilation.

**Completion checks:** Read current AGENTS/decisions/amendments; preserve working edits and active corrective scope; Resolve adapter packaging and configuration-entry-point choices explicitly; Compile reference-only shapes separately from shipping Core; no executable placeholder pass; Restore/build the actual affected graph; record ordinary build success separately from runtime cases; Validate disjoint write claims and enumerate shared edits.

**Shared-file requests:** Approved optional HTTP adapter framework/project identity; Probe and minimal HTTP/CountryList fixture/example projects; Solution links, test references, package policy/lockfile updates only where actually required; No new runtime package in Core and no SDK/tool upgrade.

### HC01 — Implement monitor policies, coherent state, snapshots, and withdrawal

**Role:** Core runtime owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C02, C03, C04, C05, C09.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core/Healthcheck/HealthcheckService.cs
implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/**
implementations/csharp/src/CIS.Core/Healthcheck/Internal/Monitors/**
planning/healthcheck/evidence/HC01/**
```

**Deliverable:** Real condition/failure/numeric/availability monitors; Atomic aggregate/readiness/revisions and immutable snapshots; Weak lifetime registry and disposal/reservation barriers; Implementation of the frozen collector and atomic execution-binding state seams.

**Verification prerequisites (not authoring blockers):** HC00.build; HC07.native-core-tests; M1.candidate.

**Completion checks:** All core policy/default/capacity cases run against actual Core; No owner callbacks/I/O/cancellation callbacks under registry gate; No duplicate health state in collector or transport; Immediate removal and later settlement both hold.

### HC02 — Implement owner-safe bounded asynchronous collection

**Role:** Collection implementation owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C03, C04, C09.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs
implementations/csharp/src/CIS.Core/Healthcheck/Internal/Collection/**
planning/healthcheck/evidence/HC02/**
```

**Deliverable:** Inert registration plus explicit manual/periodic activation; Bounded rotating/coalesced dispatch and join semantics; Lease acquisition, invocation boundary, stale-result protection, real cleanup/drain.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC08.native-collection-tests; M2.candidate.

**Completion checks:** No one-waiting-Task-per-monitor design; Cancellation of a wait never releases a real slot; Withdraw-before-entry suppresses owner code; all acquired holds settle; Old successes and old errors cannot overwrite new direct reports.

### HC03 — Implement shared bounded snapshot and pipe-protocol serialization

**Role:** Reporting codec owner  
**Start prerequisites:** HC00  
**Contract inputs:** C03, C06, C08, C09.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core/Healthcheck/Reporting/**
planning/healthcheck/evidence/HC03/**
```

**Deliverable:** One snapshot writer used by HTTP and pipe; Bounded complete report/error serialization; Strict shared framing/schema reader helpers and detached buffers.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC09.native-reporting-tests; M1.candidate; M2.candidate.

**Completion checks:** No live state read during serialization; Bounds enforced while writing and before success commitment; Strict malformed/duplicate/version/precision fixtures; No outcome reinterpretation in transport.

### HC04 — Implement the reserved ASP.NET health branch

**Role:** HTTP implementation owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C06, C07, C09.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core.Healthcheck.AspNetCore/Healthcheck/**
planning/healthcheck/evidence/HC04/**
```

**Deliverable:** Early reserved branch extension and middleware; GET/HEAD, 200/500, 403/404/405, details policy, cache headers; Request-local cancellation and detached complete response.

**Verification prerequisites (not authoring blockers):** HC00.http-project; HC03.report-codec; HC01.core-runtime; HC09.native-reporting-tests; M1.candidate.

**Completion checks:** Real host verifies zero business-side-effect execution for all reserved paths; /_healthcare remains ordinary routing; HEAD and GET select same response/overflow status; No ASP.NET reference in generic Core.

### HC05 — Implement local pipe server and the matching probe executable

**Role:** IPC implementation owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C06, C08, C09.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core/Healthcheck/Pipes/**
implementations/csharp/tools/CisHealthProbe/**
planning/healthcheck/evidence/HC05/**
```

**Deliverable:** Explicit local server lifecycle and bounded connection handling; Real split-read framed request/response protocol; Probe CLI with exact 0/1/2 outcomes and one total budget.

**Verification prerequisites (not authoring blockers):** HC00.probe-project; HC03.report-codec; HC01.core-runtime; HC09.native-reporting-tests; M2.candidate.

**Completion checks:** Real Windows/Linux endpoint tests; slow/many client cases; No malformed/missing/timeout result exits zero; No unbounded waiting-client task queue; Verify permissions on pinned runtime and preserve actual handler settlement.

### HC06 — Implement execution-fact binding and prepare the real Heartbeat handoff

**Role:** Execution binding owner  
**Start prerequisites:** HC00  
**Contract inputs:** C02, C03, C05.

**Exclusive write set:**

```text
implementations/csharp/src/CIS.Core/Healthcheck/Execution/**
implementations/csharp/src/CIS.Core/Heartbeat/HealthcheckIntegration/**
planning/healthcheck/evidence/HC06/**
```

**Deliverable:** Binding-created/external monitor ownership rules; Suitability validation before activation; Bounded terminal deduplication, phase/settlement facts, fault isolation; Adapter source only against a reconciled actual Heartbeat API; no substitute scheduler.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC10.native-execution-tests; M2.candidate.

**Completion checks:** Fact contract qualifies against real monitors; Terminal duplicate and settlement races covered; No policy arithmetic copied into Heartbeat; Real adapter evidence distinct from fact-binding evidence.

**External limits:** Actual Heartbeat API/lifecycle must exist before compiling/qualifying its adapter; Do not add absent Heartbeat implementation into Healthcheck scope.

### HC07 — Author and execute native core policy and snapshot tests

**Role:** Core test owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C02, C03, C09.

**Exclusive write set:**

```text
implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Core/**
conformance/healthcheck/core/**
planning/healthcheck/evidence/HC07/**
```

**Deliverable:** Independent C# policy/identity/aggregate/configuration/capacity tests; Snapshot immutability, counters, Unicode bounds and weak-lifetime cases; Contract-derived portable core case vectors.

**Verification prerequisites (not authoring blockers):** HC00.options-and-contracts; HC01.core-runtime; M1.candidate.

**Completion checks:** Compile-only stage explicitly distinguished from runtime results; Assertions come from BRD/catalog, not production policy helpers; Native selected tests discovered and executed; zero/skips not pass.

### HC08 — Author and execute native collection, race, and lifetime tests

**Role:** Collection test owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C03, C04.

**Exclusive write set:**

```text
implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Collection/**
implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Collection/**
conformance/healthcheck/collection/**
planning/healthcheck/evidence/HC08/**
```

**Deliverable:** Controllable lease/collector scenarios using common frozen test support; Race tests for withdrawal, posted wrappers, direct reports, cancellation, fair capacity; Actual originating-scope dependency lifetime evidence.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC02.collector-runtime; M2.candidate.

**Completion checks:** Use deterministic barriers/clocks rather than timing guesses; Prove no concurrent callback and no early scope disposal; Both stale failure and stale success discarded; Noncooperative collector stays accounted until actual completion.

### HC09 — Author and execute native HTTP/pipe/wire tests

**Role:** Transport test owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C03, C06, C07, C08, C09.

**Exclusive write set:**

```text
implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/**
implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Reporting/**
implementations/csharp/tests/fixtures/HealthcheckHttpHost/Program.cs
conformance/healthcheck/reporting/**
planning/healthcheck/evidence/HC09/**
```

**Deliverable:** Independent wire fixtures including invalid inputs and large integers; Real HTTP branch/status/body/HEAD/access tests; Real pipe/client framing/budget/exit tests and shared-snapshot parity.

**Verification prerequisites (not authoring blockers):** HC00.http-fixture; HC01.core-runtime; HC03.report-codec; HC04.http-adapter; HC05.pipe-client-server; M2.candidate.

**Completion checks:** Test actual Kestrel host/pipe endpoint at claimed integration boundary; No JSON generation using production writer as expected oracle; Validate payload bytes and overflow before headers/frame; Record platform-specific runtime/permissions evidence.

### HC10 — Author execution-binding and business lifecycle integration cases

**Role:** Execution/business test owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C02, C04, C05, C07, C08.

**Exclusive write set:**

```text
implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Execution/**
implementations/csharp/tests/CIS.Core.IntegrationTests/Healthcheck/Execution/**
implementations/csharp/tests/CIS.Core.BusinessCases/Healthcheck/**
conformance/healthcheck/execution/**
conformance/healthcheck/business/**
planning/healthcheck/evidence/HC10/**
```

**Deliverable:** Binding ownership/deduplication/fault isolation tests; Country-list priming/refresh/readiness/shutdown proofs; Separate real Heartbeat and future startup-coordinator evidence receipts.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC02.collector-runtime; HC06.execution-binding; HC04.http-adapter; HC05.pipe-client-server; M2.candidate.

**Completion checks:** No callback-success redefinition or invented freshness rules; Startup failure has a retained coordinator-owned monitor; Mocks demonstrate only their seam, never real scheduler/host acceptance; External HC-AT obligations stay visibly pending.

**External limits:** HC-AT-38 real Heartbeat activation; HC-AT-43 real future IStartable coordinator; Real Heartbeat portions of HC-AT-32..36, 52, 54, 59.

### HC11 — Write API documentation, usage examples, and deployment guidance

**Role:** Documentation/example owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C02, C03, C04, C05, C06, C07, C08, C09.

**Exclusive write set:**

```text
docs/modules/healthcheck/**
implementations/csharp/examples/healthcheck/CountryList/**
planning/healthcheck/evidence/HC11/**
```

**Deliverable:** Public usage/error/default/lifetime/transport documentation; CountryList executable development example with explicit startup sequence; Kubernetes HTTP and exec-probe fragments with permissions/timeout notes.

**Verification prerequisites (not authoring blockers):** HC01.core-runtime; HC02.collector-runtime; HC04.http-adapter; HC05.pipe-client-server; HC06.execution-binding; M2.candidate.

**Completion checks:** Mark examples drafted until compiled/executed against matching candidate; Do not implement a generic startup coordinator in the example; No watchdog/forced-abort promise or native Kubernetes pipe field; Documentation follows same frozen schema and does not copy divergent contracts.

**External limits:** Actual Heartbeat example activation, when the scheduler is available.

### HC12 — Qualify architecture, mutation, compatibility, and release evidence

**Role:** Qualification owner  
**Start prerequisites:** HC00  
**Contract inputs:** C01, C02, C03, C04, C05, C06, C07, C08, C09.

**Exclusive write set:**

```text
implementations/csharp/tests/CIS.Core.ArchitectureTests/Healthcheck/**
implementations/csharp/tests/CIS.Core.CompatibilityTests/Healthcheck/**
implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/**
compatibility/csharp/healthcheck/**
planning/healthcheck/qualification/**
planning/healthcheck/evidence/HC12/**
```

**Deliverable:** Native dependency/direction/public surface checks; Critical-invariant mutation coverage and current compatibility fixtures; Six-family Windows/Linux qualification record for exact candidate/package.

**Verification prerequisites (not authoring blockers):** M2.candidate; HC07.native-core-tests; HC08.native-collection-tests; HC09.native-reporting-tests; HC10.native-execution-and-business-tests; HC11.executed-examples; M3.external-evidence.

**Completion checks:** No release-gate reduction for speed; Original history immutable; no invented published baseline; Package/source/binary/config/wire cases tied to actual candidate hashes; New verification utilities C#; existing shell entry points only invocation glue.

**Shared-file requests:** Any native checker dispatch/verification-profile/allowlist edit via queue; Publication ledger update only under separate publication authorization.

### HC13 — Integrate candidates, reconcile acceptance, and evaluate the pilot

**Role:** Integration coordinator  
**Start prerequisites:** HC00  
**Contract inputs:** C10.

**Exclusive write set:**

```text
planning/healthcheck/integration/**
planning/healthcheck/pilot-results/**
planning/healthcheck/acceptance-status.json
planning/healthcheck/evidence/HC13/**
```

**Deliverable:** M1/M2 combined candidates and native evidence; Per-case acceptance with no lost source requirements; Queue/critical-path/rework/cost measurements and findings; Final clearly scoped implementation/integration/release status.

**Verification prerequisites (not authoring blockers):** M1.candidate; M2.candidate; M3.external-evidence; HC12.qualification.

**Completion checks:** Integrate in short coherent increments; owners repair their own files; No green branch result represented as tested combined revision; External pending evidence not silently waived; Do not claim speedup without a matched comparison; report observed timings honestly.

**Shared-file requests:** Only coordinator applies approved shared-file transactions; Global README/AGENTS/solution changes only when actually needed.

## 6. Native test development and evidence

Use the existing focused launcher from repository root after native test classes exist:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Filter 'FullyQualifiedName~Healthcheck'
pwsh -NoProfile -File eng/test.ps1 -Project IntegrationTests -Filter 'FullyQualifiedName~Healthcheck'
```

These commands are selected-run examples, not commands executed in this deliverable. Prefer the specific class/category owned by the current task during an edit. Confirm current launcher options, pins, native fixture prerequisites, and case names; an empty filter is failure. At a coherent checkpoint run the existing `eng/verify.ps1` workflow and its required profiles without rewriting thresholds. A focused pass is not a substitute for unexecuted coverage/mutation/platform/release checks.

Every receipt records source/contract/build hashes, exact command, selected tests and outcomes, runtime/platform, fixture/port/pipe/output identities, and whether the subject is reference-only, mocked, real source, combined source, or exact packaged candidate. Do not use stale binaries with --no-build when inputs changed. Reuse evidence only when its relevant inputs and required integration context are unchanged; invalidation must include contracts, project/package graph, fixtures, runtime and verification configuration, not just edited source files. Final combined/release gates still run as required.

Test authors can submit compile-only native test source before production exists. Label runtime status Pending instead of creating fake successful implementations or weakening assertions to make early branches green. Incomplete executable tests can be staged in nonshipping development branches/reference builds; do not add shipping placeholder behavior or blanket test skips to the shared accepted baseline.

## 7. Preflight and ownership enforcement

Before dispatch, check each active task has one coherent outcome, a concrete write set, frozen shared inputs, justified prerequisites, actual verification requirements, and no hidden shared decision. Normalize paths (including case collisions for Windows); reject overlapping directory globs, rename targets, generated file collisions, and unassigned shared changes. Record declared shared-resource needs such as builds, ports, pipes, fixtures and output directories.

At handoff, compare the actual Git diff to the allow/exclude set. An out-of-scope file must be routed to its owner/coordinator; it is not excused because the change looks small. Do not modify the original BRD or old evidence to match a new implementation. Fix ambiguous contracts at the shared baseline.

The document checker accompanying this package checks the finite task IDs, path-prefix claims, authoring DAG, local links, and coverage of 59 source cases. It does not inspect actual future diffs, expand a live repository's generated outputs, prove semantic independence, or enforce locks. Those are dispatch/integration duties.

## 8. Pilot measurements and decision rule

Record event timestamps for foundation start/approval, task start/blocked/resumed/submitted/integrated, shared-change requested/applied, contract revision, native check start/end, and each accepted milestone. Use one task-owned append-only receipt or coordinator merge rather than a log file every worker rewrites.

Report design-to-M1, design-to-M2, and design-to-accepted elapsed time separately; active implementation time; waiting for shared files/contracts/integration; rework specifically caused by changed assumptions; native build/test/checkpoint time; token/agent/tool cost where available; human interventions; combined-only defects; and preservation of required behavior/defaults. Distinguish useful throwaway experiments from avoidable rework.

The first pilot establishes observed bottlenecks and feasibility. It cannot honestly supply a causal speedup without a matched baseline. For later comparison, use independent clean starts with equivalent requirements, models/budgets, acceptance cases, infrastructure, and hidden solution knowledge; include design/integration/failure/retry time and incomplete attempts. Do not compare this detailed plan with a deliberately disorganized parallel baseline.

Keep the mechanism only where it reduces total accepted-delivery time or material failure risk. If shared-file queue time dominates, pre-create/coalesce structural changes. If contract changes dominate, improve the TI. If integration dominates, stop starting more work and integrate. If validation dominates, improve correct selection/reuse without dropping required gates. Do not respond to a bottleneck by automatically adding another orchestration framework.
