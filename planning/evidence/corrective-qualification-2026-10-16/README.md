# Corrective native qualification — 16 October 2026

The original logging roadmap goal remains paused while authorized corrective qualification continues. The review-input and PowerShell assessment commit is `b09b3cf9`; this receipt records execution separately.

## Inputs and prerequisites

The frozen source snapshot is `396e87d6c425ade0083bdc3eadcc4dcb2b07becc`, plus five previously declared user-owned inputs. All **50,583 copied source files** match the inventory; all **803 implementation files** match the completed Windows Developer checkpoint at `7a606607`. The [copy audit](copied-source-audit.json) and native before/after audits pin this comparison.

The source archive SHA-256 is `f368c0d822c7e2001dabdb81978f2ebbf1bf43d8d7c0dfdd1a42aac7e51a3aa5`; its input inventory SHA-256 is `6edb2ef4db1ea6b953e239f24663e7037721146ef5be4f304b2fea0cfd19a023`. The initial slow cross-filesystem copy was stopped before any verification ran. The complete archive was extracted into a new native Ubuntu directory and checked before execution.

Ubuntu's missing PowerShell prerequisite was supplied as a local portable Microsoft PowerShell 7.6.5 archive, matching the [official release hash](https://github.com/PowerShell/PowerShell/releases/tag/v7.6.5). [Tool provenance](powershell-source.json) records the source/hash. No system installation was required. The first Git-backed run used mounted tool paths and reached the unchanged 20-minute architecture limit: 616 of 619 architecture cases passed, three remained unfinished, and business did not run. Its [failed summary](ubuntu-mounted-toolchain-attempt.json), [source audit](ubuntu-mounted-toolchain-source-audit.json) and [missing-case comparison](architecture-abort-difference.json) are preserved. The SDK and PowerShell were then staged on Ubuntu's native filesystem. A [4,907-file SDK audit](native-toolchain-audit.json) confirms unchanged file bytes; no timeout, test or pass criterion changed. Both lanes select SDK 10.0.401 and runtime 10.0.12.

## Full Ubuntu Developer run

**6,440 cases passed: 3,710 unit, 2,087 integration, 619 architecture and 24 business; zero failures/skips/issues.** Run `b59278c57bf84869bbe76b4e7a404567` began 20:19:59 UTC and finished 20:28:52 UTC, exit 0. The post-run source audit also passes. [Native summary](ubuntu-developer-summary.json) and [validation](ubuntu-developer-validation.json) preserve the full result.

The [cross-lane case comparison](integration-case-difference.json) accounts for the two additional integration rows: Linux adds `symlink-member` and `write-jsonl-os-limit`, while each lane has its own named validated-path case. These are executed OS-specific rows, not skipped-case credit.

Summary SHA-256: `5d1b1cc216abd50db1703558ed722d0ee6315a6d6a7147a2726255f5ba375467`; source inventory: `6ecc5edaa377a9d897306b5676de9a69c7bdd02b920d58c7a3d73f1b7e733e43`. Native Core DLL: `cf4f3c838c3ffa8adbc86e851565136363a7c6a7365f90f718f34783f45c05ba`; native package: `cc6f0ca0eabe3da71a3a144903d46b71af8f59ca5006d9d9ff0ce6ce617b282b`. Both differ from the Windows package identities; the shared exact-candidate release gate stays open.

The first archive-only attempt failed architecture inspection because it had no Git metadata. Its [unchanged failed summary](ubuntu-first-attempt.json) records exit 1 and zero executed test families; its [post-run source audit](ubuntu-first-source-audit.json) still passes. A separate native Git index/ref was then initialized at the actual frozen commit, reading existing objects through a local alternate to the canonical repository. Its status contains exactly the five declared user-owned inputs. The subsequent full rerun records the real commit in its native summary. No report was backfilled, and no inspector requirement was weakened. The shared object store is a local prerequisite, not a complete offline/retrievable bundle. This is a complete Developer profile; compatibility/mutation families remain separately unexecuted obligations.

## Fresh shipping coverage

| Lane | Assembly | Covered lines | Covered branches (conservative lower bound) | Assembly gate |
| --- | --- | ---: | ---: | --- |
| Windows | CIS.Core | 3,740 / 3,881 — 96.37% | 2,537 / 2,767 — 91.69% | Pass |
| Windows | CIS.TypeManager.Build | 82 / 104 — 78.85% | 37 / 70 — 52.86% | Fail |
| Ubuntu | CIS.Core | 3,724 / 3,881 — 95.95% | 2,497 / 2,767 — 90.24% | Pass |
| Ubuntu | CIS.TypeManager.Build | 82 / 104 — 78.85% | 37 / 70 — 52.86% | Fail |

Windows executed all **6,438** cases under the collector (3,710 unit, 2,085 integration, 619 architecture, 24 business), with zero failures/skips. Its exact package remains `43d14bbbfa25ea57eada6f34f37fd334a4f0a0e913d84e564969d47187397930`. [Raw Windows observation](windows-coverage.json) preserves per-source uncovered/partial lines and original TRX/Cobertura identities.

Ubuntu also executed all **6,440** cases under the collector with zero failures/skips. Its package remains `cc6f0ca0eabe3da71a3a144903d46b71af8f59ca5006d9d9ff0ce6ce617b282b`. [Raw Ubuntu observation](ubuntu-coverage.json), SHA-256 `1452390b8c15016be4b71f21dc3fc44bc96f937f046ecdac7e9e853411459f12`, retains every report identity. Ubuntu's combined aggregate is 95.51% lines / 89.32% branches; its helper also fails the unchanged per-assembly gate, `qualified=false`, native command exit 2. Lane observations are kept separate.

The required thresholds remain **95% lines and 90% branches for each shipping assembly**. Windows' combined aggregate reaches 95.91% / 90.73%, but the build helper fails its own gate: `qualified=false`, exit 2. No threshold, executable source exclusion or branch-denominator policy was weakened. A passing test suite does not close that coverage gap.

The helper's measured gaps include asset classification/error paths, metadata/satellite/conflict validation, relative-path guards and reference-attribute encodings in WriteTypeInventory.cs. Existing ShippingBuildInventoryTests cover the real task positive and escaping-path rejection; additional standalone inventory/profile checks are not automatically measurements within these four coverage families. Extend the native C# qualification tests, then recollect; do not rewrite shipping behavior to improve a score.

## Retained evidence and remaining work

The compact [artifact index](index.json) pins native summaries, source audits, package/Core identities and fresh coverage observations. Full reports/logs remain under ignored local artifacts and the named native Ubuntu run directory. They are retained local evidence, not a complete retrievable/offline release bundle.

The [feature report](../../feature-status-2026-10-15.md) carries the current remaining work. Complete Claude review remains service-limited; its refreshed source inputs are prepared. Source-inventory policy is the smallest next native tooling migration. Helper coverage, full shipping mutation, cumulative compatibility, one unchanged exact package consumed on both lanes, complete release/replay gates, supported IDE acceptance and finished teaching examples/skills/publication remain open. Separately rebuilt lane packages do not satisfy the shared exact-candidate release requirement.

The formal original logging roadmap count remains 132 of 199; corrective qualification does not silently approve or close additional roadmap tasks.

CIS graph refresh returned `CIS-GRAPH-STORE-002` / exit 5 when persisting `.cis/local/graph/context.db` (access denied). The existing build `b1e9dc7927dcf33d62fff73906d40b5e3e0e93d8dc32ae4d32df23a7ffd778d7` remains readable; subsequent status reports valid/fresh, 10,976 nodes, 54,123 edges and zero diagnostics. This records a failed refresh, not a new graph build. Derived graph files were not edited by hand; canonical sources/native reports remain the qualification evidence.
