# SlowPartial154 source-only failure review

This review made no source edit, build, test invocation, clock change or retry. The failed canonical result remains failed. The later retained single-scenario diagnostic is separate evidence and cannot clear it.

## Actual observations

The original focused run selected all 56 HealthcheckPipeTransportTests rows. Build succeeded with zero warnings/errors; native execution ended with 55 passed, one failed, zero skipped and exit1. Failed GUID16d782d9-23dd-8e68-9b11-d50118708bee is SlowPartialFrameTimesOutAndCapacityIsReusable. Its actual duration was2.4126665s, start2026-10-24T07:31:12.7987132+02:00, end07:31:15.2114342+02:00.

Its failure is EndOfStreamException from TransportTestSupport.ReadFrameAsync line79, called by the actual healthy recovery RequestAsync. The original scoped partial-peer Read0-or-IOException phase had therefore returned and source0 had passed before the recovery task was created. The Aggregate records the original body failure and rejoining the separately retained recovery task, both reporting EOF. This is not evidence of a cleanup watchdog, unfinished owner graph, unknown producer failure or native-host crash. The original run did not retain per-connection timer/capture/callback timestamps or facts. In particular it does not establish whether the recovery reached CaptureSnapshot, whether its timer fired before writing, or which server connection-close path caused EOF.

The failed TRX also records concurrent execution:

| Actual method | Start local+02:00 | Duration |
| --- | --- | --- |
| OneConnectionIsClosedAfterExactlyOneResponse |07:31:12.7987055|2.3963857s|
| SlowPartialFrameTimesOutAndCapacityIsReusable |07:31:12.7987132|2.4126665s|
| ManyConcurrentClientsSettleAndStopIsTerminal |07:31:12.8002117|2.4429377s|
| StopWaitCancellationDoesNotCertifyBlockedHandlerSettlement |07:31:13.6688550|1.5243812s|

The ordinary assembly configures four MethodLevel workers. Several owner-review methods perform synchronous blocking compacting GC. Concurrent24 uses two workers and5000ms per served connection. This makes ambient scheduling, callback delay and GC interference concrete competing hypotheses. It does not prove any one of them happened inside this recovery's100ms budget. Earlier healthy snapshot cases had already passed in this same run, so generic first-ever formatter/JIT cold-start is a weaker explanation than a measured phase delay; no cold-start cause is proven.

Root's one retained diagnostic invokes the unchanged RunSlowPartialScenario plus full Review by reflection. It reports the same fixture path and DLLb9169328b0a5fe79b129a25bfabcb2fbc05c569eca71d5403312e7d45728b4c3, PDB0d9bbde624bf5af958502c8a9ad9e2127804b2a306e2798dab690ed153f7cb67, Core6c06cf587f546e3f83ae2118d3e5b7e3336c76f12b8902a2e1345f17a6b33cf6. It completed230.8152ms with captures1/returns1, timers2/drains2, callbacks1/1, actual body/client/raw scopes/state joins and no unknown errors. Full Review observed11 live graph members become0. This proves one successful unchanged scenario under that diagnostic host, with no native-adapter/full-class claim. The original TRX itself identifies assembly location rather than a DLL content hash; the diagnostic's hash is a physical observation of its own loaded file, not a newly reconstructed original-run receipt.

## Exact source and requirement distinction

ConnectionRecovery line31 sets PipeRequestTimeoutMs=100 on the server; that applies to BOTH initial partial connection and the healthy recovery. The helper client has a separate15s outer cancellation bound. The original152 source preimage already used100ms. No recent OwnedTimer154 change touched this helper.

Server creates a fresh HealthPipeBudget only after actual accept. Its real System clock measures one total budget across request framing, synchronous capture/formatting, response write and the final matched-client close read. The pre-write monotonic guard intentionally prevents a late successful response even if the timer callback was delayed. A healthy request can therefore legitimately receive EOF when this configured budget expires; a100ms configuration is not a promise to complete healthy formatting/IO within100ms under arbitrary host scheduling. Other accept/endpoint/IO paths remain possible absent actual failed-phase evidence.

The published configuration contract states default1000ms, allowed50..60000. Technical intent states a default total budget1000ms and no implicit retries. It does not specify this fixture's100ms nor a100ms healthy-recovery latency guarantee. Exact monotonic boundary and total-budget behavior are covered separately by the controlled-clock149/151 cases; this test's concrete role is partial-client expiry followed by capacity reuse. Nevertheless100ms is the preserved existing source choice and changing it would change this scenario, so it needs a reviewed amendment rather than silently rewriting old evidence.

## Smallest honest next step

Do not repeatedly retry this case until a green result appears, widen the original IOException catch, swallow recovery EOF, remove actual cleanup/unknown-fault checks, or reinterpret the failed run as success.

The narrowest evidence-producing change, if root approves, is observation only in the existing ConnectionRecovery System-clock wrapper and body: per-connection actual creation/due time/callback entry-return, monotonic timestamps at original partial-peer terminal return, recovery creation, source entry/return and recovery completion/failure, plus final actual facts on failure. It must forward every original callback/state/dueTime/period unchanged and must never advance time, fire callbacks or alter IO. One bounded full-class diagnostic with the same failed-source configuration can distinguish an actually expired recovery budget from an early endpoint/IO failure. Its result must be retained even if it fails; it is not a qualification retry.

For a source repair after root review, the least behavioral change is to isolate this short wall-clock case with its own DoNotParallelize attribute while preserving its100ms budget, original IO/assertions and all cleanup checks. This removes observed same-process method overlap but cannot guarantee immunity to external pauses; one passing isolated result cannot prove the original cause. If the intended test is recovery at the published default rather than a100ms latency condition, explicitly changing only this scenario to1000ms is also contract-compatible and preserves actual expiry plus recovery, at the cost of a longer initial expiry. That alternative changes the scenario budget and should be documented; it still must fail any actual source/IO/cleanup error. No such repair is implemented by this review.

Scope is limited to these retained records and current exact sources. No Windows scheduling trace, failed-run GC trace, timer-expiry attribution, Linux run, mutation credit, denominator change or90% gate is claimed.