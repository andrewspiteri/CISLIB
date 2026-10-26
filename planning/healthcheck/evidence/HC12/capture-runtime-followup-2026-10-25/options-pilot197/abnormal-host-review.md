# Five actual197 abnormal Unit hosts

Read-only causal review of NEW report-local selectors494/499/500/526/527. No code, native execution, classifier, report, audit or score changed. All five actual abort/normality/incomplete-result holds remain. Their rawKilled labels are not new credit. The separate5167 non-direct-error census is outside this review.

Full local receiving is verified0/noextras: `../unit197-options-pilot-closed-receiving01/receiving01.json`,9548 regular files/1,202,421,696 bytes, manifest **70684b6c208a41d00665a7b6708d3a9247b06734d653d12f94848e701efa7f98**. The body root below is `../unit197-options-pilot-closed-receiving01/readback01/unit197-options-pilot-run01/`. Sources and native bodies were only read. No new utility/build was needed.

Compact inputs at `../unit197-options-pilot-compact-receiving01/` independently rehashed:

| Body | SHA256 |
| --- | --- |
| mutation-report.json |40da10564d02a7045f0d0dc8b44e0e38d7b76d3b74cd4bd5ac0d8ef199ef5936|
| actual-native-audit.json |003049b2f144a2fd27466b65526f5694d3fe1f373c768b735e31c497cfb46ec5|
| actual-emitted-session-budgets.json |ff4cd79fe0811c43b6044a709ab86e38fe0aa73e556341d7e802561013c965d4|
| actual-scope.json |1041a8a3bf1c79dfb85eb59fa22f250edaa93e0ed2bdda811d7489e3b9e0b19a|
| actual-testhost-shutdown-capture.json |90279d76854b8c1cf22644f06cc95f16c95af5051aeb60ac366ff278a0cd41d7|
| results.json |038d0b9eba9499a64e3fd89f80a191dda886a0857e7c0ada73f1d82f56f794ed|

## Exact source association

All five are in `source/implementations/csharp/src/CIS.Core/Healthcheck/Contracts/Options/HealthcheckOptions.cs`, physical SHA **75d3e327b107682e5321b65991825fd51b7c5a6c12f7c539aded2dd758d8d419**. Its full7428-character text equals the actual197 report's embedded source exactly. UTF16 half-open spans below were read from that text and the actual report positions, not mapped by an older numeric ID. All five have static=false.

| New ID | UTF16 span / line | Exact original → replacement |
| --- | --- | --- |
|494|[4889,5298),101–111|The complete `new HealthcheckOptions { MaxConcurrentCollections=..., MaxMonitors=..., DetailsEnabled=..., PipeEnabled=..., PipeName=..., PipeMaxClients=..., PipeRequestTimeoutMs=..., MaxReportBytes=... }` initializer → `new HealthcheckOptions {}`; Object initializer mutation.|
|499|[5513,5543),117|`"CIS_HEALTHCHECK_MAX_MONITORS"` → `""`; String mutation.|
|500|[5554,5624),118|`CheckRange(PipeMaxClients, 1, 64, "CIS_HEALTHCHECK_PIPE_MAX_CLIENTS");` → `;`; Statement mutation.|
|526|[6547,6584),135|`issues.Any(issue => issue.Key == key)` → `!(issues.Any(issue => issue.Key == key))`; Negate expression.|
|527|[6547,6557),135|`issues.Any` → `issues.All`; Linq Any-to-All mutation.|

## Actual timeline: three different mechanisms

All times are UTC2026-10-26. Native result StartTime/EndTime fields carry+01:00; times below subtract that offset without changing retained bodies. Returned row counts and duration sums read both typed StatsChange and completion-tail rows, deduplicating by actual case GUID; each of these five hosts had no repeated result GUID. These sums are diagnostic observations, not witness qualification.

| ID / actual host PID | Actual subset / budget | Parent deadline arm → host Abort | Returned results and last-body facts |
| --- | --- | --- | --- |
|494 /2981554, Runner2|811 /32120ms|06:03:20.074 →06:03:52.215|736 rows (714P/22F), summed durations20.7412272s. `ARealDeadlineCallbackDuringCaptureClosesOnlyThatConnection(False)` started06:03:31.8419516, returned06:03:46.8701629 with actual OCE after15.0273859s. Stack is `NamedPipeClientStream.ConnectInternal` →`JoinCanceled` →`RunCanceledScenario` line54. Next same method starts06:03:46.871 and is still unreturned at Abort5.344s later.|
|499 /2981672, Runner3|850 /32185ms|06:03:45.593 →06:04:17.805|766 rows (762P/4F), summed durations21.6470531s. Longest returned row1.9573704s; all other returned rows shorter. Last returned `RetiredReservationDecisionCannotCarryALiveOperation` ended06:04:17.7507665. `ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource` starts06:04:17.769, only36ms before Abort. Its eventual result is absent; a hang is not established from36ms.|
|500 /2981725, Runner0|850 /32185ms|06:03:54.363 →06:04:26.658|ALL850 requested GUIDs returned (845P/5F); summed durations16.7785699s. Last row `OversizedPositiveRevisionIsRejectedBeforeAllocatingItsDecimalRepresentation(True)` ended06:04:26.562967. Parent timeout callback fires06:04:26.630, about67ms after that row. Host emits aborted completion06:04:26.688, then another nonaborted850 completion06:04:26.697. This remains a malformed/aborted two-completion race; the second frame cannot repair the first. No body hang is shown.|
|526 /2982211, Runner0|850 /32185ms|06:04:59.805 →06:05:32.211|335 reported rows (334P/1F), summed durations25.8252423s; longest0.8541797s. At Abort, `ActiveReportKeepsItsTemporaryMonitorAliveDuringAReentrantClockRead` has run about487ms; it actually ends06:05:32.2152732, about4ms AFTER Abort, duration0.4861449s. The host records several further finite cases, then starts `StaleHealthResultCanStillPublishUncontestedMeasurementKeys`06:05:32.349 before process exit. No single15s body accounts for this cutoff.|
|527 /2982219, Runner2|850 /32185ms|06:04:59.912 →06:05:32.161|386 reported rows (385P/1F), summed durations25.414073s; longest1.4418801s. At Abort, `TemporaryReceiverRemainsActiveDuringItsReportClockCallback("availability-refresh-failure")` has just ended06:05:32.1605175, duration0.7756776s; RecordEnd is logged afterward. Host continues finite retirement cases and starts `DisposingTheActuallyExecutingMonitorRejectsItsOwnWaitBeforeReturningATask`06:05:32.506 before exit. No single long returned row accounts for the cutoff.|

The actual native parent `TestRunRequest.OnTestSessionTimeout` lines bind the32120/32185ms deadlines, followed by actual host `TestExecution.Abort` and aborted completion. Earlier stale timeout callbacks in those same parent segments explicitly say their old request was disposed and are ignored; they are not assigned to these hosts merely by proximity.

Relevant physical line locators:

| ID | Parent request / deadline / actual timeout / exit callback | Host Abort / completion / tail |
| --- | --- | --- |
|494|Runner2:2279 /2343 /3949 /3964|8755 /8758; false-row error8745; next RecordStart8743|
|499|Runner3:9724 /9788 /11428 /11443|8947 /8950; last RecordEnd8936, next RecordStart8943|
|500|Runner0:4029 /4098 /5876 /5900|9901 /9904 and9907; last RecordEnd9888|
|526|Runner0:8197 /8261 /9065 /9081|3987 /4045; active-case RecordStart3983, later RecordEnds4094/4106|
|527|Runner2:11992 /12056 /12961 /12977|4568 /4625; active-case RecordStart4562, later RecordEnds4615/4627|

The execution deadline includes host setup, adapter/collector initialization and result/completion reporting, not just the sum of body Duration fields. For example500's first body starts06:04:03.150, almost8.8s after the parent deadline arm. For526/527 the returned GC/weak-ownership cases are individually finite but collectively consume most of the available running interval. Their actual sources contain real forced full-GC observations (`HealthcheckServicePolicyTests.cs:695/697`, `HealthMonitorActiveReportLifetimeTests.cs:32/34`). This establishes where time is spent, not CPU/memory pressure or a causal global-GC diagnosis. No blanket cumulative explanation is applied to494's explicit15s connect wait.

## Kernel and process closure stay separate from abort qualification

Following only actual CLONE_THREAD child edges from each host's kernel trace finds25/23/24/23/22 retained members respectively (117 including five leaders). Each retained member has exactly one exit0 terminal; no missing/nonzero/signaled terminal was found in that bounded group read. Latest member/leader terminals are06:03:52.322188,06:04:18.179571,06:04:26.892458,06:05:32.582586 and06:05:32.649554. Console callbacks record exit0 at52.353,18.211,26.962,32.685 and32.684 respectively. This is known thread-group closure, not a claim about all non-thread descendants, actual fixture settlement or successful normal native completion. The actual abort/malformed/incomplete holds remain.

## Smallest actionable next steps

1. **494 has a concrete fixture startup precondition gap.** The mutant removes the copied `PipeEnabled=true` and PipeName; the stored server options become disabled defaults. `HealthPipeServer` copies them at construction (`HealthPipeServer.cs:29`), and its accepted disabled Start opens no endpoint (`:73–87`). `CreateStartedServer` currently accepts any successful Start task (`HealthPipeCanceledResponseIsolationTests.Startup.cs:60–89`) without checking that this explicitly enabled fixture published its real endpoint. The actual failure is the first real client Connect, not the source callback or Core frame read. A narrow prospective Linux enabled-start endpoint assertion, using the existing maintained `TransportTestSupport.StartServerAsync` pattern, should run in the existing startup factory before any client escapes, retain all existing Stop/Dispose/owner-failure handling and leave Windows behavior/assertions intact. It is an execution repair, not a claim of an additional semantic kill; this source already produced earlier direct assertions in the held host. Do not accept the OCE or reduce its deadline.
2. **499/500/526/527 need matched finite-cohort calibration before another wait patch or timeout change.** The observations distinguish cutoff during a36ms newly started case, a completed850-case/reporting race, and ongoing finite GC-sensitive cases. They do not prove those last cases hang. The smallest next measurement is the already proposed original→inactive same-selected-cohort calibration with the exact850 identities and existing32185ms total budget under the intended worker conditions, retaining setup and completion overhead as well as real child/kernel guards. No ordinary case should be deleted or an abort normalized. If calibration itself cannot complete, it is a configuration/performance prerequisite failure; changing perTest/additional timeout remains a separate reviewed decision.

These are proposals only. No new fixture, platform framework, native rerun or acceptance change was authored here.

## Selected original body pins

All paths below are relative to `native/logs/` in the full receiving root.

| Body | Bytes | SHA256 |
| --- | ---: | --- |
|Runner 2-log.host.26-10-26_06-03-20_09326_13.txt|6469988|5a72f38f5a43a2bd9e7121f60182620394cdb2c62865242735a42241dd69f8ca|
|Runner 3-log.host.26-10-26_06-03-45_60408_4.txt|6690434|36d9525cda3e2d97b799007c55a44ed0caf30d710dbc79f9c6bddc363e30c1dd|
|Runner 0-log.host.26-10-26_06-03-54_38294_14.txt|7208172|f0f20fb4aa4d61e9887f7fe85eb1c55946dd0d9a1676b929eb411515049a221b|
|Runner 0-log.host.26-10-26_06-04-59_81274_12.txt|4085037|c879b00a7e79b3c0ebc9c25122c3cbb9fa89c7d21c3d7405cc3a7724dde22bc3|
|Runner 2-log.host.26-10-26_06-04-59_91805_12.txt|4364675|45e73b81e4e27c3b10efbf234d0d3e47ee20635b51941dce8e659f65353f2591|
|Runner 0-log.txt|41864246|87e9b95c43eefe8661a835d57c1baf590ff797081dec8d887a7c13055e574d31|
|Runner 2-log.txt|55770635|246fcdf8bae71c23745f623fe58369ee72b4ef10bb6cfd2a606b3c81b7cf0254|
|Runner 3-log.txt|55290158|af77df0d3a1d1ebb7c73de8c4faf5a06ea1f63d30de2954a1c2a73c17f03d309|

The raw fixture source6af1f77dca4877e03e4bc749effb83746d5f49c1fdcf2b5291f8e43745b70a21 and startup helper2e7b1687472860ccfc9a9add845d1fd7e89fb333788a77ac2481cbc462f17f2b were read from this same frozen source tree. No copied-current-source substitution was used.
