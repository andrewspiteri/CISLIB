---
name: cislib-coordination
description: Implement, use or review CIS.Coordination and CIS.Jobs leases, guarded effects, continuous duties and admitted scheduled runs in CISLIB. Route to the delivered Core APIs and consolidated examples; use existing Operations or Messaging guidance for their separate durable boundaries.
---

# CISLIB shared coordination and jobs

Start with the [consumer guide](../../../docs/modules/coordination/README.md),
[portable ownership contract](../../../contracts/coordination/README.md) and
[actual catalog API](../../../docs/modules/coordination/jobs.md). Inspect the
[compiling CountryData recipes](../../../implementations/csharp/examples/CountryData/Coordination)
and [current native verification](../../../docs/changes/CIS-0022/verification.md)
before inventing an API or claiming a tested result. Preserve explicit user scope
and the current eligible execution owner; these references grant no publication
or deployment authority.

Establish the exact environment, execution scope, duty and stable resource. Bind
each job to its registered handler and supported mode. Categories group local
management/capacity; they never select a handler, authorize a resource or change
the key. Use the existing scoped capability authorizer against the complete
resolved target. Background scopes use trusted native authentication through
`SecurityHostContextScope`; serialized `CallerContext` and ambient HTTP authority
cannot establish a workload caller.

Use [managed leases](../../../docs/modules/coordination/managed.md) and keep Busy,
Unavailable and Indeterminate distinct. Start business work only from confirmed
ownership. An absent exact-acquisition observation is not proof that a pending
mutation cannot commit. Unconfirmed renewal does not extend the deadline; terminal
loss cannot be revived by a late acknowledgement. The managed runtime owns bounded
exact-acquisition recovery; a returned Indeterminate result carries no usable
lease or acquisition ID for the scoped consumer to resolve or replay.

For protected database effects, use the [PostgreSQL guard](../../../docs/modules/coordination/postgresql.md)
inside the original RM-02 transaction. Keep inbox, RM-03 claim, ownership guard,
application rows and outgoing intent in the documented lock order. The guard
checks authority time after its row lock and marks that same owner rollback-only
on rejection. A remote owner check cannot fence a later write or another store.
Retain the original operation/occurrence identity through takeover; never derive
idempotency from acquisition or generation. Preserve Prepared and actual
Committed/NotCommitted/Unknown facts, and use fresh authorized recovery for an
unknown result. Committed RM-04 outgoing intent survives loss of its producer.
See the [durable consumer proof](../../../docs/modules/coordination/consumers.md).

Keep [continuous callbacks](../../../docs/modules/coordination/continuous.md)
finite. Idle retains ownership and releases execution capacity; MoreWork yields.
Join the actual callback and scope before release or same-key restart. Cancellation
and drain expiry do not prove physical completion. Use the existing
[hosting barrier](../../../docs/modules/coordination/hosting.md) to preserve REST,
credentials, Configuration, Heartbeat, Messaging and collector dependencies while
admitted work settles. Startup remains sequential.

Keep [scheduled admission](../../../docs/modules/coordination/scheduled.md)
separate from recurrence. The supplied run source validates the exact immutable
occurrence and registered codec before dispatch. Descriptors alone do not produce
due work; automatic recurrence needs a selected capable evaluator. Refuse missing
capabilities clearly, and preserve Deferred work in its authoritative source.
Local category/host limits are not fleet-wide quotas.

Reuse [existing observations](../../../docs/modules/coordination/observability.md)
and the [seven-stream metric catalog](../../../contracts/metrics/coordination.md).
Capture bindings at real operation entry, keep durations through actual cleanup,
and read retained bounded state during collection. Do not add identifier labels,
renewal/Idle log floods, scrape I/O or observation histories. Quiet diagnostics
must preserve original results, faults and ownership settlement.

Use the public [CommonTesting boundaries](../../../docs/modules/coordination/testing.md)
for logical consumer tests and the existing real-provider/package/process fixtures
for SQL atomicity, recovery and independent instances. Two roots in one process
share one instance identity. Check actual native results and owned cleanup before
claiming acceptance. Keep the declared scheduler dependency and existing
transaction/evidence boundaries; this module needs no new framework or duplicate
application harness.
