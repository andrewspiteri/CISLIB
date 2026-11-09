---
title: 'Periodic file timer lifetime'
type: document
status: unknown
cis:
  stable_id: cislib.document.modules.logging.file.timer.lifecycle
---

# Periodic file timer lifetime

WP05-S09-T03 detaches the owned periodic timer when file admission closes or the file output faults. It starts one suppressed-context asynchronous timer-disposal task per acquired timer. Repeated closure, fault and terminal worker cleanup reuse that task. Timer callbacks continue to register only existing fixed-boundary controls; no I/O or per-tick task is introduced.

Timer disposal executes after the shared admission gate is released, including an outer gate held by runtime closure or admission failure. Fixtures additionally mark entry/exit of the actual outer closer and check that disposal observes its exit; the current-thread Monitor probe and source-inspected gate barrier remain separate supporting facts. Closure/fault state is fully published first. Custom TimeProvider timer code therefore does not run beneath that lock or retain the closing caller's ExecutionContext. The sequential output worker awaits the same disposal task after its actual writer call settles, then releases its owned file resource. Borrowed console resources retain their existing ownership rule.

Scheduling cancellation prevents future dispatch; an already-dispatched callback can still complete. Closed/faulted queues ignore that callback. Completion waits for the timer's asynchronous callback join. Blocked writer I/O also remains pending, even if scheduling has already been cancelled. Cancellation does not prove that an OS write ended, and a timer callback cannot restart the output.

A periodic writer-flush exception uses the existing terminal FlushFailed category and system UTC fault observation. Previously written records remain written; queued unstarted records become abandoned. The file worker faults and the console can keep accepting/writing/flushing. A concurrent admission fault while an actual flush is blocked preserves its unresolved registration until that real I/O settles.

## Dependency ordering

The original T03 mentions the shared stop operation, but the original plan implements the public bounded StopAsync/DisposeAsync operation in the dependent WP05-S10 story. T03 implements and qualifies its timer lifetime foundation against the actual existing atomic runtime closure and one shared OutputQueues.Completion task. It does not substitute a fake stop result, expose a public placeholder or claim bounded public shutdown. S10 must join this same worker/timer cleanup when implementing the public operation, original monotonic deadline and eventual safe ownership release. That integration remains required before lifecycle completion or publication.
