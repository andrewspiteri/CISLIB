# Bounded Healthcheck mutation diagnostics

User direction on24 October2026: diagnose failures with small, bounded experiments, justify time budgets, then repair specific assertion gaps before scheduling more broad reruns. The90% conservative gate, original evidence and unresolved runtime/resource holds remain unchanged.

## Working sequence

1. Read the actual closed failure and bind its source, case and phase. Review each new failure family after20 minutes of initial triage; choose a concrete experiment or record the obstruction instead of repeating unchanged controls.
2. Select one public test and one exact source change. Require a fresh original positive before the negative. Retain new outputs for both; do not retry an unchanged failure.
3. Preserve genuine work and cleanup. A failed assertion must survive actual resource cleanup; unknown exceptions, pending tasks, live owners and failed cleanup remain holds. A shorter expiry supplies no kill credit.
4. Review the actual native result, case identity, error and cleanup alongside process receipts. Add a proven assertion to the maintained suite and run its focused test plus the relevant current cohort.
5. Schedule another complete mutation campaign only after a coherent repair batch and an explicit source/cohort/resource review. Diagnostic results do not substitute for complete mutation reports or the three-report union.

## Executed Pipes183 envelope

The reviewed inert-server resource case uses a90-second VSTest session watchdog,120-second owned process wait,15-second bounded reap and15-second bounded stdout/stderr drain. These are separate phases; the process wait alone is not the total envelope. Input hashes, arguments, actual exits and expiry/kill/reap/drain flags are retained. The private supervisor neither retries nor assigns assertion or mutation credit.

The actual test has15-second main and ancillary public-call observations and starts no endpoint, worker, snapshot capture or request timer. It observes the cancellation source's actual owned handle, records the server-disposal assertion, joins both real public cleanup calls, then releases any residual handle through its actual owning cancellation source. It never directly disposes a borrowed handle or repairs the saved assertion.

The original case passed in9.5818 ms. Removing only `HealthPipeServer.stoppingSource.Dispose()` caused the same case to fail at the intended handle-closure assertion in16.6727 ms. Both native invocations finished in about one second, without expiry, termination, log-drain failure or cleanup hold. Source and actual-result peer reviews cleared adoption. The maintained source is `HealthPipeServerResourceDisposalTests`; the canonical single-case run and complete1390-case Healthcheck Unit cohort passed.

This envelope is justified for that inert case. It is not a universal test SLA, a complete622-case Pipes mutation budget or an approval for a different source variant. Each reuse needs its own phase and ownership review. Expiry requests termination only of the supervisor's owned child tree; it does not prove normal settlement. A child retaining a log pipe remains a hold even after its parent exits.

## Executed Unit183 envelope

The actual Linux170 original1389-case baseline passed in10.7719908 seconds. Its current mutation requests still have roughly75-minute watchdogs. Actual failure accumulation is also expensive: one source-bound publication mutation returned159 failed cases without a protocol abort or cancellation in26 minutes32.576 seconds; the inert-registration row alone spent10.0152833 seconds and reported both the rejected Register operation and a cleanup timeout. Those protocol fields alone do not prove complete process and resource settlement.

The executed Unit183 diagnostic uses a150-second native session watchdog,180-second owned process ceiling,15-second reap and15-second log-drain bounds. Its supervisor is a reviewed literal-only derivative of Pipes183. A separate300-second caller watchdog was proposed but never implemented or executed. The old176 launcher's unbounded post-exit stream drain was not reused.

The same original public test passed in26.6947ms. Changing only the source-bound publication assignment from `true` to `false` failed the intended valid-registration assertion in29.6792ms. Before that assertion can escape, the fixture joins the real created entry's retirement task and independent state drains, reads the actual retirement-error record, and requires the whole runner/service/state/monitor/clock/owner graph to become unreachable with empty producer-error histories. No missing registration handle is declared joined. Both native invocations reaped/drained with no expiry or supervision hold.

Two separately built fixture instrumentation controls used the same negative Core pair. Retaining the actual runner produced an outer Aggregate live-owner hold after10.0440531s. Calling the actual fixture timer producer recorded its real exception and produced an outer Aggregate producer/history hold in29.6631ms. These verify the fallback guards; they are not claims that the SUT made those calls. Nested assertions do not turn these Aggregate failures into direct assertions. Independent source and actual-result peers cleared the exact uninstrumented change, which is now adopted; its maintained single case and complete1390-case Healthcheck Unit cohort passed.

These manual diagnostics supply zero mutation credit and do not establish a complete campaign or release profile. Their ownership reasoning concerns the reviewed inert source path, with no Start, collector acquisition, timer or installed external retirement subscriber. Unknown exceptions, incomplete retirement, retained owners and recorded resource errors remain holds.

## Separate progress measures

Report accepted kills, held kills, survivors, uncovered definitions, timeouts and unfinished execution separately. The old completed union remains1591/3065=51.908646% until a complete replacement is audited. Its1069 held kills comprise804 abnormal/incomplete-only,242 non-direct-only and23 in both categories. These are affected definitions, not1069 independent root defects. Recovering all held kills would still leave99 additional accepted kills needed at that fixed denominator.

The active143/170/180 campaigns keep their immutable source and timeout profiles. New canonical test changes are not inserted into their snapshots or reports. In particular, the new resource case increases the current Unit Healthcheck cohort to1390 and the current Pipes cohort to622; active170 and180 retain their recorded1389 and621 populations. Their eventual complete reports and the new focused diagnostics must be labelled separately.

Evidence: [score/cause ledger](../evidence/HC12/assertion-gaps-2026-10-23/reviews/execution183-score-and-failure-ledger.md), [bounded resource diagnostic](../evidence/HC12/assertion-gaps-2026-10-23/reviews/pipes183-bounded-resource-diagnostic.md), [actual-result peer](../evidence/HC12/assertion-gaps-2026-10-23/reviews/pipes-owned-cts183-actual-result-peer02.md). These supply no whole-Core or release approval.
