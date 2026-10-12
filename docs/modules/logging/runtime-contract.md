# Logging admission, results and runtime ownership

WP02-S03 defines the unpublished runtime contract using the source guide sections 10–13 and the [current amendments](../../../planning/logging/plan-amendments.md). [Portable semantics](../../../contracts/logging/v1/runtime-contract.json), [C# signatures](../../../implementations/csharp/verification/contracts/startup/runtime-surface.json) and compiled consumers are the specification. They supply no shipping logging implementation. Startup resolution remains WP03; capture, workers, outputs and provider behavior remain WP04–WP06.

## API and ownership

`CoreLogRuntime.Create()` is the current compile-only draft: the future runtime reads caller-populated values, owns its prepared settings and initializes configured outputs and returns an application-owned runtime. `CreateLogger(component: "Orders")` returns a logger borrowing it. `BeginScope(context: data)` returns a copied, bounded scope owned by that runtime and logical asynchronous flow. The creator alone disposes the handle in LIFO order. A non-head active scope throws InvalidOperationException without changing the head; an absent scope whose handle has never completed disposal in any flow also throws unchanged. Repeated disposal in the creating flow after its successful pop is harmless. Child tasks inherit normal execution context, but must not receive or dispose another concurrent flow's handle. These state guards detect some misuse; other misuse is unspecified and universal cross-flow detection is not promised. Scope depth remains limited to 32. The [scope guidance](operation-scopes.md) records the user-selected ownership rule and context-lifetime limits.

`CoreLogger.IsEnabled(level: ...)` checks running output interest, including per-output thresholds. It does not reserve queue capacity. `Write(level: ..., data: ...)` and `WriteLazy(level: ..., factory: ...)` are distinct methods; method severity is the only authority. There is no factory overload of Write or positional settings overload of Create. The full method/parameter/property types live in runtime-surface.json.

`FlushAsync(timeout: ..., cancellationToken: ...)` observes fixed output boundaries. `StopAsync(cancellationToken: ...)` starts or joins shared shutdown. `DisposeAsync()` implements `IAsyncDisposable` and joins that same operation; there is no synchronous runtime Dispose. `GetDiagnostics()` remains available after stop/disposal and returns owned immutable observations. Loggers never independently close the runtime.

`CIS.Logging.MicrosoftLogger.CoreLoggerProvider(runtime: runtime)` implements the approved Microsoft `ILoggerProvider` and `ISupportExternalScope` interfaces inside CIS.Core. It borrows the runtime. Provider disposal releases its own registrations/scopes; the application still owns stopping and disposing the runtime. The initial API has no owning-mode overload. The compile-only fixture uses the exact approved/restored Microsoft abstraction DLL without adding another package dependency.

Runtime creation failure throws `CoreLogInitializationException` with safe OutputName/ErrorCode; rollback closes only newly owned resources and preserves existing files. File handles/workers belong to the runtime. Console writers are borrowed and never disposed by it. Normal logging I/O failures become output state/results, without escaping into business code or recursively logging themselves. Error codes contain categories, never raw payload, secrets or paths.

Runtime/logger methods support concurrent calls; the future runtime must own fixed settings after creation; the passive option store itself is mutable. Caller factories/content traversal run outside admission synchronization. Recheck closure after capture before the synchronized nonblocking fan-out, so Stop cannot split that fan-out. If shutdown wins, return Stopped with no accepted ticket; already captured truncation remains visible. This race does not make early Stopped calls capture content or make Stop wait for caller factories. Results are immutable; scope handles remain tied to their owning logical flow. The lifecycle catalog preserves this pending race test.

## Admission and truncation

Evaluate required argument null checks, then defined method-level enum values, before checking state. Null data/factory raises ArgumentNullException even after stop; undefined levels raise ArgumentOutOfRangeException. Then evaluate closed admission → Stopped, None/no configured interest → Filtered, all interested outputs faulted → Rejected, and only then capture and independent output admission. None after closure returns Stopped. These early decisions do not traverse event content or invoke factories.

An interested lazy factory executes at most once, even with full queues. Its own exception propagates unchanged; null return yields InvalidEvent with InvalidFactoryResult. Invalid captured content yields one global invalid-event increment, a safe ErrorCode and no per-output entries. This does not advance output tickets. Oversized supported payload instead produces a bounded marked record under the [event truncation contract](event-contract.md).

`LogWriteResult.IsTruncated` reports actual capture omission, including omission already recorded by copied values/scopes. `TruncatedEventCount` increases once per valid captured truncated event, independently of eventual output admission. It is not an invalid-event count. Filtering, early rejection and caller-factory exceptions do not increment it. A truncated event can return Accepted, PartiallyAccepted or Rejected according to its output attempts. The wire entry still carries `truncated:true`.

Enabled outputs appear in console-then-file order; disabled outputs are omitted. Each output reports Accepted, Filtered, QueueFull, Faulted or Stopped. AcceptedSequence is a positive, contiguous per-output ticket only on successful admission. Empty boundaries are zero. Queues reject the incoming event for that output when full, without evicting older accepted work or retrying. Other outputs continue independently.

| Write outcome | Meaning |
| --- | --- |
| Accepted | Every interested output admitted the captured event. |
| PartiallyAccepted | At least one interested output admitted it and at least one rejected it. |
| Rejected | No interested output admitted it. |
| Filtered | No output was interested; no capture. |
| Stopped | Admission was already closed; no capture. |
| InvalidEvent | Capture could not represent valid content; ErrorCode is set and output results are empty. |

Only InvalidEvent has ErrorCode in normal write results. The enums retain the guide's explicit integer values; additional diagnostic/writer-flush states are prepublication maintainer choices in the signature catalog.

## Flush and stop observations

Flush/stop results retain BoundaryCapturedAtUtc, final ObservedAtUtc, initially captured InvalidEventCount/TruncatedEventCount, and each enabled output's accepted boundary and initial cumulative rejection snapshot. Later admissions/rejections cannot change those observations. UTC may move backwards after a clock correction; elapsed deadlines use monotonic time instead.

For each covered boundary, `AcceptedSequenceBoundary = Written + Failed + Abandoned + Pending`. Counts cover only accepted work through that boundary, rather than mixing later admissions into the result. WriterFlushStatus distinguishes NotRequested, Pending, Succeeded and Failed; WriterFlushSucceeded is true only for Succeeded. Unavailable/faulted writer completion reports Failed instead of inventing a successful flush.

| Result property | Meaning |
| --- | --- |
| Completion=Completed | No covered work or writer flush remains unresolved; terminal failure can still exist. |
| Completion=TimedOut | Covered work or writer flush remains unresolved at the deadline observation. |
| Succeeded | All covered work was written and every writer buffer flush succeeded. |
| LossObserved | Captured cumulative observations include invalid/truncated capture, queue-full/unavailable rejection, covered failure/abandonment or a failed flush. |

Completed with Succeeded=false is a completed failure observation. Succeeded=true with LossObserved=true means admitted work drained successfully but earlier events or payload were lost. Filtered events are intentional and do not count as loss. Pending work alone indicates uncertainty, not confirmed loss. No enabled outputs yields Completed/Succeeded, while captured global loss can still be visible.

Diagnostics distinguish runtime Running/Stopping/Stopped and per-output Running/Faulted/Stopping/Stopped. Each output records accepted tickets, filtered and interested admission attempts, rejection/failure/abandonment counts, pending gauge and safe last fault category/time. `AdmissionAttempts = Accepted + QueueFull + Unavailable`; `Accepted = Written + Failed + Abandoned + Pending`. Last fault information survives shutdown. Counters are nonnegative Int64 and must never wrap. The implemented global invalid/truncated loss counters saturate at Int64.MaxValue, a lower bound once the total is unrepresentable; output-owned ticket/counter limits remain worker implementation work. Portable result fixtures encode them as exact decimal strings to avoid transport precision loss; this does not change event JSON.

## Coordination, cancellation and disposal

Each output has one bounded event queue and one sequential worker. Successful admission orders each output independently. There is no cross-output total order. Flush registers nondroppable coordination under the same synchronization that captures accepted boundaries. Writes and flushes are serialized; later admissions cannot extend the target.

Same-boundary flush coordination coalesces: at most one retained control per outstanding accepted boundary plus the completed boundary currently being flushed. Caller waits preserve their own boundary/counters/deadline, detach on completion/cancellation/timeout and do not accumulate retained cancelled waiters. Queue saturation cannot drop a flush request. A periodic file-buffer flush is scheduled even after a single event followed by idle traffic.

Flush validates positive finite timeout before registration. Precancelled caller tokens still register coordination, then cancel that caller's wait with OperationCanceledException. Other workers/waiters continue. The first Stop request closes admission and starts one configured overall deadline even when its caller token was already cancelled. Subsequent Stop/Dispose requests join it and cannot restart the deadline or reopen admission.

At the shutdown deadline, abandon only queued work that has not started. Request cancellation of in-flight I/O, retaining unresolved work as Pending. Cancellation does not prove a writer stopped. Do not dispose underneath unresolved I/O, launch replacement writes or automatically recover faulted outputs. Close safely owned resources when outstanding I/O resolves. Store one immutable stop result in LastStopResult; later live diagnostics may change without rewriting that result. Repeated Dispose is safe, including after the original deadline.

Accepted means enqueued, Written means complete encoded record handed to the writer, and Flushed means its userspace buffer flush succeeded. These results promise neither fsync/crash durability nor collector acknowledgement/exactly-once delivery. Abrupt termination can lose buffered records.

## Qualification scope

Forty-five admission examples execute against a pure development specification evaluator. Twenty-eight result examples validate fixed observations, accounting and truthfulness, including deliberately invalid projections. Eighteen lifecycle/provider/worker expectations remain explicitly unexecuted. Reference-only consumers and compiler rejection controls check API shape. These checks define WP02-S03; real concurrent admission, cancellation, truncation, disposal and I/O behavior must be demonstrated with the shipping implementation later.

WP04-S05-T01 now implements the [runtime-bound logger foundation](logger-foundation.md), including real capture/encoding and immutable admission results behind internal output composition. The remaining full runtime reference above still defines future public output creation, lifecycle and provider behavior.

WP04-S05-T02 qualifies the [exact argument/state/capture/admission order](logger-decision-order.md) in that real foundation, including mixed faults and closure during caller work. Full S05 and public lifecycle/output/provider integration remain later tasks.

WP04-S05-T03 qualifies [once-only lazy capture, global rejection and output-owned outcomes/tickets](logger-capture-outcomes.md), and prevents global loss counts wrapping. The complete outcome table and S05 acceptance mapping remain T04.
