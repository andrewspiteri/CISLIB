# Source-replay policy migration — 19 October 2026

The maintained replay entry points now launch C# SourceReplay in the existing nonshipping engineering tool. The roadmap remains paused; Claude was not used.

| Original responsibility | Native replacement and controls |
| --- | --- |
| Capture or load a pinned source inventory | Existing NUL-safe SourceInventory, owned inventory bytes/hash, normalized relative paths, unique paths and lowercase SHA-256 validation. |
| Materialize input/work copies and preserve pins | Hash before/after copying and recheck original, input and work after the child exits, including failures. Thirty cases cover malformed inventories, changes/deletions, output preservation and actual incomplete-wrapper failures. |
| Invoke the actual verification wrapper in isolation | Preserve Developer/Release and lane arguments, child-only fresh CLI/package/HTTP caches, compiler isolation, and a Git ceiling at the replay output. |
| Retain diagnostics and actual exit status | Established verification log plus hashed original stdout/stderr bytes and process metadata; no successful report is synthesized. |

Complete Developer checkpoints pass 6,891 Windows and 6,893 Ubuntu cases, with zero failures/skips/validation issues and required-case reconciliation. Run IDs: Windows cf896f8e67e941d0990b31a1bc6e6b96; Ubuntu 6d84fba7d67341e4b2bd2f9b9ca2ec3c. All six family records remain: four execute, full mutation and cumulative compatibility are explicitly NotRun. The 880 selected code/build/configuration/shared-contract inputs match, including 661 C# files; publication schemas and ledger also match. Thirty focused replay cases pass on each lane.

The actual root replay launcher was also run against an explicitly incomplete input containing only the real root verification wrapper. Both native runs correctly exit 1, retain actual child diagnostics, and verify all one-file input/work pins before and after. These refusal controls do not qualify a full replay.

[RetainEvidence.cs](RetainEvidence.cs) audits both native checkpoint references and source sets before retaining byte-exact summaries, validation and raw incomplete-input diagnostics here. Complete checkpoint trees and lane-local packages are additionally archived under ignored .artifacts/corrective/source-replay-migration-2026-10-19. [Audit](audit.json) records counts and identities. The Windows HEAD context is b09da5c7 plus recorded task hashes; native Git metadata, complete snapshots and lane-local packages differ.

Complete clean/offline and Git-aware replay, historical binary/package replay and release bundle qualification remain open. Source copying does not retain Git history or all external tools/packages. This migration does not close coverage, mutation, compatibility or publication gates. One assessed substantive PowerShell policy entry remains: write-microsoft-logging-bundle.ps1; the other maintained PowerShell files include intentional invocation/orchestration glue.
