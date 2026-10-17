# Native publication validation and Debug test prerequisites

The original logging roadmap remains paused. This is the requested corrective migration; no Claude service or external source review was used.

## Implementation

Publication validation now executes inside the existing C# `CIS.DependencyChecker` tool. The root launcher is unchanged, the language launcher only resolves defaults/builds/invokes the tool, and `Publications.psm1` is retired. The two retained canonical schemas, ledger and baseline records are unchanged. A small native reader implements their current schema vocabulary and fails closed on unsupported assertions; no schema package or runtime dependency was added.

The port retains strict UTF-8/JSON and recursively unique member names, schema constraints, trusted previous-ledger hashes, immutable header/array prefixes, membership/version/time checks, artifact path/link/length/hash checks, record/fixture/execution links and report output ownership. Publication integrity does not attest an actual publication, execute compatibility consumers or qualify a release. Empty history remains empty.

All 43 original controls call the native validator directly. Twenty-nine additional controls cover exact integer spellings/fractions, unsupported schema additions, nested escaped duplicates, duplicate/origin links, linked artifacts, protected output bytes and two stable-launcher success/rejection paths. Their separate inventory binding supplements the original group; planned release cases are not replaced.

## Visual Studio report and corrective scope

The user reported 132 failures from Test Explorer Run All. Inspection of the local VS store found missing `pwsh` process starts and stale packaged-Core assertions; the store was not treated as a complete structured failure census. A native Debug solution reproduction also exposed deadline failures under the previous processor-count-sized worker default. The incomplete unbounded attempt was stopped and earns no pass credit.

* Host setup: Microsoft PowerShell 7.6.7 was installed for this Windows user through winget, which verified the installer hash. It resolves through the normal WindowsApps PATH. Tests no longer depend on Codex's private PowerShell distribution being on PATH. Existing IDE sessions may need restarting if their PATH predates host setup.
* Test execution: ordinary MSTest runs default to four workers. The existing Developer session budget is ten minutes, following a five-minute Windows integration abort after 2,099 passing cases; architecture retains twenty minutes. Case membership and assertions are unchanged.
* Debug package consumers: create a fresh package from their existing Debug build inside their own candidate feed, check the loaded Core assembly hash, then restore/build/run against that isolated package. Release runs continue to consume the selected prebuilt package and reject stale bytes.
* Compiler-control evidence: the LogValue architecture test checks the Release assembly that produced its retained Developer report, rather than comparing that report to an unrelated Debug test-host assembly. Source hashes remain checked.
* ApiCompat setup: a tool restore originally reused resolver metadata pointing at the old `.artifacts/csharp/wp01-s03-discovery/packages` cache. Three compatibility cases rejected the absent default-cache tool. The old resolver metadata was backed up, and the existing 10.0.401 pin was restored from nuget.org into the normal user cache. Version and .NET 8.0.31 execution checks remain unchanged. A further rerun identified that the normal Windows host had only .NET 8.0.32. The official 8.0.31 x64 runtime installer was checked against Microsoft release metadata SHA-512 and its valid Microsoft signature, then installed successfully alongside the other runtimes.

No shipping runtime behavior, package identity, dependency boundary, historical baseline or Type Manager implementation changed. The unrelated user-owned Web launch settings are preserved.

## Verification

Publication baseline before migration: 43 passed, Windows focused run `6bd25312305646a4b496d1e8a68c5ce9`. Native publication controls: 72 passed on Ubuntu (`5fd48632bacf447eb75e45a07cb0ccf4`); Windows targeted run `e56dd2d4e80d4564b56a8b841beb46a7` passed those 72 plus three related LogValue architecture cases. Debug candidate consumers: all 20 passed (`1a92cc195ceb4ffc801a0fafc95dfa93`). A new link-rejection control initially exposed fixture cleanup order on Windows; explicit link cleanup fixed that fixture. The first Ubuntu checkpoint stopped on formatting, before executing any families. These preliminary attempts remain in ignored artifact logs and earn no checkpoint credit.

All five Debug suites have passing native reports: 3,750 unit, 2,120 integration, 694 architecture, 111 compatibility and 24 business cases (6,699 total). The four non-compatibility reports come from Run All; compatibility is a separate complete rerun after host setup. This is native CLI evidence rather than a claim that the IDE UI has been rerun. A Windows checkpoint source-enumeration deadline refusal and the incomplete integration-budget run are preserved; neither is credited. The first completed Ubuntu checkpoint passed 6,590 cases under the previous stricter budget. Both final profiles below use the same ten-minute settings.

Both final complete Developer profiles return exit 0, zero failures/skips/validation issues and complete required-case reconciliation:

| Lane | Unit | Integration | Architecture | Business | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows | 3,750 | 2,120 | 694 | 24 | **6,588** |
| Ubuntu | 3,750 | 2,122 | 694 | 24 | **6,590** |

Windows run `d7be10e49d734ed7a034cac4b0ae2aef`; Ubuntu run `d6bae63d17544d2fa45907929af75e98`. The final Windows integration family completes in 5 minutes 32 seconds. The existing twenty-minute architecture limit is unchanged. All six family records remain; mutation and cumulative compatibility are explicitly NotRun in Developer. The separate 111-case Debug development-compatibility result does not qualify the cumulative released-package family.

The [C# retention audit](RetainCheckpointEvidence.cs) verifies native Debug suite outcomes, all six Developer family records and NotRun reasons, report/source/inspection/package/reconciliation hashes, the eight new method bindings (29 executions), identical sets of **855 executable/build/configuration/shared-contract inputs**, including **636 C# files**, and the two canonical publication schemas plus unchanged ledger in both recorded inventories and live trees. [Audit](audit.json), [input identities](matching-executable-inputs.json), raw [Windows summary](windows-summary.json)/[validation](windows-validation.json), and [Ubuntu summary](ubuntu-summary.json)/[validation](ubuntu-validation.json) are retained here. Complete reports, logs, inventories and lane-local packages are archived under `.artifacts/corrective/publication-validation-migration-2026-10-17/{windows,ubuntu}-Developer` and `*-packages`.

Windows base is `4b25ed7518af8fb29051ba711e944dfc8578f179` plus the recorded task inputs; native Git metadata remains `a4df38e0`. These lane-local packages are not one unchanged cross-lane release candidate. Planning/report metadata written after execution and the user-owned launch settings are outside executable-input equality. The retention audit initially rejected the older failed compatibility report because of a mixed-separator directory comparison; normalizing that comparison selects the successful explicit rerun and preserves the failed report separately.

CIS graph build `sha256:f883b1912793bb3e55c89a46ce19abce21c29cadadf3ddbeff5afa297203d901` is fresh, with **11,296 nodes**, **56,248 edges**, zero diagnostics and `strict=false`. `CIS.Verification.PublicationValidation` resolves uniquely and untruncated in `cis-dependencychecker` at its canonical source line 10. Graph navigation is supporting context rather than proof of semantic/test completeness. Planning evidence is excluded from graph inputs.

Full logs and diagnostic native reports are under `.artifacts/corrective/publication-validation-migration-2026-10-17`. Historical earlier receipts remain unchanged. No fresh coverage, full mutation, cumulative release compatibility, offline replay or publication qualification is claimed. Remaining substantive PowerShell slices include compiler/dependency policy, Type Manager provenance, coverage collection and bundle/replay retention; take those separately.
