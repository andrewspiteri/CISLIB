# Full shipping qualification, 19–20 October 2026

The user requested full shipping mutation coverage and Release qualification after the passing six-file diagnostic. **The candidate is not release qualified.** Full shipping line/branch coverage and the packaged build helper's strict mutation threshold pass on both lanes. Full Core mutation completes and fails the unchanged 90% threshold at **77.6008% Windows / 77.4801% Linux**. Both Release profiles pass all implemented stages/test families and refuse the missing mutation integration, required groups and release validators. Shipping implementation source remains unchanged from candidate commit `8254022298b8f42c2d7f2f01f1df5e418b44c4aa`. Both shipping assemblies' 103 handwritten source/project files match the preserved mutation inputs byte for byte ([Core snapshot](evidence/shipping/core-shipping-match.json), [helper snapshot](evidence/shipping/helper-shipping-match.json)).

## Required scope

The subsequent [20 October source-bound triage](full-core-triage.md) adds 18 regression cases and a deterministic lifecycle-fixture correction, with a passing 7,089-case Windows Developer checkpoint and 3,885-case preserved Linux functional selection. Its narrow replay/instrumentation observations do not replace either full-suite score in this report.

- Every handwritten source file in `CIS.Core`, including Environment, Text, Logging, the embedded Microsoft adapter and Type Management.
- Every handwritten source file in the packaged `CIS.TypeManager.Build` helper.
- Complete Stryker mutation level, SDK 10.0.401, target runtime 10.0.12 and pinned Stryker 4.16.0 on the explicit 8.0.31 tool host.
- Windows x64 and native Ubuntu 24.04 x64 lanes. Strict score remains `Killed / (Killed + Survived + NoCoverage + Timeout)`, minimum 90%; no timeout or independent replay is promoted to a native kill.
- Source/tool/candidate pins, baseline and restored native tests, source membership checks, known-kill controls, compiler/filter dispositions, and actual child activation evidence.
- Native Release execution, exact-package functional/compatibility controls, shipping coverage, examples and retained-kit/licence gates. A passing mutation score alone does not satisfy these obligations.

## Maintained harness changes

The C# mutation driver now exposes `core-mutation` and `build-helper-mutation` alongside the existing `logging-mutation` command. Full selections require the all-source glob, no method/mutation exclusions and exact native-report source membership. Snapshots include shared contracts as well as conformance/compatibility inputs. Both source copies remain hash-bound, and a fresh unmutated rebuild/test follows native execution. Child-only `RestoreLockedMode=true` preserves lockfile bytes on both lanes.

The helper suite executes the actual task through SDK-selected MSBuild types. It asserts managed metadata/identities and normalized paths, required versus optional assets, task defaults, safe error categories, satellite handling, native-image rejection, deterministic JSON, unchanged-content writes and success messages. It is not a substituted implementation of the shipping task.

`eng/verify.ps1 -Profile Release` now executes the shared build/package checks and all five implemented VSTest families rather than only initializing NotRun slots. It still refuses absent mutation execution, missing cases and release gates. `-OutputRoot` selects a new retained evidence directory; host/lane validation applies to both profiles.

## Executed helper observations

| Scope | Windows | Native Ubuntu |
| --- | --- | --- |
| Final locked-restore helper run | `helper-windows-05` | `shipping-helper-05/run` |
| Baseline / restored cases | 30 / 30 passed | 30 / 30 passed |
| Killed / Survived / NoCoverage / Timeout | 94 / 6 / 3 / 0 | 95 / 5 / 3 / 0 |
| CompileError / Ignored | 20 / 24 | 20 / 24 |
| Strict score | **91.2621359223%** | **92.2330097087%** |
| Known task-success mutation | Killed | Killed |

Native statuses remain unchanged. Independent replay of all 44 compiler/ignored entries at the preceding 27-case snapshot completes with 27 detected variants, 12 build-negative variants and five selected-test survivors, with passing baseline/restored controls. Additional generic-attribute, metadata-module and local reference-attribute cases produce the intermediate 29-case suite. Supplemental replay detects all three metadata survivors (134/135/136), with 29 passing baseline/restored cases. The combined distinct-entry review is **30 detected variants, 12 build-negative variants and two source-equivalent variants**; no native status or denominator is rewritten.

The remaining ignored block 80 removes the explicit metadata-module rejection. `AssemblyName.GetAssemblyName` then rejects the module and the same outer BadImageFormat wrapper produces the same safe task error. Block 106 removes the path-conflict throw: stable SDK-selected output files give one identity for a normalized path, and conflicting simple-name selection is rejected earlier. The contract does not require concurrent replacement of deployed output files while this task inspects them.

The preceding 29-case `04` runs each have six native survivors: **18, 19, 58, 78, 81 and 82**. Mutation 84 is killed; it is not a survivor. Review also found that 18 is not equivalent on Linux: two case-distinct deployed filenames can contain different assemblies. The added platform-path case accepts those distinct Linux files and asserts the existing rejection of Windows case aliases. An isolated native Linux replay detects exact mutant 18 with 30 passing baseline/restored cases ([result](evidence/shipping/helper-path-replay-linux-results.json)); the subsequent `05` native run itself kills 18. No replay is substituted for that native result.

The remaining comparer mutation is lane-bound: 18 has the original true condition on Windows; 19 has the original false condition on Linux. On Windows, 19 cannot bypass the earlier conflicting simple-name/path selection check for stable case-alias files. Mutation 58 changes a secondary sort direction after unique primary assembly identities. Mutations 78/81/82 change inner BadImageFormat text or the explicit metadata-module rejection while preserving the same outer safe error wrapper. Three NoCoverage entries remain: 96 is the defensive missing assembly-name branch; 107/108 concern the stable-path conflict invariant. These are source-bound dispositions under the exact source/SDK/runtime pins, rather than added kill credit. Handcrafted metadata fixtures are used only for metadata inspection, and no unsupported runtime assembly is loaded to force a branch.

Final native reports and driver results are retained for [Windows](evidence/shipping/helper-windows-05-results.json) and [Linux](evidence/shipping/helper-linux-05-results.json), with complete compressed native logs/reports beside them. The earlier `04` reports remain unchanged.

The first 22-case helper run failed at 76.699029%. Stronger defaults, formatting/message, native-image and satellite assertions raised the score. A passing Windows refinement preceded the final locked-restore run. The first Linux attempt correctly failed its source-byte check after forced restore rewrote a lockfile; it receives no complete qualification credit.

## Child activation controls

Real Core consumer and SDK MSBuild children each pass before/after controls and fail by the expected assertion/task result with an active native Stryker mutation. The controls verify loaded Core bytes/process identity and require real injected `MutantControl` metadata. They retain original process streams, commands, binary pins and the distinction between an assertion failure and infrastructure failure. These controls are separate observations; they do not change native mutation scores or claim a complete child-process mutation matrix.

Windows uses captured native instrumented Core and helper binaries. Linux uses its captured native Core plus the same retained Windows-instrumented helper and compiled control consumer; binary provenance is explicit. The first Linux control rejected an already-restored helper without instrumented metadata. Its retry uses the retained instrumented binary and passes both before/after controls.

## Full Core and Release attempts

Full Core baseline passes all **3,867 selected unit cases**; only the three declared measurement classes are omitted from test execution. Mutation source selection remains complete, with 6,131 generated mutants. Both initial full executions stalled after early test-stop requests, beyond the configured per-run deadline. Their exact native process trees were stopped; parent drivers perform fresh unmutated restoration, and neither attempt has a final score. The current `05` executions use `disable-bail=true` to let selected tests finish and continue processing native variants, retaining full source selection, Complete level and the 90% threshold.

The no-bailout `05` Core snapshots preserve that complete shipping functional test selection and predate the one later repository/checkpoint-selection unit guard. The current normal unit total is therefore 3,892, versus 3,867 in this preserved mutation selection (24 measurement rows plus that one verification-only guard). The guard tests evidence-root isolation rather than shipping behavior; the Core/helper source and project membership/bytes match the current product inputs exactly. The newer helper suite is executed separately with 30 baseline/restored cases per lane.

Both `05` native runs complete and **fail the unchanged 90% threshold**. Native Stryker scores are 78.74% Windows and 78.40% Linux. The stricter repository results are **`3215 / 4143 = 77.6007723872%`** and **`3210 / 4143 = 77.4800868936%`**, respectively: 47 Windows and 38 Linux timeouts receive no kill credit. Exact driver results, source/status audits and non-killed catalogs are retained for [Windows](evidence/shipping/core-windows-results.json) ([audit](evidence/shipping/core-windows-review.json), [catalog](evidence/shipping/core-windows-non-killed.json)) and [Linux](evidence/shipping/core-linux-results.json) ([audit](evidence/shipping/core-linux-review.json), [catalog](evidence/shipping/core-linux-non-killed.json)).

| Core observation | Windows | Native Ubuntu |
| --- | ---: | ---: |
| Killed | 3,215 | 3,210 |
| Survived | 668 | 642 |
| NoCoverage | 213 | 253 |
| Timeout | 47 | 38 |
| CompileError | 996 | 996 |
| Ignored | 992 | 992 |
| Total | 6,131 | 6,131 |

On each lane, both unmutated controls pass all 3,867 cases with zero skips/failures, all four known-kill controls are Killed, and the driver completes fresh restoration. Each audit independently matches all **100 current handwritten Core source files**, recognizes every final status and verifies the exact source/selection of the compiler probe. Neither a passing baseline nor complete filename membership approves the 996 compiler-filtered or 992 block-filtered entries per lane. Both native tools exit 2 for the failed score; each parent driver exits 1 and retains `diagnosticGatePassed=false` and `exclusionsReviewed=false`.

Within each complete native execution, the original six-file frontend/interpolation subset has the same 244 Killed, seven Survived, four NoCoverage, one Timeout, 28 CompileError and 62 Ignored: strict **95.3125%**. This is a derived subset observation, not a replacement for the original focused diagnostic or for the failed full-Core score. The larger gaps therefore require review outside that feature subset.

The largest Linux clusters of Survived/NoCoverage/Timeout are `OutputEventQueue` (74/4/14), `CoreLogRuntime` (49/21/7), `CoreLogRuntime.Incoming` (37/29/6), `EventByteProjection` (46/9/0), `LoggingFilePath` (7/38/0) and `IncomingEventQueue` (28/5/4). The same six lead Windows: 74/4/16, 49/21/7, 36/29/6, 46/9/0, 37/2/0 and 28/5/4, respectively. Source-bound module observations are:

| Core module | Windows strict score | Linux strict score |
| --- | ---: | ---: |
| Environment | 75.8621% | 76.5517% |
| Logging | 78.5034% | 78.3712% |
| Text | 92.8571% | 92.8571% |
| Type Management | 58.5106% | 57.9787% |

These numbers prioritize source-bound triage; they do not identify 668 or 642 production defects or automatically justify exclusions. Platform-specific paths, equivalent variants, missing assertions and tool attribution must be distinguished against exact inputs. Lanes and module scores are not merged to conceal a failing scope.

The first sandboxed Windows restore failed before useful diagnostics. The next baseline exposed missing contract fixtures in the copied inputs; the snapshot closure was corrected. A simultaneous driver build collided on its own compiler output and received no execution credit. Subsequent scopes use separately built snapshots. All failed attempts remain distinct from successful native evidence.

A source-bound Linux probe confirms that the compiler rollbacks are qualification gaps, not proof that every removed variant is invalid. The actual pinned trace creates mutant **2984**, `!writable` → `writable` in `ConsoleOutputWriter.Initialize`, then removes every mutation in that method after an unrelated synthetic definite-assignment error. Replaying that exact source change against existing console composition tests **compiles and produces seven failing native cases**, with one scheduling control passing; all eight baseline and eight restored cases pass ([result](evidence/shipping/core-compiler-probe-linux-results.json)). The final native JSON confirms **CompileError** and matches the exact source hash, original/replacement text and line 25 span. This independent observation adds no native kill credit or score adjustment.

The preceding broad probe has 3,867 passing baseline cases, but the changed full suite reaches its five-minute deadline. Its source is restored to the exact original SHA-256 `84f05f14c44feb1bfc20a2a852d12311f01929a84084d9cab52bd4aff87b4fcc`; it has no completed restored test control and no kill credit. Its failure/trace are retained separately from the successful focused probe. Both probes operate only in isolated copies and leave the live native runs unchanged.

Windows Release run `14c14e40da4b44e4be22328aaeeefb96` in `release-windows-01` passes 3,891 unit and 2,121 integration cases. Architecture reports 1,016 passed and 17 failed of 1,033. All 17 failures use the older Developer checkpoint's package report (`ddd1ef…`) against the newly packed candidate (`f16982…`), rather than the current Release inspection. The original failed report remains retained. The runner now supplies a repository-associated checkpoint root to its test children, and architecture readers select that root; isolated fixture repositories retain their own default. All 17 failed cases pass against the actual Release inspection after this fix; the new repository-isolation regression also passes. Coherent Developer follow-ups pass on both lanes. A later Windows `release-windows-02` attempt fails before tests because the overlapping helper driver holds a shared verification DLL open. That original failure remains retained; after the driver closes, `release-windows-03` retries in a fresh evidence root. Linux uses `shipping-profile-01/release-02`. Missing release gates remain explicit failures.

Native Linux Release run `4c444f46156c443d9a09a3cb8ce8a015` completes all **17 implemented stages** and **7,185 tests**, with zero failures/skips: 3,892 unit, 2,123 integration, 1,033 architecture, 112 compatibility and 25 business. Its [summary](evidence/shipping/release-linux-summary.json) and [validation](evidence/shipping/release-linux-validation.json) retain overall **exitCode 2**, because the mutation family is not integrated, nine distinct original required groups remain Planned/unbound, and eight release gates still lack qualification validators. The outer PowerShell/WSL orchestration reports exit 1 for that native refusal. No failed stage or test is hidden behind these pending gates. The complete native bundle is retained as `release-linux-native.tar.gz`.

Fresh Windows Release run `bf737c089aeb47b88d6ae4d25cdaea18` also completes all **17 implemented stages** and **7,183 tests**, with zero failures/skips: 3,892 unit, 2,121 integration, 1,033 architecture, 112 compatibility and 25 business. Its [summary](evidence/shipping/release-windows-03-summary.json) and [validation](evidence/shipping/release-windows-03-validation.json) retain the same **exitCode 2** refusal, with no failed stage. The full native archive is `release-windows-03-native.tar.gz`. The original 17 architecture checkpoint-selection failures are absent in both coherent Release runs. Each validator emits 17 MissingRequiredCase entries for **nine distinct IDs** (eight appear at both family and Planned-inventory checks), one MissingFamilyExecution and eight MissingReleaseGate entries; these are not 17 different unimplemented requirement groups.

The nine IDs are `MUT-FULL-CORE`, `ARCH-PUBLIC-TYPES`, `ARCH-IMMUTABLE-CONFIG`, `COMPAT-API`, `COMPAT-SOURCE`, `COMPAT-BINARY`, `COMPAT-CONFIG-WIRE-BEHAVIOR`, `BC-009` and `BC-010`. The eight gate IDs are `runtime`, `package-suites`, `coverage`, `mutation`, `history`, `examples`, `lanes` and `bundle`. The retained validator messages reflect the maintained pending gate inventory; the independent passing observations below do not remove those gates or establish that their validators are implemented.

## Coherent checks and full shipping coverage

Windows Developer run `556a533272bb4f99a8c5af51f49c3ee1` passes **7,071 cases** (3,892 unit, 2,121 integration, 1,033 architecture, 25 business). Native Linux run `cd4302542b8642acaab6b78fec21a834` passes **7,073 cases** (3,892 unit, 2,123 integration, 1,033 architecture, 25 business). Both have zero failures/skips and pass all 16 enabled stages. Their original summaries, discovery/reconciliation, native TRX files, source inventories and logs are retained in the `developer-*-native.tar.gz` archives. These Developer profiles do not claim mutation or compatibility execution.

All four normal native coverage families pass on each lane. Their initial shipping observations nevertheless fail the per-assembly policy: the helper coverage collected through those families is insufficient. Windows' aggregate alone clears both thresholds, demonstrating why the individual assembly checks are required. Both original failed observations remain retained as `coverage-4families-*.json`.

A dedicated 30-case native helper run loads a byte-verified copy of the actual packaged helper and matching symbols, without rebuilding shipping code. Its input record pins original/copied test sources, candidate, helper DLL/PDB and reference fixture; loaded helper bytes are checked again after execution. The C# preparation/check source and generated projects are retained with the native reports. This supplements the four standard reports. It does not turn the original four-family invocation into a pass or claim that the maintained entry point automatically runs this additional suite.

| Shipping assembly | Windows lines | Windows branches | Linux lines | Linux branches |
| --- | ---: | ---: | ---: | ---: |
| CIS.Core | 95.8711% | 91.7852% | 95.5082% | 90.6161% |
| CIS.TypeManager.Build | 99.0385% | 95.7143% | 99.0385% | 95.7143% |

The final five-report observations pass the unchanged **95% line / 90% branch** thresholds for both assemblies separately ([Windows](evidence/shipping/coverage-final-windows.json), [Linux](evidence/shipping/coverage-final-linux.json)). Native denominators remain 4,408 lines/3,165 branches for Core and 104 lines/70 branches for the helper. Branch aggregation takes MAX covered counts for each unchanged class/source/line denominator; it never sums unidentified branch hits. No handwritten executable source, attribute or function is excluded, and OS lanes are not merged.

The four-family collectors complete their source/candidate/input checks before the helper test is refined from 29 to 30 cases. The added verification-only case and dedicated report are separately pinned; shipping source and candidate bytes do not change. Integrating this direct helper suite into the maintained collection command remains an automation follow-up.

## Exact candidate and examples

The retained Windows candidate is `CIS.Core.0.1.0-dev.1.nupkg`, SHA-256 **`da92c37a0b4dd61fa80a4876ed0c40f6ecaf68eab114ade0052e663cf12905d8`**. Current Windows coverage and first-release/example controls use it. Linux coverage uses its own native Developer package/build; the same Windows package receives the separate cross-platform controls. Earlier candidate hashes in the parent record remain historical observations.

Fresh Release packing creates distinct containers: Windows **`7fb327a95f9767e71ae5e18ef892a9c2affb7e4d928a51aef645c7854cba5678`**, and Linux **`3457982640ece405633dcfc4c8c9ecb9c27915557b8aad50ab0df910ebecbebf`**. The Linux coverage container is **`558640dd4265f79d227e862b3f8edc4f3cde63af18dea21b239a6127a8034d61`**. Exact [Windows](evidence/shipping/package-payload-windows.json) and [Linux](evidence/shipping/package-payload-linux.json) comparisons verify that **all 14 member names and uncompressed member bytes match within each lane**, including both shipped DLLs. The containers themselves are not byte-identical; their separate hashes and originals are retained. This establishes unchanged binary payloads for the coverage association. It does not reclassify an executed consumer check as execution against another container: the separate first-release/example controls remain credited to `da92c37a…`, while the Release profiles consume their newly packed containers.

All eight first-release source/API/binary positive and negative controls pass on each lane, with the same unchanged Windows-compiled frozen consumer reused on Linux. The publication ledger still contains zero releases: status is **NoBaselineForIdentity**, with zero released versions replayed. These controls do not qualify the complete cumulative configuration/wire/behavior matrix.

The Type Manager examples pass all **66 controls per lane**, including exact-candidate source/package build, publish/layout and negative controls. The original Windows missing-SDK-host-package attempt remains a failed attempt; the approved pinned host/runtime downloads are supplied to the retry cache. The logging `ConsoleHuman` host remains a configuration-only scaffold, so these Type Manager observations do not satisfy the entire release examples gate. The complete retained kit and licence review also remain unqualified.

## Remaining qualification work

1. Triage the full-Core non-killed catalogs against exact source and relevant native tests. Start with the largest queue/runtime and byte-budget clusters, and the Type Management slice. Reproduce suspicious survivors individually before choosing stronger assertions, a product fix or a narrowly evidenced equivalence disposition. Retain timeouts separately; they are not strict kills.
2. Review compiler rollbacks and ignored blocks across full Core. The valid, independently detected 2984 variant demonstrates why the native CompileError label alone is insufficient. Complete source membership and the passing six-file subset do not qualify the other filtered entries.
3. Integrate the maintained full-shipping/native-lane matrix into Release reconciliation, including actual child execution and source-bound exclusion review. `VerificationEvidence.GetIssues` currently refuses the absent mutation family and explicitly reports the unimplemented full-matrix validator; independent reports do not silently fill those slots. Also integrate the direct helper coverage suite into the maintained collection entry point.
4. Implement and bind the nine remaining required groups, complete cumulative or explicit first-release configuration/wire/behavior qualification, and finish the executable logging example and retained-kit/licence gates. Preserve the passing exact-package controls and their container identities when assembling the eventual release bundle.

These are required follow-ups, not scope waivers or claims that the shipping implementation needs changes. Publication remains unqualified until the unchanged gates accept actual evidence.

## Source context

The refreshed local graph uses schema **2**, build `sha256:a15102d5400a0643905f2828799dfd2410f6dd3c1972c3f93a239d43592c62c2`, with **12,574 nodes / 64,623 edges**, fresh status, zero errors/warnings and compiler capabilities `cis.csharp.compiler/6` and `cis.csharp.semantic-facts/3`. The exact method root is `CIS.Verification.LoggingMutationQualification.RunAsync(string[], string)` in the engineering project. An exact-ID query returns one root; a depth-one incoming `calls` query with limit five returns two nodes and the single CLI call at `Program.cs:7`, without truncation or diagnostics. The earlier text query with limit two is truncated and is not treated as complete context. Argument shapes and compiler call edges do not prove runtime execution.

Artifact-only probe tools and the generated helper test project are outside that canonical compiler graph. Their retained exact source, compiled tools, native process records and TRX establish the execution observations. Derived graph state remains local and is not a replacement for source or native reports.

## Retained working roots

Windows work is under `.artifacts/csharp/shipping-release-2026-10-19/`. Linux work is under `/home/user/cislib-verification/release-82540222-20261019/`:

- Helper: `shipping-helper-05/run`; Windows `helper-windows-05` (the preceding `03` and `04` runs are retained).
- No-bailout Core: `shipping-core-05/run`; Windows `core-windows-05`.
- Child controls: Linux `shipping-children-02/run`; Windows `child-controls-windows-01`.
- Helper source replay: Windows `helper-replays-windows-01`.

Input archive SHA-256 values: initial shipping snapshot `8ad8d82e3d7350a685299cb86b9f1bcbadd5159c8aadc7cb4f837298126dbb6b`; locked helper snapshot `94b68e65b532f94383da91782623e19b8f5d4a785a0a869087ea8a062c9bfaa3`; no-bailout Core snapshot `7778a2fd8fbf2face273af1aeba4f02ab33a42731ff4ea7f9e8c54007e0f8f5d`. Installed toolchains/caches remain prerequisites. These working roots are not yet a complete retained release kit.

The [retained final helper input closure](evidence/shipping/shipping-inputs-current.tar.gz), SHA-256 `833a635d19b0e8719c6af0a27b2279625b6263184189fa037a254b86f88c2956`, contains **997 verified files**, with [membership/checksums](evidence/shipping/shipping-inputs-current.json). It preserves the language, conformance, shared contracts and compatibility inputs from that native snapshot. The unrelated user-owned untracked Web launch settings are explicitly omitted from source content and remain untouched in the workspace. Later documentation annotations are separate from this executed snapshot. This is a source input closure, without installed SDK/cache material or a completed licence review, and is not a complete offline release kit.

`core-driver-*.tar.gz` and `helper-driver-*.tar.gz` retain the actual compiled verifier runtime closures separately from source. The final helper verifier hashes are `d5ce7cdcf664ef146082b7b90206339b7d3d06d0fe69eedd057c6f7855174abb` on Windows and `8067b10ef401c1abaee7c9aba5a571579cd68314095bf65b7e7d7b37983b080f` on Linux. `qualification-tools.tar.gz` and `core-compiler-probe-tools.tar.gz` preserve the additional probe/comparison/retention source and compiled tools; the raw generated projects and original commands/results remain in their native evidence archives.

`core-linux-primary-native.tar.gz` preserves the original full native JSON/HTML reports, primary Stryker trace, input inventory, native process records and baseline/restored TRX. The native report SHA-256 is `024b1c16717d10894332e0f957fdbbe725d280c4d3d5b6d6ce5dc9873e6c76e1`. Bulk per-runner diagnostics total 7.6 GB before compression and remain in the native working root plus the **462,160,615-byte** ignored `.artifacts/csharp/shipping-release-2026-10-19/core-linux-native.tar.gz`, SHA-256 `467a9a2af366f4b61c3e2e34a8e555a28d751c3c73878a4b74c2a5daa6c9fa87`. They are deliberately not duplicated into the canonical evidence directory. That working retention is explicit, not a durable complete-kit claim.

The corresponding **51,847,823-byte** `core-windows-primary-native.tar.gz`, SHA-256 `8b3717568452ffffca319ab5454cfd2618413fed5fe333462f7722ebe5f56b93`, retains the same primary evidence categories. Its original native report SHA-256 is `1747e79123eefaff0bbad2e5fde28b5099f4119ab45dc016d6f1ddc634f782be`. Complete bulk diagnostics remain in the native working root and the **446,934,822-byte** ignored `.artifacts/csharp/shipping-release-2026-10-19/core-windows-native.tar.gz`, SHA-256 `42327f1915cc26e11c6afff0eb873b718631a36932b4bf3cd85ff7cb863f976f`; that archive's readability is checked. Actual compiled Core verifier runtime closures are retained separately: Windows verifier SHA-256 `6f5c55928adbe087e12daae9ce09ea200e58331d469b0447f6712c8eecb06bd6`, Linux `33e5ceec6508f344a53af1f07743343078d956ca071668e47c1b3f145c01e779`.

The final [evidence checksum manifest](evidence/shipping/checksums.json) binds **119 files / 216,325,206 bytes**, excluding the manifest itself. Every listed file hash and final file membership is independently checked after capture. Manifest SHA-256 is `b36bac01d6ac124665c565cd4bebf1256c92b7f50cd29f1d1cec2843a674581a`. It covers the canonical shipping evidence directory, including the compact primary archives and graph queries, rather than the separately named bulk working archives, installed toolchains or the whole repository. Checksums establish file consistency; they do not approve the failing mutation scores or pending Release gates. No commit, push or publication is performed.
