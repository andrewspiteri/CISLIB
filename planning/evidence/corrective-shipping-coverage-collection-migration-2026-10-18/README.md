# Native shipping-coverage collection migration

18 October 2026. Corrective tooling migration only. The original roadmap remains paused; Claude was not used.

The existing [PowerShell entry point](../../../implementations/csharp/eng/verify-shipping-coverage.ps1) now bootstraps the existing engineering tool and invokes [ShippingCoverageCollection.cs](../../../implementations/csharp/eng/CIS.DependencyChecker/ShippingCoverageCollection.cs). The launcher retains its early focused-fixture refusal to avoid building the shared tool from an invalid focused run. Collection decisions and process execution use C#, the existing source-inventory implementation and the existing coverage summarizer. No shipping implementation, dependency, collector setting or threshold was changed.

## Preserved controls

| Boundary | Native implementation and verification |
| --- | --- |
| Output ownership and focused fixtures | New output only, including rejection of existing empty directories/files; focused inspection overrides fail before reports are written. |
| Inputs and provenance | Pin all repository inputs, tool sources/configuration/binary, shipping sources, DLLs/PDBs, native test DLLs and present shipping copies. Add/delete/change controls protect source membership; byte changes protect binaries, symbols and shared contracts. |
| Developer observations | Require a successful Developer profile; check inspection, startup-reference and package pins. Relative references remain contained. |
| SDK and candidate | Check the exact SDK; evaluate the candidate version; require exactly the shipping DLL inventory in the archive and equality with Release binaries and every loaded copy. Missing Core copies fail; absent optional helper copies remain permitted. |
| Collection | Preserve all four native families, Release/no-build/no-restore arguments, Microsoft Cobertura collection, retained family logs and command records. Child processes have finite deadlines; node reuse is disabled for them without changing the caller's environment. |
| Completion | Recheck package, source membership and all input pins before the unchanged native summarizer. A complete threshold-failing measurement returns 2 and remains unqualified. Invalid evidence cannot produce a passing report. |
| Policy paths | Reject escaped source/project/settings paths, duplicate assemblies/entries and escaped family names, including dot segments. |

[ShippingCoverageCollectionTests](../../../implementations/csharp/tests/CIS.Core.ArchitectureTests/ShippingCoverageCollectionTests.cs) supplies **40 native policy cases**, all passing on Windows and Ubuntu. The existing parser/aggregation tests remain unchanged. Required case group `ARCH-NATIVE-SHIPPING-COVERAGE` binds seven methods and 40 executions on each lane; the C# audit verifies those bindings in the complete reconciliation.

## Complete Developer checkpoints

| Lane | Run ID | Passed | Failed/skipped/issues |
| --- | --- | ---: | ---: |
| Windows | `aebfa15c6f0f4a13926b3f3dfee86693` | 6,747 | 0 |
| Ubuntu 24.04 | `b3a0722d34bd4c049dec1bfff04f5e1c` | 6,749 | 0 |

Each checkpoint retains all six family records: unit, integration, architecture and business execute; mutation and cumulative compatibility remain explicitly NotRun with their declared reasons. Formatting, build, contract, dependency/package and publication checks pass, with complete required-case reconciliation.

**872 selected maintained code/build/configuration/shared-contract inputs match, including 653 C# files.** Both publication schemas and the unchanged ledger match too. Windows base is `aae140d9` plus the recorded task hashes; native Git metadata and lane-local package bytes differ. This does not qualify one unchanged release package across lanes. Planning evidence, Markdown and the user-owned launchSettings.json are outside the selected executable-input equality.

The first checkpoint attempts were stopped for the final dot-segment correction. A Windows retry encountered a transient shared-tool DLL lock left after interruption; those processes ended and the subsequent complete checkpoint passed. Superseded/failed logs remain under the ignored artifact root; only the run IDs above receive passing credit.

The first Windows collector attempt failed two isolated fixture-build deadlines: 851 of 853 architecture cases passed. Both failures reproduced alone with the default compiler server. Setting UseSharedCompilation=false for the collector process made both cases pass in eight seconds under the same collector, without changing either deadline. The fresh complete Windows retry records that scoped setting and its observed MSBuild property in [windows-coverage-launch.json](windows-coverage-launch.json). Test selection, shipping bytes, instrumentation, exclusions and thresholds are unchanged; failed/default and passing/isolated control TRX hashes remain in the audit and local archives.

## Fresh native coverage

| Lane | Assembly | Lines | Branches (lower bound) | Per-assembly gate |
| --- | --- | ---: | ---: | --- |
| Windows | CIS.Core | 96.27% | 91.47% | Pass |
| Windows | CIS.TypeManager.Build | 78.85% | 52.86% | Fail |
| Ubuntu | CIS.Core | 95.86% | 90.14% | Pass |
| Ubuntu | CIS.TypeManager.Build | 78.85% | 52.86% | Fail |

All four native collector families execute on each lane, with **6,747 Windows / 6,749 Ubuntu native cases**, zero failed or skipped cases. Each collection binds its exact Developer package and verifies unchanged source/input pins before summarizing. Per-assembly qualification remains authoritative; an aggregate pass cannot hide a failing helper.

Both measurements return **qualified=false and native exit 2**. The Windows aggregate passes (95.82% lines / 90.52% branches), while Ubuntu's aggregate does not (95.41% / 89.22%); the failing helper independently prevents qualification on both. Add meaningful native C# helper qualification cases and recollect; do not remove the helper or lower the unchanged 95% / 90% gates. The C# audit rechecks all 51,031 Windows and 50,653 Ubuntu pinned inputs before retention.

## Retained evidence and navigation

[RetainFinalCheckpointEvidence.cs](RetainFinalCheckpointEvidence.cs) is the executed C# retention audit. The earlier prepared RetainCheckpointEvidence.cs is retained unchanged because Ubuntu's source snapshot already pins its bytes; it was superseded before retention ran. [audit.json](audit.json) records the checkpoint, package, coverage and input checks; [matching-executable-inputs.json](matching-executable-inputs.json) retains the selected input hashes. Raw lane summaries, validation reports, coverage observations, candidate/environment records, collector commands and native exits are alongside them.

Complete Developer trees, packages, collector TRX/Cobertura attachments, input snapshots and logs are retained locally in `.artifacts/corrective/shipping-coverage-collection-migration-2026-10-18`. The native Ubuntu originals remain in the dedicated verification checkout. These local archives are not a portable offline release-verification kit.

CIS graph schema 2, build `sha256:7046282b8030763e83f14c8542ed558cd3638c0e818f3a04af790fb974ef4cd6`, is valid/fresh after the code and guide changes: 11,593 nodes, 58,149 edges, zero errors/warnings/information, strict=false. Extractor cis.csharp.compiler/6 provides privacy-safe invocation argument shapes and intraprocedural call ordering; neither implies complete semantic dataflow.

Exact roots are CIS.Verification.ShippingCoverageCollection and CIS.Tests.ArchitectureTests.ShippingCoverageCollectionTests; the latter was disambiguated from its nested Fixture using the full ID. Fresh find queries use limit 3. The collector's outgoing depth-one/limit-12 context returns two nodes and one component-ownership traversal, untruncated, excluding proposed edges. This bounded ownership context does not establish caller/test completeness. An earlier mid-edit query of ShippingCoverage correctly warned that the graph was stale; final navigation uses the rebuilt graph. Derived .cis/local state is not committed. Native reports and the C# audit remain execution evidence.

Passing these migrations does not complete full shipping mutation, cumulative released compatibility, complete Release profiles, publication or offline retention/replay qualification. Five assessed substantive PowerShell scripts remain: verify-microsoft-logging.ps1, verify-toolchain.ps1, record-toolchain-inputs.ps1, replay.ps1 and write-microsoft-logging-bundle.ps1. The 27 maintained PowerShell files include intentional launch/orchestration glue. See the [current feature report](../../feature-status-2026-10-15.md) and [assessment follow-up](../corrective-powershell-assessment-2026-10-16.md).
