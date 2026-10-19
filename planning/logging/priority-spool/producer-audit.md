# Producer and ownership audit

Scope: current rotating frontend, 19 October 2026. This is a source/control-flow review with native held-gate, probe, ownership and pause evidence. It is not a hard wall-clock scheduling guarantee or complete compiler-graph proof. Final combined qualification is recorded in the feature README.

## Ordinary public paths

Public `Create`/`TryCreate` composition enables incoming processing; the direct internal output-only qualification factories remain a separate historical path.

| Path | Reviewed behavior |
| --- | --- |
| CoreLogger direct/lazy/convenience and provider entry | Existing argument/threshold/state checks and owned capture remain upstream. Legacy callbacks stay on the caller, at most once when enabled, with recoverable failures contained. No deferred closure enters the ring. |
| CoreLogRuntime.Write / IsEnabled | Public incoming branch returns into `WriteIncoming`; it does not traverse the legacy synchronous admission branch. Backend fault interest is a volatile bool read. `IsAdmissionClosed` also observes a failed/closed frontend. |
| WriteIncoming early paths | Stopped, disabled, all-faulted, invalid and recursive paths use immutable startup output metadata and fixed-cost atomic counters. They do not call backend RecordFiltered/RecordUnavailable or acquire the admission/control gate. |
| EventProjection / scopes / supplied formatter | Owned capture remains outside enqueue. Supplied callback/enumerator/clock execution is upstream work, not hidden in the scan. Full-caller measurements include ordinary capture; no arbitrary callback is supplied to those measurements. |
| IncomingEventQueue.TryAdmit | Checks supplied facts/level/closure, advances one correlation counter, creates a distinct immutable occurrence and invokes the ring. No clock callback, task, notification, control registration or shared gate. Correlation rollover does not reject admission. |
| IncomingRingBuffer.TryPublish | State read, publisher increment, state recheck, bounded selection, one event-reference exchange, fixed admission/removal bookkeeping and publisher decrement in finally. No retry/helping/owner-reservation path. |
| AtomicLogCounter.Increment | One Interlocked increment and at most one sticky saturation write; no CAS loop. Saturation never rejects or blocks an event. |

The only producer search loop is `SelectSlot`: precomputed K iterations at most, starting slot included, immediate return on observed null, local wrap without revisiting a position. Capacity is at most 65,536 and percentage at most 100, so the integer ceiling calculation does not overflow. Severity and timestamp comparisons read immutable event facts. A changed candidate never restarts selection.

The event-reference exchange is publication's ownership transition. Its actual returned reference determines eviction and actual severity. Consumer claim uses reference-conditional CAS; reference identity is distinct even when captured business data is reused. Public queue composition never reinserts a removed occurrence identity.

Normal unsaturated ring publication performs six logical producer RMW operations for a free final slot, seven for replacement: publisher increment/decrement, cursor increment, reference exchange, admission increment, then waiting increment or two eviction increments. The queue adds its ticket increment; caller capture/result construction are separate. Measurement reports derive these logical counts from actual admitted/evicted totals and these source sites. They do not report hardware retry/instruction counters. Probe maxima and actual ownership/removal counts are observed by native tests.

## Consumer and controls

The sole consumer captures/sorts its C-entry index outside control coordination, then serializes only each fixed claim/disposition metadata update with expiry/snapshot observation. Backend preparation, commit callbacks, output-boundary capture, sorting, awaits and capacity-sized cleanup passes run outside that frontend gate. Backend queues retain their own synchronization and I/O ownership.

Stop closes enrollment before waiting for publisher zero. A producer paused before registration sees closure at its recheck and never accesses slots; a producer past recheck remains counted through final bookkeeping. A final fresh pass after zero establishes emptiness. Native public factory/capture races cover pre-enrollment closure, and staged real-ring races cover pre-exchange and post-exchange publisher pauses. No producer is required to help another participant.

The final control's ready signal proves that this drain frontier completed. A late registration increment from an attempt paused before the closure recheck can briefly appear in the raw publisher gauge after the frontier, but cannot publish. Native `RejectedEnrollmentAfterFinalPassCannotTurnCompletedStopIntoTimeout` holds the real consumer at final snapshot capture, stages that actual atomic counter increment and checks completed shutdown without concealing the diagnostic gauge. This adds no producer step or coordination.

Expiry classifies only actual waiting removals as Abandoned. An already-claimed event remains Consumed even if backend preparation is blocked. A late enrolled publication is abandoned by the live cleanup owner. The minimal output-stage integration guard rejects a late handoff after observed expiry even if its timestamp sample predates expiry. It does not move policy/writer work onto callers or alter ordinary output overflow.

One current batch, one pending group and one shared stop control are the retained control roots. Live callers retain only request/budget/observation metadata, including per-output scalar rejection facts. There is one ring array and one reusable index, with no per-caller payload target array, participant registry, overflow list or eviction history. Current/pending groups retire after completion or loss of all waiters.

## Evidence boundaries

Native evidence includes: held backend/control gates while ordinary calls complete; exact-reference replacement/claim races with 6,000 competing publications; K/probe and one-publication accounting; timestamp/cursor/ticket/saturation boundaries; stale index ordering; shared cancellation; 100 retired expired requests; late stop publication; active backend/resource ownership; and explicit worker/timer/boundary failure results.

The retained-index reachability measurement used C=7 and 4,000 publications. It observed 14 retained occurrence references while ring plus old index were held, then zero after releasing both. This supports the stated reference bound for that controlled case, not a universal heap-byte limit. The full bound additionally includes consumer and O(P) producer references plus O(F) live waiter metadata.

The source graph is navigation only. The implementation-start graph was fresh; an intermediate refresh produced 12,166 nodes and 61,744 edges, zero reported warnings/errors, build `sha256:8d5f63a3377155d05217c26b759c0f0125db340286726f17d0608757f70f6452`. Subsequent qualification/catalogue changes require a final refresh. Schema version and complete semantic/compiler coverage are not inferred from those counts.
