# Finite continuous duties

Implement `IContinuousJobHandler.ExecuteAsync(JobExecutionContext, CancellationToken)`
as one finite unit. Register its exact definition and handler through
`AddCisContinuousJob<THandler>`, explicitly enable the host/category/job in a
validated configuration snapshot, and select stable resource partitions:

```csharp
var runner = new ContinuousJobRunner(runtime);
ContinuousJobStartOutcome start = await runner.StartAsync(
    "wallet-service", "wallet.compaction", "partition-12", startupCancellation);

// Later, while dependencies remain alive:
await runner.StopAsync("wallet-service", "wallet.compaction", "partition-12");
```

`Started` means that a bounded local lifetime was accepted. It does not mean the
instance already owns a distributed grant. `AlreadyRunning` includes an earlier
callback still executing after ownership loss. `Deferred` represents local
enablement/capacity refusal; unknown registrations and denied permissions return
Unsupported and Unauthorized respectively. Actual required startup failure still
propagates through the runtime's sequential start.

When [managed admission](managed-admission.md) is selected, a closed gate also
returns Deferred for a new duty and pauses the next finite iteration or
acquisition attempt. It does not cancel an already entered callback or remove a
still-valid existing grant. Reconciliation wakes the existing runtime condition.

The initial caller token covers initialization and eligibility only. Once Started
is returned, cancelling it does not stop the duty. Use `StopAsync`, configuration
disablement or the owning runtime's stop. These initiate bounded graceful drain;
tokens request stopping without proving that a callback has physically finished.

## Ownership and finite iterations

The runner acquires through the existing scoped `ICoordinationService`, then
creates a fresh independently authorized scope for each finite iteration. The
scope resolves only the exact registered handler. No scoped dependency remains
alive during an idle delay. The managed coordination runtime renews independently
of business capacity and handler duration.

Return a `ContinuousIterationResult`:

| Outcome | Local behavior |
| --- | --- |
| MoreWork | Drain the actual iteration scope, release its business slot, yield, then compete for a fresh iteration admission. |
| Idle | Drain the scope, release business capacity and await the positive configured idle delay while retaining ownership. |
| Failed | Fault this duty locally after the callback/scope settle and conditionally release its grant. |
| Indeterminate | Preserve unknown business completion, fault locally and use the application's existing durable recovery contract. |

Default idle and known-not-committed acquisition retry delay are five seconds.
Timeout is optional and cooperative. Maximum job drain defaults to thirty seconds
and cannot exceed the managed lease's one-day public limit; the coordination
runtime may impose a shorter configured ceiling. Other accepted job delays and
timeouts remain positive and at most 49 days.

Normal standby contention and a known-not-committed acquisition Unavailable result
can retry after the configured delay. An Indeterminate acquisition stops this
local duty without another mutation or a business callback. The coordination
runtime owns its bounded exact-acquisition recovery. Unexpected handler exceptions,
business Failed/Indeterminate outcomes and cancellation of an actual invocation
do not trigger automatic business replay. Other duties remain independent.

If ownership ends while idle, the duty can stop safely. If ownership ends during
work, signal cancellation and await that actual callback and scope. The stable
local key and execution slot remain occupied even if the handler ignores the
signal. An explicit later start can only acquire the local key after the previous
lifetime actually settles. Another instance can take over after expiry, so every
correctness-sensitive transaction still needs the ownership guard.

## Guarded application work

Keep the original application-owned RM-02 transaction. Invoke RM-03 with the stable
application checkpoint/operation key and guard inside its new-effect handler,
outside nested ADO.NET callbacks. Use the original context for protected rows,
original durable result and RM-04 outgoing staging. Await the entire operation
before completing the owner. Follow inbox (where present), RM-03 claim,
coordination row, application rows, outgoing lock order.

Do not derive business identity from a grant or generation. Unknown business commit
stays Unknown until the established fresh authorized recovery resolves it.
Committed outgoing intent remains deliverable after ownership ends. A cancellation
exception alone does not prove rollback. A remote ownership check followed by an
unguarded write is not atomic enforcement.

The runner retains original `TransactionCompletion` evidence. A known Committed
iteration can remain a successful iteration observation even when subsequent loss
or scope cleanup stops the duty. An Indeterminate result plus cleanup failure
remains visible as both facts, with no automatic retry. Cleanup diagnostics never
invent a different business completion.

The unit cases use existing CommonTesting manual time, manual leases and scripted
coordination results to prove local capacity, scope ownership, fair opportunities,
uncertainty and drain. They establish no SQL atomicity or automatic lease renewal;
the managed runtime and real-provider integration cases establish their separate
facts. See [shared runtime ownership](runtime.md) and [catalog contracts](jobs.md).
