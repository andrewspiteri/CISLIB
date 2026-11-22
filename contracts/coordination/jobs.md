# Portable job and scheduled-run contract

The job catalog is management and eligibility metadata over shared coordination.
It does not create a scheduling engine, journal, inbox, outbox or transaction
coordinator. The [C# guide](../../docs/modules/coordination/jobs.md) documents the
actual binding, registration and validated settings.

## Identity and eligibility

A job definition contains exact execution scope, stable JobId, display name,
description, one primary category, supported mode(s), resource-scope description
and required execution capabilities. C# handler Type and codec instances stay in
the registration boundary, outside portable identity.

One exact `(ExecutionScope, JobId)` selects one compatible local handler binding.
Unknown targets, missing handlers, duplicate bindings, unknown categories and
unsupported modes are refused. A combined-mode binding explicitly implements both
ports. Discovery and category labels never select a handler or grant authority.
Every participating instance also needs current established authorization for the
resolved scope/resource.

The coordination key remains environment, execution scope, JobId and ResourceId.
Changing display text, category, schedule or settings revision changes none of
those key components. Occurrence IDs, process identity, lease ID and generation
are separate identities. Text equality is ordinal and case-sensitive. The C# guide
publishes exact UTF-8 limits; no trimming or normalization occurs.

## Settings and local state

Definitions are inspectable without activation and new jobs are disabled.
Effective local enablement combines host, category and job gates, compatible
registration and actual authorization. Category disablement and its hard local
capacity ceiling dominate a job override. Category settings are scoped to their
responsible application/execution scope.

Apply a complete validated immutable snapshot or retain the last valid one. Local
Configuration revision is diagnostic local convergence, distinct from durable
scheduler admission revision. Continuous settings apply between iterations.
Mode/source changes require bounded drain, deactivation and reactivation; a prior
unfinished callback continues to exclude another local callback for its key.

Owned continuous duties and actual unfinished business invocations consume
different capacities. Idle retains ownership while releasing business capacity.
An uncooperative callback retains its invocation capacity and observation after
cancellation. Limits are per instance; they grant no cluster-wide quota or fairness
guarantee. Diagnostics are bounded pages and never perform database I/O.

## Continuous execution

Each handler performs one finite scoped iteration and returns MoreWork, Idle,
Failed or Indeterminate. The runtime owns repetition, fair yielding and positive
idle waiting. Renewal progresses independently of business execution capacity.
Unexpected business failure faults that local duty by default. Acquisition retry
delay is not permission to replay an uncertain mutation, failed business callback
or Unknown commit.

Keep original RM-03 operation/checkpoint identity application-owned across owner
changes. Ownership loss and cancellation do not prove rollback or physical stop.
Drain the actual callback before exact-grant release; drain-budget expiry stops
renewal and requests cancellation while retaining unfinished task observation.

## Scheduled admission and completion

An admitted occurrence carries exact scope/job/resource, stable occurrence ID,
original durable business-operation identity, optional scheduled UTC time,
authoritative admission/settings revision, immutable execution snapshot, bounded
owned input and a trusted source/reference. A body asserting those fields does not
establish authentication or admission.

The C# binding reuses Messaging's Command envelope, MessageContract, explicit
IMessageCodec and original MessageOperationReference. Producer metadata is not
authority. The selected source validates the exact immutable admission. A future
remote source must use existing service authentication and authorization.

Dispatch validates target, admission, supported mode and permission before work.
It respects local disablement and obtains immediate business/category capacity
before acquiring ownership. Disabled or full dispatch returns Deferred and
preserves authoritative pending work. Occurrences contend on the stable
job/resource key; unsupported parallel-occurrence policy is rejected.

Results distinguish Completed, PreviouslyCompleted, Busy, Deferred, OwnershipLost,
Cancelled, Failed, Indeterminate, Unsupported and Unauthorized. The scheduling
provider owns durable acknowledgement/retry. Prepared or staged effects are not
Completed. Preserve original RM-02 Committed/NotCommitted/Unknown evidence;
uncertain commit remains Indeterminate until established recovery. Takeover or
redelivery retains the same occurrence and RM-03 identity. Committed RM-04 outgoing
intent stays valid after the originating grant ends.

## Scheduler responsibilities

Registering schedule metadata arranges no recurring execution. ExternalOccurrences
requires a trusted already-admitted source. AutomaticRecurrence also requires a
separate evaluator supporting the configured descriptor and policies. Missing
capabilities fail enabled activation; disabled inspection remains usable.

FixedDelay is measured from shared completion of the preceding logical occurrence.
Daily and Weekly use explicit IANA zones and local time; Weekly has a nonempty
weekday set. Initial DST policies are Skip for nonexistent times and
OnceAtEarlierInstant for repeated times. All admitted instants are UTC.

Enabled calendar activation validates the actual available explicit IANA zone;
disabled metadata remains inspectable without loading a zone. Missed-run policy
is explicit: Skip, RunLatestOnce, or CatchUpBounded with finite
count/window and durable pending state. Calendar calculation, actual supported
zone evaluation, authoritative next-due state, catch-up production and atomic
revision/enablement admission belong to the scheduler. The controlled external
trigger is a demonstration, never production automatic recurrence.
