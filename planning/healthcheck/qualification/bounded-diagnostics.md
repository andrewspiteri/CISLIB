# Bounded Healthcheck mutation diagnostics

## Prospective CIS outcome policy — 26 October 2026

The user approved adopting CIS's shared native-outcome rule for future Healthcheck
campaigns. CIS now provides `cis test mutation run` with target profiles, bounded
execution/storage, explicit diagnostic versus qualification results, profile-pinned
target environment, and minimal transient capture. Use that common workflow rather
than introducing another module-specific mutation controller or proof catalogue.

The [shared incremental receiver](shared-incremental-receiving.md) describes the
new saved-result protocol and its current limitations. It does not grant
qualification credit to stale campaigns or replace child-process evidence.

New union inputs select `schema: cis.healthcheck.mutation-union-inputs/v2-native-completed`
and `outcomePolicy: cis-native-completed/v1`. Existing v1 inputs retain their original
classification; adding an outcome-policy field to v1 is rejected. Historical
reports and their recorded scores remain unchanged. The output identifies its policy.
Use newly captured campaign inputs; do not relabel retained historical reports as v2.

Under the prospective rule, an exact, completed native failed test can detect its
reported mutant, including an ordinary product exception before an assertion.
It does not need approved assertion wording or a per-mutant semantic proof. Source,
definition, requested test GUID, selector ownership, native outcome and completed
execution must still agree. Native timeouts, framework deadline/cancel diagnostics,
initialization/cleanup failures, crashes and unresolved process or protocol failures
remain holds. A product exception's type name alone does not establish infrastructure
failure.

The prospective configuration may enable native first-failure bail with explicit
`disable-bail: false`, preserving the complete mutation scope, selected cohort and
100/90/90 thresholds. Omitted results alone do not prove intentional stopping: use
the pinned runner's actual intent record, exact completed failure and terminal
cleanup. Do not infer intent from any abort or cancellation. The 90% full-scope
conservative gate, no timeout credit, genuine unfinished-work holds, survivor
dispositions and separate platform lanes remain unchanged.

The small native Windows control verified completed product exceptions, original
engine intent, subject cancellation and normal cleanup against the maintained
verifier. However, the pinned MSTest 4.5.1 adapter still completed all three queued
cases after cancellation. Actual omitted-case work savings were not demonstrated;
the incomplete-result receiving controls do not claim that runtime observation.
Measure selected-cohort cost before expanding a campaign. These workflow controls
provide no Healthcheck mutation credit.

The dated investigations below describe their recorded snapshots and legacy policy.
They are not instructions to recreate large evidence bundles or repeat old runs.

User direction on24 October2026: diagnose failures with small, bounded experiments, justify time budgets, then repair specific assertion gaps before scheduling more broad reruns. The90% conservative gate, original evidence and unresolved runtime/resource holds remain unchanged.

Later user direction on24 October2026 requests much shorter mutation iterations.
The [short profiles](gate-closure/short/README.md) replace the75/35-minute added
allowances with1,000ms. The pinned tool remains adaptive: earlier measurements
imply roughly20–35 seconds for Unit requests and191 seconds for complete Pipes
requests, plus cancellation/shutdown. These are not hard15-second limits. Old
run inputs remain immutable; explicitly stopped runs stay incomplete and uncredited.

## Working sequence

The [24 October startup investigation](../evidence/HC12/unit-startup-investigation-2026-10-24/README.md)
found that short adaptive budgets with eight traced workers can expire during
instrumented host initialization. Diagnose those separately from fixture-body
hangs. A control must match the actual selected-test request path: SDK source
requests add whole-assembly discovery and can introduce unrelated fixture errors.
Corrected cached-test controls passed8/8 with each tracing mode; the optimized
batch took11.308 seconds versus17.336 seconds. That fixed-budget single-case
control does not license eight adaptive1,000 ms workers. The new complete Unit
run instead uses the [maintained-budget two-worker profile](gate-closure/stable/README.md),
with optimized tracing and all existing qualification guards.

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
