# Native C# source inventory — 16 October 2026

Source enumeration, deletion handling, hashing and copy fallback now execute in `CIS.DependencyChecker/SourceInventory.cs`. `Get-SourceInventory` retains its existing callers and is bootstrap/invocation glue. The redundant `check-source-inventory.ps1` fixture is retired. Shipping Core and Type Manager implementation are unchanged.

## Preserved controls and bounded changes

At a Git root, the checker includes tracked and nonignored untracked inputs, excludes Git-confirmed unstaged deletions, hashes actual working bytes and leaves the index unchanged. Null-delimited output handles space/non-ASCII filenames. A corrupt index rejects inventory instead of falling back. Copy enumeration applies outside the selected directory's own Git root, includes hidden source and excludes build/control directories without following directory links. Unique paths sort ordinally and digests remain lowercase SHA-256. Full verification still requires real Git HEAD; this does not close directory-only/offline replay.

Six C# architecture tests cover the original Git/index/digest control, copy exclusions/hidden source, a nested copy independent of its parent index, corrupt-index rejection, command JSON/missing-root failure and an unbuilt tool copy bootstrapped through the existing shell bridge. Assertions and fixture ownership/cleanup are C#. The bootstrap script only invokes the module and serializes its result. Readonly Git-object cleanup runs in finally inside owned fixtures. The existing case ID now requires all six native methods; no original planned release case was removed.

## Executed verification

| Lane | Unit | Integration | Architecture | Business | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 3,710 | 2,085 | 624 | 24 | **6,443** |
| Native Ubuntu 24.04 x64 | 3,710 | 2,087 | 624 | 24 | **6,445** |

Both complete Developer profiles return exit 0 with zero failures, skips or validation issues. Each architecture family executes all six source-inventory controls. Six focused controls also passed on each native lane before the full runs. The normal locked restore, dependency/format/build/compiler/package checks and host execution passed. See the [native C# audit](verification-audit.json), raw [Windows summary](windows-summary.json)/[validation](windows-validation.json) and [Ubuntu summary](ubuntu-summary.json)/[validation](ubuntu-validation.json).

Windows run: `8cea9beed63d4d6fbfc51be99449f82b`. Ubuntu run: `b085be0a23854217bbd7b6f6916004cc`. Both execute base `a4df38e0dd91cd783325dbce108ba755d0ce7b67` with these task edits and the five already declared user-owned inputs. The C# audit compares all **50,610 source input paths and hashes**, including all **803 C# implementation inputs**, with no lane differences; it also checks copied input identities, retirement and native counters. Raw inventory bytes differ by platform JSON newlines, so comparison uses their parsed path/hash identities. [Copy delta](source-copy-inputs.json) pins the 36 paths brought forward from the earlier native workspace. Receipt/report metadata written after execution is outside that captured snapshot.

Full reports, inventories, native TRX/discovery, logs and package/Core copies remain under `.artifacts/corrective/source-inventory-2026-10-16/`. Ubuntu additionally retains its complete passed archive under `/home/user/cislib-verification/ubuntu-developer-396e87d6-archive/source-inventory-2026-10-16/passed-Developer/`. The pinned native SDK/runtime/PowerShell setup is the preceding qualification receipt's toolchain. Each lane rebuilt its own candidate; a shared unchanged package is not qualified here.

## Rejected preliminary runs

The first full profiles (`309042d849254c32ae828cc973127e24`, Windows; `fa7e7a2fd3d9493ba2ad127524d3d396`, Ubuntu) stopped at architecture discovery because the new grouped case omitted the required explicit `method: null`. Their unit/integration families passed; architecture/business execution was not credited. The case record was corrected to the existing grouped shape, retaining six required bindings and the unchanged validator. [Windows](windows-failed-summary.json) and [Ubuntu](ubuntu-failed-summary.json) summaries and complete failed-run archives are preserved. The final runs above supersede those rejected attempts.

## Review, graph and remaining work

Local source review and both native profiles are complete. A Claude transfer was blocked by automatic approval review before its process started; no new source was sent. The [prepared review set](../../reviews/corrective-review-set-2026-10-16/README.md) preserves the reason, exact inputs and pending external review. Preparation is not a review verdict.

The graph rebuilt successfully after code/guidance edits: schema 2, compiler/semantic extractors 6/3, 10,997 nodes, 54,285 edges, zero diagnostics, build `b4c40cd3f6aa7a0196697c5db2858cffaa643858aa327ee1c77edcd3b6902bda`. Status was fresh. Exact SourceInventoryTests plus bounded related context and the new SourceInventory class lookup were untruncated; canonical reads supplied semantics. This resolves the prior persistence failure for this recorded build without promising complete graph coverage.

Release evidence validation is the next PowerShell policy migration; publication validation follows separately. The earlier coverage receipt still reports the packaged helper below unchanged gates. No coverage, mutation, full Release, publication or offline replay gate is closed by this batch. The original roadmap goal remains paused.
