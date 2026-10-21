# Source-bound review of automatic03 survivors and uncovered literals

This read-only review grants **no equivalence waiver, exclusion, kill or score adjustment**. The original automatic03 report retains its nine `Survived` and three `NoCoverage` statuses. New resource-release tests are authored separately; their execution and an exact isolated mutant control remain root-owned. Frozen automatic/replay snapshots receive no retroactive test credit.

The raw report is `.artifacts/healthcheck/qualification-next/automatic-state-windows-03/native/reports/mutation-report.json`, SHA-256 `cf22c436cae175745414562918318b2e3cd23cb5e307e62c4c4cba9a448aae02`. Line numbers below refer to its exact original `HealthStateCoordinator.cs` source, SHA-256 `7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a`.

## Survivor review

| Native ID / line | Exact source replacement | Inspected behavior and disposition limit |
| --- | --- | --- |
| 2122 / 143 | `disposed || entry.Withdrawn` → `disposed && entry.Withdrawn` | `WithdrawLocked` sets Withdrawn and removes the active entry under the same gate. Service disposal withdraws all entries before setting disposed. The remaining active-lookup/reference guard therefore still rejects these handles. Existing `AllPolicyMutationMethodsRejectWithdrawnHandles` and `WithdrawalRemovesContributionImmediatelyAndKeepsOnlyIdentityReadable` test the published rejection rule. Candidate redundant guard; no reachable public difference identified. |
| 2180 / 216 | `entry.Generation < long.MaxValue` → `entry.Generation > long.MaxValue` | This increment occurs only after terminal monitor withdrawal. Closed/withdrawn/reference guards already invalidate retained targets. |
| 2181 / 216 | Same comparison → `entry.Generation <= long.MaxValue` | Overflow changes the withdrawn entry's private generation; a withdrawn entry cannot attach again. No private overflow assertion is proposed solely to kill this mutant. |
| 2182 / 216 | Same comparison → `!(entry.Generation < long.MaxValue)` | Same terminal-state reasoning. |
| 2184 / 216 | `entry.Generation++;` → `;` | Same terminal-state reasoning. |
| 2185 / 216 | `entry.Generation++` → `entry.Generation--` | Same terminal-state reasoning. These five remain equivalence candidates requiring owner review, not approved equivalents. `MonitorWithdrawalHidesItsStateButCannotSettleAReservedWrapperEarly` covers withdrawal and late-publication rejection. `WithdrawnAttachmentCannotBeReplacedUntilItsActualOperationSettles` exercises the separate live-monitor attachment-replacement path; its independent generation increment is unchanged by these mutants. |
| 2191 / 220 | `RequestCancellation(operation, effects);` → `;` | The current attachment is closed first, and `CloseAttachmentLocked` already requests its operation cancellation. An earlier detached operation was requested when that attachment closed; pending operations prevent premature replacement. RequestCancellation is itself idempotent. Existing monitor/collection withdrawal and reentrant cancellation tests assert actual cancellation and retained settlement. Candidate redundant request; no missing public detector identified. |
| 2234 / 253 | `entry.Policy == HealthPolicyKind.FirstSuccessAvailability && flagged` → `entry.Policy == HealthPolicyKind.FirstSuccessAvailability || flagged` | Availability reaches SetCondition through Invalidate(true); publication/refresh outcomes use SetOutcome. Additional writes affect Condition/Numeric HasUsableData, which those policies do not consume. Result-kind and binding validation prevent routing availability outcomes into those policies. Existing direct/collected availability and unsupported-binding tests exercise those contracts. Candidate observational equivalence under policy isolation; no waiver granted. |
| 2346 / 427 | `source.Dispose();` → `;` | Material resource-release gap: cancellation/drain tests do not establish disposal of materialized owned CTS kernel wait handles. Omitting disposal can retain those resources while a caller retains a token. This deserves real resource tests, rather than an exclusion. |

The withdrawal/stale-result reasoning maps to HC-DSP-01 through HC-DSP-04 and the collection reservation/commit contract. The availability reasoning maps to HC-POL-05 and the policy-matching collector contract. These source invariants must be reconsidered if a future API permits reactivation or cross-policy reporting; current survival is not permission to remove safeguards.

## Three uncovered message mutations

| Native ID / line | Exact literal replacement | Reachability and contract review |
| --- | --- | --- |
| 2079 / 76 | `"The monitor policy is unsupported."` → `""` | Public factories accept the four sealed supported options types. A custom subclass of the abstract base can only reach this fallback through the internal base-typed Create seam. Public policy-validation tests already exercise actual invalid configuration rejection. |
| 2097 / 112 | `"Unsupported policy."` → `""` | ValidatePolicy rejects an unsupported base policy before CreateLocked; binding requests likewise select supported sealed policy types. This later fallback has no identified validated public route. |
| 2258 / 275 | `"Unknown numeric policy."` → `""` | Enum.IsDefined rejects unsupported NumericComparison values before entry construction; the validated immutable comparison then reaches SetNumber. Existing exact numeric-boundary and invalid-policy tests cover public behavior. |

Exact exception prose in these fallback arms is not a published contract. Reflection corruption, bypassing validated construction, or asserting those exact words solely for mutation credit would not prove a missing public requirement. All three remain NoCoverage and receive zero kill credit; no unreachable-code/equivalence exclusion is approved by this note.

## Authored resource controls and verification boundary

New `HealthcheckCancellationResourceQualificationTests` contains two cases. The first materializes the real reservation lifetime token's WaitHandle, enters and commits the real operation, calls synchronous Complete, then asserts its captured SafeWaitHandle is closed although cancellation was never requested. The second materializes the distinct idle attachment retirement token's WaitHandle, calls WithdrawCollection, then verifies both cancellation and handle closure while the caller-owned monitor remains active. Both check that the handle was open beforehand. Using the existing synchronous state seams avoids racing an asynchronously scheduled settlement continuation against the final disposal loop. Local cleanup completes any remaining actual reservation even after an assertion fails.

Root must execute these two cases unmutated, then the exact raw 2346 replacement alone with freshly built Core/test copies and native cause evidence, then restore exact source/binaries and rerun them. Until those receipts exist this is authored coverage only. A narrow independent 2346 observation would remain separate from automatic03's immutable result and the frozen 103-row replay; a later refreshed automatic qualification would be needed for new automatic credit.

## Supporting source pins

All paths below are relative to `implementations/csharp/src/CIS.Core/Healthcheck/`.

| Source | SHA-256 |
| --- | --- |
| `Internal/State/HealthStateCoordinator.Collection.cs` | `a4d9049a2a1304a40d487584c708e85749f2e6a9f9305855bb690afa48315240` |
| `Internal/State/HealthStateCoordinator.Execution.cs` | `e56bd396a62d75da7ea188c8b48f628a285c1237e6e53ee6ee1a01f5ce71f0f8` |
| `Contracts/Options/MonitorOptions.cs` | `3d3233f74648d598a662f901654fece450fc5fc4be96218f5d7eafe5ff0b4cc9` |
| `HealthcheckService.cs` | `d2db8425685ee203d077eed9b477fc528f994670b667a73d19829d28a8148730` |
