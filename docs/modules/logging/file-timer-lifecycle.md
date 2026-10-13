# Periodic file timer lifetime

WP05-S09-T03 detaches the owned periodic timer when file admission closes or the file output faults. It starts one suppressed-context asynchronous timer-disposal task per acquired timer. Repeated closure, fault and terminal worker cleanup reuse that task. Timer callbacks continue to register only existing fixed-boundary controls; no I/O or per-tick task is introduced.

Timer disposal executes after the shared admission gate is released, including an outer gate held by runtime closure or admission failure. Fixtures additionally mark entry/exit of the actual outer closer and check that disposal observes its exit; the current-thread Monitor probe and source-inspected gate barrier remain separate supporting facts. Closure/fault state is fully published first. Custom TimeProvider timer code therefore does not run beneath that lock or retain the closing caller's ExecutionContext. The sequential output worker awaits the same disposal task after its actual writer call settles, then releases its owned file resource. Borrowed console resources retain their existing ownership rule.

Scheduling cancellation prevents future dispatch; an already-dispatched callback can still complete. Closed/faulted queues ignore that callback. Completion waits for the timer's asynchronous callback join. Blocked writer I/O also remains pending, even if scheduling has already been cancelled. Cancellation does not prove that an OS write ended, and a timer callback cannot restart the output.

A periodic writer-flush exception uses the existing terminal FlushFailed category and system UTC fault observation. Previously written records remain written; queued unstarted records become abandoned. The file worker faults and the console can keep accepting/writing/flushing. A concurrent admission fault while an actual flush is blocked preserves its unresolved registration until that real I/O settles.

## Dependency ordering

The original T03 mentions the shared stop operation, but the original plan implements the public bounded StopAsync/DisposeAsync operation in the dependent WP05-S10 story. T03 implements and qualifies its timer lifetime foundation against the actual existing atomic runtime closure and one shared OutputQueues.Completion task. It does not substitute a fake stop result, expose a public placeholder or claim bounded public shutdown. S10 must join this same worker/timer cleanup when implementing the public operation, original monotonic deadline and eventual safe ownership release. That integration remains required before lifecycle completion or publication.

## Executed case design

The shared seven-case inventory is conformance/logging/file-timer-lifecycle-cases.json. The fixture invokes actual Core queues and timer callbacks:

- close-write and close-flush cancel scheduling while the actual first writer call stays held; original flush controls remain pending and finish after release.
- close-callback uses concrete configured FileOutputWriter and its actual lease. A dispatched callback is held before invoking Core. Scheduling is cancelled, the asynchronous callback join stays pending, and a competing exclusive lease open remains rejected. The same lease and active handles open exclusively after join.
- flush-failure and flush-failure-callback observe a timer-created registration before releasing a failed writer flush. Three actually accepted (unseeded) events become one written and two abandoned. A fourth event still reaches console; the actual public FlushAsync result shows file failure. The latter case additionally holds an active timer callback during fault cleanup.
- admission-fault seeds only the internal maximum ticket as an explicit failure trigger after three actual accepts. It does not claim an enormous emitted history. A blocked active flush stays pending while the timer is cancelled and the two queued events are abandoned. Its actual later successful flush remains successful.

The detached-arm-throw case closes and joins the runtime while a trusted timer is paused before arming. A later custom ObjectDisposedException still propagates from this internal startup method, while exactly one shared asynchronous disposal owns the detached timer. A duplicate unpublished extra timer still receives local cleanup through the existing T02 case.

Each closure case runs 64 concurrent runtime closures and asserts actual reference identity for both the original shared completion and disposal task, plus exactly one periodic disposal. Controlled-time advancement after cancellation cannot produce new callbacks or writer work. Time guards only bound execution; no arbitrary sleep establishes correctness.

Qualified timers dispose without throwing. A failure from trusted internal custom ITimer.DisposeAsync currently propagates through worker cleanup, can replace its original worker exception and can leave Stopped diagnostics with a faulted completion. General cleanup-failure diagnostics and preservation of the primary failure remain an explicit S10 integration obligation; T03 does not claim that latent custom-provider case is qualified. An inactive startup timer whose synchronous Dispose throws is currently attempted again by the startup catch; that inherited custom-disposal case is also unqualified and assigned to S10. No public custom timer API is exposed.

The nonshipping clock models the ITimer asynchronous callback join and invokes the real Core callback. It does not claim to measure OS timer resolution. Existing System-timer startup/caller-timeout regressions remain separate. Controlled MemoryStream writer cases are supplemented by the physical callback ownership case and existing physical file interval regressions.

The C# matcher checks principal facts. The independent native reader checks held/final timer phases, sequential I/O order, exact counters, safe fault timestamps, public results, original producer PID and actual Unicode JSONL bytes; the physical case also compares the actual active file. It rejects 29 deliberate report corruptions for exact reasons. Boolean identity/flow/lease fields attest fixture assertions; successful child execution and source inspection support those facts rather than an external measurement of object identity.

T04 full S09 acceptance, public runtime creation/shutdown/provider, complete release qualification and activated examples/skills remain pending. Historical receipts, supplied source plans and all 41 Planned release-family rows remain unchanged.
