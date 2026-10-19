# Logging frontend and interpolation pre-commit review

The user requested review, permitted Claude, and authorized committing and pushing the resulting logging changes on 19 October 2026. Local review confirmed one production lifecycle defect: an idle poll configured longer than a lifecycle budget delayed controls despite an empty frontend. The fix wakes the consumer for flush, stop and abandonment controls. Ordinary publication still has no notification, retry, task or logging-owned lock.

The original roadmap remains paused. This review and Developer qualification do not constitute release, Linux, mutation or historical compatibility qualification.

## Exact external review scope

Automatic approval initially rejected transmission because it required the exact payload and destination. Nothing was sent in that attempt. The user then explicitly approved both prepared payloads to Anthropic Claude:

| Review | Files | Payload bytes | SHA-256 |
| --- | ---: | ---: | --- |
| Frontend | 19 | 231365 | f51423b32e8d3f1ec8fc488c3b83b376b9ff1fd6bbcbeaff158693c27e81a078 |
| Interpolation | 15 | 146516 | 3b52bb538d67ae7e4bf0e1224a5929e1f947ae1be153e36fcc400f1abbe64be2 |

The bundles overlap in four files, covering 30 distinct source/test/document paths at commit `0288f8cdd86bdaafe9bc6be9c49e4f8e1644e425` plus the declared uncommitted work. They do not cover every changed path. Exact file hashes, raw responses and process restrictions are retained beside this record. Full prepared prompts remain in ignored `.artifacts/csharp/logging-review-2026-10-19/{frontend,interpolation}/`.

The C# runner used the established Claude Code 2.1.295 executable, opus/high (resolved to `claude-opus-5-5`), no tools, strict empty MCP, no slash commands or Chrome, no persistent session and an explicit no-delegation/read-only system instruction. Both responses succeeded in one turn with no permission denials. Findings are retained unmodified; their labels are reviewer opinions, not accepted defect counts. No changed-source follow-up payload was sent.

## Local triage

| Finding | Disposition and evidence |
| --- | --- |
| Frontend F1: idle poll blocks controls | Confirmed and fixed. A coalesced control signal wakes idle waits; the signal is captured before inspecting control state so a concurrent request cannot be lost. Closed-state cleanup polls eligible publishers at 1 ms independently of the idle setting. `IdleLongPollCannotDelayFlushOrStopControls` starts real workers synchronously to their first empty wait, uses a 60000 ms idle setting, verifies an empty flush, a 100 ms stop and actual owned-writer disposal. |
| Frontend F2: writer overflow bypasses priority | Expected separate backend behavior. The user expressly scoped P01 to frontend storage; backend queues retain independent capacity/admission policies. No backpressure, retry or backend priority policy is added. |
| Frontend F3: format failure classification | Intentional public contract, covered by `NativeFormatFailureOccursInConsumerAndDoesNotFaultLaterEventsOrWriters`: one occurrence increments Incoming.Failed and leaves later events/writers usable. This does not set the frontend worker-fault ErrorCode. |
| Frontend F4: nested setup failure | No recoverable throwing path demonstrated in the supplied cleanup/observation operations. Catastrophic exceptions are explicitly excluded. No speculative shutdown redesign. |
| Frontend F5: idle scan cost/lifetime | Existing explicit polling/ownership tradeoff with measured capacity/interval knobs. F1 addresses control latency; this review does not establish a new CPU SLA or automatic disposal of an undisposed runtime. |
| Frontend F6: internal clock under control gate | Applies to internal injected-clock composition after drain, not the ordinary producer path. Public creation uses System time. No callback or control-lock promise is extended to arbitrary qualification clocks. |
| Frontend documentation inconsistency | Fixed: incoming guidance links the implemented interpolation API; runtime qualification label records Developer completion. Historical input documents remain unchanged. |
| Interpolation 1: ineffective worker-culture regression | Strengthened. The real consumer delegate explicitly runs with fr-FR culture; expected invariant numeric text now differs from its ambient culture. |
| Interpolation 2 and frontend brittle gate helpers | Fixed without a shipping hook. Test helpers identify the existing IncomingEventQueue worker by its delegate target instead of launch position/counter. |
| Interpolation 3: non-whitelisted numeric formatter costs | Clarified documentation/contract. Precision bounds apply to copied built-in scalars; other framework types, including BigInteger/NFloat, use caller formatting with its intermediate cost. Arbitrary user format semantics are not reinterpreted. |
| Interpolation 4: 32 KiB formatting scratch | Bounded implementation choice, already included in recorded helper measurements. No throughput defect or allocation regression reproduced. Pooling/unsafe initialization changes are outside this correction. |
| Interpolation 5: clipped template metadata | Existing payload-truncation semantics include template text; a complete message can coexist with truncated metadata. Template metadata is not reparsed by the consumer. No promise of a parseable template after truncation is added. |
| Interpolation 6: synchronous internal seam | Documented and tested its InvalidEvent/InterpolationFormatFailed result. Narrowed the FormatException catch to rendering alone so downstream Core formatting defects are not relabeled as caller interpolation errors. |
| Interpolation 7: later capture error category | No first-error-precedence contract exists; either retained fixed category truthfully identifies invalid capture. No new ordering guarantee introduced. |
| Interpolation 8: possible abandoned/failed double count | Refuted by the supplied queue source: claim records Consumed first; abandonment removes only waiting slot references. Backend failure and frozen timeout observations cannot relabel that claimed occurrence as Abandoned. Existing active-preparation/late-publication tests cover the ownership frontier. |
| Expression-tree/source recompilation notes | Documented handler rebinding, invariant formatting and generated metadata. Native compiler coverage now includes an explicit string conversion inside an expression tree, preserving the ordinary-message overload and absence of generated metadata. |

The frontend reviewer did not receive interpolation implementation; the interpolation reviewer did not receive queue implementation. Their corresponding speculative findings were reconciled locally across the two scopes. Existing real process/concurrency/output-format tests remain required; missing-file statements do not establish missing whole-repository coverage.

## Native verification

The original coherent Developer run `7952095e19d34f0c8eadd3a0b509897d` passed 7016 tests. Before corrections, 941 code/test/tool/contract inputs were compared with that inventory: 940 matched and the interpolation contract differed only in its reporting state; behavior fields matched. Those results identify the pre-review snapshot, not the corrected source.

The new lifecycle regression failed before the fix in focused run `33a984d65dd84f4ca0c0f8f04d2eb962`: expected Completed, actual TimedOut on the empty flush. After correction `7180f57aa2154c29be29baf5607e7c0c` passed all 48 selected native cases. Final focused run `0db6123fdcd54ec8b0b192dcfdc52b47` also passed 48, including culture, expression-tree and internal-format coverage, with zero failures/skips. Raw focused reports and source identities remain under `.artifacts/csharp/focused/<runId>/`.

Follow-up coherent Developer run `aa81990671ac4c9d9686dc52fa94f4d1` passed with exit zero and no validation issues on Windows x64 using the pinned SDK/runtime. It ran from `2026-10-19T18:13:49.5876894Z` to `2026-10-19T18:37:22.0452808Z`.

| Native family | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Unit | 3841 | 0 | 0 |
| Integration | 2120 | 0 | 0 |
| Architecture | 1032 | 0 | 0 |
| Business | 24 | 0 | 0 |
| Total | 7017 | 0 | 0 |

Every enabled gate passed, including formatting/build, 25 startup compiler controls, 22 compatibility sample controls, 12 actual-Core consumer controls, package/dependency/ownership inspection and the example host. Mutation and historical compatibility families remain explicitly NotRun; Linux and full release qualification remain separate. The inventory contains 724 declared cases, including the new lifecycle regression bound to AC19; declarations do not credit unexecuted lanes.

The exact [summary](developer-summary.json) and [validation](developer-validation.json) are retained here. Input inventory SHA-256 is `a4c20cdf62b202ee8de0f4b958b3d444f7d55167373a5c32b690c0b8f94a1003`; all 941 critical code/test/tool/contract inputs still matched after completion. Summary SHA-256 is `63d064db5798b27ee60c7431750d9a741fc6a06a6b2cce496921311985472b1a`; validation SHA-256 is `ef4b67392fb7611ed6ad4dfa4f8f5b916535d50cb40ae5e01f6c742ccf21adfb`.

Full reports, native reconciliation, source inventory, compiler outputs, inspection and local package are archived at `.artifacts/csharp/logging-review-2026-10-19/developer-final-aa81990671ac4c9d9686dc52fa94f4d1/`. Package SHA-256 is `83990892fdc28e9446a5779983e70bc5b557c4f65e8eca170601ddfe3146330b`. Only completion reporting and narrowly scoped evidence-byte attributes changed after this pass; no shipping/test/contract behavior changed. The attributes preserve the native summaries and Claude receipt bytes through Git checkout, following the repository's existing evidence rules.

## Source and staging review

Local review inspected publication, actual-victim accounting, exact-reference consumer claims, control group capture, publisher-zero/final-pass ownership, caller capture and immutable interpolation snapshots. The narrow internal catch and control wake are the only shipping corrections made during review. Native test changes strengthen behavior rather than weaken ownership or compatibility gates.

The original three approved input-document copies retain intentional Markdown hard-break spaces. All other staged paths pass `git diff --cached --check`. The unrelated `implementations/csharp/examples/type-manager/Web/Properties/launchSettings.json` is excluded; its original SHA-256 is `1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be`.

The initial graph was schema 2, fresh/valid, build `sha256:54789180037a328f144ec0db5a66f14ea46b88ba40ac8f0165f0138442347bdd`, 12439 nodes/63541 edges, zero warnings/errors. Exact TryPublish lookup returned one root without truncation; its bounded callers query returned six nodes/five traversals with truncation and excluded proposed edges. Compiler capabilities were `cis.csharp.compiler/6` and `cis.csharp.semantic-facts/3`. This is navigation evidence only. A final refresh follows canonical reporting edits, with receipts at `.artifacts/csharp/logging-review-2026-10-19/graph-final-*`. No complete semantic or test-coverage claim is made.
