# Deadline drain and cancellation

WP05-S10-T02 extends the existing shared StopAsync operation. No second deadline, output worker, replacement write or retry is introduced. Public creation, DisposeAsync and complete cleanup-failure coordination remain following lifecycle work.

Closure already registers the final flush controls, closes admission and cancels periodic file scheduling. Until the original monotonic deadline, the existing workers drain admitted records and flush each output. At expiry, the shared admission gate removes only records that have not started. The active record remains pending until its actual write completes or fails. Both outputs are abandoned before one immutable observation is captured.

A flush registration whose covered work is entirely abandoned resolves as unavailable/Failed: its accepted boundary can never reach writtenCount. A registration behind an active write may remain retained until that write settles. An already-started flush stays unresolved until actual I/O settles. After any already-started I/O settles, the worker resolves every remaining unstarted flush as unavailable without calling a writer. No new flush starts after the stop deadline.

Configured console and file writes/flushes receive an output-owned CancellationToken. After the expiry observation is frozen, the runtime requests cancellation asynchronously, outside admission synchronization. A slow or throwing stream cancellation callback cannot extend the stop wait. Cancellation is a request, never proof that an OS write stopped. No owned resource is disposed beneath active I/O.

A dequeued record counts as started at the worker reservation, even if cancellation precedes its first OS I/O; only still-queued records are abandoned. Later cancellation/cleanup diagnostic refinements remain T04.

At expiry, the immutable stop result reports TimedOut even if abandoning the queued tail resolves every covered output disposition. Succeeded remains false and LossObserved records abandoned work. The current shared budget includes worker completion and cleanup as well as record/flush progress. An active write stays Pending in the cached observation even when cooperative cancellation later records a live WriteFailed. An active flush can similarly become a later FlushFailed without reclassifying already-written records.

The existing worker cleanup joins cancellation callbacks before disposing their token source. Full primary-error preservation and cleanup-fault diagnostics remain T04; this task does not claim those existing cleanup limitations are solved. T03 retains caller-wait cancellation/disposal integration, and T05 retains the broader process races, physical blocked-file ownership and abrupt-termination qualification.

Eight barrier-driven cases exercise actual Core. Normal dual output uses the real file writer and a controlled borrowed console stream. Held/cooperative/blocked-callback modes use a controlled Stream passed through the real configured console writer; queued-only uses the existing internal worker-launch seam. A separate unit case exercises the actual file queue's cancellation token and owned-resource lifetime. These controls do not claim a real OS file write was forced to block. Independent Windows/Ubuntu readers validate exact DLL identities, counters, complete JSONL bytes and deliberate report corruptions.

See [case catalog](../../../conformance/logging/stop-drain-cases.json) and [shutdown progress](../../../planning/logging/shutdown-implementation-notes.md).

The 16 October native-reader migration retains all 32 start/drain/join scenarios and 74 former corruption controls in C#. It reconciles the old Completed-on-expiry and early-registration-removal assertions with current runtime behavior, checks original bounded records and eventual dispositions, and waits for cooperative cancellation before releasing held test I/O. See the [migration receipt](../../../planning/evidence/corrective-stop-readers-migration-2026-10-16.md) for native Windows/Ubuntu scope and remaining qualification.
