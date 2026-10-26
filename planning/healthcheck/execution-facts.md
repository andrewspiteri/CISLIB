# Execution-fact validation — HC00 revision 0.2

These are receiving-binder obligations for HC06; input DTO construction alone does not execute them.

Every fact has a nonempty RegistrationId matching its binding and a defined Kind. Supplied enum values must be defined; durations are finite nonnegative measured milliseconds, never wall-clock subtraction. Normalize times to UTC and compare instants. Execution IDs are positive non-wrapping Int64 values and need not be contiguous. A producer may report an outcome without earlier phase notifications.

| Kind | ExecutionId | Required fields besides common values |
| --- | --- | --- |
| Registered | Absent | OccurredAtUtc is the registration event time. |
| Eligible | Positive | EligibleAtUtc equals OccurredAtUtc. |
| Admitted | Positive | AdmittedAtUtc equals OccurredAtUtc. |
| ConditionStarted | Positive | ConditionStartedAtUtc equals OccurredAtUtc. |
| CallbackStarted | Positive | CallbackStartedAtUtc equals OccurredAtUtc. |
| Outcome | Positive | WorkCompletedAtUtc equals OccurredAtUtc; one Outcome; SourcePhase is Condition or Callback. |
| ErrorHandlingStarted | Positive | ErrorHandlingStartedAtUtc equals OccurredAtUtc; ErrorHandlingStatus is Running. |
| ErrorHandlingCompleted | Positive | ErrorHandlingCompletedAtUtc equals OccurredAtUtc; terminal ErrorHandlingStatus. |
| CleanupCompleted | Positive | CleanupCompletedAtUtc equals OccurredAtUtc; terminal CleanupStatus. |
| Settled | Positive | SettledAtUtc equals OccurredAtUtc; terminal ErrorHandlingStatus and CleanupStatus. |
| Retired | Absent | OccurredAtUtc is the registration retirement time. Optional IntegrationError is a detached lifecycle diagnostic; it does not assert a health failure. |
| IntegrationFault | Optional positive | IntegrationError; OccurredAtUtc is the diagnostic event time. Optional SourcePhase is defined. |

Outcome is forbidden on every other kind. Condition outcomes permit Skipped, Failed and ExpectedCancellation; Callback outcomes permit Succeeded, Failed and ExpectedCancellation. Callback false can be Failed without OriginalError. Handler success cannot replace a failed outcome. Already-observed secondary handler/cleanup context may accompany an outcome.

SourcePhase is present only on Outcome or IntegrationFault. SettledAtUtc and TotalDurationMilliseconds are present only on Settled; a pre-settlement outcome cannot claim them. Registered omits all execution-specific values/diagnostics. Retired omits execution-specific values except its optional detached IntegrationError reason. A registration-scoped IntegrationFault omits execution-specific timing/status/diagnostic fields. Execution-scoped IntegrationFault may carry previously observed context. No fact supplies an event timestamp for a phase known not to have occurred.

Terminal secondary statuses are Succeeded, Failed, ExpectedCancellation, NotApplicable and NotInvoked. NotApplicable means no step was required. NotInvoked means its target/owner was unavailable or unsafe to invoke; an optional detached secondary diagnostic explains why. NotStarted and Running are nonterminal. A retained step can progress from NotStarted to Running to a terminal value, or directly from NotStarted to an uninvoked terminal value. It cannot reopen after completion. Repeated immutable event timestamps/durations/terminal payloads must agree with retained facts. Do not impose wall-clock chronological ordering: the clock can move. Later facts may fill previously absent context. Late facts update only their matching retained execution and never move the current execution backward.

The first valid Registered fact is Recorded; an identical normalized repeat is Duplicate; a retained contradiction raises a bounded InvalidOperationException. Registered is optional, creates no monitor and resets no counters. Bind is the actual atomic ownership transaction. A duplicate active RegistrationId at Bind is rejected atomically with InvalidOperationException before another monitor is created; the caller retains and reuses its binding. Heartbeat's successful duplicate registration must reuse that existing binding instead of binding again. A new registration lifetime supplies a new Guid; no unbounded historical-ID tombstone set is required.

The first valid Retired fact closes binding admission synchronously and releases its association once. A binding-created monitor withdraws immediately; an external monitor remains caller-owned. Report returns Recorded for that retirement. Every later fact, including a repeated Retired, returns Retired. The binding retains the returned shared settlement Task. DisposeAsync performs this release if needed and awaits that same barrier; it cannot reopen admission or certify nonexistent settlement. Finish all required owner-dependent handoffs before reporting Retired. Later detached notifications may safely be ignored.

Within retained current/previous records, identical terminal outcomes are Duplicate and contradictions are bounded integration faults. Older facts outside retained history are Obsolete. Settlement never counts another outcome. Reporting faults remain integration diagnostics isolated from callback execution and never request OnError or release owner protection early.

User-approved additive observation amendment (21 October 2026): a registration-scoped IntegrationFault updates `HealthMonitorSnapshot.RegistrationDiagnostic`, not the health reason or a fabricated execution. Each binding retains only its latest accepted registration diagnostic, normalized event instant and optional source phase for bounded deduplication. An equal instant, equal optional source phase and equal detached code/message/exceptionType/isTruncated returns Duplicate and changes no revision, including when another binding has since supplied the monitor's latest diagnostic. A changed instant, source phase or diagnostic is Recorded, replaces the monitor-lifetime slot in state commit order, and advances revision without an outcome or health-observation timestamp. Wall-clock instants need not increase. Execution-scoped IntegrationFault continues to affect only its matching execution. First Retired with IntegrationError publishes that registration reason atomically with retirement; a missing reason retains the last monitor-level registration diagnostic. Later notifications remain Retired. Disposal of an external binding preserves its monitor and this last diagnostic; ownership withdrawal removes current monitor state normally. The optional details wire field `registrationDiagnostic` has the existing bounded diagnostic shape, is omitted when absent, and permits older payloads without the field.
