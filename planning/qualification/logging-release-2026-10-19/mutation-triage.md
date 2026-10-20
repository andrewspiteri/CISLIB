# Logging mutation triage, 19 October 2026

The user subsequently authorized the fixes. Current implementation, native regression results and source-bound dispositions are recorded in [mutation fixes](mutation-fixes.md); this document preserves the preceding triage observations.

The first fix is the mutation harness. The original six-file report has false survivors and compiler rollbacks that are not genuine source compile failures. No defect in the unmodified shipping implementation has been confirmed by this triage. Production code, the 90% threshold and the strict timeout rule remain unchanged.

Disabling MSTest method parallelism in an isolated diagnostic detects **33 of the original 77 survivors with unchanged assertions**. The resulting strict score is **191 / 256 = 74.609375%**, still below 90%. The actionable remainder is 44 Survived, 19 NoCoverage and two Timeout observations, plus source/tool disposition of the unchanged 28 CompileError and 62 Ignored entries.

This reviews candidate `8254022298b8f42c2d7f2f01f1df5e418b44c4aa` and the original report SHA-256 `ccc3c3e095875b49325bcf496088a0a8cbaef4a2b567f6076e8290d2a3ce8dd9`. The original 159 Killed, 77 Survived, 19 NoCoverage, one Timeout, 28 CompileError and 62 Ignored observations are preserved. The original strict 62.109375% is a historical native diagnostic, not a reliable estimate of test adequacy after the attribution problem below was confirmed. It provides no release credit.

## Confirmed harness and scope findings

The native initial host trace explicitly reports MSTest method parallelism with four workers. Several original coverage mappings identify unrelated tests: mutant 1950 changes the capture-failure error code but is assigned to the compiler/overload test; 1270 removes positive alignment padding but omits the formatting/alignment test. Those assignments cannot support a missing-assertion conclusion.

The [original host observation](evidence/mutation-triage/original-host-parallelism.txt), the report's original `coveredBy` IDs and the standalone native TRX results establish the local failure. The matching upstream [MSTest attribution issue #3757](https://github.com/stryker-mutator/stryker-net/issues/3757) describes the same 4.16.0 behavior. That issue is corroboration, not a substitute for the local controls. Stryker's [configuration guidance](https://stryker-mutator.io/docs/stryker-net/configuration/#coverage-analysis-string) recommends checking suspicious survivors without the coverage optimization; any such run must still count timeouts as non-kills here.

Each standalone replay applies the exact original report span and replacement to an otherwise clean isolated Linux checkout. It runs an existing test, without adding assertions. Sources are checked byte-for-byte before replacement and restored afterwards. SDK 10.0.401, runtime 10.0.12 and Release configuration are retained.

| Original ID/status | Exact change | Existing test that fails with the standalone change | Finding |
| --- | --- | --- | --- |
| 317 / Survived | Clear `ConflictingInterpolationInput` | `InterpolationTests.InvalidUnicodeAndConflictingRichTextHaveExplicitInvalidResults` | False survivor; the diagnostic already has an exact assertion. |
| 1270 / Survived | Remove positive-alignment padding | `InterpolationTests.StandardFormattingAlignmentBracesNullAndRawStringsUseInvariantCulture` | False survivor; rendered padding is already asserted. |
| 1904 / Survived | Reject a successfully decoded span rune | `InterpolationTests.SpanAndCopiedHandlersCannotMutateAlreadyPublishedOccurrences` | False survivor; valid span ownership/content is already asserted. |
| 1950 / Survived | Clear `InterpolationCaptureFailed` | `InterpolationTests.RecoverableCaptureFailureReturnsSafeResultWithoutRetainingException` | False survivor; recoverable failure already has an exact diagnostic assertion. |
| 4094 / Survived | Remove batch sorting | `IncomingBatchIndexTests.CapturedCreationOrderDoesNotCompactSlotsAndSkipsNewerReplacement` | False survivor; non-slot creation order is already asserted. |
| 4402 / Survived | Remove stopped-admission return | `IncomingRingBufferTests.EnrolledPublisherMayFinishAfterCloseWhileOtherProducersKeepProgressing` | False survivor; rejection after closure is already asserted. |
| 1952 / CompileError | Reverse the part-limit comparison | `InterpolationTests.UnicodeWidthPrecisionAndPartLimitsProduceBoundedTruncatedRecords` | Compiles alone and fails the test. Investigate instrumentation rollback; do not exclude it as an inherently invalid C# mutation. |
| 4092 / CompileError | Empty the batch-entry insertion block | `IncomingBatchIndexTests.CapturedCreationOrderDoesNotCompactSlotsAndSkipsNewerReplacement` | Compiles alone and fails the test. Same rollback qualification gap. |
| 4464 / NoCoverage | Remove lifetime abandonment increment | `IncomingEventQueueTests.ExpiryLeavesActiveOwnershipUnresolvedThenAbandonsOnlyWaitingWork` | Detected by an existing test outside the original three-class filter. Broaden relevant test scope before claiming a global gap. |

All nine changed-source test invocations fail by assertion with exit 1, rather than a compile error or process timeout. The original 43-test suite passes before and after the first six variants and again after the rollback variants. The lifecycle control passes before and after mutant 4464. Native [replay exits](evidence/mutation-triage/exits.txt), per-mutant TRX/logs and original source hashes are retained under [mutation-triage evidence](evidence/mutation-triage/). [Standalone](evidence/mutation-triage/standalone-variants.json), [rollback](evidence/mutation-triage/rollback-variants.json) and [test-closure](evidence/mutation-triage/closure-variants.json) manifests bind the exact variants to the original report. The retained C# probe exports those spans; it does not alter either native report.

## Serialized diagnostic and complete ledger

The second native run changes only the isolated test assembly attribute from `Parallelize(Workers = 4, Scope = MethodLevel)` to `DoNotParallelize`. Stryker remains 4.16.0 on the explicit 8.0.31 host; SDK/runtime pins, all six source files, 43 selected tests, assertions, Complete mutation level, four process runners, timeout allowance and thresholds remain unchanged. [Exact test-source delta](evidence/mutation-triage/serialized-test-assembly.diff) and [input hashes](evidence/mutation-triage/serialized-input-hashes.txt) preserve that distinction. This is a successful diagnostic of the attribution problem, not a completed fix to the maintained mutation entry points.

| Native status | Original run | Serialized run |
| --- | ---: | ---: |
| Killed | 159 | 191 |
| Survived | 77 | 44 |
| NoCoverage | 19 | 19 |
| Timeout | 1 | 2 |
| CompileError | 28 | 28 |
| Ignored | 62 | 62 |
| Selected total | 346 | 346 |

Thirty-three Survived entries become Killed; 4383 changes from Killed to Timeout. Every other native status remains unchanged. The [ledger](evidence/mutation-triage/ledger.json) checks exact file, source text, location and replacement identity for all 346 selected mutants. Its 188 rows cover all **187 original non-killed entries**, plus newly non-killed 4383. Each row retains both native statuses, source SHA-256, replacement, coverage test names, review topic and next action. An unchanged Ignored/CompileError status is not an accepted exclusion. Equivalence candidates below remain unapproved for score adjustment.

The [serialized native report](evidence/mutation-triage/serialized-mutation.json) hashes to `cae7526f67394eb5991e151174b491b50846b3ce4ef6e22998e2164c4c96258a`. Its native score is 75.390625% (displayed 75.39%) because it credits both timeouts; this repository credits neither. Both metrics still fail the unchanged 90% gate, exit 1. The [console](evidence/mutation-triage/serialized-console.log) and [compressed native logs/HTML](evidence/mutation-triage/serialized-native-logs.tar.gz), archive SHA-256 `32f0ec6276785f52d1aaed1fe9d18817d435ad2b105b571eda811d24c57423af`, retain the complete diagnostic. Shipping source is restored, and the [unmutated serialized regression](evidence/mutation-triage/serialized-restored.trx) passes all 43 tests with zero failed/skipped.

## Fix order

1. **Make maintained mutation execution trustworthy.** Carry the isolated serialization control into the mutation-only harness, independently of Stryker's separate process concurrency. Keep normal Developer test parallelism. Make the effective setting observable and add small known-kill controls for caller capture, consumer rendering, batch sorting and closed admission. Merely changing Stryker `concurrency` to one does not establish serialized methods inside its test host. Keep both original reports and compare exact source/replacement identities, not changing IDs from unrelated runs. Audit other evidence using the same MSTest/runner settings before trusting its test attribution.
2. **Use the complete relevant test closure.** Include the functional incoming queue, frontend, runtime, startup and lifecycle classes alongside ring, batch and interpolation tests. Existing coverage of TryAbandon must be credited through execution, not duplicated speculatively. Keep performance observations separate. Qualify direct abandonment ownership with current/stale expected references, lifetime counters, waiting gauge and frozen batch classification where the broader tests still leave a gap. Include the closed-enrollment recheck interleaving without adding a shipping lock or callback solely for mutation testing.
3. **Strengthen observable boundary assertions where the corrected report still survives.** The concrete cases below are candidates identified from source and existing assertions. Implement only the missing ones, then demonstrate that the relevant exact mutations are detected. Do not replace existing padding, ordering, capture-failure or conflict-diagnostic tests just because the old report missed them.
4. **Resolve compiler/filter gaps separately.** Replay the two confirmed valid rollback mutants through corrected native instrumentation. Review the remaining 26 compile observations by their exact syntax and compiler diagnostic. The 62 ignored blocks are native block-filter decisions, not automatically equivalent or killed. Bind each to its body, relevant behavioral tests and separately executed mutations where needed. Full Core's whole-method rollback, build-helper scope, child-process injection and both release lanes remain separate qualification work.

## Behavior and test cases to examine

| Area and original IDs | Concrete missing distinction to check | Expected production behavior |
| --- | --- | --- |
| Public guards: 306, 309 | Invoke interpolation overloads with null `values` and null rich `context`; assert exact exception type and parameter name. | Preserve the explicit public null guards. |
| Template metadata: 1206, 1208, 1213, 1216, 1221 | Assert the complete `message_template` for escaped literal braces, several indexed holes, positive/negative alignment and format strings, alongside the rendered message. Existing ordinary rendering assertions do not establish exact template metadata. | Escaped literal braces and monotonically indexed holes retain their alignment/format metadata. |
| Independent truncation causes: 1227, 1288, 1914–1916, 1965, 1968 | Exercise clipped literal braces plus template clipping, multiple individually bounded values whose combined text exceeds the capture budget, oversized valid spans and omitted formatting followed by clipped left alignment. Assert immediate write-result and wire truncation flags alongside exact content. | Truncation remains sticky whenever any retained stage clips or omits content; capture has an aggregate owned-text budget. |
| Render budget: 1229, 1233, 1235, 1240, 1243, 1244, 1248, 1258, 1259 | Exercise exact 8192-scalar output followed by empty/null, padded empty, omitted and nonempty parts; numeric output above 4096 characters but within the permitted precision. | Empty unaligned tails add no truncation; lost content does. Scratch capacity must handle supported bounded native formatting. Do not manufacture arbitrary user-defined consumer formatters to reach a defensive branch. |
| Span ownership and limits: 1892, 1899, 1907, 1910, 1912, 1914–1916 | Check empty and valid spans around 8192 scalars, supplementary Unicode at the cut, malformed omitted suffixes and literal/span appends at the part limit. Reuse the existing valid-span and malformed-suffix tests; assess equivalent candidates before adding redundant assertions. | The complete supplied span is validated; only an owned bounded prefix survives publication. |
| Part/format limits: 1921, 1925, 1929, 1931, 1932, 1938, 1939, 1953, 1959 | Distinguish 1023/1024/1025 parts and 255/256/257 format characters; check a format whose owned prefix consumes the remaining capture text budget. Observe retained content and omission, not just an upper size bound. | Accept the inclusive limits; later content is bounded and omission is explicit. |
| User formatting: 1942, 1944, 1964 | Add null-returning plain and IFormattable values if absent; review whether altered invalid-Unicode fallback content can be observed after rejection. Existing caller-thread, ordinary fallback and capture-error assertions now detect their corresponding mutations. | Caller formatting is owned once; null output becomes empty; malformed Unicode/recoverable failures produce safe invalid results. |
| Numeric precision: 1986, 1987, 1991, 1998, 2001, 2002, 2005 | Check a valid precision around 4096, inclusive 8192 and omitted 8193, including short/ushort and another integral/floating type. Check ordinary single-letter and custom formats without treating every custom character as a precision digit. | Bounded supported native formats remain valid; excessive standard numeric precision is omitted safely. |
| Batch tie order: 4099 | Assert equal creation timestamps across enough entries to distinguish an unstable sort, plus stale-reference/reindex behavior already covered by existing tests. | Consumer ordering ties use physical slot. Distinct creation-time ordering already has tests. |
| Ring bounds and ownership: 4372, 4374, 4375, 4414, 4415, 4461, 4463–4466 | Cover inclusive maximum capacity, invalid capacities/options, enrollment closure recheck and both successful/stale abandonment in the functional closure. Mutant 4464 already has a broader killing test. | Preserve capacity validation, reject closed enrollment, remove only the expected occurrence and account the actual owner once. |

These groups are source-review topics, not a claim that every ID is a distinct missing test or production defect. Some mutations have no observable contract difference; some already have killing tests that the initial run failed to select. Native statuses remain in the ledger.

## Equivalence and defensive paths

Keep these dispositions separate from test fixes; no score exclusion is approved here:

- **1262:** captured literal parts are constructed with alignment zero. Computing the ordinary alignment formula for a literal therefore also produces zero padding. Validate the construction invariant rather than inventing a public literal-alignment behavior.
- **1267 and 1278:** changing `> 0` to `>= 0`, or `< 0` to `<= 0`, adds only a `Pad(0)` call at zero alignment. It leaves output, remaining budget and sticky truncation unchanged. These are source-supported equivalent candidates.
- **1991:** a single-letter numeric format has no precision digits. Iterating an empty precision suffix still leaves zero and returns false. This is an equivalent candidate.
- **1910 and 1912:** AddSpan's retained intermediate string is passed through AddValue/OwnText and bounded again. Assess the final owned content, flags and format-budget interaction before demanding tests of an intermediate counter.
- **4365 and 4435:** changing the initial cursor origin or choosing another inspected victim with identical severity and creation timestamp may satisfy the window P01 contract. The consumer's documented slot tie rule does not impose an extra producer tie rule. Do not add such a requirement to kill a mutant.
- **1258 and 1259:** the consumer sees owned built-in scalars, bounded formats and a fixed scratch buffer. Establish whether `TryFormat` can return false for a public supported input before labeling this a missing test. An invalid native format that throws is a different failure path and already has a regression test.
- **1938:** OwnText already marks actual clipping. Check invalid-format Unicode and invalid-result flags before deciding whether the second truncation assignment is redundant.

Mutant **4420** reverses the ring scan increment and produces an unbounded scan. The native timeout detects the hang but receives no kill credit under the repository metric. Keep a bounded process watchdog; do not change the correct producer loop to accommodate that synthetic defect or call the timeout equivalent.

Mutant **4383** multiplies the computed scan numerator by 100 instead of dividing by 100. Its work budget becomes excessively large; it is Killed in the original run but Timeout in the serialized run. Replay it against the existing exact-budget test in isolation, retaining the new timeout until there is a fresh native result. This is a synthetic bounded-work regression, not evidence that the unmodified budget formula is wrong.

The 28 original compiler observations have the following source dispositions. Only the two standalone controls establish compile-and-test behavior here; the other groups identify the exact compiler limitation to confirm in native diagnostics or isolated syntax replay. None receives kill credit.

| IDs | Source disposition and follow-up |
| --- | --- |
| 1204, 1211, 1212, 1217, 1218, 1222, 1223, 1225, 1272, 1285 | Stryker changes StringBuilder.Append to Prepend, which is not the required mutating StringBuilder API. Confirm the missing-member/invalid chained-call diagnostic; no production change is called for. |
| 1274, 1955, 1966 | Count/Count() becomes Sum: an invalid method-group comparison or no matching Rune sum overload. Confirm the exact compiler error. |
| 1219, 1252, 1254, 4091 | Negated pattern variables are used inside a branch where their original positive-pattern binding does not hold. Confirm definite-assignment/binding diagnostics. |
| 1239, 1948, 1988, 4368, 4395 | Mutated pattern conjunctions are contradictory null/string, unrelated sealed types, or disjoint range bounds. Confirm impossible-pattern diagnostics. |
| 1250, 1253, 1255, 1260 | Removing a text-assignment branch leaves the local `text` unassigned before use. Confirm definite-assignment diagnostics. |
| 1952, 4092 | Valid standalone source mutations, both detected by existing tests. Repair/replay the native instrumentation rollback rather than classifying the source mutation as intrinsically noncompiling. |

## Scope and evidence boundaries

The original and serialized diagnostic select only six Core files and three functional test classes. Neither qualifies the entire incoming control implementation, all Core, CIS.TypeManager.Build, child processes or both platform mutation lanes. The earlier all-Core run stopped without a final score and still needs source-bound whole-method rollback review.

Graph context before these documentation edits was fresh: schema 2, build `sha256:ece9497ecc577eb09e301dab1d6697c76d28f6b267b4ffc5785d28cbfda9d474`, compiler capability cis.csharp.compiler/6 and semantic facts cis.csharp.semantic-facts/3, no compiler errors/warnings. Exact roots were `CapturedInterpolation.Render(CapturedEventFacts)`, `IncomingBatchIndex.Capture(IncomingRingBuffer)` and `IncomingRingBuffer.TryAbandon(int, IncomingEvent)`. Bounded depth-one incoming-call queries for Render and TryAbandon were not truncated and led to the canonical runtime and consumer call sites. Linked test/lambda bindings are not complete execution coverage. Some broad source/log reads were truncated and were followed by bounded reads; they are not treated as complete graph or source evidence. Derived graph state remains local and is refreshed after canonical edits.

The native replay working directory is `/home/user/cislib-verification/release-82540222-20261019/triage-01`; the serialized native run is `triage-serial-01`. The local C# probe and generated variants remain under ignored `.artifacts/csharp/mutation-triage-2026-10-19/`, with its reviewed inputs retained in evidence. Only documentation/evidence changes are intended in the main checkout. No implementation fix, commit, push, external source submission or package publication is performed by this task.
