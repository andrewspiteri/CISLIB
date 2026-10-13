# Registered flush boundary capture

WP05-S08-T01 implements the internal registration needed by the later public flush operation. One short admission lock captures the enabled outputs' last accepted sequence, filtered/queue-full/unavailable counts, and global invalid/truncated counts. Each returned boundary owns its initial scalar facts; later admissions, rejections, writes and faults cannot rewrite them. Console and file can have different targets because their thresholds and queues are independent.

The lock is the same coordination used by admission and each output worker. Registration happens before later admission can overtake that target. Invalid events increment an atomic global counter independently; the final atomic invalid-count read supplies the observation point while the other captured facts remain stable under the gate. BoundaryCapturedAtUtc samples the system UTC clock inside that lock, following the existing diagnostic observation policy. It does not invoke a supplied event clock or caller callback there. UTC is a label; elapsed deadlines belong to T03.

## Queue and writer responsibilities

OutputQueues owns an in-memory event queue and sequential worker for each enabled destination. The runtime's ConsoleOutputWriter/FileOutputWriter references prepare the selected representation and enforce entry truncation before admission. Prepare performs no output I/O. The worker invokes WriteAsync and FlushAsync outside the admission lock. Startup initialization/recovery happens before the runtime is published.

Flush controls live separately from the bounded event channel. A saturated channel cannot reject or discard a registered flush. Outstanding requests for the same accepted sequence share one control while keeping their own initial scalar observations. The worker finishes the covered writes and checks the earliest ready flush before starting a later record. It consumes a single coalesced wake signal rather than allocating an event-channel marker or a signal per producer.

This minimum ordering/coalescing foundation makes T01 registration concrete. T02 still owns caller-wait attachment/detachment, full coordination-bound qualification and lifecycle integration; T01 does not claim completion of that task.

## Completion and faults

WriterFlushCompletion reports the actual userspace buffer flush status. It is not the public drain/result observation: covered pending writes, terminal/loss accounting, timeout and cancellation still need T02/T03 composition. An unavailable, faulted or exiting worker resolves an unstarted flush false without retaining a control. A trusted internal test composition without a flush callback also reports false; it cannot invent confirmation.

An already-started flush remains pending until its actual I/O settles even if admission exhausts in the meantime. If that flush then fails, FlushFailed replaces the earlier AdmissionExhausted fault code, following the existing last actual I/O failure policy; the failed-event count remains unchanged. An actual flush failure faults the destination with the safe FlushFailed code, abandons unstarted queued records and does not count a record-write failure. Original exceptions remain in the internal worker task; safe diagnostics expose the fixed code. Worker exit is published before owned-resource cleanup so registrations made during cleanup cannot be stranded. All continuations are asynchronous and all writer/cleanup I/O runs outside the admission gate.

Configured console/file flush callbacks call Stream.FlushAsync/FileStream.FlushAsync. This is userspace flushing; there is no fsync, power-loss durability, collector acknowledgment or cancellation of an already-started stream operation.

## Executed development coverage

Fifteen focused unit cases and seven child-process integration modes exercise empty and different-output targets, frozen initial facts, saturated event slots, coalescing, concurrent producers, write/flush faults, registration during cleanup and admission exhaustion during actual flush. Barrier-controlled writers establish order directly. Both in-process and child-process tests apply the same literal expected initial targets/counters and writer order. The independent native reader additionally checks literal final counters and admission outcomes, comparing initial targets with larger final totals (for example, console target 3 before final acceptance 5). Repeated serialization is only a serialization consistency check. The physical mode reads two actual JSONL records after the registered file flush, then confirms released ownership.

The independent native reader runs all seven modes on Windows and Ubuntu with exact .NET 10.0.12 and final frozen Core/fixture hashes. It validates literal initial/final counters, record bytes, actual producer PID, whole-record framing, truncation, fixed-target writer order, physical membership and released controls. Twelve scalar/byte corruption controls require their specific rejection reason. Previous physical startup/rotation/retention/recovery/group lanes are rerun because the worker loop changed. The task receipt contains the executed totals and source/evidence bindings.

Public FlushAsync, caller cancellation/detachment, positive finite timeout validation, monotonic deadline behavior, immutable public results and the full S08 acceptance criteria remain T02–T04. No public placeholder, new package or general I/O framework is introduced. The 41 Planned release rows and supplied source plans remain unchanged.
