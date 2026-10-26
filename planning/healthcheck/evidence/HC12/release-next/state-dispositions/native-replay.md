# Native State rollback replay and bounded follow-up

The completed 103-mutant replay is **Incomplete**, exit 1. It records 43 independently observed source compiler errors, 30 kills, four survivors, 17 test timeouts and nine native failures rejected by the conservative classifier. Its strict conservative result is **226/277 = 81.58844765342961%**, below the unchanged 90% threshold. This is not mutation or release qualification.

This note records read-only inspection of retained native receipts. It changes no original report, default-replay status, score, equivalence disposition or acceptance record. The later finite proof runs below are separate evidence; they do not rewrite the frozen run or receive retroactive automatic credit.

## Completed default replay

`.artifacts/healthcheck/release-next/rollback-full-windows-01/summary.json` records all 35 originally CompileError and all 68 originally Ignored State mutants applied independently against the frozen source candidate from commit `8fc2f9ccb78ba948f9794cfb4c0ea37cfd2956eb`. It finished at `2026-10-21T15:10:05.7933561+02:00`. Baseline and restored native closures each passed 650 cases; exact baseline binaries were restored and that frozen candidate's inputs were unchanged. The complete 1,127-file relevant input manifests before/after have the identical SHA-256 `aa6d32ea6cd810bdb794545233f8a22c343de8a7e8047b4fc491d27a01f2bf2b`. This guard does not equate the frozen tree with the later source/test candidate used for the 652-case proof runs.

The numerator is the immutable automatic03 196 kills plus 30 observed supplemental kills. The denominator retains all 320 original selected mutants except the 43 individually demonstrated source compiler failures. All survivors, timeouts, uncovered and unresolved variants receive zero kill credit. No compiler/analyzer policy, threshold, mutation kind or production safeguard was relaxed.

All four survivors compiled and passed the exact 650-case closure:

| ID | Exact edit in original State source | Source-bound assessment; no waiver |
| --- | --- | --- |
| 2120 / line 143 | Final active-lookup/reference `||` becomes `&&` in EnsureActive. | Withdrawn entries are atomically marked and removed; entries are never replaced under the same identity, and handles report to their own owner. Candidate invariant redundancy. |
| 2183 / line 216 | `{ entry.Generation++; }` → `{}` on terminal monitor withdrawal. | Withdrawn/closed/reference guards already invalidate targets; withdrawn monitors cannot attach again. Candidate invariant redundancy, matching the original terminal-generation survivor family. |
| 2190 / line 220 | `{ RequestCancellation(operation, effects); }` → `{}`. | Closing the current attachment already requests cancellation; previously detached operations were requested at their own retirement. Candidate redundant request. |
| 2345 / line 427 | `{ source.Dispose(); }` → `{}` in the disposal loop. | A real owned-resource release gap. The frozen 650-case closure predates the two new handle-release tests. The later 2346 proof does not supply a native 2345 kill. |

All nine unresolved rows compiled, executed native exit 1, preserved the exact 650 cases and serialized Scheduling case, and had neither a run error nor a timeout. Each already has direct contract assertion failures. The default classifier requires every failed case to be recognized; additional test-side consequences therefore prevent kill credit:

| ID / original edit | Assertions + other failures | Observed origin of rejected consequences |
| --- | --- | --- |
| 2107 / line 119, initial-flag pattern `true` → `false` | 2 + 1 | StartupCompletionIsIdempotentAndDoesNotClearAStartupFailure dereferences missing Diagnostic at PolicyTests line 227: test-side NullReferenceException. Named initial-reason assertions also fail. |
| 2210 / lines 232–242, erase ReportMeasurement body | 11 + 8 | Missing measurements produce test-side Single/JSON-array access exceptions. Direct capacity assertion expects 32 retained keys and observes 0. |
| 2216 / lines 236–241, erase its lock body | 9 + 8 | Same measurement consumer consequences and direct 32-versus-0 assertion. |
| 2227 / lines 245–248, erase ReportCondition body | 29 + 1 | EscapedControlTextAndSupplementaryUnicodeRoundTripAsStrictUtf8 reads missing diagnostic JSON at FormatterTests line 213: KeyNotFoundException. Withdrawal and condition assertions fail independently. |
| 2228 / line 247, erase its lock body | 28 + 1 | Same missing-diagnostic consumer exception; stale-result and condition assertions fail independently. |
| 2231 / lines 251–256, erase SetCondition body | 54 + 1 | Same missing-diagnostic consumer exception; explicit Unhealthy-versus-Healthy assertions fail. |
| 2242 / lines 259–263, erase ReportNumber body | 20 + 2 | NumericPolicyAndAllDiagnosticScalarKindsRetainTheirWireTypes reads missing lastValue JSON at FormatterTests line 149; RequiredNumericErrorRetainsLastFiniteValueAndItsOriginalObservationTime dereferences missing timestamp at StateSeamTests line 323. |
| 2246 / line 262, erase its lock body | 20 + 2 | Same numeric consumer exceptions; numeric state/value assertions fail directly. |
| 2249 / lines 266–278, erase SetNumber body | 20 + 2 | Same numeric consumer exceptions and direct state/value failures. |

These are conservative classifier rejections, not evidence that the existing tests lack detectors. The nine statuses remain NativeFailureUnresolved in the frozen receipt. No arbitrary test exception is accepted as a kill.

## Separate native proof receipts

The initial six-ID guard attempt `rollback-guard-windows-01` stopped on the existing 30-second Git inventory deadline, before baseline or mutant execution. It is retained as an infrastructure failure with no credit. The subsequent `rollback-guard-windows-02` completed as SelectedSubset: 2027/2030 IndependentCompileError, 2089/2221 Killed, 2202/2204 TestTimeout. It restored 650 cases and exact binaries with unchanged inputs. The timeout rows receive no kill credit despite earlier narrow synchronous disposal assertions.

`resource-survivor-windows-01` completed as SelectedSubset, exit 0: exact original survivor 2346 (`source.Dispose();` → `;`) was Killed. Both new `HealthcheckCancellationResourceQualificationTests` cases failed specifically on `Assert.IsTrue(resource.IsClosed)` with actual false. Their native IDs are `1db7ceae-0744-8466-b90b-9d992e247a99` (entered operation completion) and `15566a72-fa72-8f4e-933c-ce92f6caf9c5` (idle attachment retirement). Real token handles were open before the transition; the first case distinguishes resource disposal from cancellation, and the second distinguishes attachment-resource cleanup from monitor withdrawal. Baseline/restored 652 cases, exact restored binaries and unchanged inputs passed. The test source pin is `10c029f57404539b2115625076316dba5486488254d3fb8bea2be471741ae778`; the receipt's verifier source pin is `68cb652ef6eadda30c8d24352c331d137333f782586538f3e075915274762d31`.

`assertion-review-windows-01` completed as SelectedSubset, exit 0: 2107, 2210 and 2216 were separately Killed using exact named direct assertions. Their mutated production source hashes match the corresponding default-replay rows. For 2107 the True data row's native ID `1d44ab4a-4a9a-81b2-8b62-349ab8ecb586` observes expected InitialFlag versus null. For both measurement mutants native ID `1a78624f-290d-8fc5-9125-d4918472774f` observes expected 32 versus 0 in MeasurementKeyLimitRejectsOnlyNewKeysAndAllowsExistingKeyReplacement. Exact messages, method/case identities, actual source stack and sequence point are required. Known test-consumer consequences are recorded separately without kill credit; there were no unknown other failures. Baseline/restored 652, exact binaries and unchanged inputs passed. The verifier source pin in this receipt is `5bd9760279dcdef210f6764fb232c52d9aecb44b3d8d91f23ef54a2cc7b9f347`.

Neither finite proof qualifies the whole State corpus or module. The six other unresolved rows, 17 standalone timeouts, remaining survivors and broader Healthcheck mutation/release gates are not closed by these receipts. No new combined qualification score is claimed here.

## Pins, limits and immutable references

All runs use SDK 10.0.401, framework runtime 10.0.12 with disabled roll-forward, locked restore, fresh serial Release project builds and serialized native Healthcheck-unit plus Scheduling execution. Build/restore deadlines are 180 seconds; the native session limit is 55 seconds with a 60-second outer process deadline. Outer ProcessRunner timeouts do not retain partial stdout/stderr; command requests and any native files remain, and no result is fabricated. Each row restores exact source bytes. Final fresh unmutated builds/tests and exact binary/input comparisons remain mandatory.

Production pins remain State `7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a` and Probe `e022538f8c4694b3b772625d58886ba9897b6fb8f075a365faf08887878f2f54`. The original automatic03 raw report remains `cf22c436cae175745414562918318b2e3cd23cb5e307e62c4c4cba9a448aae02`, retaining 196 Killed, nine Survived, nine Timeout, three NoCoverage, 35 CompileError and 68 Ignored. Its original strict 196/217 subset score and statuses are unchanged.

Paths below are relative to `.artifacts/healthcheck/release-next/`:

| Receipt | SHA-256 |
| --- | --- |
| `rollback-full-windows-01/summary.json` | `280ac36b2302cda5e64d9540d7e347f66dc38180520019fc733f6c61c40c35ce` |
| `rollback-guard-windows-01/summary.json` | `6dd5581e03b7a67ee097279bc40e35e45d7d8d565e936d87f10f60f9b945c041` |
| `rollback-guard-windows-02/summary.json` | `1d1a89a4e34abb845c8c1ab9634060cbf191cc839682ac734bd8848c6d0dc19b` |
| `resource-survivor-windows-01/summary.json` | `42bbfdf15ca5903787d2032405a69d4e783a78e6c2f423f0e90f8f0aa5cda292` |
| `assertion-review-windows-01/summary.json` | `ac9fc3f40e63fe8d908895eb1a13449b15fb9a776c01dddc23aabdd032083ffc` |

Previous records remain immutable references: [source-bound review](review.md), SHA `2c7612276c9e300ff87337f30563175c0f28e6148d982dd0d5b72c0607a01398`; [earlier compiler diagnosis](../../qualification-next/compiler-rollback-review.md), SHA `0f8474c63257359d03b19ef7768e47e8f728fea275a3dd5f5f6f4ef70b4a42ee`; [four earlier manual compiler controls](../../qualification-next/compiler-collateral.json), SHA `215f8c35b07c7154f2bfd18c705b8af3aa957720a97e2be3f4426b9798b85b70`; and the [original replay authoring plan](../rollback/README.md). No old receipt was replaced by this note.
