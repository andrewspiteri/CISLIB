# Full-Core mutation triage, 20 October 2026

This continues the user's request to progress from full shipping qualification into source-bound triage. **Release remains unqualified.** The original full-Core strict results remain **77.6008% Windows / 77.4801% Linux**. This work adds 18 maintained regression cases, makes one existing lifecycle fixture deterministic and independently detects selected source variants. It does not rewrite either native report, grant replay kill credit, lower the 90% threshold or complete the outstanding Release validators. Shipping source, public API, dependencies and frontend policy are unchanged.

## Complete inventory and bounded review

The C# cross-lane audit checks every native source against the current **100 handwritten Core files**. Seventeen files contain no generated variants; the other 83 contain **6,131 variants**. Every mutant ID has the same file, source bytes, mutator, span, original text and replacement on both lanes. The original native report hashes remain `1747e79123eefaff0bbad2e5fde28b5099f4119ab45dc016d6f1ddc634f782be` Windows and `024b1c16717d10894332e0f957fdbbe725d280c4d3d5b6d6ce5dc9873e6c76e1` Linux.

There are **674 variants surviving on either lane, 636 surviving on both, 255 with NoCoverage on either lane and 50 with a timeout on either lane**. These queues overlap. All 996 CompileError and 992 Ignored identities have those same statuses on both lanes. The complete identity/status comparison and non-killed catalog are retained; these are inventory observations, not automatic exclusion approval. The largest survivor mutators are String (132), Statement (119), Equality (107), Boolean (65) and Logical (56).

[Identity/status audit](evidence/core-triage/identity-summary.json) and [complete cross-lane non-killed catalog](evidence/core-triage/non-killed-crosslane.json) bind these observations to the original native hashes.

This pass reviews 15 selected identities spanning byte projection, output cancellation, runtime enablement/accounting and Type Management. It does not claim individual semantic disposition of the remaining full inventory. At the original eligible denominator of 4,143, Windows would need at least 514 additional native kills and Linux 519 to reach 90%, before considering any separately evidenced exclusion review. Selected replay detections are not substituted into that calculation.

## Findings and repairs

| Native IDs | Finding | Maintained assertion or disposition |
| --- | --- | --- |
| 1313, 1322 | Provider identity and payload comma accounting can over-allocate a tight record. | Preserve integer `event_id`, clip the payload to the exact remaining UTF-8 record cap, and require successful encoding. |
| 1328, 1329 | Attribute accounting changes can consume or invent bytes for the following template. | A provider payload leaves exactly four template bytes; `keepZZ` must become `keep` at the exact record cap. |
| 1347, 1348 | Missing/inexact template boundary decisions were not distinguished. | Empty, ASCII and Unicode templates fit exactly before descriptions, with and without a reserved provider identity. |
| 1365 | Description accounting can incorrectly leave space for a second description. | Keep `os_description` at the exact cap and omit `runtime_description`. |
| 4566 | A cancellable flush without a cancellable write lacked direct coverage. | Observe a genuinely cancellable token in the actual flush, request I/O cancellation and observe its callback, then release and join the real worker. |
| 5772, 5776 | Existing malformed-path cases could fail only because the selected file was absent. | Supply an actual selected assembly and reject backslashes, empty segments, dot segments and parent segments as malformed paths, retaining FormatException and the submitted path/identity. |
| 5780 | Removing the explicit dot-segment pattern is lane-dependent. | Windows still rejects `.` via its trailing-dot guard: source-equivalent on that lane. Linux has no such guard and the new cases detect the exact variant. |
| 592, 593 | Frontend IsEnabled lacked independent threshold cases across multiple healthy destinations. | Two real destinations at Information/Error thresholds must enable Information and Error, reject Trace/None and write nothing; test both binding orders. |
| 696 | Removing the legacy CallerUnavailable increment has no public observation in its supported construction mode. | Source-bound equivalence: Write dispatches a frontend runtime to WriteIncoming before FinishWithoutCapture; legacy queue accounting still records unavailability, while GetDiagnostics reads the separate caller counter only for a frontend runtime. No new kill credit. |
| 6055 | Removing the early cache hit repeats scanning/allocation but preserves the previously published cached result. | Add a real allocation guard under Type Manager D09. Warm 32 queries, then measure 256 synchronous queries on the current thread with a generous 128 KiB bound. Exact replay grows from zero to about 10.8 MB and fails the guard on both lanes. |

The counter disposition assumes supported sealed startup: StartIncoming is invoked before factory publication, rather than arbitrary later use of internal startup seams. It does not exempt the frontend increment in FinishIncomingEarly. GetOrComputeImplementations still uses TryAdd and returns the existing dictionary result after a repeated scan; a reference-equality assertion alone would not distinguish 6055. The allocation test observes actual repeated work without timing assertions or a new public allocation promise. Its workload/bound is a pinned qualification choice, not a guarantee of every possible caching implementation.

The tests exercise existing behavior; no production defect is confirmed. The new boundary helpers use owned captured facts with earlier loss already marked, protected metadata from the real projection and actual wire-byte observations. Small baseline records fit without invoking projection, so their measured overhead cannot hide the mutated projection. The flush test always releases and joins its worker, including a failing cancellation assertion.

## Executed evidence

| Observation | Windows | Native Ubuntu |
| --- | --- | --- |
| Original six variants 1322/1347/1365/4566/5772/6055, full preserved functional selection | Six survive; 3,867 baseline/restored cases pass | Six survive; 3,867 baseline/restored cases pass |
| Original runtime variants 592/593/696, full preserved functional selection | Three survive; 3,867 baseline/restored cases pass | Original native statuses remain Survived; no separate original-runtime replay claimed |
| Final distinct selected source variants with added assertions | 13 detected, two survive across 15 identities | 14 detected, one survives across 15 identities |
| Initial maintained focused entry point, before the final cache/fixture refinement | 110 passed, zero failed/skipped | Covered by independent native replay and the full preserved functional selection below |
| Final full preserved functional selection | Covered by the coherent Developer checkpoint below | 3,885 baseline/restored cases passed; exact 6055 fails only the cache-allocation case |

Windows' two final survivors are 5780 and 696, with the source-bound dispositions above. Linux's one is 696. The separately replayed 6055 performance variant survives the original complete functional selection on both lanes and is detected after the allocation assertion. The initial updated Windows probe has eight detections and two survivors among ten variants: its exact-template tests do not distinguish 1328, and 5780 remains redundant on Windows. The refined clipped-template case detects 1328/1329; the separate runtime probe detects 592/593. Those raw attempts remain separate.

Retained results include [initial Windows repairs](evidence/core-triage/updated-initial-windows-results.json), [refined Windows repairs](evidence/core-triage/updated-refined-windows-results.json), [Linux repairs](evidence/core-triage/updated-linux-results.json), [Windows cache control](evidence/core-triage/cache-work-windows-results.json), [Linux cache control](evidence/core-triage/cache-work-linux-results.json) and [final Linux functional controls](evidence/core-triage/final-functional-linux-results.json). Compressed native process/TRX bundles preserve the original observations beside them.

Replay source selections come from the original native JSON, not recreated descriptions. Baseline/restored checks require nonempty, unskipped native TRX and actual serialized scheduling. Each change is restored as original bytes. Updated probes pin and recheck every copied test-overlay file. SDK/runtime remain **10.0.401 / 10.0.12**. These are independent direct-source native observations, not new Stryker scores or a complete child-process matrix.

The preserved mutation input predates the later repository/checkpoint-selection unit guard and omits only the three measurement classes from execution. Its old functional total is 3,867; the 18 new cases raise that selection to 3,885. This is not represented as the entire current ordinary unit inventory, which has 3,910 cases. Updated focused replays include 106 cases at the first overlay and 111 at the second overlay, including the scheduling control. The initial maintained focused entry selects the four classes without that scheduling case and reports 110. The final Linux full-selection controls include the cache case and corrected lifecycle fixture.

The first sandboxed focused build fails without compiler diagnostics and receives no execution credit. Its native retry passes 102 cases; the expanded focused pass has 110. The first coherent Developer attempt stops at format validation before any family execution: two dictionary initializer line breaks need correction. The exact format-only diff is retained. A second attempt passes formatting/build/package checks but has one failed case among 3,909 units: the existing FixedBatchReportsStaleTargetsWithoutDoubleCountingAndLaterTrafficCannotExtendIt assertion on the later result. Integration/architecture/business are not executed in that failed attempt.

Source review found that fixture could enqueue four output records into a separate capacity-three writer queue while testing frontend eviction. The corrected fixture holds the later batch's processing until the old writer boundary has drained; it preserves the fixed target/eviction/immutability/success assertions and additionally requires zero writer rejection. This removes an independent writer-capacity race rather than changing runtime behavior.

The final Windows Developer run **9f94bcff31644a3ea9160dd5aa56bfa7** passes **7,089 cases** with zero failures/skips: **3,910 unit, 2,121 integration, 1,033 architecture and 25 business**, with all **16 enabled stages** passed and exit 0. Mutation and compatibility are NotRun within this Developer profile. It is a coherent current-source checkpoint, not Release qualification; the existing release gates remain unchanged. Both preceding failed Developer attempts are retained separately.

[Developer summary](evidence/core-triage/developer-windows-summary.json), [validation](evidence/core-triage/developer-windows-validation.json) and the compressed `developer-windows-03-native.tar.gz` retain that coherent checkpoint.

## Cache instrumentation control

The isolated allocation experiment has two passing baseline/restored cases on each lane. On the first 1,839-definition catalog, 256 queries allocate **0 bytes unmutated / 10,766,336 bytes for exact 6055 / 0 bytes restored**. The final Linux functional controls use a 1,845-definition catalog and observe **0 / 10,768,384 / 0 bytes**, with only that allocation case failing among 3,885 mutated executions.

A pinned Windows Stryker diagnostic first selects only TypeManager.cs and the allocation/scheduling tests. It has 89 variants: 10 Killed, 33 Survived, 22 NoCoverage, three CompileError and 21 Ignored, including an actual native Killed status for 6055. Further diagnostics instrument all 100 Core files and generate all 6,131 variants, but still run only those two tests. Windows has 41 Killed, 83 Survived and 4,019 NoCoverage; Linux has 40 Killed, 81 Survived and 4,022 NoCoverage. Each has 996 CompileError, 992 Ignored and zero Timeout. In both reports 6055 is Killed, the source/status auditor matches all 100 current source files, and both restored cases pass. The guard therefore tolerates the additional instrumentation on its warm-query path on both lanes.

All diagnostic scores fail the unchanged 90% threshold (15.38% for the one-file run, 0.99% Windows / 0.97% Linux for the full-source instrumentation runs); the deliberately narrow two-test selection cannot qualify shipping mutation coverage. Their kills are not copied into the original full-suite reports. The first Windows diagnostic's post-run restoration initially used SDK 11 from the repository root and fails; its raw failure is retained. Repeating from the language directory pins SDK 10.0.401, rebuilds unmutated source and passes both restored controls. This invocation error is distinct from the passing native instrumentation observation.

The independent [Windows](evidence/core-triage/cache-instrumentation-windows-audit.json) and [Linux](evidence/core-triage/cache-instrumentation-linux-audit.json) audits verify final statuses and source membership. Their deliberately narrow test closure is explicit in the retained [config](evidence/core-triage/cache-native-full-source-config.json).

## Remaining work

- Continue source-bound review of the full survivor/NoCoverage/timeout queues, especially OutputEventQueue, CoreLogRuntime.Incoming and remaining byte-budget/Type Management behavior. A surviving variant is not a confirmed product bug.
- Complete compiler rollback and ignored-block review. The previously demonstrated valid 2984 rollback remains a tool/exclusion gap; this pass gives no blanket approval to other filtered identities. Earlier six-file dispositions remain bounded to their exact source/version.
- After the broader repairs, rerun the maintained full Core/helper/platform mutation matrix and complete Release reconciliation, required groups, examples and retained-kit/licence gates. The prior full shipping coverage/helper scores remain historical passing observations, with their exact candidate pins.

No commit, push, publication, remote source review or roadmap-goal resumption is part of this continuation.

## Source context and retention

Graph context uses schema **2**, C# capabilities `cis.csharp.compiler/6` and `cis.csharp.semantic-facts/3`, final build `sha256:eec4172e522714b3b6717dabd127fdcf63347649c31a21f44e0e1f4660becbee`, **12,599 nodes / 64,896 edges**, fresh status and zero warnings/errors. Exact product roots are `CIS.Logging.Events.EventByteProjection.Fit(CapturedEventFacts, int)`, `CIS.TypeManagement.TypeManager.GetOrComputeImplementations(Type, string?)` and class `CIS.Logging.Runtime.OutputEventQueue`. Bounded incoming `calls` queries for the two methods use depth one/limit five and return three nodes/two edges apiece without truncation. The cache-allocation test has its own single exact method root. The initial byte-method text search is truncated at two nodes; the final exact-ID root query is not. The first final graph persistence attempt fails with CIS-GRAPH-STORE-002 access denied; retrying the official CLI with native filesystem access succeeds. Generated graph state remains local.

Graph edges and argument shapes do not prove runtime behavior, and artifact-only tools/overlays are outside its compiler coverage. Canonical source inspection and actual native reports establish the findings. The retained native archives include original process outputs, commands, source/test pins, baseline/variant/restored TRX and failed attempts. Tool archives preserve C# source and compiled runtime closures, the three explicit test overlays, final maintained test files and exact diagnostic configs. The original 119-file shipping evidence/checksum manifest is unchanged; this continuation has a separate evidence directory and manifest. Installed SDKs/caches and complete materialization/licence review remain prerequisites, so these records are not claimed as a complete offline release kit.

The [continuation checksum manifest](evidence/core-triage/checksums.json) binds **53 files / 46,159,983 bytes**, excluding itself; all listed hashes and final membership are checked. Its SHA-256 is `4460a2750d1b6211d1091d836aafa2193d34a3429328ef78ba4f7b478e8ec2c0`. The preceding shipping manifest retains SHA-256 `b36bac01d6ac124665c565cd4bebf1256c92b7f50cd29f1d1cec2843a674581a`. These consistency checks do not approve pending exclusions or failing release gates.
