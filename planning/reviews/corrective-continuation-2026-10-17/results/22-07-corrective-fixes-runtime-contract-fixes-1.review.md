# Review: 07-corrective-fixes-runtime-contract-fixes-1 (static, read-only)

This is a static source review. No tests, builds or tools were run. It covers only the assigned files and the bounded context supplied. It is not a whole-change PASS and not release qualification.

## Overlapping fixes checked (R1/R2)

These fixes are consistent with the stated intent within the supplied source:

- **Per-caller registration snapshots.** `RegisterIncomingBoundary` captures invalid, truncated, `QueueFull` and caller filtered/unavailable counts under `admissionGate`.
- **Coalesced flushes.** They share one `IncomingFlushBoundary` but keep their own snapshot through `Capture(boundary, callerQueueFull)`.
- **Worker-effect freezing.** `RegisterFlushBoundaryAt` runs under the same gate, because the queue gate is `admissionGate`. It subtracts the caller-side counts and returns the worker invalid/truncated counts at prefix resolution. Over-counting between overlapping registrations and resolution cancels correctly.
- **Envelope-too-large disposition.** It is now deferred into the commit action, so abandoned preparation adds no invalid count.
- **Locking claim.** `Invalid()` now increments under the gate, so the updated comment in `CaptureAccounting` ("Admission, disposition and loss-counter changes share this gate") is accurate.
- **Stop-race truncation.** Truncation from a capture that loses the race to Stop is counted live but excluded from the stop result. This matches `threadSafety`.
- **R2 catalog addition.** `IncomingQueueFull` and `RecursiveProcessing` are present in both `RuntimeSurface.cs` and `runtime-surface.json`.
- **New tests.** I traced all four new `IncomingRuntimeTests` by hand. Their assertions follow from the code shown.

## Findings

### F-1 (Medium): A timed-out incoming flush drops registration-time per-output rejections, so `LossObserved` can be false despite confirmed loss

**Location:** `CoreLogRuntime.Incoming.cs`, `CapturePipelineResult` (~L100–121), lines `var entries = downstream?.Entries.ToArray() ?? [];` and the use of `boundary.Rejections[...]`. Also `LogFlushResult` constructor, `LossObserved`.

**Failure:**
- When the incoming prefix is unresolved at the flush deadline, `downstream` is null and `Outputs` is empty.
- The caller's own registration facts in `boundary.Rejections` (`CallerUnavailable` / `CallerFiltered`) are therefore never reported.
- Invalid, truncated and incoming `QueueFull` registration facts are preserved, but output-level facts are not.

**Reproduction:**
1. Register writers A (Information) and B (Error).
2. Fault A.
3. Call `logger.Info(...)`. This takes the early `Rejected` path and sets `CallerUnavailable(A)=1`.
4. Call `logger.Error(...)`, which is accepted while the worker is blocked (for example by a slow interceptor).
5. Call `FlushAsync(50ms)`.

**Result:** `TimedOut`, `Outputs` empty, invalid/truncated/`QueueFull` all 0, so `LossObserved == false`. The contract's `lossObserved` rule says registration output unavailable counts confirm loss, and `corrections.md` claims each caller "retains its own … early output rejection facts".

**Fix:** When `downstream` is null, include `boundary.Rejections` in loss detection, for example by passing a registration-loss flag into `LogFlushResult`. This avoids inventing downstream entries. Add a regression test next to `FlushTimeoutKeepsIncomingWorkAndCancellationDoesNotCancelDrain`.

### F-2 (Low): The contract and XML docs omit the callback-flow behaviour on the public surface

**Location:** `contracts/logging/v1/runtime-contract.json`, the `arguments.IsEnabled`, `FlushAsync`, `StopAsync` and `DisposeAsync` entries and `writeOrder`. Also `CoreLogRuntime.cs` XML docs for `FlushAsync`/`StopAsync`/`DisposeAsync`.

**Failure:** The code does all of the following, but the contract does not say so:
- `StopAsync`/`FlushAsync` throw `InvalidOperationException` whenever `EventProcessing.IsCallbackActive`. This check is static across runtimes and inherited by child tasks.
- `FlushAsync` performs that check before timeout validation, contradicting "Validate positive finite timeout before …".
- `DisposeAsync` therefore throws synchronously from such flows, contrary to the unconditional "Repeated disposal safe".
- `IsEnabled` returns `false` in callback flows. This is not in the contract's false-conditions list.
- The recursion check precedes "Admission closed" in `Write`, but `writeOrder` omits it.

Only `Write` was updated in this delta.

**Related gap (same place):** `Create(writers)` and `Create(processing, …)` can throw undocumented exceptions:
- `ArgumentException` for null or duplicate registrations or a null interceptor.
- `ArgumentNullException` for null `Interceptors`.

The contract lists only null-argument `ArgumentNullException`.

### F-3 (Low): The safe error-code catalog still omits codes the runtime visibly emits

**Location:** `runtime-surface.json` `errorCodes` and `RuntimeSurface.cs` `LogErrorCodes`, compared with the emitting code.

**Emitted codes missing from the catalog:**
- `ConsoleInitializationFailed` (`QueuedRuntimeFactory.AcquireConsoleWriter`).
- `FileFlushTimerInitializationFailed`.
- `WorkerInitializationFailed`, raised with `OutputName "incoming"`, which is not an output name despite the contract's "safe OutputName".
- `ShutdownTimerCleanupFailed`, which surfaces through `RecordCleanupFault`.

**Failure:** Consumers validating against the catalog will see unlisted codes. Either the catalog is incomplete or these codes are unpublished.

### F-4 (Low): Detached callback child tasks are counted as worker invalidity for unrelated later prefixes

**Location:** `CoreLogRuntime.cs`, `Invalid()`, which uses `processing?.IsOwnCallbackActive`. Related: `EventProcessing.Active` flows to child tasks by design.

**Failure:**
- An interceptor's `Task.Run` captures `Active = this`.
- A later `Write` from that task, after `Prepare` has returned and possibly after item N's boundary resolved, increments `workerInvalid`.
- That count is then folded into whatever later prefix resolves next. A flush registered before that write still reports it as "worker processing".
- This contradicts "Later unrelated caller activity never replaces the registration facts".

**Fix:** Count `workerInvalid` only while the worker's synchronous `Prepare` is active, for example with a worker-side depth/flag checked at the increment. Otherwise treat the attempt as caller-side.

### F-5 (Low): Public enum docs still describe outcomes the public runtime never returns

**Location:** `LogWriteOutcome.cs`, `PartiallyAccepted`, and `LogOutputWriteStatus.Accepted` / `QueueFull`.

**Failure:** Public incoming `Write` returns only these combinations:
- `Accepted`, `Rejected`, `Filtered`, `Stopped` or `InvalidEvent` at the outcome level.
- Only `Filtered`, `Faulted` or `Stopped` per-output entries.

`Accepted` was clarified as public versus internal, but `PartiallyAccepted` and these output statuses still read as reachable public results.

## Questions (unresolvable from the supplied context)

1. **Reserved writer names.** Do `LogWriterRegistration` or `OutputQueues` reject custom names `console`, `file` or `incoming`? If not, the new `outputs.Single(...)` in `RegisterFlushBoundaryAt` would throw inside the worker's locked `ResolveReady`, faulting the incoming worker. Separately, `outputs.ToDictionary(...)` in `RegisterIncomingBoundary` would make `FlushAsync`/`StopAsync` throw `ArgumentException` when a built-in output with the same name is enabled.
2. **CoreConfigurationException claim.** `CreateConfiguredQueued`, `ValidateSettings` and `ValidateShutdownTimeout` throw `ArgumentOutOfRangeException`/`ArgumentException`. The `Create` docs and the contract promise `CoreConfigurationException` for invalid settings. Does `LoggingOptions.Prepare` (not supplied) validate everything first?
3. **`.gitattributes` and pinned JSON.** Were `planning/reviews/corrective-2026-10-16/*.json` first committed under the earlier `*.json text eol=lf` rule? If so, CRLF content may already be normalized in the blobs and mismatch the recorded hashes. The pattern also covers only that directory's top level.
4. **Checkpoint wording.** "Windows/Ubuntu file selections passed 152/153 cases" reads as a pass ratio. `corrections.md` shows Windows 152 and Ubuntu 153, all passing. The checkpoint wording should be clarified.

## Limits

- **Tested versus reviewed source.** The tested source `7a606607` versus the current head `396e87d6` and the pinned working-tree inputs could not be verified. I cannot confirm that the supplied Core files are byte-identical to the tested ones.
- **JSON byte identity.** Both JSON files are shown minified with `\u003E` escapes, while their deltas are pretty-printed. The shown text is probably a re-serialization, so byte identity was not checked.
- **Not supplied:**
  - Required-case binding files for the four new tests.
  - `ProcessingPolicyTests` and `IncomingEventQueueTests`.
  - `LogErrorCodes`, `LogStopResult`, `OutputEventQueue`, `OutputQueues`, `LoggingOptions.Prepare`, `CoreLogger`.
  - The diagnostics IL guard.
  - Raw logs and reports.
- **Fixes without supplied regression tests.** No supplied test covers the envelope-deferral fix, own-runtime recursion attribution, or worker-side truncation from processing/rendering in incoming mode. These may exist in the unsupplied classes.
- **Not covered by this bundle.** Deleted-Python-reader parity, the file-recovery claims and the Type Manager corrections (F1–F6, T1–T4) are outside this bundle and were not reviewed.