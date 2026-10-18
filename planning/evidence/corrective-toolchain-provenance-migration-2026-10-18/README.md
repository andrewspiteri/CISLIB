# Native toolchain and input-provenance migration

18 October 2026. Corrective verification work; the logging roadmap remains paused. Claude was not used.

The existing verify-toolchain.ps1 and record-toolchain-inputs.ps1 commands now launch C# in CIS.DependencyChecker. No shipping API, package dependency, threshold, historical review or roadmap task is changed.

| Existing control | Native implementation and verification |
| --- | --- |
| Approved fresh package/tool inventory, metadata, licences and retained archives | ToolchainInputs; 18 provenance cases, one immutable-prerequisites case and six path controls. Extracted metadata/licence/dependency bytes must match their NuGet archive; copied bytes are checked against SHA-256 pins. |
| Source snapshot, SDK/runtime/tool selection, fresh caches, commands and evidence inventory | ToolchainQualification reuses the NUL-safe SourceInventory and existing immutable-output/source-integrity controls. Each command retains original stdout/stderr bytes, diagnostic text and actual/expected exits. Human console decoding does not alter structured reports or byte hashes. |
| Native completed tests and Microsoft collector/Cobertura gates | ToolchainReports; 16 cases retain positive executed/passed counters, collector/attachment identity, denominators and 95% line / 90% branch thresholds. Identical collector copies are not added together; conflicting bytes are rejected. |
| Source/tool-bound mutation review and conservative score | ToolchainReports; 16 cases retain Killed-only numerator and Killed + Timeout + Survived + NoCoverage denominator, positive counts, unchanged 90% threshold, exact exclusion fields and no reclassification/kill credit for ignored/noncompiling mutants. |
| Native host arguments, console byte ownership and XML input boundary | One pinned-tool/raw-byte case and one DTD-rejection case. |
| API controls and post-mutation rebuild/package checks | Existing Core self-comparison, positive/breaking disposable controls (CP0002 and Parse), source pins, rebuild/hash equality and dependency/package checks remain in the C# workflow. They are not reached in this batch's real smoke runs because the earlier coverage gate fails. |

The new required group ARCH-NATIVE-TOOLCHAIN-PROVENANCE binds seven native methods and 59 cases on both lanes. These are policy controls, not 59 native tool qualification runs.

## Executed evidence

Both complete Developer checkpoints pass with zero failures, skips or validation issues:

| Native lane | Run ID | Unit | Integration | Architecture | Business | Total |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 281f488c3e9644d7a6433b27d17c2e76 | 3,750 | 2,120 | 967 | 24 | 6,861 |
| Ubuntu 24.04 x64 | 53726a0675724fae8f409e7bb8b309db | 3,750 | 2,122 | 967 | 24 | 6,863 |

All six family records and required-case reconciliation remain; complete mutation and cumulative compatibility are explicitly NotRun in Developer. The checkpoints execute in the fresh toolchain source snapshots. Their 878 selected executable/build/configuration/shared inputs match the canonical Windows and native Ubuntu checkouts, including 659 C# sources; both publication schemas and the ledger match too. Full source snapshots and Git metadata differ. Windows HEAD context is 0f884134 plus the recorded task hashes; the native checkout has different Git history. This does not qualify one unchanged release package or a Git-independent replay.

Fresh caches contain 18 approved project packages, two development-only CLIs and five Ubuntu / six Windows approved SDK framework downloads. Downloads are explicitly classified separately from Core PackageReferences and runtime dependencies. Missing required project/tool packages, unknown packages or unapproved versions still fail. NuGet's original nuspec filename casing is supported; the real ApiCompat archive exposed the old Linux filename assumption.

The [C# audit](RetainEvidence.cs) verifies native report/process hashes, all retained source pins, restored archive/licence/dependency hashes, native family references, required bindings and matching selected inputs before producing [audit.json](audit.json). Complete Developer/package archives are under .artifacts/corrective/toolchain-provenance-migration-2026-10-18/. Full smoke snapshots, fresh caches and retained NuGet archives remain in .artifacts/tci-w02/ and the native checkout's .artifacts/tci-u02/. Receipt JSON/TRX files retain their original bytes.

The earlier Ubuntu tci-u01 attempt failed on the nuspec casing assumption and is retained. The initial Windows tci-w01 process was stopped during source inventory before a snapshot/run report was created, so the casing fix could be applied to both lanes. Neither attempt is passing evidence. A standalone corrected Ubuntu record-toolchain-inputs.ps1 invocation also passes against the actual fresh tci-u01 cache; its local output is .artifacts/tci-input-u02/.

## Smoke qualification remains open

Both real toolchain commands pass SDK/tool invocation, locked restore, package provenance, the complete Developer checkpoint, exact runtime hosting and all 93 parser tests under the Microsoft collector. Both then return exit 1 at the unchanged coverage gate. The historical parser-only test filter now measures the larger Core assembly: Ubuntu records 15/3,647 lines and 75/2,779 branches. The per-lane native counts are retained in coverage-observation.json; this is a parser-smoke scope mismatch, not the complete shipping coverage measurement.

Source integrity passes for both complete source snapshots and original inputs. API fixture controls, parser mutation, unmutated rebuild/repack and final package-boundary smoke stages are NotRun after the failed coverage gate. They are not silently bypassed or labelled passed.

The preserved smoke-dispositions.json also has a different source-byte hash from today's parser. The C# stale-review controls reject that mismatch; the real command does not reach mutation in these attempts. No exclusions were updated or inferred.

Complete shipping coverage (including the packaged Type Manager build helper), full mutation, cumulative compatibility, exact-candidate release profiles, supported IDE UI acceptance, SDK/runtime distribution retention, embedded third-party notices, clean/offline replay and publication remain open. The existing adapter mutation scores remain below their 90% gate.

Two assessed substantive PowerShell policy scripts remain: replay.ps1 and write-microsoft-logging-bundle.ps1. Other maintained PowerShell files are intentional launch/process glue.

## CIS context

The initial schema-2 local graph was fresh/valid, build sha256:7faeff0ef8590a92334cece2353de5724668231a0c86dfd6dd1f08ed7f98d987, with 11,675 nodes / 58,622 edges and no diagnostics (strict=false). The bounded class query located SourceInventory and its tests without truncation; both PowerShell sources were read directly. Derived context is refreshed after this batch. Compiler arguments are privacy-safe shapes and dataflow is intraprocedural ordering; graph output is not complete semantic or runtime evidence.
