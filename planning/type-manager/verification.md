# First Type Manager slice: verification

Latest additions, 14 October 2026: [package lifecycle](evidence/package-lifecycle/README.md)
passes 57 checks per OS against the unchanged development package; [query ordering](evidence/query-ordering/README.md)
passes 80 focused native cases per OS against its separately identified frozen
Core candidate. Earlier sections below remain dated historical checkpoints.

Executed on Windows x64, 13 October 2026, using .NET SDK 10.0.401 and runtime
10.0.12. This is an implementation-start checkpoint, not a completed Type Manager
milestone or release qualification.

| Check | Observed result |
| --- | --- |
| Inventory build/lifecycle regression script | 18 checks passed. |
| Focused Type Manager architecture tests | 5 passed, 0 failed, 0 skipped. |
| Full `pwsh -NoProfile -File eng/verify.ps1` Developer profile | Exit 0, no verification issues. |
| Unit family in Developer run | 3,174 passed, 0 failed, 0 skipped. |
| Integration family in Developer run | 1,257 passed, 0 failed, 0 skipped. |
| Architecture family in Developer run | 433 passed, 0 failed, 0 skipped; includes the 5 new tests. |
| Evaluated project/dependency policy | 15 projects passed; Core retains exactly its 2 approved runtime packages. |
| Formatting and Release-configuration build | Passed in the Developer run; build had 0 warnings/errors. |
| `eng/verify.ps1 -Profile Release` | Exit 2 as expected: required release evidence remains missing; the release gate was not weakened. |

Developer run ID: `dd335e66d097420480e1d0a6bb4072fa`.
The Developer profile also passed its existing startup/source compatibility
contracts, publication-ledger checks, pack/package boundaries and example host.
The dedicated mutation, compatibility and business test families are not enabled
in this Developer profile; this result does not claim all six release families.

The logging chat was active in the same checkout. This run checked the combined
working tree, including its uncommitted logging work. It is not an immutable source
replay or proof for either eventual commit in isolation. No logging edits were
authored or staged by this Type Manager change.

## Retained evidence

Small tracked evidence is under `evidence/inventory-start-windows/`: inventory
results, build/publish manifests, focused native TRX, Developer validation result,
bounded graph queries and hashes of Type Manager/shared integration sources.

Detailed local evidence is under `.artifacts/type-manager/`:

- `inventory/`: executable regression logs, generated manifests and published host.
- `tests/type-manager-boundaries.trx`: focused architecture execution.
- `repository-verification/`: a copy of the completed Developer report directory,
  preserving its native reports and contract outputs before logging reuses the
  shared verification directory.
- `repository-verify.log`: actual full-run command output.
- `release-gate.log`, `release-validation.json`: expected failing Release-profile
  check, run ID `e159414b89cf44e9939037139a095010`.
- `graph-after.txt`, `graph-execute.txt`, `graph-related.txt`: graph refresh and
  bounded evidence. The refreshed graph reported 8,601 nodes, 34,911 edges and 79
  extraction warnings. The exact `WriteTypeInventory.Execute()` query matched one
  symbol without truncation; the call query found one traversal. This is partial
  semantic coverage, not proof of the complete SDK build graph.

The supplied ZIP SHA-256 is
`eee355bdb158d09e40ceb403c5875315f34ec849c4e08731c7a5095b612ac3da`.
The files retained under `sources/` are unchanged design inputs.

## Remaining qualification

M1 remains open. Native/RID package selections, the Linux build/publish matrix,
remaining fixture variations and the runtime manifest reader still need work.
The Type Manager public API, loader, runtime catalog, caching/query behavior,
cancellation, examples, mutation, historical/binary compatibility, performance and
packed-consumer release checks are not implemented or qualified here. Neither
successful Developer verification nor the pack's version number authorizes a
release or marks the original roadmap complete.

## Runtime promotion checkpoint — 13 October 2026

The earlier results above describe the initial inventory-only slice. Core runtime
promotion now supersedes its statements that the runtime and baseline do not exist.
The full Developer run above has not yet been repeated against the promoted runtime.

| Current check | Result | Local report |
| --- | --- | --- |
| Actual Core Type Manager unit tests | 62 passed, no failures/skips | `.artifacts/type-manager/core-promotion/type-manager-core.trx` |
| Inventory lifecycle including actual discovery and mid-load cancellation | 30 passed | `.artifacts/type-manager/inventory-runtime-qualified/results.json` |
| Windows canonical loader/generator scenarios | 8 runtime + 2 generator cases passed | `.artifacts/type-manager/runtime-canonical/windows/results.json` |
| Ubuntu frozen managed binary replay | Same 10 cases passed | `.artifacts/type-manager/runtime-canonical/linux/results.json` |
| Initial API compatibility, unchanged compiled consumer, source rebuild/run | 3 checks passed | `.artifacts/type-manager/compatibility-canonical/results.json` |

Runtime replay and compatibility used Core SHA-256
`52e7e273b39ec45a8d9e5daf9f90e216aec07988b4c4271aae633af286c79810`.
The unchanged consumer SHA-256 is
`cc12ab193a64987855b39426ec64fb10d1878ba2ae4f4f1e808a93ba7e3dc491`.
Ubuntu replay runs the frozen managed binaries under the verified SDK 10.0.401;
it does not claim a Linux source rebuild or a package-consumer test.

Reproduce runtime scenarios with
`python implementations/csharp/tests/fixtures/TypeManager/verify-runtime-processes.py --repo-root . --output-root .artifacts/type-manager/runtime-recheck`.
Use `--replay-root <previous-build-output>` for unchanged managed binary replay.
Run compatibility with `implementations/csharp/eng/check-type-manager-compatibility.ps1`
and `-CoreAssembly` pointing at a candidate DLL alongside its dependency DLLs.

A clean isolated package-import probe failed NETSDK1047 on both initial and repeated
builds: NuGet restore excludes the package props that infer RID. Logs are in
`.artifacts/type-manager/early-package-import/windows`. This is an unresolved
package setup defect, not an expected successful supported profile. Source import
results cannot be used to claim TM-032 or M6 complete.

## Reviewed runtime checkpoint — 14 October 2026

The follow-up review led to a two-pass loader (validate all selections before any
new load), rejection of dynamic bind/reuse candidates, reentrancy-safe cache
publication and consistent path/identity argument diagnostics. New process
regressions fail against the previous DLL and pass against the reviewed candidate.

Current retained evidence is under `evidence/runtime-promotion/`: 72 focused unit/
runtime architecture tests pass on Windows and Ubuntu; 13 runtime/generator
process checks pass on each; API, source and unchanged-binary compatibility pass.
These all use Core SHA-256
`e5ac309a81db3342df379437c0d6e49fdfa12702c052e70a30c2a051ffe39b63`.
This supersedes the earlier 62-case and ten-process checkpoint for runtime review
coverage, without rewriting its historical logs. The new unit count is 63 plus
9 architecture cases; the five build-boundary tests passed separately earlier.

The normal combined UnitTests project briefly failed discovery because a
BuildProjectReferences=false run combined new logging reference metadata with an
older normal-output Core DLL. No passing result is claimed for that invocation.
The scoped verifier now compiles canonical Type Manager tests into an isolated
harness referencing a frozen actual Core DLL, so concurrent logging output cannot
mask or create runtime results. A coordinated whole-Developer run remains required.

The canonical Web SDK Debug inventory matrix passed nine cases per OS after task
hosting/reference-detection hardening. Dependency checking passed all 21 registered
projects with the existing two-package Core runtime closure. A final runtime review
is pending; the broader roadmap gates remain open.

## Final runtime review closure — 14 October 2026

`evidence/runtime-closure/` supersedes the earlier runtime checkpoints with Core
SHA-256 `53619d263b200fa3d53bb5d9455dfa599110ce83ad30b83448b480bd542e1cb3`:
72 focused unit/architecture cases and 14 process/generator cases pass on each OS.
Initial API/source/unchanged-binary compatibility passes with corrected independent
log attribution. The final targeted Claude review returned PASS; its incorrect
reasoning about a collectible-context fixture is documented alongside the corrected
noncollectible fixture and actual negative controls. Ordinary discovery/cancellation
also passes on both platforms against this same frozen Core binary.

The coordinated combined Developer run `c99f0dea97d7415f821bec2e607eba94` passed
restore and the 21-project dependency boundary, then stopped at fixture formatting.
No build/test-family result is claimed for that failed attempt. Type Manager's four
namespace-spacing issues were fixed; the logging owner is formatting its fixture.
Later attempts caught duplicate native IDs in nine Type-valued DynamicData cases
and a logging architecture assertion that still expected the old runtime surface.
Type Manager now uses nine stable string DataRows with unchanged coverage; the
isolated verifier also uses the repository's strict native TRX reader. The logging
owner updated its architecture assertion and added its shutdown integration tests.

## Coordinated Developer checkpoint — 14 October 2026

Run `06673e19d022465091e1f241285f454f` completed with exit 0 on Windows:
3,251 unit, 1,276 integration and 452 architecture tests passed, with no failures
or skips. Restore, 21-project boundaries, format, build (zero warnings/errors),
contract/compiler consumers, publication ledger, pack, package boundaries and the
existing host smoke test passed. Mutation, business and the separate compatibility
test family are not enabled by this Developer profile; no pass is inferred for them.

The immutable local copy is
`.artifacts/type-manager/combined-developer-06673e19d022465091e1f241285f454f/`.
`evidence/combined-developer/checkpoint.json` records the run, source inventory,
actual combined Core hash, stages, counters and native report hashes. It describes
an uncommitted development checkpoint, not the frozen 53619d runtime candidate,
a portable evidence bundle or package release qualification.

An earlier isolated build-only package experiment also passed clean build, publish
--no-build, host execution and late-flag rejection on Windows and Ubuntu.
`evidence/package-sdk-restore-proof/` retains the prototype: an SDK-owned second
restore supplies the inferred RID. That design is not adopted. The subsequent
package implementation uses an optional MSBuild SDK in the same CIS.Core package
to make RID inference visible during first restore, without nested restore.

## Inventory review corrections — 14 October 2026

The subsequent source-inventory review found disabled package-copy handling,
relative publish-path resolution and stale-output/broad-error test hazards.
The corrected source integration passes 32 Windows lifecycle checks and a
seeded stale-publish negative control. Native Ubuntu passes nine Web SDK CLI
matrix cases plus default relative publish and disabled-copy rejection checks.
The targeted read-only follow-up returned PASS. See `evidence/inventory-review-fixes/`
for original findings, qualified results, corrections to the Linux verifier and
the precise unsupported-profile limits. These results postdate the combined run.

Git attributes preserve Type Manager input/evidence and compatibility baseline
bytes. All six frozen baseline hashes survived an indexed checkout unchanged.

## Package delivery and review corrections — 14 October 2026

The actual CIS.Core package now includes its optional SDK import, inventory targets
and build-only helper. The SDK participates in first restore and supplies an exact
matching runtime reference, including central package management. No build target
performs a second restore. Source integration remains available.

`evidence/package-delivery/` retains per-platform exact-package reports, native
architecture results, manifests, available command logs and read-only reviews.
Both platforms pass 35 checks against identical package/verifier hashes. The final
Linux JSON stdout survived, but its temporary detailed logs did not; an earlier
same-package workspace run is retained separately, with the limitation documented.
The producer
solution built with zero warnings/errors. There are 34 passing architecture and
dependency cases, including an unchanged package and deliberate changes to SDK,
helper and valid Core DLL bytes. A separate check confirms dependency inspection
leaves all 42 helper source/build files unchanged. Source build and no-build
publication still pass after the shared validation timing correction.

The consumer matrix covers console and Web hosts, fresh parallel restore/build,
forced manifest regeneration, unchanged incremental output, default folder publish
without building, cross-RID selection, parallel no-restore, forced offline locked
restore, central version matching/mismatch, host-only SDK guards, replaced hooks,
SDK pinning, disabled integration and five deployment settings through actual build
and publish commands. Core and helper hashes are checked at the consumer. The final
review returned PASS after the stated corrections and negative controls.

Cross-RID restore replaces a project's assets graph; a later native no-restore
build is expected to fail until native restore runs. Concurrent different-RID work
against one project/intermediate directory is unsupported. Static-graph restore,
Visual Studio task hosting and its separate PublishUrl stage are not qualified.
These results do not complete all six verification families or authorize release.

## Expanded runtime acceptance — 14 October 2026

`evidence/runtime-acceptance/` retains 77 native cases and 17 process checks on
each platform against the same unmodified Core bytes. New assertions cover
generic/non-generic equivalence, shared successful and failed awaiters, cancellation
after success, report element replacement, both manifest conflict orders, deployed
lookup misses, and fixed membership after an actual later assembly load.

A separate test-only source copy wraps the sole `GetTypes` call and injects a
callback immediately before the final cancellation check. Five checks per OS
record one enumeration per selection per preparation, no enumeration during
concurrent queries, and cancellation without publication at that boundary. Three
deliberate faults (double enumeration, query enumeration and a removed final
cancellation check) are rejected for their expected diagnostic. Two additional
controls reject mismatched candidate bytes and source records before building.
These targeted controls do not constitute the complete mutation family.

The unmodified producer uses `--no-incremental`. Its completed report records the
Core hash, all Core C# file hashes, and five named project/config hashes. The
instrumentation verifier compares that subset before copying and checks copied
bytes. This is accidental-mismatch protection using a trusted producer report;
it is neither a full transitive build-input attestation nor proof that the
instrumented binaries equal the unmodified DLL. Linux replays the exact Windows
managed outputs and the same workspace source bytes through `/mnt/c`, avoiding
checkout line-ending differences. Native reports, command logs and review
findings/corrections are retained. The candidate includes the coordinated logging
working-tree snapshot; this is not a clean release build or portable release bundle.

## Runnable host integration — 14 October 2026

`evidence/host-integration/` retains 66 passing checks on Windows and 66 on native
Ubuntu against identical example/verifier inputs and the same exact development
package. Source builds are recorded separately. Each lane exercises console and
Web hosts in Debug/Release build, Release folder publish and publish without
building. Every layout checks initial unused-library discovery, inherited/nonpublic
matching, fixed membership after a real later load, and three broken deployments.
Failures require exit 1, the specific file path and original failure detail on
stderr before any builder/container/application boundary. Each lane's Web Release
build also serves two HTTP requests with proven scoped consumer identity and exact
prepared-manager/query-cache reuse.

No global opt-in property masks the example project files: the verifier runs their
documented local setup. Child processes use controlled environment settings and
actual restored assets must identify only the private feed/cache. Core DLL hashes
are checked in package consumer output; helper hashes are checked in the private
package cache, and tooling is forbidden in deployment. The root solution's locked
restore, formatting, build and checker across all 26 registered projects pass in
an isolated Windows snapshot, with zero build warnings/errors. Eleven separate
boundary checks include a targeted mutant proving the example-only framework role
guard matters. This is an integration checkpoint, not full mutation qualification
or the final six-family release bundle. The source build includes the coordinated
logging working tree; no logging changes belong to the example commit.

## Frozen behavior and schema compatibility — 14 October 2026

`evidence/compatibility-behavior/` retains 86 passing checks on each supported OS
against the same frozen runtime-acceptance Core. The original six-file API baseline
is unchanged. A new unpublished baseline supplies 22 authored snapshots and raw
schema fixtures. Native source rebuilds against the original API reference must
reproduce its frozen consumer DLL byte for byte. Candidate-source rebuilds and
an unmodified standalone TypeManagement module each pass the same 22 cases.

Nine source mutants produce ten specified behavior regressions; a separate public
property rename is rejected by API, unchanged binary and source checks after the
matching module positives pass. Three provenance controls reject mismatched Core,
baseline and source-record inputs before compatibility execution. Reports record
the exact dependency/tool/runtime-policy/input hashes and observed outputs.

Read-only reviews strengthened bindable-unselected lookup, baseline inventories,
negative controls and reproducibility. The test consumer uses explicit fixed
metadata and omits generated source/default Win32 manifest bytes that vary by OS
or Git revision. This qualification is separate from the package candidate and
the combined Developer run; final package replay and the six-family gate remain.

## Performance observations — 14 October 2026

`evidence/performance/` retains 56 fresh Windows and 56 fresh native Ubuntu
processes against identical deployed managed bytes. Each lane has 48 measured
observations in four balanced catalog/mode groups after two retained warmup rounds.
The 334/4,366-type inventories include Core and both approved runtime dependencies.
Bootstrap/first-use regions execute identical preparation at different points.
Preparation, first queries, 20 warmed cached-query batches, allocations and retained
process memory are recorded with exact runtime, SDK, CPU/OS and JIT metadata.

Read-only reviews corrected warmup, counter scope, memory semantics, timeout logs
and deployment provenance. Three mismatched-input controls reject before measuring;
two controlled timeout representations preserve the original error and partial log.
An independent audit recomputes group summaries and batch aggregates. The separate
enumeration-counter evidence matches this candidate's producer/source hashes; the
timed DLL is not instrumented. Observations do not promise a steady JIT tier, cold
OS file cache, isolated manager memory, application latency or a speedup. Native
lanes run serially on one workstation, with Linux consuming `/mnt/c` under WSL.
Full release qualification and final clean-package replay remain separate.
