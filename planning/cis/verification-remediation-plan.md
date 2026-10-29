# CIS testing execution and evidence remediation plan

**Status:** Proposed · **Date:** 29 October 2026

Restore a predictable development loop and a reusable verification workflow before continuing Healthcheck mutation qualification. The work covers shared CIS execution, graph selection, caching and evidence, together with CISLIB's test structure and fixtures. PARR provides a working reference for project-level execution and behavior-oriented tests.

Mutation execution remains paused. This plan starts no campaigns, changes no approved gate and authorizes no publication. Keep the single `CIS.Core` assembly and existing product contracts. Replace or simplify the problematic verification subsystems rather than rewrite the whole library.

## Findings that determine the work

- The reported ordinary solution run executed 11,028 cases, failed 21 and took 742.8 seconds, with 760 seconds including build. Its project durations overlap and must not be added together. A passing run with that cost would still be unsuitable for routine edits.
- The ordinary suite includes stress measurements, child processes, real timeouts, compiler controls, repeated packaging and consumers of earlier checkpoint artifacts. One architecture class rebuilds a complete candidate for each of 15 cases; a retained earlier run spent approximately 313 aggregate case-seconds in that class.
- The latest frozen Unit mutation campaign completed 15 of 41 files. Their 458 executable mutants had 424 Killed, 28 Survived and six Timeout outcomes. These are partial historical findings, not a current whole-module qualification score.
- Those 15 native mutation invocations consumed approximately 125 minutes summed across parallel workers. Seven zero-mutant files consumed 45 minutes; three one-mutant files consumed another 25 minutes. These totals describe invocation time, not elapsed campaign time or CPU time.
- Pinned Stryker 4.16 instruments and compiles all Core source before applying its mutation file filter. CIS currently launches that work once per file. PARR normally launches once per smaller source project.
- PARR's local runner also excludes string mutations and several project categories, and does not enforce the CISLIB 90% gate. Its release threshold and timeout accounting differ. Reuse its practical execution patterns without silently copying different acceptance rules.
- Healthcheck has approximately 5,890 production C# lines, 32,955 Unit-test lines, 9,472 Integration-test lines and 17,998 lines of qualification tooling. Counts include comments and fixtures. The ratios warrant a design review; they do not prove that every test is redundant.
- Missing child-session configuration invalidated the apparent ASP.NET detections. The timer-transfer baseline also exposed a race in the test's stack observation. Neither problem should have been discovered after expanding campaigns.

## Existing work to preserve and revalidate

| Change | Current status | Treatment |
| --- | --- | --- |
| Process output capture, commit `c68066a` | Implemented and tested | Preserve; include cancellation and output limits in the end-to-end pilot. |
| Stable reuse across SSH sessions and healthy Unit refreshes, including `1d9efc4` | Implemented | Preserve and exercise unchanged reconnect/restart cases. |
| Bounded diagnostic mutation allowance, `3fd277a` | Implemented | Keep the original default; explicit diagnostic reruns remain separate from original reports. |
| Recovery without repeating completed native mutation, `aa4ef01` | Tested locally and on Ubuntu | Preserve the original native report, execution and finish time. The Ubuntu recovery passed; its invalid child-run findings still receive no product credit. |
| Native work cleanup, retained-log limits and cache caps | Implemented in the newer runner | Extend lifecycle coverage across maintained entry points; these controls do not prove that every legacy artifact path is bounded. |
| Timer-transfer live-frame observation | Local repair; 1,451 Healthcheck cases pass | Validate on Linux and reconcile affected cached results after a fresh graph. |
| Child-session producer and receiver | Component checks pass; actual captured run pending | Review against the simplified evidence contract before adoption. Do not expand this into another module-specific framework. |
| Source-variant diagnostics | Primitive implemented; first current diagnostic graph attempt timed out before tests | Keep optional and off the normal path. Do not require repeated full graphs merely to pursue incidental survivors. |

Existing native reports retain their original sources, inputs and policy. Review pending changes before committing them. Preserve unrelated work in either repository, including the existing shared CIS graph changes; do not stage it as part of this plan without ownership review.

**29 October publication checkpoint:** Removed transient Healthcheck archive payloads from unpublished history before pushing; preserved the original commits and all 364 current archives locally without copying their 6.6 GB of data. Source, tests, documents and native report metadata remain unchanged by that filtering. Historical receipts that reference those archives require the local payloads for replay; a fresh checkout alone cannot replay them. This storage correction does not revalidate or qualify historical results.

## Work order and ownership

| Work | Owner | Depends on | Deliverable |
| --- | --- | --- | --- |
| R0 Preserve the paused baseline | Coordinator | None | One compact status and measurement baseline; no active campaigns. |
| R1 Repair the ordinary test structure | CISLIB | R0 | Self-contained development tests and deduplicated required verification suites. |
| R2 Simplify evidence and retention | Shared CIS, with CISLIB migration | R0 | One reusable result contract and bounded artifact lifecycle. |
| R3 Group native execution by backend cost | Shared CIS | R2 contract | Bounded multi-source invocations with reusable source findings. |
| R4 Correct selection and cache costs | Shared CIS | R0; coordinate with R3 | Precise invalidation and inexpensive validation phases. |
| R5 Prove adapter and child execution | Shared CIS adapter support and CISLIB integration | R1, R2; final execution through R3 | Small genuine native controls with healthy restoration. |
| R6 Validate the complete workflow | Coordinator | R1–R5 | Measured clean, unchanged, changed and interrupted runs. |
| R7 Resume Healthcheck qualification | CISLIB | R6 passes | Current 46-file assessment and explicit remaining release work. |

R1 and the R2 contract can proceed in parallel. Graph precision work in R4 can proceed independently where files and interfaces are disjoint. Serialize R3/R4 edits to shared execution and identity code. Coordinate R5 only after the evidence contract is stable. Use one build stream per checkout; isolate worktrees when build outputs would otherwise collide.

### R0 Preserve the paused baseline

Confirm that no mutation controllers or children remain active. Record recovery holds and unfinished groups without restarting them. Preserve current native results and a compact list of their source, test and tool identities. Do not make another full repository or evidence copy.

Use existing reports for the initial timing baseline. Instrument the next small validation to distinguish graph loading, selection, hashing, report parsing, build, test execution, mutation compilation, recovery and publication. Avoid another 12-minute run solely to rediscover known structural costs.

**Exit:** the coordinator can explain what is saved, current, stale, incomplete and held, with actual time and storage units. The historic 160 GB total has no defensible complete breakdown; do not invent one.

### R1 Repair the ordinary test structure

Separate fast product Unit/component tests, fast architecture rules, verification-tool Unit tests, runtime/process integration, stress/measurements, and package/toolchain/compatibility qualification. Keep `implementations/csharp/CisLib.slnx` as the ordinary developer solution: plain `dotnet test` there and IDE Run All on that solution select the fast, self-contained projects. Put the complete verification solution below `verification/` so a second solution does not make the ordinary command ambiguous. Its explicit command/CI workflow selects all required verification projects. Move checker tests into a verification-tool test project referencing the checker rather than linking its implementation throughout product test projects. Ordinary developer tests must work after supported restore/build without an earlier Developer checkpoint, a manually chosen Release package or hidden environment overrides.

Remove repeated baseline preparation. Build and pack one immutable candidate per matching source snapshot, configuration, framework/runtime and toolchain, produce one inspection, and pass those explicit inputs to consumers. Never mix Debug/Release artifacts or share a mutable global candidate. Refactor the 15 inspector scenarios so they reuse that preparation; compile only the variant that actually needs different binary behavior. Test detailed rejection rules with small data where a real compiler or package is unnecessary. Retain a small set of real end-to-end inspector controls.

Move the 5,000-cycle stress loops, forced-GC measurements, saturation matrices and real process-timeout exercises out of the default Unit loop. Retain small deterministic checks of their behavioral invariants. Keep necessary expensive suites required in the appropriate affected CI and release workflows. Moving classes alone is not a performance fix: reduce duplicate builds and invocations in the complete pipeline too.

Audit Healthcheck tests against existing requirements and fault classes. Consolidate repeated fixtures and overlapping matrices. Prefer injected clocks, controlled leases, explicit completion signals and public results. Isolate the few necessary interruption, private-boundary and GC probes. Keep captures and tool security checks outside ordinary behavior fixtures where possible. Do not add public production APIs solely for tests.

Apply the timer-transfer repair and validate the affected cases on Windows and Linux. Decide the diagnostic-message guarantee once: use a small common assertion if explanatory reasons are required; otherwise review the exact misses as noncritical. Do not invent equivalence or pin English prose merely to raise a score.

Compatibility jobs must prepare and verify their pinned ApiCompat/runtime inputs before starting their matrix. Missing prerequisites should produce a prompt, actionable failure. Preserve the required compatibility checks rather than converting them to skips.

**Exit:** the default loop is self-contained, required suites have explicit owners, and both the ordinary and complete verification paths perform substantially fewer baseline builds.

### R2 Simplify evidence and retention

Define a shared result contract containing the actual input/tool identity, selected scope, original native report reference, execution outcome and timings, storage measurements, and the reason for reuse or invalidation. Record a graph/projection identity once per relevant snapshot. Preserve original reports and native statuses; source findings may reference the same group report without copying it for every file.

Use the approved prospective native-outcome policy. Completed native failures, including product exceptions, do not require an assertion-word whitelist or a bespoke semantic proof. Infrastructure failures, unverified child activation, aborts and cleanup failures remain holds. Timeouts receive no kill credit. Keep raw and separately credited rerun outcomes distinct.

Retire prospective dependence on per-mutant C# proof catalogues, repeated source archives and chains of historical runner installations where a self-contained native receipt suffices. Keep necessary survivor review as compact data tied to exact definitions. Preserve historical records under their original policy; do not rewrite them into current results.

Give every maintained producer an owned artifact root, an aggregate quota and a cleanup policy. Include copied source trees, concurrent scratch, reports, child captures and failure diagnostics in accounting. Shared SDK/NuGet installations are measured separately; they are not evidence. Never clear unrelated work or a shared cache while another job uses it.

Delete successful scratch immediately. Retain only active reusable cache entries and bounded diagnostics for unresolved failures. Prune resolved or superseded diagnostics after confirming that active results no longer reference them. A shared native report remains while any source finding references it. Keep a compact release record rather than every intermediate attempt. Offer a bounded cleanup inventory before removing historical material. Reserve aggregate storage atomically across concurrent producers; independent free-space checks cannot prevent simultaneous overcommit.

**Exit:** maintained normal, failure, cancellation, restart and recovery paths all respect their declared ownership and storage limits; no entry point silently creates an unbounded attempt archive.

### R3 Group native execution by backend cost

Decouple the unit of native execution from the unit of cached findings. Group selected source files sharing a compiled project and test context into one bounded Stryker invocation. Run the selected test union once, instrument/compile once, and restore/postflight once per native group. Keep `CIS.Core` as one assembly.

Preserve Complete target mutation scope, all required files and the original native report. Empty/interface-only files remain accounted for in the full inventory without paying for an independent whole-Core invocation. An empty completion needs explicit inclusion and a verified zero-definition result; an absent entry, ignored population or compile-error population cannot be relabeled empty. Native IDs are local to their report; compare source/operator/span/replacement definitions when producers renumber, never numeric IDs alone.

Save per-source findings with the actual group scope, test union and dependency identities. Use stable group recipes rather than a new whole-campaign identity for every launch. A relevant change may legitimately invalidate its whole native group; preserve unaffected groups. A file finding cannot claim independence from tests or dependencies that contributed to its group result. Groups may cross logical module boundaries when they share a compatible build/test context.

Checkpoint each completed group atomically. Never start another group when its estimated native work and cleanup cannot fit the remaining budget. A time-budget stop leaves pending work rather than deliberately clipping the next compilation. Unknown costs need a conservative small pilot. Recovery-only failures must reuse admissible completed native work.

Keep build/startup, per-test/per-mutant and cleanup budgets separate. The current 15-second Stryker setting is an additional allowance, not a 15-second limit for compiling and testing a whole batch. Explain these semantics in status output. Diagnostic extensions remain explicit and cannot turn an incomplete execution into a kill.

Reuse official backend capabilities first. If grouping still cannot meet the agreed budget, evaluate an early-filter backend change in isolation, comparing exact target definitions and outcomes. A new backend producer requires explicit compatibility review; do not silently replace the pinned tool or relabel old results.

**Exit:** a low-density multi-file sample uses one native compilation, retains the same required target definitions, and resumes without repeating completed groups.

### R4 Correct selection and cache costs

Profile the repeated `Read`, `Select`, `Unchanged`, producer-history and storage-accounting paths. Reuse one immutable graph/projection and deduplicated file hashes within a validation phase, with currency checks at the execution boundary. Compute Unit and mutation identities together where their inputs overlap. Avoid reconstructing the complete old/new selection repeatedly for each stage or history link. Bound the cost of repeated directory scans and storage polling while preserving actual quota enforcement and physical-path safety checks.

Audit project/assembly identity, linked test sources and external metadata edges. Investigate broad dependencies such as qualification tooling entering a product test closure; do not assume they are either correct or false. Ambiguous bindings must be visible. Integrate separately owned graph fixes only after review.

Prove that unrelated changes retain valid results, related code/shared-fixture changes invalidate the correct scope, and added/deleted tests enter discovery. Preserve meaningful configuration, native-filter, tool and environment dependencies. Keep the SSH/session fix. Do not ignore changed inputs merely to increase reuse.

Explain each invalidation briefly. Report native invocations, build count, executable mutants, empty verified files, reused/current findings, unfinished work and holds. File count alone must not represent qualification progress. An incomplete full scope still has no final gate score.

**Exit:** selection/reuse decisions are reproducible, correctly scoped and meet the warm metadata budget below.

### R5 Prove adapter and child execution

Choose and freeze a supported runner/adapter combination for each target. PARR's xUnit/MTP success is a useful reference, not proof that the same combination fixes MSTest. Keep the current compatibility overlay explicit, bounded and pinned; document its maintenance and retirement criteria. Share runner/adapter controls across compatible modules; each module adds only the small canary or child-path control needed for its actual boundary.

Before expanding a campaign, demonstrate a genuine product kill, a genuine survivor/control, an unmodified passing baseline and healthy restoration. An infrastructure failure that reports every mutant Killed must fail this preflight. A canary requiring only a positive kill count is insufficient.

For Pipes/ASP.NET, exercise the actual producer and receiver with a small native run. Set the mutation session only on the inner mutation process; normal baseline/recovery/postflight use uninstrumented outputs. Verify actual child origin, mutated body, selector and terminal lifecycle under the existing contract. Preserve the distinction between neutral Unit-only sessions and actual child propagation. Resolve baseline-only nonempty sessions through structured audit behavior, not English-message exceptions or synthetic audit successes.

Do not introduce new per-child test-GUID or per-mutant replay requirements beyond the maintained contract. Native killing-test identities still require their normal source binding. Child refinement credit needs its own verified rerun evidence; an original child audit cannot authorize it.

**Exit:** the real Windows/Linux paths pass their applicable controls, reject missing or wrong session setup, preserve original native reports and clean up within the cap.

### R6 Validate the complete workflow

Run the following small acceptance matrix before any full Healthcheck campaign. Use named hosts, pinned versions and fixed scope; report cold setup separately from warm execution. The budgets are proposed engineering targets, not achieved results.

| Scenario | Proposed acceptance target |
| --- | --- |
| Clean checkout and prerequisites | Supported restore/build and explicit verification preparation need no prior checkpoint. Missing tools/runtimes fail promptly before the expensive matrix, with a precise resolution. Measure cold setup separately. |
| Warm ordinary Unit/component and fast architecture tests | At most 30 seconds of test execution, and 60 seconds including an unchanged incremental build. |
| Required non-mutation regression on the current platform | Target at most five minutes after initial build for ordinary tests, checker Unit tests, runtime/process integration, package/architecture controls and compatibility. Required extended stress, platform and measurement jobs have explicit additional budgets; report their time and the entire release critical path as well. |
| Warm plan/status or unchanged-cache check | At most five seconds; zero native test/mutation reruns when compatible results already exist. |
| Low-density native sample | Combine the recorded seven empty and three one-mutant files, or an equivalent fixed sample, into one native invocation. Target at most ten minutes on the named Ubuntu host with unchanged target scope and accounting. |
| Small relevant edit | Execute only invalidated groups/work and report the exact cause; preserve unaffected completed findings. |
| Unrelated edit or SSH reconnect | No unexplained result invalidation or native rerun. |
| Cancellation, process failure or reboot | No leaked processes/work trees; repeat only unfinished work; recover completed native work without rerunning it when admissible. |
| Evidence storage | Initial pilot ceiling of 2 GiB across all pilot-owned copies, concurrent scratch and retained evidence, plus a 2 GiB free-space reserve. Enforce aggregate capacity when choosing concurrency. |
| Concurrent storage admission and cleanup | Two producers approaching the aggregate limit cannot both overcommit it. Retiring one source does not delete a shared report still referenced by another. |
| Reuse across modules and Core growth | Exercise the shared path on a second representative or neutral target without a new module-specific verification engine. Measure a fixed target with increasing unrelated Core source: grouping alone must not be advertised as solving Stryker's project-wide instrumentation cost. |

Keep the existing per-phase defaults as starting limits: 512 MiB native scratch and 256 MiB ordinary retained cache. Previously proposed child exceptions are explicit maxima, not normal usage targets: ASP.NET 384 MiB retained/256 MiB child evidence; Pipes 1 GiB retained/896 MiB child evidence. Reduce concurrency or duplicate content before increasing a ceiling. Adopt final budgets only after reviewing the pilot's actual workload and measurements.

The historical 70 minutes for low-density files was summed across concurrent invocations. Use it as context, not a controlled speedup benchmark. Compare future speedups on the same host, scope and concurrency. Do not spend another 70 minutes recreating the old workflow merely for a headline comparison.

Do not meet a budget by dropping required behavior, weakening assertions, hiding failures, crediting timeouts, or reporting unexecuted suites as passing. Fixes to genuinely incorrect tests must preserve the intended requirement and pass their relevant controls.

**Exit:** one compact acceptance summary contains actual results for clean, warm, changed and interrupted workflows, with remaining failures explicit. Component test counts alone do not satisfy this exit.

### R7 Resume Healthcheck qualification

Freeze the corrected candidate, execution policy and tool versions. Refresh its graph and reconcile saved results once. Reuse only findings whose actual inputs remain valid. The latest 15 completed file batches belong to the earlier frozen candidate; the timer-fixture change means that some require reassessment.

Complete the 41 Unit/runtime, four Pipes and one ASP.NET file inventory through the new grouped execution path. Prioritize real behavioral survivors and unexplained execution failures. Treat incidental diagnostic strings through the agreed common policy. Keep scope omissions, infrastructure failures and unresolved cleanup visible.

Apply the approved 90% conservative full-scope gate without averaging file percentages. Keep no timeout kill credit, exact native report/definition identity, and explicit survivor dispositions. Complete the other applicable coverage, compatibility, platform, package and integration requirements from the existing Healthcheck plan; reaching mutation 90% alone does not approve a release or external integrations that remain unexecuted.

Retire superseded diagnostic paths and duplicate transitional code after the new workflow has passed its pilot and migration checks. Update commands, documentation and status together. Commit reviewed changes in coherent repository-specific chunks; publication remains a separate action.

## Delivery discipline

Use a short task table derived from R0–R7 rather than a new orchestration framework. Assign one owner to shared project/configuration changes and serialize native builds that share outputs. Parallel work should follow disjoint code ownership and measured CPU, memory and disk limits.

At each completed work package, report the actual behavior changed, relevant checks, wall time, peak/retained bytes, reusable results and remaining holds. Keep the plan current with small status updates; avoid creating another evidence archive or a separate document for every mutant.

The first implementation wave is R1 and the R2 contract, with bounded R4 profiling alongside them. Complete that profiling before performance-sensitive R2/R3 implementation. R3 follows the stable result contract. Broad mutation resumes only after R6, not after the next isolated fix passes.

## Reference material

- [Ordinary builds and focused native tests](../../docs/architecture/csharp-focused-tests.md)
- [Unit project and linked fixtures](../../implementations/csharp/tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj)
- [Architecture project and verification-tool links](../../implementations/csharp/tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj)
- [Current shared-result receiving rules](../healthcheck/qualification/shared-incremental-receiving.md)
- [Existing Healthcheck task and release scope](../healthcheck/task-plan.md)

Shared CIS implementation references are `IncrementalTestService.cs`, its selection/input/storage/refinement/recovery partials, and the native process guard. PARR comparison references are its root mutation scripts, source project boundaries, `mutation-enforcement.json` and release configuration. Keep this cross-repository operational plan in the private repository; shared CIS documentation must use neutral examples.
