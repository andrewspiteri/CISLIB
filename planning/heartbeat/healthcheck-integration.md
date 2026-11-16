# Heartbeat and Healthcheck integration

**Change:** CIS-0006

**Status:** Implemented receiving contract and adapter; scoped qualification is recorded separately.

**Date:** 16 November 2026.
**Source:** Heartbeat specification v0.2, particularly HB-OWN-04 and HB-HLT-01–04.

The [actual outcome authorization](../../docs/changes/CIS-0006/authorization.json) covers this implementation. The [verification record](../../docs/changes/CIS-0006/verification.md) identifies actual native reports and remaining compatibility/release limits. Current repository configuration/dependency decisions supersede older source assumptions. This document records development behavior, not publication or blanket acceptance.

## Receiving baseline and ownership

`HealthExecutionBinder` supplies the existing explicit selections:

| Selection | Overload and ownership |
| --- | --- |
| Suitable existing monitor | Bind(IHealthMonitor, HealthExecutionBindingOptions); monitor remains caller-owned. |
| Consecutive-failure policy | Bind(IHealthMonitorFactory, ConsecutiveFailureMonitorOptions, HealthExecutionBindingOptions); binding owns its created monitor. |
| Availability policy | Bind(IHealthMonitorFactory, AvailabilityMonitorOptions, HealthExecutionBindingOptions); binding owns its created monitor. |

Only appropriate active Core monitors/factories are accepted. Condition and numeric-threshold monitors are not execution-policy targets. Availability requires explicit SuccessPublishesUsableData = true. Healthcheck validates policy values; Heartbeat does not interpret allowances or reset arithmetic. The additive ValidatePolicy entry point performs that validation without creating a monitor or mutating state.

Binding receives a fresh nonempty registration Guid, optional copied schedule metadata and display name. Names use at most 128 valid Unicode scalar values. Bind performs no callback or scheduling. An active duplicate registration Guid is rejected before creating another monitor, so Heartbeat performs its own instance duplicate coordination and reuses the original binding.

`IHealthExecutionBinding.Report` synchronously commits detached state without I/O, queuing or durable-delivery guarantees. Its return is the local handoff boundary. Recorded, Duplicate, Obsolete and Retired dispositions, immutable retained context and bounded current/previous execution history remain. Contradictions fail visibly without replacing prior accepted facts. UTC instants are normalized; durations are monotonic, finite and nonnegative. UTC ordering is not imposed as an execution-order guarantee.

The pre-Heartbeat fact baseline already covered registration, eligibility, admission, user phases, outcomes, error handling, cleanup, settlement, retirement and integration faults. It lacked distinct dispatch and neutral suppression observations. The additive receiving implementation below supplies them. Existing QueueDelayMilliseconds retains eligibility-to-admission meaning; callback skip/cancellation never substitutes for prevented work.

## Additive dispatch, suppression and raw context

Existing HealthExecutionFactKind values 0–11 are unchanged. Dispatched = 12 and Suppressed = 13 are appended. The existing HealthExecutionPhase and outcome enum values/vocabulary are unchanged.

The fact/snapshot context adds:

| Property | Meaning |
| --- | --- |
| DispatchedAtUtc | Actual successful dispatch event instant. |
| DispatchDelayMilliseconds | Monotonic admission-to-dispatch delay, including capacity waiting. |
| SuppressedAtUtc | Decision preventing the next user phase. |
| SuppressedPhase | Condition or Callback only; the prevented phase did not start. |
| SuppressionReason | ApplicationStopping or TargetUnavailable. |
| ConditionResult / CallbackResult | Actual returned boolean for a phase that ran; absent when no such result exists. |
| ExpectedCancellationPhase | Condition or Callback that acknowledged known cancellation, independently of a later cleanup failure. |

Dispatched requires a positive execution ID and matching event/dispatch instant. Suppressed requires a positive ID, matching event/suppression instant, reason and prevented phase; it forbids an Outcome on that fact, SourcePhase and a start timestamp for the prevented phase. Dispatch projects the existing Admitted phase. Suppression retains the last actual phase until cleanup/settlement. Neither fact applies health-policy outcome arithmetic.

An entirely unstarted execution has no callback Outcome. A running false/faulted/canceled condition produces its actual result; a running true condition followed by a closed callback gate reports callback suppression. If later owned cleanup genuinely fails after user work ran, the one final Outcome is Failed with its actual source phase and preserved raw/suppression context. Withdrawal cleanup with no user phase remains an integration diagnostic, never a fabricated callback failure.

The added context is immutable within retained records. Identical repeats deduplicate; contradictions are rejected. Older discarded executions become Obsolete; retirement remains terminal. A returned boolean and acknowledged cancellation cannot both describe the same phase; false condition forbids callback result/start. No unbounded execution-ID tombstones or additional outcome counters are introduced.

Optional details-wire fields are dispatchedAtUtc, dispatchDelayMs, suppressedAtUtc, suppressedPhase, suppressionReason, conditionResult, callbackResult and expectedCancellationPhase. Missing values are omitted; explicit null or inconsistent supplied combinations fail validation. Schema version, required fields and existing phase/outcome strings remain. Reports with none of the added facts retain their existing encoded shape. Portable schema/fixtures and formatter/reader validation carry the additive context; actual frozen-contract execution is recorded separately.

## Policy selection through the actual owner

The sealed named-init `HeartbeatHealthcheckOptions` contains Monitor, ConsecutiveFailures, Availability, MonitorFactory and SuccessPublishesUsableData. Exactly one monitor/policy source is selected. Typed policies require the actual Core factory; availability also requires the usable-data declaration. Calling ordinary registration without this integration creates no monitor or implicit failure threshold.

The extension Register(IHeartbeatService, IHeartbeatSubscriber, HeartbeatRegistrationOptions, HeartbeatHealthcheckOptions, condition) requires the actual owner-bound facade. It validates required arguments, copied schedule and policy even for a duplicate, without creating another binding. A new registration reserves its identity, performs the concrete Bind outside scheduler locks, then commits or retires/rolls back if binding or a competing owner/global cutoff fails. A configured policy is never silently dropped.

Idle scheduler entries retain neither policy setup nor arbitrary monitor factories/closures. Target-dependent condition/cleanup data remains in the weak-key graph; actual owner associations are weak. An independently retained audited Core reporting binding preserves retirement capability after the weak key is collected. Its Core state and monitor-handle graph is exercised for no subscriber, owner or arbitrary exception path; interface naming alone is not proof of detachment.

On key loss, retirement is claimed once, Report(Retired) and binding disposal execute outside scheduler locks, and registration/global barriers join their actual completion. A caller-owned surviving monitor remains active. A binding-created monitor withdraws with its binding. Required final facts precede Retired; every later binding notification is Retired.

## Execution results and error precedence

| Observation before a later owned-work fault | Candidate result | Subscriber OnError |
| --- | --- | --- |
| Callback true | Succeeded, Callback source | No. |
| Callback false | Failed, Callback source; no invented exception | No. |
| Condition false | Skipped, Condition source | No. |
| Acknowledged canceled known execution token | ExpectedCancellation, actual source | No. |
| Unexpected escaping exception/cancellation | Failed with original detached diagnostic | Await once when safe. |
| Null condition/callback task | Failed contract violation, actual source | Await once when safe. |
| Entirely unstarted suppression | Lifecycle suppression; no callback Outcome | No. |

One final Outcome is selected after failure-producing owned cleanup. A real condition/callback/handler/cleanup failure has precedence over success, skip or expected cancellation. Preserve observed raw booleans/cancellation phase. Cleanup-only failure uses the actual phase that ran and CleanupError without inventing OriginalError. Expected cancellation already observed remains factual context even if cleanup changes the final result to Failed.

OnError handles only exceptions escaping condition/callback. It is awaited once, cannot recurse and cannot replace the originating result. Secondary handler errors remain secondary. Subscriber cleanup is part of its returned task. Integration cleanup cannot start a new subscriber error-handler call; it affects the final result when user work ran and separately surfaces an integration fault. Reporting failure remains isolated from work outcome and never invokes OnError.

The initial production execution token is uncanceled. Shutdown or a canceled wait does not cancel running work; token request alone does not replace a normal boolean result. Independently owned application cancellation/deadlines remain application policy.

## Fact order and actual settlement

Each completed registration reuse has a new Guid; execution IDs are positive non-wrapping Int64 values. Committed registration reports Registered. Actual eligibility/admission precede successful dispatch publication. A wrapper cannot start user work before that publication, even if executor entry precedes TryPost return. User-phase facts follow coordinated start gates, not posting.

For a true condition followed by a callback, terminal WorkCompletedAtUtc is the actual callback end. A terminal false/faulted/canceled condition uses its own actual end. Outcome OccurredAtUtc matches that work-completion instant, even when the fact is handed off after asynchronous error handling/cleanup. It never claims the later handoff/settlement time.

Await all failure-producing owned cleanup, then synchronously project exceptions through HealthDiagnostic.FromException and report the one final Outcome while the execution still owns its target, reservation, slot and owner hold. Await any owner-dependent FlushAsync before the execution task ends; the current concrete Core adapter's flush is already complete because Report synchronously detached/committed its payload.

After actual task completion, coordinated bookkeeping clears transient target/binding/exception references and releases the counted owner hold and capacity slot. Detached Settled uses one captured UTC event instant, terminal secondary statuses and measured total duration. It is not a second Outcome and does not reacquire owner access. A registration-level publication guard prevents retirement or new acceptance from overtaking this final notification. Settlement establishes the fixed-delay recurrence origin.

Clock failure or UTC correlation overflow cannot silently release ownership early. Real infrastructure faults surface through the runtime's bounded detached InfrastructureFailure awaitable. A fault that prevents proving settlement cannot be labeled a successfully completed barrier; other safely drainable work is still joined.

## Readiness, shutdown and evidence

Bindings exist before their registrations activate. Healthcheck remains usable through final handoffs and retirement. BeginStopping rejects new bindings but existing bindings can receive final facts before destructive Healthcheck disposal. Heartbeat drains first; neither monitor state nor traffic-readiness policy controls scheduling.

Availability may remain Pending until a real successful usable-data publication. That does not delay engine activation or create a first-success startup barrier. Applications needing initial data before external traffic arrange that application readiness separately, without waiting inside the startup method whose completion opens Heartbeat readiness.

Implemented tests exercise receiving state/wire validation, dispatch publication, duplicate/reuse behavior, phase suppression, cleanup/result precedence, real surviving-monitor reachability and detached exception observations. [Acceptance mapping](acceptance.md) preserves required case-level scope. The [verification record](../../docs/changes/CIS-0006/verification.md) supplies actual terminal reports, candidate/platform and remaining limits. Authored tests, source inspection and additive schema design alone do not establish frozen compatibility, package/platform or complete release qualification.
