# CISLIB static review: subpass 03-provider-lifecycle-queues-diagnostics-2

This review covers only the six assigned baseline-deleted Python readers and the bounded context supplied. Nothing was executed, and no files outside the bundle were read. This is not a whole-change or release PASS. The native C# replacements for the six deleted readers were not supplied, so parity is not established.

## Confirmed finding

**F-1 (Low; truthful public documentation): `LogOutputDiagnostics.QueueFull` says "incoming", but the value comes from the output stage.**
- **Location:** `implementations/csharp/src/CIS.Core/Logging/LogOutputDiagnostics.cs`, about line 41 (the summary text) and line 15 (`QueueFull = counts.QueueFull`).
- **Where the value comes from:** `OutputEventQueue.CaptureAccounting` sets `OutputAccounting.QueueFull` from `queueFullCount`. That counter is only incremented in `TryAdmit` when this output's own `channel.Writer.TryWrite` fails. Incoming queue-full is a different counter (`IncomingEventQueue.queueFull`) and is exposed separately through `CoreLogDiagnostics.Incoming`.
- **Concrete failure:** a consumer who reads `Outputs[i].QueueFull` as incoming rejections misattributes loss. In the public incoming pipeline, a real incoming queue-full never appears in that per-output counter.
- **Why this holds without `CoreLogRuntime`:** suppose the runtime instead substituted the incoming count when building `OutputAccounting`. Then `AdmissionAttempts = Accepted + QueueFull + Unavailable` (in `OutputAccounting`) would mix incoming and output-stage events. That contradicts `counterRules` ("Per-output admissionAttempts counts interested accepted/queue-full/unavailable attempts"). Either way, the public text and the data cannot both be correct.
- **Fix:** describe it as "Cumulative interested output-queue-full rejections for this output, saturating at Int64.MaxValue". This overlaps the R2 XML-documentation fix, which distinguishes public incoming acceptance from output-stage qualification.

## Questions requiring missing context (not confirmed)

1. **A cross-output cleanup fault may strand queued records.**
   - `OutputQueues.RecordCleanupFault` applies `OutputEventQueue.RecordCleanupFault` to every output. That method sets `faulted = true` but, unlike `Fault()`, does not complete or drain the channel.
   - If this runs while another output still has queued records (stop requested, deadline not yet expired), that worker's next loop hits `if (faulted) { workerExiting = true; return; }`. Its queued records then stay `Pending`, with no writer, until the runtime's deadline `AbandonUnstarted`.
   - Result: stop/flush becomes `TimedOut` with `Abandoned` records instead of draining them.
   - The method comment says "only after queue closure or worker exit", but closure is not the same as drained. I need the `CoreLogRuntime` call site for the "shutdown-timer cleanup fault" to confirm or rule this out.
2. **Registered writer names may collide with built-in names.**
   - `CreateConfiguredQueued` checks registration names only against each other, not against `"console"`/`"file"`.
   - If a registration is named `"file"` while file output is enabled, `OutputQueues.Add` throws `ArgumentException` instead of `CoreLogInitializationException`.
   - If it is named `"console"` while console output is disabled, `queues.Console` (`FirstOrDefault` by name) returns the custom writer's queue to `CoreLogRuntime` as the console queue.
   - Does `LogWriterRegistration` reject reserved names? It was not supplied.
3. **Initial output state.** `OutputEventQueue.state` is never explicitly initialized. Is `LogOutputState.Running == 0`?
4. **Deadline-cancelled in-flight writes.** These go through the `ProcessAsync` catch and become `Failed`, with `LastFaultCode` set to "WriteFailed" or a writer-specific code. Is that the intended published classification for a deadline cancellation? The literal expectations for `STATE-CANCEL-FILE` were not supplied.
5. **Suppression counters and the R1 claim.**
   - `RecordSuppressed` and `RecordDuplicateKeyEviction` take no gate.
   - If `EventProcessing.prepare` calls them outside the gate, a preparation later discarded at expiry (`if (abandoned) return;` before commit) still increments suppression counts. Those counts then appear in boundaries frozen by `AbandonCore`.
   - This would matter for the R1 "frozen through covered prefix" claim only if suppression counts as a reported effect. `EventProcessing` was not supplied.

## Fix checks where this bundle overlaps

- **R1 incoming snapshots:** consistent with the supplied `IncomingEventQueue`.
  - Each caller's `callerQueueFull` overrides the shared frozen value.
  - A boundary resolves exactly when `settled == target`, which freezes writer rejections through the covered prefix.
  - Commit is skipped after expiry, and a failed preparation is counted under the gate.
- **R1 follow-up (readonly construction):** `Capture`/`CaptureAccounting` use direct construction with no `with` copies.
- **Neither:** the supplied source does not show their native regression tests or bindings.

## Parity limits for the deleted readers

The native replacements must be supplied before parity can be judged. They need to preserve, at minimum:
- the exact-runtime child invocation (`--fx-version 10.0.12 --roll-forward Disable`, with an invalid `CORE_LOG_QUEUE_CAPACITY`);
- empty stderr and an empty owned working directory;
- pins for the host, adjacent `CIS.Core.dll` and catalog hashes;
- case-ID sets: 11, 11, 12, 15, 12 per task, and 5 modes matching `runtime-create-cases.json`.

Comparator behaviour to preserve:
- canonical Int64 strings;
- the accepted/rejected/attempts identities;
- monotonicity that excludes `pending`/`inFlight`;
- comparisons that distinguish bools from ints;
- byte-exact framing (no BOM, one LF, no CR, ≤65,536 bytes);
- ticket contiguity, uniqueness and absence on rejection;
- fault-time interval and retention;
- `privateExceptionAccesses == 0`.

Control counts to preserve: 9, 10, 7, 9, 13/14 and 13. Only `verify-runtime-create` pinned the expected rejection reason. The others accepted any `AssertionError`/`KeyError`/`ValueError`, so an unpinned native port inherits that weakness rather than introducing it.

Historical weaknesses a replacement should not rely on:
- `verify_transport` uses `zip(bindings, results)` without checking that the lengths match, which matters on the burst path.
- `verify-runtime-create` uses a 16,384-byte record bound and depends on the unsupplied `verify-flush-results.py`.

## Missing context

- Native C# replacements and their required-case bindings.
- `CoreLogRuntime` (cleanup-fault, `Abandon`, `StartIncoming`, diagnostics construction).
- `LogWriterRegistration`, `LogOutputState`, `EventProcessing`, `FileOutputWriter`, `ConsoleOutputWriter`, `OutputFlushRegistration`, `ElapsedBudget`.
- Fixture catalogs, `verify-flush-results.py`, `runtime-create-cases.json`.
- `planning/decisions.md`, plan amendments and raw execution logs.