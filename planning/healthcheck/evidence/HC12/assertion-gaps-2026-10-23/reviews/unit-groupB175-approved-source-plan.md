# Actual168 groupB44: source map and minimal assertion plan

The read-only C# map `groupB44-source-map01.json`, SHA2560bb73dc55fc9622feb5566b0f32806926d0452381a6ac2fcec4a8d12554cd2a7, joins every assigned actual native event to its original GUID/FQN/display, TRX error/stack and frozen consumer source. All44 are rejected by unchanged compiled DirectAssertion36de775d…/sourceefebe087… . Input pins are c8af393266698976c4ebf7bcd345e605b26425c7d04bdc733aefafb3f5504fa5. Actual168 audit3ba0df6d…/projection27210147…/receivingbfb646e1… remain authoritative:1389 recorded,1160P/229F,82direct+147RuntimeArgument, no missing result/fatal. These44 are case failures from the same identifier147 subject, not44 distinct mutant definitions or new credit.

Scope is exactly HealthcheckServicePolicyTests22 failed rows/11 methods and HealthcheckStateSeamTests22 failed rows/17 methods. Their complete ordinary cohorts contain46 and42 identities respectively,88 total. Frozen Policy source is96116989d087017bf52e0d068498b43aeb888c9b71ac04ec0d3d1b9f9304d8a0 (`frozen-source01/unit/0086`); State isab2ce02b4299d73ca4eaaeb1ef0cb0e71a29f20f5224afa0e3cfdc07562b8693 (`.../0087`). No fixture, Core, subject or campaign was built/executed for this map. Other Unit/Pipes103 rows are outside this assignment.

There are38 actual caller-created diagnostic failures and6 genuine production diagnostic-conversion failures. The latter require narrow assertions on the actual subject call; replacing fixture diagnostic construction cannot repair them. All original invalid configuration, identity, numeric, binding and registration checks remain unchanged. No actual diagnostic literal at these caller sites intentionally tests invalid Unicode/code bounds: codes are fixed short nonempty ASCII, optional messages are ordinary bounded literals and exception fields are omitted.

## First priority: genuine normalization/projection boundaries (6 rows)

| Original method / actual rows | Actual production cause | Minimal expected-success boundary at original phase |
| --- | --- | --- |
| NullAndMismatchedRequiredResultsBecomeOneEvaluationFailure,2 | CommitResult line99 creates valid `Healthcheck.CollectionResult` after reservation/TryEnter, for intentional null or wrong-policy result | Catch only ArgumentException around that actual CommitResult invocation, assert it did not propagate and its returned attempt is nonnull, then retain all original disposition/evaluation/counter assertions. Materialize the same result argument immediately before the call if needed so the catch cannot absorb a result-factory fault. |
| OptionalErrorPreservesPolicyAndPreviousMeasurementWithoutRefreshingEither,True1 | Same CommitResult line99, optional registration receives an execution result | Apply that same narrow actual-call acceptance boundary only on the existing mismatchedResult branch. The False row instead constructs `OptionalReadFailed` and uses CommitEvaluationFailure; handle its constructor separately without changing branch choice, clock advance or reservation phase. |
| MeasurementCapacityRejectsTheWholeBatchWhilePreservingAValidRequiredHealthResult,2 | CommitResult line123 creates valid `Healthcheck.MeasurementCapacity` after a32-key monitor receives a two-measurement batch with one new key | Catch only ArgumentException around the actual CommitResult call and assert non-propagation. Preserve its originally ignored return, required/optional branch, exact existing measurements, unchanged32-key assertions and valid required health result. Do not preflight a fabricated capacity error. |
| ThrowingCancellationHandlerRecordsItsActualErrorTimeWithoutAHealthFailureOrStrandedDrain,1 | Actual BeginStopping→Dispatch→CancelOperation line304 projects the actual cancellation-handler error with valid code `Healthcheck.Cancellation` | Catch only ArgumentException around the original actual BeginStopping call after handler registration and the two-hour clock advance; assert it did not propagate. Retain exact callback throw expression, actual error-time/state assertions, Finish and both actual disposal waits. Do not manufacture an Aggregate or move the producer exception before registration. |

For the first two normalization paths, CommitResult has not assigned `operation.Evaluation` when diagnostic construction throws. The capacity path may already have committed required health before its error constructor throws. These facts justify asserting the actual returned/nonthrow contract; they do not justify inventing an evaluation/disposition after a throw. For cancellation, CancelOperation's real finally **still sets CancellationSettled, finalizes and dispatches even if FromException throws**. It is therefore wrong to claim this projection error necessarily strands cancellation. Its concrete violation is a propagated error where the original test expects an integration diagnostic and retained drain semantics.

All six use the original await-using StateScope. Its unchanged DisposeAsync finishes every actual reservation not already in `finished`, then awaits real service disposal10s; Finish marks an operation finished only after actual Complete returns. Keep this entire mechanism unchanged. The acceptance assertion can fail only after its actual caller cleanup runs; any actual Finish/disposal/unknown error remains a runtime hold under the unchanged classifier.

## Second priority: the actual concurrent worker obstruction (1 row)

ConcurrentReportsAndWithdrawalPublishOnlyCompleteSnapshotVersions constructs `ConcurrentFailure` inside the writer before arriving at its first barrier (Policy line733). The actual168 row duration is30.0185346s and reports the constructor ArgumentException. Source shows the other worker waits on the same30s AsyncBarrier; a writer constructor failure prevents its arrival. Task.WhenAll awaits both and can expose the writer's first exception while the sibling barrier's eventual failure is absent from the adapter's single error. The30s duration is actual; the sibling failure explanation is source-based, not a second native failure record.

Recommended explicit exception to original construction phase/count: create the **actual immutable reason used by the reports** once immediately before either worker is launched, after the same service/monitor/startup/barrier-array setup. Narrowly assert that constructor accepts its valid code and returns nonnull, then pass that same reason to each of the original32 ReportFailure calls. Preserve every report, success, barrier, snapshot, withdrawal, round count/deadline and final actual Task.WhenAll. This is a visible32→1 diagnostic allocation/phase amendment, requiring root review. The test compares diagnostic values/counters, not per-round diagnostic reference identities; every failure is still followed by success. It avoids launching a sibling when construction is rejected. A fabricated unused preflight reason is not proposed.

Do not merely assert inside the failing writer and allow its sibling deadline to be hidden by Task.WhenAll's first error. Do not catch/unpack generic Task.WhenAll failures or release barriers artificially. If root rejects the explicit actual-reason allocation/phase remedy, retain this row as a runtime hold until a separately reviewed real sibling-settlement design exists.

## Remaining valid caller diagnostic calls (37 rows)

Use a tiny class-local accepted-diagnostic helper, called **only at the original valid constructor expression positions**. It constructs the same HealthDiagnostic with the same arguments, catches only that constructor's ArgumentException, asserts constant expected acceptance and nonnull, then returns the same object. It does not wrap monitor/state methods, callbacks, the body, workers or cleanup. Other exception types propagate. Use it for the existing valid reason expressions in the affected methods, including later valid reasons that were unreachable after the first actual rejection. Preserve existing per-call/loop allocation counts except the explicit concurrent remedy above.

| Policy methods | Actual rows / first phase |
| --- | --- |
| NumericComparisonsUseTheirExactSelectedBoundary |12; `Observation` after monitor creation and two-second clock advance, as ReportValue argument |
| IdenticalOptionsProduceIndependentMonitorsAndUniqueIdentities |1; `FirstOnly` after both monitors/identity assertions, as first.Flag argument |
| ExplicitlyClearInitialConditionDoesNotPublishAnInapplicableReason |1; `UnusedInitialReason` in original options initializer before CreateCondition |
| StartupCompletionIsIdempotentAndDoesNotClearAStartupFailure |1; `PrimingFailed` in the original condition options initializer |
| ConsecutiveFailuresBreachAtTheThresholdAndSuccessResetsOnlyTheStreak |1; one existing `RefreshFailed` local before its original loop |
| SkipAndExpectedCancellationRemainNeutralToAnExistingFailureStreak |1; original `OriginalFailure` ReportFailure argument |
| InvalidDirectNumericValuesLeaveTheLastAcceptedVersionUntouched |1; first valid `TooSlow` ReportValue reason; preserve later deliberate NaN/infinity rejections |
| AvailabilitySurvivesRefreshFailureUntilExplicitInvalidation |1; original InitialFailure/RefreshFailure/CacheInvalid expression positions and call counts |
| SnapshotReadsHaveFreshCaptureTimesWithoutChangingObservationOrRevision |1; original `Fault` Flag argument |
| ActiveReportKeepsItsTemporaryMonitorAliveDuringAReentrantClockRead |1; see lifetime constraint below |

| State methods | Actual rows / first phase |
| --- | --- |
| NewerDirectObservationDefeatsStaleCollectionSuccessAndFailure |2; `Direct` after reserve/TryEnter/clock advance; retain later `Old` argument position |
| StaleHealthResultCanStillPublishUncontestedMeasurementKeys |1; original Direct Flag argument after actual admission |
| OptionalErrorPreservesPolicyAndPreviousMeasurementWithoutRefreshingEither(False) |1; OptionalReadFailed in its existing False branch only |
| RequiredConditionEvaluationErrorFlagsAndAValidEvaluationRecovers |1; RequiredReadFailed local after attachment, before its original reservation |
| RequiredNumericErrorRetainsLastFiniteValueAndItsOriginalObservationTime |1; RequiredReadFailed after admitted failure reservation and clock advance |
| RequiredFailureEvaluationsCountOnceEachUnderTheDeclaredThreshold |1; constructor remains **inside each original failure-loop iteration**, no allocation hoist |
| RequiredAvailabilityErrorPreservesOnlyAnAlreadyEstablishedPublication |2; ReadFailed at original admitted CommitEvaluationFailure argument |
| StaleRequiredEvaluationErrorCannotReplaceANewerDirectSuccess |1; OldError after direct success and original before snapshot |
| MonitorWithdrawalHidesItsStateButCannotSettleAReservedWrapperEarly |1; Late after original Withdraw/disposal pending assertions; no movement before withdrawal |
| LeaseAcquisitionFaultRecordsAnIntegrationErrorWithoutAnAdmittedOutcome |1; AcquireFailed after its actual reservation, before Finish |
| ActualCleanupFaultDoesNotCountTheSuccessfulEvaluationAgain |1; CleanupFailed **after actual successful evaluation commit**, before original Finish |
| OutcomeCommitsPolicyCountersAndDetachedExecutionFactsAsOneVersion |1; CallbackFailed after binding/original reportedTime, before original fact |
| LifecycleSettlementAddsActualFactsWithoutASecondOutcomeOrWallClockInference |1; CleanupFailed after actual ApplyOutcome/observed snapshot |
| PreviousExecutionSettlementDoesNotReplaceTheCurrentExecutionOrItsOutcome |1; PreviousCleanup in original late-settlement initializer after both original outcomes |

The temporary-monitor helper is special: original NoInlining `ReportConditionOnTemporaryMonitor` evaluates `service.CreateCondition(...).Flag(new HealthDiagnostic("CommittedAfterClock"))`. Registration occurs before reason construction; Flag performs the second clock read/reentrant GC. Keep its NoInlining attribute and receiver expression, replacing only the **Flag argument** with the accepted-diagnostic helper. Do not hoist the reason before monitor creation, add a long-lived monitor local/field, add KeepAlive, change the callback or weaken the during-acceptance state assertions. That would mask the receiver-lifetime obligation this test exercises.

Suggested private validation after approval: complete original88 ordinary identities on unchanged correct Core, then each of the44 actual whole public identifier147 rows once with raw outcome retained before the unchanged compiled classifier. Join each actual async row through its original await-using/Task cleanup; no preflight-only success or claim that later successful-body assertions ran after acceptance failed. Inverse-check only the two class source amendments, preserve all other methods/helpers/attrs/parameters/intentional invalid checks, and bind full source/PE/PDB. A source proof is not a new actual control. Any residual timeout/Aggregate/unknown cleanup failure remains held.

This is a plan, not implementation authorization. No generic constructor/input reclassification, NativeAudit8 relaxation, case exclusion, raw status change, runtime waiver, conservative-score transfer or release claim is made. The current147 non-direct rows remain real zero-issue receiving blockers until actual repaired assertions and complete native evidence exist.
