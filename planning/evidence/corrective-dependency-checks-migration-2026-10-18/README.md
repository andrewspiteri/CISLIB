# Native dependency-check migration — 18 October 2026

The logging roadmap remains paused. Claude was not used. This is the second requested corrective batch, following compiler migration commit 8c186bc4.

The existing CIS.DependencyChecker now owns the dependency policy. check-dependencies.ps1 retains its parameters and only resolves paths, bootstraps and invokes C#. Existing C# boundary tests invoke the native command directly, avoiding repeated PowerShell/build startup. No new project, package dependency or shipping behavior is added.

| Former script control | Native policy |
| --- | --- |
| Selected project and repository inventory | Registered selection, no unregistered maintained projects, pinned SDK agreement. |
| Evaluated project settings | Framework, packability, shipping assembly/package/namespace identity, test enablement and non-test executable fixture roles. |
| Project references | Exact distinct inventory and all three build-only metadata requirements. |
| Framework and manual references | Implicit frameworks and example-only extras, exact pinned SDK origins/non-copying references, approved test-tool NuGet origins, complete build-tool reference inventory. |
| Package references and restored graph | Exact distinct explicit identities, private test/fixture packages, approved role-specific runtime/development closures and versions, required direct/restored-project membership and exact Core closure size. |
| Compiler and compiled assembly | Actual resolved framework/approved NuGet references, reference-name evidence, Core assembly identity and unloaded AssemblyRef inspection. |
| Package contents | Exact asset names, case-insensitive duplicate rejection, allowed metadata path, required Core/helper/SDK assets, source/helper hashes, matching package/SDK version, exact distinct nuspec direct dependencies, compiled packed DLL references and optional checked-DLL equality. |

The existing example-boundary verifier retains its eleven controls; all eleven pass through its standalone entry point on Windows and native Ubuntu (the two results.json reports are retained here). Its role-guard probe now compiles the actual C# checker with only the example-role condition removed. The verifier records the native checker source hash, and the new C# regression demonstrates that the real checker rejects the widened shipping framework while the compiled mutant accepts it. There is no runtime bypass switch. The other Type Manager SDK lifecycle/provenance and example-verifier policies remain a separate migration slice.

Focused Windows and native Ubuntu runs each pass **66 cases**, including the original 51 and 15 new migration cases. Windows run 7c0df37a6eab4e3691df17b777af4f12; Ubuntu 1ccb0b5d48804140b60fe46a62ef3a70. All 27 projects also pass the stable dependency entry point on both lanes. The first Windows selection rejected an older packed helper after the preceding compiler commit changed build identities; repacking the current development candidate restored all positive package controls. No boundary or assertion was weakened.

Focused checks alone do not establish a full checkpoint or release qualification. The complete final checkpoints and their retained audit follow.


## Complete combined checkpoint

Both final native Developer profiles return exit 0, zero failures/skips/validation issues and complete required-case reconciliation:

| Lane | Unit | Integration | Architecture | Business | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows | 3,750 | 2,120 | 729 | 24 | **6,623** |
| Ubuntu | 3,750 | 2,122 | 729 | 24 | **6,625** |

Windows run fa78315e8c8c4cae90869bcbf9a16a22; Ubuntu 686a3d3a62a9497086e995f08f3a3625. All six family records remain: four execute in Developer; mutation and cumulative compatibility remain explicitly NotRun with their existing reasons.

The [C# retention audit](RetainCheckpointEvidence.cs) verifies report/source/inspection/package/reconciliation references, both new method-binding groups (20 compiler plus 15 dependency executions), all six family records and matching recorded/live sets of **861 selected executable/build/configuration/shared-contract inputs**, including **642 C# files**. Canonical publication schemas and the unchanged ledger also match. [Audit](audit.json), [selected input identities](matching-executable-inputs.json), raw [Windows summary](windows-summary.json)/[validation](windows-validation.json) and [Ubuntu summary](ubuntu-summary.json)/[validation](ubuntu-validation.json) are retained here. Complete lane-local reports, source inventories, logs and packages are archived under .artifacts/corrective/dependency-checks-migration-2026-10-18/{windows,ubuntu}-Developer and *-packages.

Both final checkpoints rerun all 59 compiler controls on the final source, including failed-prerequisite handling and the successful SDK-command requirement. Their first preflight attempts stopped at formatting before test families: a single mixed CRLF line in CompilerChecks.cs was normalized to LF on both lanes. Failed preflight logs/summaries and the earlier stale-package focused attempt remain separately under the ignored corrective directory; none receives checkpoint credit. Earlier compiler artifacts were copied to .artifacts/corrective/compiler-checks-migration-2026-10-18/{windows,ubuntu}-contracts before these final runs replaced their default locations.

The retained eleven-control example results use the supported standalone entry point. An initial Ubuntu in-process adapter printed completion but returned its last intentionally failing child exit code; the explicit standalone rerun returned 0. Its earlier output is preserved and is not counted as a successful invocation.

CIS graph build sha256:9d4f239c8b3ed271681409021b5c9ad7b25ca57e4d61f05072e3aa4b7e0728e9 is fresh, valid, strict=false, with 11,411 nodes, 57,044 edges and zero diagnostics. The exact DependencyChecks class resolves uniquely and untruncated in cis-dependencychecker at source line 12. Graph navigation does not establish test completeness.

Windows base is 8c186bc4 plus the recorded task hashes. Native Git metadata remains a4df38e0 and packages are lane-local, so this is not one unchanged release candidate qualified across platforms. Markdown metadata written around execution and user-owned launch settings are outside the selected executable-input equality. No shipping runtime behavior, public API, dependency inventory or publication history changed. No new coverage, full shipping mutation, cumulative released-package compatibility, IDE UI/design-time, offline replay or publication qualification is claimed.

Remaining substantive PowerShell policy includes the separately scoped Type Manager SDK lifecycle/compatibility/provenance checks, coverage collection and bundle/toolchain/replay retention. Stable launchers remain intentionally present. The original roadmap stays paused.
