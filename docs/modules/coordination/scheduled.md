# Scheduled occurrences

`ScheduledJobDispatcher` implements `IScheduledJobDispatcher` over one
`JobRuntime`. It consumes a trusted already-admitted occurrence from the runtime's
fixed `IJobRunSource`. It does not calculate calendars, generate catch-up work,
acknowledge durable source records or automatically replay a business callback.

Selected [managed admission](managed-admission.md) is checked before source or
lease admission and again at actual callback entry. A closed gate returns
Deferred; it cannot consume the source's occurrence or cancel a callback that
already entered under its captured settings and independent valid authority.

Register the exact `JobDefinition` and
`JobHandlerBinding.Scheduled<T, THandler>(definition, codec)`. The handler implements
`IScheduledJobHandler<T>`; the captured `IMessageCodec<T>` and `MessageContract`
select the DTO. Unknown targets, unsupported modes and mismatched input contracts
are refused. Category and producer metadata grant no permission.

An `AdmittedJobRun` contains the stable scope/job/resource, occurrence ID,
original `MessageOperationReference`, authoritative admission revision, immutable
`JobExecutionSettings`, owned `EncodedMessage` Command and source-owned admission
reference. The Command must carry the same original operation. The operation's
business scope may differ from the job execution scope; the source validates the
complete mapping and the handler uses that original identity for RM-03 recovery.
No generation, acquisition or occurrence ID is added to the stable coordination key.

## Admission and actual work

Dispatch reserves bounded pending capacity before source validation. It validates
the selected source, exact target/mode and input shape, then awaits the source's
validation of the complete immutable admission. It opens a fresh trusted workload
scope and authorizes the definition's complete execution capabilities against the
actual resolved resource. Caller-supplied capability or category claims do not
authorize execution.

A known locally disabled target returns Deferred without requiring a source or
constructing an authorization/business scope. Atomic admission and callback entry
both recheck current enablement after every asynchronous boundary.

Only then does dispatch reserve immediate global/category invocation capacity and
local stable-key occupancy, before requesting a managed lease. Disabled or full
work returns Deferred without acquiring ownership. An already occupied local key
returns Busy, including when its old callback ignores cancellation. Separate
resources can execute concurrently within the local limits.

The acquisition borrows the initial authorized scope. After actual acquisition
and initial scope cleanup, dispatch opens and authorizes an independent execution
scope. At callback entry the runtime rechecks current effective enablement, fixed
activation, draining state and current local Owned lease state. The context retains
the source's exact immutable admitted settings, input and operation identity;
local configuration convergence does not silently rewrite an admitted occurrence.
The source must resolve any category timeout default into the admitted settings.
An admitted null timeout remains null when later local category defaults change.

The callback token combines caller cancellation, ownership loss and an optional
cooperative timeout. Graceful host/configuration drain closes new admission while
the already-entered callback can continue within its finite lease drain budget.
It does not immediately cancel that callback merely because stopping began.

Invocation capacity remains charged through the actual callback and execution
scope cleanup. Release follows that cleanup. Local key occupancy remains charged
through lease cleanup and duty settlement. Cancellation, a timed-out caller wait
or lease loss does not establish physical callback completion. Shutdown joins the
actual tasks and scopes before their dependencies can be disposed.

## Results and durable identity

Results remain Completed, PreviouslyCompleted, Busy, Deferred, OwnershipLost,
Cancelled, Failed, Indeterminate, Unsupported or Unauthorized. The source owns
its durable acknowledgement/retry policy. A Deferred response preserves that
source's pending occurrence; no hidden local queue takes over its ownership.

A returned Completed/PreviouslyCompleted result with known Committed completion
survives later lease loss, caller cancellation and ordinary cleanup diagnostics.
Unknown remains Indeterminate. An unexpected callback exception is Failed and is
never automatically retried. A callback that throws cancellation after an
identified cancellation signal reports Cancelled or OwnershipLost; cancellation
does not imply a database rollback. The adapter cannot discover a commit fact that
the handler did not return.

Bounded local snapshots retain Failed/Indeterminate as Faulted with their distinct
outcome code. A confirmed completion after lease loss remains a completed business
result while the local duty snapshot reports Lost and a completed-after-loss code.
Ordinary cleanup diagnostics remain separate from those original result facts.

Use the same original RM-03 operation identity when redelivering an occurrence or
recovering after takeover. Prepared is not Completed. Authorized read-only result
recovery remains available after ownership loss, and valid committed RM-04
outgoing intent remains deliverable. New protected effects still require the
ownership guard in the original business transaction.

## Schedule metadata and capabilities

All jobs are disabled by default. Disabled definitions are inspectable without a
run source, evaluator or handler construction. Enabled scheduled activation needs
a complete descriptor and an explicitly selected source advertising
`ExternallyAdmittedOccurrences`. `AutomaticRecurrence` additionally requires a
separate compatible `IJobScheduleEvaluator`; missing capability fails startup.

FixedDelay means a delay from shared completion of the preceding logical
occurrence. Daily/Weekly descriptors have explicit IANA zones; Weekly has a
nonempty copied weekday set. Enabled activation validates the available IANA zone.
Nonexistent local times declare Skip and repeated local times declare
OnceAtEarlierInstant. Missed-run policy is explicit: Skip, RunLatestOnce, or
CatchUpBounded with a finite count and window. The external source/evaluator owns
calendar calculation, durable admission, next occurrence and catch-up production.
RM-05 supplies no production recurring engine or fabricated next-run time.

See the [controlled source tests](scheduled-testing.md),
[portable job contract](../../../contracts/coordination/jobs.md) and
[verification record](../../changes/CIS-0022/verification.md) for actual evidence
and its limits.
