# Logging mutation harness and boundary fixes, 19 October 2026

The user authorized the [triage fix order](mutation-triage.md). The final focused mutation diagnostic **passes at 94.53125% on Windows and native Linux**, with all known-kill controls and 133 baseline/restored tests passing on each lane. This change implements mutation-only MSTest serialization, expands the functional test closure and strengthens observable capture/rendering/ownership assertions. **Shipping Core and build-helper source are unchanged.** The 90% threshold, Complete mutation level, native reports and strict timeout rule remain unchanged. This is the six-file diagnostic; full shipping release qualification remains separate.

## Maintained execution

[LoggingMutationQualification](../../../implementations/csharp/eng/CIS.DependencyChecker/LoggingMutationQualification.cs) exposes the `logging-mutation` engineering command. It captures source hashes, creates an isolated copy, checks SDK 10.0.401 and the pinned runtime/tool versions, builds and runs a native baseline, then executes Stryker 4.16.0 on the explicit .NET 8.0.31 host. It checks source membership/text and source stability, rebuilds unmutated code and requires passing restored tests. Native process streams and reports are retained; the derived result uses `Killed / (Killed + Survived + NoCoverage + Timeout)`.

`CisMutationTesting=true` is a child-process build property. [Test targets](../../../implementations/csharp/tests/Directory.Build.targets) translate it to `CIS_MUTATION_TESTS`; [TestAssembly](../../../implementations/csharp/tests/TestAssembly.cs) selects DoNotParallelize only in that mode. Ordinary Developer builds retain four-way method parallelism. SchedulingMatchesRequestedMode asserts that the requested environment matches the compiled assembly attribute, and its native stdout marker is required in baseline/restored TRX. The parser smoke and adapter mutation entry points also request the mutation-only mode for their isolated child runs; their historical results are not requalified by that code change.

Four source-bound native controls must be Killed: capture failure diagnostic 1950, positive-alignment rendering 1270, batch sorting 4094 and closed admission 4402. The driver reports a scoped `diagnosticGatePassed`; it deliberately leaves `exclusionsReviewed=false` and does not satisfy the full Release profile. [Invocation and boundaries](../../../implementations/csharp/verification/mutation/README.md).

The source selection remains IncomingRingBuffer, IncomingBatchIndex, CapturedInterpolation, InterpolationCapture, LogInterpolatedStringHandler and CoreLogger.Interpolation. Functional execution now includes the ring, index, interpolation, incoming queue, frontend, runtime, startup and lifecycle classes, plus the scheduling control. Performance-observation workloads remain separate. The small allocation-bound regressions below are functional invariants, rather than timing benchmarks.

## Behavioral assertions

[Interpolation boundaries](../../../implementations/csharp/tests/CIS.Core.UnitTests/InterpolationTests.Boundaries.cs) now assert:

- Exact null-guard exception types and parameter names; complete escaped template text, hole numbering, alignment and formats.
- Inclusive 1024-part, 256-character format and 8192-scalar/precision limits, including the first excess value and literal.
- Aggregate owned-text limits, repeated clipping, template plus capture clipping and left-padding truncation after an earlier omission.
- Empty/null, nonempty, padded and omitted tails at the exact rendered limit; stopping native formatting after the rendered budget is full.
- Omitted aligned values retain one template hole and contribute no padding; a clipped owned format omits the value and preserves the immediate truncation flag. This uses formatted text for the earlier value so template clipping cannot mask a cleared capture flag.
- Valid supplementary Unicode across the scalar cut; repeated oversized spans; bounded temporary allocation when capturing a large borrowed span and no copied span after the part budget is exhausted.
- Null-returning user formatters, ordinary single-letter formats and custom numeric formats containing long digit sequences followed by a custom placeholder.

The allocation tests warm the capture path and use coarse managed-allocation ceilings. They distinguish a bounded owned prefix from an intermediate string proportional to the entire supplied span; they do not assert elapsed time or exact allocation counts. Source review corrected the earlier equivalence candidate for 1912: the final message can remain identical while the intermediate allocation violates the capture bound.

[Ring tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/IncomingRingBufferTests.cs) add inclusive maximum capacity/rounded budgets, invalid capacity/options parameter failures and successful/stale conditional abandonment with exact owner/counter/gauge observations. [Batch tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/IncomingBatchIndexTests.cs) exercise equal creation timestamps across 64 slots, distinguishing physical-slot ties beyond small sorts. No producer lock, callback seam, reservation protocol or new priority rule was added.

## Regression checkpoints

The first expanded functional scope passes **129/129** native tests on Windows. At that coherent snapshot, Developer run `fe73d679126b46b3bab5859fc9a74ba4` passes **7,066 tests** with zero failed/skipped: unit 3,887; integration 2,121; architecture 1,033; business 25. All enabled stages and validation pass. Its native archive hashes to `ca7d003860772715af8b8a398d9fa8c79c904f31b6f6f6bfaa7b8b453913e2ac`.

The final refinement adds four unit-only cases: the two allocation bounds, full-budget native-format stop and long custom-format row; it also strengthens the existing owned-format assertion. All **133/133** functional cases pass in the isolated candidate. After applying those exact bytes to the main checkout, the entire unit family passes **3,891/3,891**, zero failed/skipped, focused run `78eb2f81d69f46668ed0b9f48f0092b4`. This is a unit-family follow-up to the preceding Developer checkpoint; the record does not relabel the older full-profile execution as having run these extra cases.

The first maintained 129-case mutation runs pass on both Windows and native Ubuntu 24.04 x64: 238 Killed, 10 Survived, five NoCoverage, three Timeout, 28 CompileError and 62 Ignored. Strict score is **92.96875%**, with all four known-kill controls passing. Those observations are retained independently of the final boundary refinements.

An intermediate 131-case mutation attempt was deliberately stopped during instrumentation on both platforms after further source review found the exhausted-span, custom-format and independently masked capture-flag cases. Its baseline and unmutated restoration pass; it has no final native report or credited mutation score. The subsequent finalized input set contains 133 cases. The stopped attempts remain in their isolated directories rather than being represented as successful runs.

## Final two-lane result

| Observation | Windows x64 | Ubuntu 24.04 x64, WSL2 |
| --- | ---: | ---: |
| Baseline / restored native tests | 133 / 133 passed | 133 / 133 passed |
| Killed | 242 | 242 |
| Survived | 7 | 7 |
| NoCoverage | 4 | 4 |
| Timeout | 3 | 3 |
| CompileError | 28 | 28 |
| Ignored | 62 | 62 |
| Strict score | **94.53125%** | **94.53125%** |
| Known-kill controls | 4/4 passed | 4/4 passed |
| Native / driver exit | 0 / 0 | 0 / 0 |

Both lanes preserve the 346 selected mutants and compute `242 / (242 + 7 + 4 + 3)`. Native Stryker displays 95.70% because it credits the three timeouts; that extra credit is not used here. Neither the 28 compile statuses nor 62 ignored blocks is silently promoted to a kill or accepted release exclusion.

The [Windows result](evidence/mutation-fixes/windows-results.json) binds native report SHA-256 `93f35652a3f9c4523838082d12912daa44367ea6a47b96e48d14a5231bf1dd73`; the [Linux result](evidence/mutation-fixes/linux-results.json) binds `8e19a68e3bcf90e97f49561ddd74d07e5632e1a0314f93083fc66a99bc981f47`. Both use config SHA-256 `efa6edb9a2a13e3331a87eb06cfbf5b87bc035b10dc8ebed53302f069aca9958` and pinned Stryker DLL `1d5fbd3df14f3ab0425421c597a36a5cbdd474ad9804a0bd4e212e5b0320eba9`. Engineering verifier binaries differ by lane and are explicitly pinned in those results.

[Windows native report](evidence/mutation-fixes/windows-native-report.json), [Linux native report](evidence/mutation-fixes/linux-native-report.json), baseline/restored TRX, exact snapshot input inventories and complete compressed native bundles are retained. The Windows archive SHA-256 is `608c1ad1dd2b77cb0fe922a62d25fa3aa14d89796d3d39e8956b778b71a3200c`; Linux is `35113ce1e5626461b669598d36a1913f0b3e89189e7cffcd1ed4dbf907e05e01`. Original native files remain byte-preserved.

## Compiler, filter and timeout dispositions

All **90** selected entries carrying CompileError or Ignored in the first maintained report were replayed independently against exact source spans in another native Linux copy. Baseline and restored test controls pass. Under the repository's compiler/analyzer settings, 48 variants do not build, 37 compile and are detected by the selected tests, and five initially survive that selection. These are independent observations; the native statuses and score are not rewritten.

Of the original 28 CompileError entries, **26 do not build alone** and **two do compile and fail existing tests**: part-limit comparison 1952 and batch-entry insertion 4092. Those two remain native instrumentation rollbacks, rather than being misclassified as invalid C# source. Some build-negative ignored blocks remove required returns, assignments or uses and trigger nullable/unused-local diagnostics under warnings-as-errors; this is recorded as failure under the maintained build policy, not automatically described as an impossible syntax tree.

The first five surviving ignored entries are 1232, 1257, 1898, 1997 and 4408. [Final supplemental controls](evidence/mutation-fixes/supplemental-exclusions.json) detect 1232 (full rendering budget), 1898 (exhausted span allocation) and 1997 (long custom numeric format), with all 133 baseline/restored cases passing. Across the 90 distinct entries, the combined independent review is therefore **48 build-negative variants, 40 detected variants and two remaining selected-test survivors**. Entry 1257 concerns the defensive false-return branch of native TryFormat; 4408 removes the first closed-state fast return while the increment/recheck/rollback path still rejects closure. These source invariants remain separate from native kill credit and are not automatically accepted release exclusions.

The three native timeouts in the first maintained run are 4383 (inflated scan budget), 4405 (removed publisher exit decrement) and 4410 (removed publisher enrollment increment). Each exact standalone variant fails an existing direct ring test by assertion, with passing before/after controls. They can stall a broader async lifecycle test before a direct assertion is reached. Their native Timeout statuses remain unchanged and receive no strict kill credit; no correct producer loop or lifecycle protocol was changed to accommodate a synthetic mutant.

The final seven native survivors have source-bound equivalence/defensive dispositions, without score adjustment:

| IDs | Source observation |
| --- | --- |
| 1262 | Captured literal alignment is always zero, so the ordinary padding formula also yields zero. |
| 1267, 1278 | The changed zero-alignment condition adds only Pad(0), preserving output, remaining budget and truncation. |
| 1907 | Invalid span Unicode has already set the permanent capture error; later owned text does not publish a successful occurrence. |
| 1910 | One extra intermediate scalar is bounded again by AddValue/OwnText; final owned content and truncation remain the same. This differs from the unbounded intermediate copy in 1912, which the new allocation test detects. |
| 1964 | Changing fallback text after invalid Unicode leaves the occurrence rejected with the same safe error category. |
| 1991 | A single-letter numeric format has an empty precision suffix, leaving precision zero and the same result. |

The four NoCoverage entries are 1258/1259 in the defensive native TryFormat-false path and 4414/4415 in the enrollment increment/closed-recheck interleaving. Capture retains only supported owned built-ins or copied user text, with bounded format metadata and a 16,384-character formatting buffer. Do not introduce an arbitrary consumer formatter to force that defensive branch. The second pair needs a real closure interleaving; no shipping pause callback or lock was added solely for mutation coverage. These observations stay uncovered in the raw report and remain explicit release dispositions.

The [primary 90-entry review](evidence/mutation-fixes/primary-exclusions.json), [primary verifier inputs](evidence/mutation-fixes/primary-replay-input-control.json), supplemental selection with its parent native-report hash, exact executed C# source/projects and original stdout/stderr/TRX are retained. Primary replay archive SHA-256 is `cd8a69c232a31510264e405b597bcb30d42abe5d24a50a6b96bcc9596ba4deab`; supplemental replay archive is `1f20877406a4b87a682cffdc68d52fa34cfdcca2fd8046d3fead4672d113003e`. The standalone timeout controls also retain native assertion failures and two passing before/after ring controls. No independent replay modifies a native mutation status or denominator.

## Retention and reproduction boundaries

[Evidence](evidence/mutation-fixes/) contains the full-profile checkpoint summary/validation/native archive, final full-unit and focused TRX, native mutation reports and source inventories for both lanes, independent dispositions and their native archives. The [current C# input delta](evidence/mutation-fixes/current-input-delta.tar.gz), SHA-256 `84169a8072adbbab732f55387a494e4b854c6b5ae29110c3e1e6b1f6226f17c5`, retains the changed engineering/test/configuration inputs beyond commit `8254022298b8f42c2d7f2f01f1df5e418b44c4aa`, including the preceding first-release tooling work. It excludes the unrelated user launchSettings file. Documentation annotations after execution do not change the executed code/test pins.

The exact final snapshot archive remains in ignored `.artifacts/csharp/logging-mutation-inputs-2026-10-19-04.tar`, SHA-256 `3b9a05ba8c11db35b3db3079b462a6e6b692abbf7639f381d019444b1eb73767`. Final Windows execution is under `.artifacts/csharp/logging-mutation-final-2026-10-19/windows-03/`; Linux is `/home/user/cislib-verification/release-82540222-20261019/mutation-fixes-03/`. Initial 129-case executions, stopped 131-case attempts and replay source copies remain independently retained. Installed SDKs, packages and tool caches are still prerequisites; these records are not a complete offline kit.

## Remaining release scope

Passing this diagnostic does not qualify all Core source, the separately packaged CIS.TypeManager.Build helper, child-process injection or the complete two-lane shipping mutation matrix. The broader all-Core compiler rollbacks and required Release-profile execution remain outstanding. Historical publication count remains zero; this task neither creates history nor changes the prior compatibility evidence. No package publication, external source review, commit or push is part of this fix.

## Graph context and evidence limits

The post-change schema-2 graph refresh produced build `sha256:f8ce5d68107b9ba2ce7646809955688cef4120d9fde8219af9ce35685d115812`, with 12,563 nodes, 64,550 edges and zero warnings/errors; status and the bounded queries reported fresh. The initial sandboxed persistence attempt failed with `CIS-GRAPH-STORE-002` (database access denied); the subsequent refresh completed successfully. Compiler capabilities are `cis.csharp.compiler/6` and `cis.csharp.semantic-facts/3`.

An exact method search (limit two, one result, no truncation) resolved `CIS.Verification.LoggingMutationQualification.RunAsync(string[])` in `implementations/csharp/eng/CIS.DependencyChecker/LoggingMutationQualification.cs:13`. Its full root is `cislib::symbol::cis-dependencychecker/project/implementations%2Fcsharp%2Feng%2FCIS.DependencyChecker%2FCIS.DependencyChecker.csproj/csharp/method/CIS.Verification.LoggingMutationQualification.RunAsync%28string%5B%5D%29`. Incoming `calls` traversal at depth one, limit five returned two nodes and one compiler-bound edge, without truncation or proposed edges: the top-level Program entry point calls this method at `Program.cs:7`. Canonical source confirms the command dispatch.

These graph observations locate the maintained entry point; they do not establish reflective MSTest discovery, mutation execution or complete test bindings. Native TRX, immutable Stryker reports and the [47-file hash manifest](evidence/mutation-fixes/manifest.json) establish the executed evidence. Broad earlier tool output that exceeded display limits was followed by bounded source/report reads; no omitted graph neighbourhood is claimed as reviewed.
