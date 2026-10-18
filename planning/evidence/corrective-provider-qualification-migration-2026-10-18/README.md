# Native Microsoft logging adapter verification migration

18 October 2026. Corrective tooling migration only. The original roadmap remains paused; Claude was not used.

The existing [PowerShell entry point](../../../implementations/csharp/eng/verify-microsoft-logging.ps1) now launches [MicrosoftLoggingQualification.cs](../../../implementations/csharp/eng/CIS.DependencyChecker/MicrosoftLoggingQualification.cs) in the existing engineering tool. Coverage and Mutation commands, the three-file matrix slice, native tools, provider filters, report names and thresholds remain. No shipping implementation, public API, runtime dependency or Type Manager implementation changed.

## Preserved controls and corrections

| Previous script responsibility | Native replacement and verification |
| --- | --- |
| Matrix/source selection | AdapterPaths requires exactly one logging-microsoft slice and exact physical source membership. Missing, extra, duplicate and foreign inputs fail. |
| Recorded commands | RunAsync retains arguments, working directories, exits and logs. The pinned SDK is checked before execution. |
| Coverage observations | Coverage resolves exactly one native TRX attachment, rejects conflicting copies and missing/unexecuted files, and merges generated classes by source line using maximum observations. Conflicting branch denominators fail. XML DTDs are prohibited. |
| Native test execution | Positive counters must show every case executed and passed. Empty, failed, skipped or incomplete runs fail. |
| Mutation observations | Mutation requires exactly the three matrix files and nonempty mutants. Only Killed, Timeout, Survived and NoCoverage enter the testable denominator; ignored/noncompiling mutants receive no kill credit. |
| Isolated source ownership | Existing C# SourceInventory safely enumerates current maintained inputs, including working-tree deletions. Copied files are hash-checked, snapshot inputs are rechecked, and the unmutated copy is rebuilt even after a failed mutation gate. Canonical adapter and verification inputs are checked too. |
| Shared provider catalogs | The snapshot now includes conformance/logging. The four catalog-reading provider unit tests find their owned copied catalogs before any parent checkout. The C# audit verifies those four catalog pins and every retained snapshot file. |
| Native console bytes | Windows Stryker emits mixed-encoding diagnostic output. Original stdout/stderr bytes are retained and hashed as .bin files; mutation.log is a UTF-8 diagnostic rendering with replacement. Structured JSON/XML validation remains strict. |
| Output ownership | Existing files and directories, including empty directories, are rejected; previous reports remain immutable. |

[MicrosoftLoggingQualificationTests](../../../implementations/csharp/tests/CIS.Core.ArchitectureTests/MicrosoftLoggingQualificationTests.cs) supplies **55 native C# controls**, all passing on Windows and Ubuntu. Required group ARCH-NATIVE-PROVIDER-QUALIFICATION binds nine methods and all 55 executions in each final complete reconciliation.

## Complete final Developer checkpoints

| Lane | Run ID | Passed | Failures/skips/validation issues |
| --- | --- | ---: | ---: |
| Windows | cb70ce8727c44d2a95fd39420a10e514 | 6,802 | 0 |
| Ubuntu 24.04 | a1ca3dd49aa04e238a531ea02e705218 | 6,804 | 0 |

All six family records remain. Unit, integration, architecture and business execute; full mutation and cumulative compatibility remain explicitly NotRun with their declared reasons. Formatting, locked restore, builds, contracts, package/dependency boundaries, publication validation and required-case reconciliation pass.

**874 selected maintained executable/build/configuration/shared-contract inputs match, including 655 C# sources.** Both publication schemas and the unchanged ledger also match. Windows base is 3e94256f plus the recorded task hashes. Native Git metadata and lane-local package bytes differ; this does not qualify one unchanged release package across lanes. Planning, Markdown and the user-owned launchSettings.json are outside this selected equality.

## Fresh adapter measurements

Both native lanes run **208 provider unit cases and 236 provider integration cases**, with no failures or skips.

| Family, on each lane | Lines | Branches |
| --- | ---: | ---: |
| Unit | 144/146 (98.63%) | 97/106 (91.51%) |
| Integration | 144/146 (98.63%) | 93/106 (87.74%) |

These remain separate observations. Coverage mode does not evaluate release thresholds; the integration branch observation remains below 90%.

| Lane | Killed | Survived | NoCoverage | Testable denominator | Native score | Exit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Windows | 105 | 31 | 6 | 142 | 73.94% | 2 |
| Ubuntu | 106 | 30 | 6 | 142 | 74.65% | 2 |

Each lane also retains 38 Ignored and 20 CompileError adapter mutants without kill credit; neither has a Timeout. Both native mutation gates remain below the unchanged 90% break threshold. Both unmutated rebuilds pass, and all 939 retained snapshot inputs per lane still match their recorded hashes. Unit tests run in-process against injected mutants; child integration mutant injection is not claimed. Differing native classifications remain a repeatability/qualification concern; this migration does not establish their cause or close that work.

An earlier Windows run produced its native JSON and a successful unmutated rebuild, but strict console decoding failed on byte FF at index 63 before recording the mutation command. That attempt is not credited as successful runner verification. Three additional native controls require byte fidelity, including invalid UTF-8 and empty streams. The final audit confirms Windows stdout is not valid UTF-8, verifies the retained original-byte hashes and confirms the successful corrected runner path. Ubuntu stdout is valid UTF-8. Earlier pre-format, pre-catalog and interrupted checkpoint attempts remain separate from the final run IDs above.

## Evidence and navigation

[RetainEvidence.cs](RetainEvidence.cs) is the executed native C# retention audit. [audit.json](audit.json) binds both completed checkpoints, all required case bindings, selected matching inputs, report identities, source pins, native mutation exits, snapshot hashes and raw console-byte checks. Raw lane summaries, validation reports, TRX observations, process records, original streams and snapshot inventories are retained beside it. Copied process records retain their original log-relative paths; flat receipt copies use lane prefixes.

Complete Developer trees and packages are archived locally under .artifacts/corrective/provider-qualification-migration-2026-10-18. Fresh full TRX/Cobertura attachments, Stryker JSON/HTML, logs and isolated sources remain in each lane's corresponding *-coverage-final and *-mutation-final directories. Ubuntu originals remain in the dedicated native verification checkout. These are local development receipts, not a portable offline replay kit. Scoped Git attributes preserve the native report and stream bytes.

CIS graph schema 2, build sha256:7faeff0ef8590a92334cece2353de5724668231a0c86dfd6dd1f08ed7f98d987, is fresh/valid with 11,675 nodes, 58,622 edges and zero diagnostics, strict=false. Extractor cis.csharp.compiler/6 exposes privacy-safe invocation shapes and intraprocedural ordering, not complete semantic dataflow. The initial broad name queries were truncated; a class-filtered limit-four query resolved the tool, tests and nested fixture without truncation. The tool's exact class root has an untruncated outgoing depth-one/limit-twelve ownership traversal: two nodes and one component edge, excluding proposed edges. This bounded navigation does not establish complete caller/test coverage. Derived .cis/local state is not committed.

Four assessed substantive PowerShell scripts remain: verify-toolchain.ps1, record-toolchain-inputs.ps1, replay.ps1 and write-microsoft-logging-bundle.ps1. The 27 maintained PowerShell files include intentional invocation/orchestration glue. Complete shipping coverage (including the failing packaged build helper), full mutation, cumulative compatibility, supported IDE UI acceptance, release qualification, publication and offline replay remain separate. See the [feature report](../../feature-status-2026-10-15.md).
