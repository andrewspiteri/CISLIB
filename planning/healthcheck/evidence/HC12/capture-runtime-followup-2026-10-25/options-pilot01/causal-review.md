# Unit194 Options pilot: retained abort and assertion-gap triage

Read-only informational review. No new verifier, classifier, audit replay, subject, mutation control or campaign was run. The original pilot remains completeExecution=false, with nativeExit0 and actualShutdownCaptureValidated=true. Its original/fresh/initial1396 controls and source/restoration facts are not relabeled. The strict normal-host/issue holds remain. No mutation credit is added here.

Inputs are the complete regular-file Windows readback at `unit194-options-pilot-closed-receiving01/readback01`, C#Retention receiving0: 9,616 regular files / 1,143,709,088 bytes / no extras, manifest SHA `407c008dc6b851d28f9567cf79fcde23001ad9c8021bf9b41de68d7405320e88`, archive SHA `90f184fa4695f3d348632104afef4aae8076f9d1d92232b3d5efab668ad0ecde`. Root separately retained the declared eleven nonregular Unix socket paths and the initial failed preparation; regular-file receiving does not claim those sockets as regular files. All six compact bodies match their exact full-manifest rows:

| Body | SHA256 |
| --- | --- |
| Emitted budgets | `5394665dd46217fe988f4158c0439cbba44ef76ff1164587b73e36c1119fb81d` |
| Original private native audit | `f5a54c0b11c2728e6fb17a29067fb507b1a887a0afde3accfb5e316c9a2b83bb` |
| Scope | `bc8df6a117da9fe9bc37965d397cec368fd46f70fd4ee6f6190d261509d6ec7b` |
| Passed shutdown capture | `6685c25c6c30e6c32b3b77def1f9af346e5de1b71c0782b5a863a6d1950e062d` |
| Raw report | `587b737dac6d0215bc3cb91d5655213e88339b63b66d1dcda263e2de833f9ee7` |
| Original result | `c9d3e3e54c4f9c3c430777b45aa70bbf273f5249edfcaac0a1116f92cd6bb1e7` |

## Counts and the actual missing-witness split

The one source has120 definitions:87Killed,5Timeout,4Survived,6CompileError,18Ignored. There are96 active requests and two unselected initial requests. The audit records98 execution hosts,71 normal, plus one normal discovery host. All57 direct IDs are members of the87 rawKilled set. Therefore the missing-direct rawKilled set is **30**, not34:22 have aborted hosts and eight have normal hosts. This is a set join, not subtraction of unrelated issue counts.

The eight normal-host rawKilled gaps are:

| IDs | Exact raw returned failure | Case/source location |
| --- | --- | --- |
|484|Unhandled CoreConfigurationException|EnvironmentOverridesSetupAndReadsEachKnownKeyExactlyOnce; Resolve line56 / test line40.|
|512,517,528|Actual `CollectionAssert.AreEquivalent failed...` errors|IndependentInvalidSettingsAreReportedWithoutRawValues line187. These were not accepted by this audit's direct assertion convention. No classifier acceptance is changed here.|
|534|79 returned CoreConfigurationException errors|ValidatedCopy line138; includes InclusiveLimitsAndPortableMaximumPipeNameAreAccepted line142 and callers constructing services with valid boundary options.|
|548|One returned CoreConfigurationException|PortablePipeNamesAcceptTheirInclusiveMinimumAndEachAllowedSeparator line18.|
|550|One returned CoreConfigurationException|InclusiveLimitsAndPortableMaximumPipeNameAreAccepted line150.|
|561|Two returned CoreConfigurationException rows|PortablePipeNamesAcceptTheirInclusiveMinimumAndEachAllowedSeparator line18.|

The five unhandled-exception definitions produce84 raw failure observations in total; the three collection-assertion definitions produce three. Their hosts returned the complete requested GUID sets (respectively1,2,2,20,850,65,65,53 by ID484/512/517/528/534/548/550/561). The old raw errors and their zero direct eligibility remain unchanged regardless of later test-source work.

## All27 aborts have body execution

Every one of the27 hosts has actual RecordStart and RecordEnd markers. None is a startup-only abort. Each has one actual TestExecution.Abort and zero TestExecution.Cancel markers. The engine log retains27 exact `Aborting test run: test run timeout of ... milliseconds exceeded.` events and zero `Each mutant's fate has been established, we can stop.` events. Parent logs also retain TestRunRequest.OnTestSessionTimeout. These are observed deadline aborts, not evidence of the engine's result-bailing path.

| Request group | IDs | Actual cohort/budget | Observed state |
| --- | --- | --- | --- |
|16 large|489,494,496,497,498,499,500,502,506,509,527,531,532,533,535,536|811/828/830/850 cases;20,864–20,947ms|39–819 RecordStarts before Abort; completion statistics38–818. Different earlier collector/state/formatter tests are active at the boundary. None has CallerCancellation as its latest pre-Abort start. Five are rawTimeout; eleven are rawKilled.|
|11 smaller|543,545,547,549,551,556,557,558,559,560,564|52/53/56/64/65 cases;11,569–11,578ms|Every latest start is CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse. Each has exactly one more RecordStart than RecordEnd (11/10,14/13,20/19 or21/20). All are rawKilled.|

Large budgets/cohorts are13 requests at20,947ms/850 cases plus one each at20,906/828,20,864/830,20,894/811. Smaller are4 at11,578/65,1 at11,578/64,3 at11,572/56,1 at11,572/52 and2 at11,569/53. These coverage subsets contain other runtime/pipe tests; mutating a synchronous Options source does not make every selected test synchronous or Options-only.

The eleven smaller hosts all stop progressing at the same real request/cancellation test, after earlier valid/invalid option rows have already returned. Their last start precedes Abort by6.059–9.077s. Returned result Duration sums are only0.156–0.327s across each host's10–20 completed cases; these sums omit the still-pending case and are not a wall-clock or native-budget replacement. Prior failure rows are largely configuration rejection at test setup. The marker pattern supports a pending body/setup-frame path, rather than failure to start a host. It does not independently identify which internal Task is pending; that requires the separate source review of the test's real probe/frame waits.

The sixteen larger hosts show cumulative execution/result handling crossing the whole-session deadline. Latest pre-Abort RecordStart age ranges3ms–1.609s; four already have the matching RecordEnd and equal starts/ends before Abort. The observed boundaries do not establish sixteen long single-test hangs. Several bodies continue to emit results or start another case after Abort and even after the typed Completed message. Existing three statistics-mismatch and two out-of-active-execution malformed-host holds are retained; those late rows cannot be silently folded into a normal result set.

## Exact illustrative timelines

All times below are original diagnostic prefix times on2026-10-26 (UTC run clock). Header-to-start is a prefix observation, not a claim about an invisible test-body stopwatch. Full paths are under `unit194-options-pilot-run01/native/logs`.

| ID / host / file time | First RecordStart | Abort | Exact interval and retained facts |
| --- | --- | --- | --- |
|547 /2975225 /Runner0 `04-15-28_07475_13`|04:15:31.836|04:15:39.652|Header28.593→firstStart3.243s; firstStart→Abort7.816s. CallerCancellation starts33.333→Abort6.319s.20/65 returned,10P10F; Completed39.711, IsAborted=true.|
|557 /2975376 /Runner6 `04-15-35_65953_9`|04:15:37.511|04:15:47.230|Header35.897→firstStart1.614s. CallerCancellation starts38.153→Abort9.077s.13/56 returned,3P10F; Completed47.274, IsAborted=true.|
|496 /2974266 /Runner6 `04-14-11_79370_13`|04:14:15.353|04:14:32.740|17.387s firstStart→Abort. Latest pre-Abort start CompactOutputMatchesTheIndependentFrozenPendingFixture at32.736 (4ms).819 starts/818 ends atAbort; Completed32.784 reports818 (816P2F). Actual literal assertion result was already sent30.838.|
|535 /2975036 /Runner4 `04-15-08_78572_13`|04:15:19.067|04:15:29.728|Header09.549→firstStart9.518s; firstStart→Abort10.661s.192 starts=192 ends beforeAbort; last end29.725, only3ms earlier. Completed29.915 reports192F, chiefly configuration rejection. Sum emitted Duration fields across192 returned observations is3.356s.|
|500 /2974388 /Runner0 `04-14-17_97818_15`|04:14:27.668|04:14:39.355|Header18.840→firstStart8.828s; firstStart→Abort11.687s.39 starts/38 ends beforeAbort; latest SweptQueuedRegistration... start38.845 (0.510s). Completed39.553 reports38P/850 requested. Raw status remainsTimeout.|
|489 /2974244 /Runner5 `04-14-10_94202_13`|04:14:14.247|04:14:31.967|17.720s firstStart→Abort;124 starts/123 ends atAbort. Completed34.991 reports124P/828, yet a later RecordStart occurs35.658 and the final retained end is35.655. Raw statusTimeout/statistics hold remains.|

Before-Abort raw returned errors beginning `Assertion failed.` or `CollectionAssert.` exist for14 of the22 aborted rawKilled IDs:494,496,497,498,506,509,527,531,543,545,547,549,551,559. Example496's failed BoundsRemainEnforcedWithTransportsDisabled result is sent04:14:30.838,1.902s beforeAbort. Example547's InvalidSuppliedPipeNamesFailEvenWhenPipeIsDisabled assertion result is sent33.146,6.506s beforeAbort and before its pending cancellation test starts33.333. This is a literal ErrorMessage/wire-order observation, not a fresh DirectAssertion classification or completed-before-abort qualification. The actual private audit's completed-before-abort witness/definition counts remain0; the failed host/source/callback/phase guards are not waived. The other eight aborted rawKilled IDs532,533,535,556,557,558,560,564 return configuration errors without that literal assertion prefix before the deadline.

## Existing holds and scope

Audit issue observations are945 NativeFailureNotDirectAssertion,30 NativeAssertionWitnessMissing,27 NativeAbortOrCancel,27 NativeCompletionFailure,27 NativeResultSetIncomplete,3 NativeStatisticsMismatch and2 NativeHostMalformed. The945 failure observations are not945 definitions and include many definitions that also have an accepted direct witness. The malformed original paths are Runner3 `04-15-04_40587_16` and Runner7 `04-15-12_29288_4`, both reporting a result outside a unique active execution. Counts of emitted result observations, unique GUIDs, completion statistics and RecordStart/End markers are deliberately not equated.

This review used direct retained JSON/protocol-text inspection and shell information grouping. No new proof executable or receiving policy was authored, and no existing checker/classifier was rerun. Prefix timestamps and supplied Duration strings are used as stated; no automatic local-time conversion of JSON EndTime supplies any claim. The source inputs, original errors/outcomes, capture success, abnormal-host failure, initial/fresh controls and regular-file retention are distinct facts. The appropriate next source work is owned separately by Root/Unit/Pipes; this note neither proposes new assertions nor claims their future outcomes.
