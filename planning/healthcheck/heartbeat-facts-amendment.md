# Heartbeat execution-fact amendment

**Owner:** CIS-0006/WORK-004  
**Authority:** [CIS-0006 authorization](../../docs/changes/CIS-0006/authorization.json)  
**Date:** 16 November 2026  
**Status:** Implemented; scoped native receiving, wire and compatibility checks are recorded in [CIS-0006 verification](../../docs/changes/CIS-0006/verification.md).

This additive receiving contract supports the Heartbeat integration described in
[the integration design](../heartbeat/healthcheck-integration.md). Existing facts,
outcomes, phase values, policy arithmetic, monitor ownership and report versions remain valid.
No historical fixture, published baseline or completed work owner is replaced.

`HealthExecutionFactKind.Dispatched = 12` and `Suppressed = 13` append to values 0–11.
`HealthExecutionPhase` and `HealthExecutionOutcome` retain every existing value.
Dispatch advances at most to the existing `Admitted` phase. Suppression preserves
the last actual phase, and neither event performs policy-outcome arithmetic.

The fact DTO and detached execution snapshot add these optional fields:

| C# property | Details wire field | Meaning |
| --- | --- | --- |
| `DispatchedAtUtc` | `dispatchedAtUtc` | Actual capacity reservation and executor-post event. |
| `DispatchDelayMilliseconds` | `dispatchDelayMs` | Monotonic admission-to-dispatch duration, including capacity wait. |
| `SuppressedAtUtc` | `suppressedAtUtc` | Actual decision preventing a user phase. |
| `SuppressedPhase` | `suppressedPhase` | `Condition` or `Callback` that did not start. |
| `SuppressionReason` | `suppressionReason` | `ApplicationStopping = 0` or `TargetUnavailable = 1`. |
| `ConditionResult` | `conditionResult` | Actual returned boolean, including true when a callback was subsequently suppressed. |
| `CallbackResult` | `callbackResult` | Actual returned callback boolean, independently of final cleanup outcome. |
| `ExpectedCancellationPhase` | `expectedCancellationPhase` | Actual user phase acknowledging known cancellation. |

`Dispatched` requires its positive execution identity and event time.
`Suppressed` requires its positive execution identity, event time, prevented phase
and reason, and carries no Outcome or SourcePhase. Its prevented phase cannot have
started or returned a result. Suppression metadata is a complete optional group.
Result booleans, cancellation phase, event instants and durations merge immutably.
A phase cannot both return a boolean and acknowledge cancellation; a false condition
cannot be followed by a callback. Retained contradictions reject without mutation.

An identical retained suppression notification returns `Duplicate` without another
revision or outcome. Older discarded executions return `Obsolete`. History remains
bounded to the existing current/previous context; suppression introduces no tombstone set.

Heartbeat produces its one final Outcome only after all failure-producing owned
cleanup. A real cleanup failure changes earlier success, skip or acknowledged
cancellation to Failed while the new raw context preserves what actually happened.
SourcePhase remains the condition/callback that ran; CleanupError describes cleanup
as the failure origin without inventing an OriginalError. Reporting faults remain
integration diagnostics and do not rewrite a work outcome or count another execution.

A completed true condition followed by callback suppression may therefore have one
Failed source-Condition Outcome if its later cleanup fails. Fully unstarted work
withdrawn during shutdown has no callback Outcome even if withdrawal cleanup fails;
that failure remains an integration diagnostic. Neither suppression nor observation
of a fault proves actual settlement or permits capacity/owner release.

`WorkCompletedAtUtc` and an Outcome's `OccurredAtUtc` remain the actual end of the
condition/callback phase, even when the final Outcome is delivered after later
cleanup. `SettledAtUtc` is reported separately after ownership actually settles.
UTC instants never replace measured monotonic durations.

Optional wire fields are omitted when absent; explicit null is invalid. Existing
reports and legacy output shape remain valid. The preserved HC00 schema permits
unknown additive execution fields and retains the phase vocabulary used by new output.
The repository has no preserved executable historical reader; schema/API/fixture
compatibility checks do not claim execution of such a binary.

The concrete Core binding may be retained for idle retirement independently of a
subscriber's weak-key setup. Its reporting handle intentionally retains a Core
monitor; the graph must not add a strong subscriber, owner or user-closure path.
On target loss, claim retirement once, report Retired and await binding disposal
outside the scheduler gate. External monitors remain caller-owned; binding-created
monitors withdraw. The real Heartbeat graph and cutoff tests belong to engine integration.

The portable authored examples are HC-WIRE-011/012 and invalid HC-WIRE-109.
Focused native receiving, formatter/parser and compatibility tests are authored
alongside this amendment. Their observed outcomes belong to the CIS-0006 verification
record after root-coordinated builds and execution; authoring is not a passing result.
