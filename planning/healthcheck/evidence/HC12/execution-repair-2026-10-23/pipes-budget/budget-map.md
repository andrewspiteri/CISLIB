# Pipes startup-omission cohort budget proposal

Status: source review only, 2026-10-23. No build, test, mutation subject, profile edit, or remote action was performed for this map. `source-pins.json` seals the reviewed bytes. This is an execution-policy proposal for root review, not an amendment to the maintained qualification profile or evidence that a timeout is a kill.

## Scope and source binding

The maintained Pipes filter selects 475 Unit and 121 Integration cases, including one `SchedulingMatchesRequestedMode` case in each assembly. Unit source enumeration reconciles as 382 singleton/DataRow placeholders plus 92 additional DynamicData expansions plus scheduling = 475. The seven DynamicData providers expand to 12 formatter-duration cases and protocol sets of 31 text, 7 diagnostic, 18 timestamp, 6 duration, 5 counter, and 20 secondary-contradiction cases. No DynamicData provider constructs a HealthPipeServer. Integration is 56 transport + 64 probe + scheduling = 121.

The exact server source is SHA-256 `809ba1a493d6176e354f84209ea556ae21eb726e626b35eb7cc777956cdba12f`, `Healthcheck/Pipes/HealthPipeServer.cs`:

| Actual native alias | UTF-16 span | Exact original | Exact replacement | Fresh-server effect |
|---|---:|---|---|---|
| 2784 | [2403,2410) | `started` | `!(started)` | Returns the unresolved startup notification before changing state or creating work. Actual Stop/Dispose can settle. |
| 2787 | [2530,2542) | `StartCore();` | `;` | Leaves `started=starting=true`, never publishes endpoints, and leaves startup and terminal settlement unresolved. |

These aliases are not portable identities; any receiving campaign must join the source hash, exact span, original, replacement, mutator, and newly generated native definition. Their retained original definition review is `planning/healthcheck/evidence/HC12/storage-cleanup-2026-10-23/retained/artifacts/hcg/current-campaign-aborts-70/peer-review/pipes-five-source-review.json`.

## Unit server methods and budgets

All listed Deadline fields are 15 seconds. The envelope deliberately counts every reachable sequential watchdog, including unchanged service disposal. It is an upper sum of declared asynchronous deadlines, not a wall-clock guarantee under starvation or a synchronous call that never returns a Task. Source-dependent unchanged normal work in the rest of the cohort is accounted separately by the fresh Stryker baseline and margin.

| Class / method | Rows | Fresh Start reached before terminal call | Watchdog path after fresh startup omission | Envelope per affected row | Envelope sum |
|---|---:|---:|---|---:|---:|
| HealthPipeActiveCancellationBoundaryTests.ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource | 2 (`stop`,`dispose`) | 2 | Start; actual Stop/terminal; actual Dispose; service Dispose | 60s | 120s |
| HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException | 1 | 1 | Start; parallel actual Stop+Dispose; service Dispose | 45s | 45s |
| HealthPipeInternalMutationQualificationTests.SuccessfulProbeDeliversBufferedOutputBeforeReturningItsHealthExit | 1 | 1 | Same | 45s | 45s |
| HealthPipePostCaptureClockFailureTests.AOneShotClockFailureAfterCaptureProducesAnIsolatedErrorAndLeavesTheEndpointReusable | 1 | 1 | Start; actual Stop; actual Dispose; service Dispose | 60s | 60s |
| HealthPipePublicLifecycleBoundaryTests.TerminalCallsCloseAdmissionBeforeTheirReturnedWaitIsObserved | 2 (`started-disabled` × two terminal operations) | 2 | First Start; alternate actual terminal; actual Dispose; service Dispose. The second body Start and terminalWait are not reached. | 60s | 120s |
| HealthPipeReceivingBoundaryQualificationTests.ASourceFailureRetainsTheCompleteSelectedPipeErrorEnvelope | 3 views | 3 | Start; actual Stop; actual Dispose; service Dispose | 60s | 180s |
| HealthPipeReceivingBoundaryQualificationTests.AnOutOfRangeCliNumberCannotBecomeACompleteHealthyExchange | 2 CLI options | 2 | Same | 60s | 120s |
| HealthPipeReceivingBoundaryQualificationTests.ARequestCompletedAtTheTotalDeadlineCannotEnterTheSnapshotSource | 1 | 1 | Same | 60s | 60s |
| HealthPipeStartupCompletionQualificationTests.AnActualSuccessfulSingleSlotExchangeConfirmsTheSavedStartCompletion | 1 | 1 | Actual connection uses one 15s CTS; actual Stop; actual Dispose; service Dispose. Saved Start is intentionally not awaited, per its receiving assertion design. | 60s | 60s |
| HealthProbeCallerCompletionQualificationTests.AnActuallyPendingConnectionCompletesAfterTheRealEndpointServesItsResponse | 1 | 1 | Start; canceled caller/context drain; actual Stop; second caller/context drain; actual Dispose; service Dispose | 90s | 90s |
| HealthPipePublicLifecycleBoundaryTests.TerminalCallsCloseAdmissionBeforeTheirReturnedWaitIsObserved | 4 (`unstarted-disabled`/`unstarted-enabled` × two terminal operations) | 0 | No fresh Start: terminal closes admission before the synchronous Throws Start observation, so the mutated started guard is not reached. Conservative allowance counts terminal body wait, alternate terminal, saved terminal wait, Dispose, and service Dispose. | 75s | 300s |
| **Total HealthPipeServer cases** | **19** | **15** | **15 affected rows: 900s; all 19 rows including conservative unaffected-row allowance: 1200s** | | **1200s** |

There are no further HealthPipeServer constructors in selected Unit classes. The endpoint-only duplex test and first-instance collision test construct BCL pipe endpoints rather than HealthPipeServer. Other selected probe tests either use a raw BCL peer or no server. They are unaffected by these two server-source substitutions and still remain requested in the full cohort.

The more specific sum that excludes unchanged service-disposal waits is 675s for the 15 affected rows. The selected proposal uses 1200s instead, preserving both the nested real terminal waits and an allowance for the four unaffected terminal rows.

## Integration server methods and budgets

`TransportTestSupport.StartServerAsync` bounds the actual returned startup task by 15s. Its Unix endpoint assertion executes only after successful startup, so it is not reached for either omission. Ancillary `PipeServerCleanup` initiates actual Stop and Dispose in parallel and gives their combined drain one 15s bound, retaining TimeoutException and real aggregate failures. First startup failure exits the body before requests, later repeated starts, or replacement-server creation. The dedicated terminal-disposal fixture retains its direct assertions and performs the parallel fallback only afterward.

Every transport method is enumerated below; counts sum to 56:

| Method | Rows | Fresh Start rows relevant to 2784/2787 |
|---|---:|---:|
| WindowsPipeEndpointDescriptorsReleasePinsAfterActualSettlement | 1 | Windows child only; inapplicable on Ubuntu |
| AnOccupiedEndpointFaultsPublicStartAndTheOriginalOwnerRemainsUsable | 1 | 1 (outer watchdog also surrounds ThrowsAsync) |
| AnActualRequestTimerCreationFailureClosesAdmissionWithoutReadingOrChangingHealth | 1 | 1; replacement is not reached |
| ActualLocalServerUsesCompleteSingleSnapshotAndFragmentedLittleEndianFrames | 6 | 6 |
| InvalidRequestsReceiveBoundedErrorsAndNeverCapture | 12 | 12 |
| InvalidFramesAreRejectedBeforeSnapshotAndDoNotPoisonTheEndpoint | 4 | 4 |
| UnknownFieldsStillRespectDepthLimitAndValidAdditionsAreIgnored | 1 | 1 |
| DetailsEnablementAndReportSizeAreEnforcedBeforeReply | 2 | 2 |
| SnapshotExceptionIsTransportErrorAndNextRequestCanSucceed | 1 | 1 |
| OneConnectionIsClosedAfterExactlyOneResponse | 1 | 1 |
| SlowPartialFrameTimesOutAndCapacityIsReusable | 1 | 1 |
| ManyConcurrentClientsSettleAndStopIsTerminal | 1 | 1 |
| StopWaitCancellationDoesNotCertifyBlockedHandlerSettlement | 1 | 1 |
| DisposalWaitsForAnActuallyRunningSnapshotSource | 1 | 1; its outer WhenAll gives cleanup one total 15s |
| DisabledServerNeverOpensEndpoint | 1 | 1 disabled Start, still affected |
| EnabledConstructorAndCanceledStartAreInertBeforeAnExplicitSuccessfulStart | 1 | 1 after a 100ms missing-endpoint check; initial pre-canceled Start is unchanged |
| MaximumValidRequestLengthIsAccepted | 1 | 1 |
| RepeatedClientsClosingBeforeRequestDoNotStopTheServer | 2 | 2 |
| ServerBudgetIncludesRequestAndSynchronousSnapshotWhenTimerCallbackIsDelayed | 1 | 1 |
| RequiredConstructorInputsAreRejectedBeforeOpeningAnEndpoint | 2 | 0 |
| NonpositiveClockFrequencyCannotCreateAReportingServer | 2 | 0 |
| StoppingBeforeStartIsTerminalAndDisposalIsIdempotent | 1 | 0 |
| ATotalBudgetIsExpiredAtItsExactMonotonicBoundary | 3 | 3 |
| TruncatedRequestHeaderOrBodyReleasesCapacityWithoutCapturing | 4 | 4 |
| ASourceFailureCannotBeConfusedWithPeerFramingOrServerCancellation | 2 | 2 |
| ACompletedRequestReleasesItsOwnedTimerBeforeServerStopSettles | 1 | 1 |
| EndpointHasCurrentUserBoundaryOnTheExecutedPlatform | 1 | 1 |
| **Total** | **56** | **50 fresh Start rows (49 enabled, one disabled), five nonstarting constructor/terminal rows, one platform-dependent diagnostic** |

`HealthcheckProbeProcessTests.ActualProbeProcessExitsExactlyForSelectedCompleteView` adds nine real enabled-server rows with the same 15s Start + 15s combined drain envelope. Its probe child starts only after successful server startup and therefore is not launched on either omission. The other 55 probe rows construct no HealthPipeServer.

For Ubuntu the exact implicated startup/drain watchdog sum is 59 × 30s = 1770s, plus the existing 100ms pre-start absence check and ordinary unaffected cohort work. Rather than reduce the cap to this figure, retain all 56 transport rows plus the nine real-server probe rows at 30s each: **65 × 30s = 1950s**. This explicit conservative Integration envelope covers the constructor/unstarted and platform diagnostic rows too; the extra 100ms is comfortably within the separate 150s margin.

The Windows-only child resource diagnostic has a 15s parent observation watchdog and a 15s actual process cleanup watchdog. Child startup and explicit Stop each have a 5s deadline and combined real Stop+Dispose cleanup has one 5s deadline. A first startup omission exits after its 5s startup plus 5s cleanup path; it does not run all 40 loop iterations. Its Timeout/Aggregate failures cannot enter the existing source-attributed resource-error normalization. Absolute child exit before the parent deadline remains subject to scheduler/process startup/synchronous BCL limits; any forced stop remains a capture/release hold.

## Remaining lifecycle review limits

No selected fresh HealthPipeServer startup or terminal disposal remains implicitly unbounded at the asynchronous Task boundary. Calls intentionally saved before a real public milestone have bounded receiving I/O and bounded real cleanup. Synchronous Throws assertions are retained where the public terminal call must already have closed admission. Every selected HealthPipeServer ownership path has explicit bounded terminal cleanup, including the nine probe rows and the Windows diagnostic child.

Unchanged service/monitor disposal, raw BCL stream disposal, ASP.NET app Start/Stop/Dispose, GC/finalizers, synchronous pipe creation/BeginStop prefixes, process-output reads after process exit, and callback execution are not proven globally bounded by these Task watchdogs. In the maintained Pipes filter the HTTP fixture is entered only by the Windows resource diagnostic, which returns before server creation on Ubuntu. The HTTP main mode is not selected, but its HealthPipeServer startup and cleanup now also have the 5s receiving bounds. These limits prohibit any claim that a larger Stryker budget establishes universal termination.

## Supported profile and controller proposal

Use a separately pinned **additional-timeout 2100000ms (35 minutes)** diagnostic profile, retaining the exact full filter, Complete, coverage off, noMix, disable-bail true, serialized test assemblies, concurrency two, and thresholds 100/90/90. The same per-assembly timeout safely exceeds the explicit Unit server envelope (1200s) and Integration envelope (1950s), with 900s and 150s margin respectively before the baseline component is added. The former 1800000ms estimate does not cover the conservative current Integration envelope and should be retired.

Pinned Stryker 4.16.0 computes `T = floor(1.5 * (max(initialSessionMs - aggregatedInitialTestMs, 0) + aggregatedInitialTestMs)) + additionalTimeoutMs`. It sets `TestSessionTimeout=T` separately for each assembly. `WaitForEnd` can then spend three additional 10000ms intervals waiting for notification/session/abort settlement. `RunVsTest` permits three failed-session attempts; sleep detection can decrement its attempt counter. Coverage off requests all tests; disable-bail keeps requesting remaining cases after failures. There is no supported per-case batching or per-case timeout option in this pinned runner. The fixed ratio is not an editable option in this version. References are the exact pinned commit source, not current-master documentation:

- https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Core/Stryker.Core/Initialisation/TimeoutValueCalculator.cs
- https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.TestRunner.VsTest/VsTestRunner.cs
- https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.TestRunner.VsTest/VsTestContextInformation.cs
- https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Configuration/Options/Inputs/AdditionalTimeoutInput.cs
- https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Core/Stryker.Core/MutationTest/MutationTestExecutor.cs

The controller must not retain a 30-minute whole-command deadline. For a finite invocation with M verified executable native mutants, let T be the freshly observed actual Stryker TestSessionTimeout. The source-based allowance for one attempt per assembly is **M × 2 × (T + 30000ms) + 1800000ms**. To accommodate Stryker's three ordinary failed-session attempts without an outer abort, recommend **M × 2 × 3 × (T + 30000ms) + 1800000ms**: both requested assemblies for every native mutant, up to three actual attempts, plus 30 minutes explicit build/baseline/restoration/capture headroom. This remains a finite diagnostic cap, not a promise that every infrastructure retry can finish. Sleep detection and any unexpected outer executor retry must retain all attempts and stop as an execution anomaly; never extend the cap automatically.

Do not invent M from two desired aliases: range siblings and retry policy must be counted from actual native definitions. If a fixed cap is required before baseline, preflight may require `T <= 2160000ms` (36m). The one-attempt formula is then **M × 4380000ms + 1800000ms** and the three-attempt recommendation is **M × 13140000ms + 1800000ms**. For example, M=2 gives 176m for the single-attempt envelope or 468m for the ordinary three-attempt envelope; M=4 gives 322m or 906m. These are conservative sizing examples, not assertions about the eventual range or predicted run durations. A higher observed T must stop preflight for a reviewed recalculation, not quietly extend the controller. If root chooses the smaller one-attempt cap, any retry is explicitly outside that planned completion budget and its truncation remains an honest controller failure.

This is supported diagnostically by Stryker, but the maintained Pipes qualification validator currently requires its exact maintained configuration (additional-timeout 10000ms). Adoption requires root's explicit profile/validator/source-pin amendment and rejection controls; do not relabel a diagnostic report as qualified before that receiving validation exists.

Required receiving review: freeze source/config/tool/native-definition pins; fresh unmutated 475+121 native cohort; actual two scheduling GUIDs; exact requested/started/completed/results/killedBy joins per assembly; emitted timeout and per-assembly reset; normal host completion/ActiveTests empty; child process closure and retained runtime/kernel capture; actual resource-drain failures; native exit codes; backup/restoration/PDB-repair/fresh controls; capacity and finite capture guards. Every timeout, abort, forced stop, and unresolved owned drain remains a failure/release hold. A completed earlier assertion may be assessed only under the separately approved source-proven assertion rule; this proposal grants no kill credit or union/release status.
