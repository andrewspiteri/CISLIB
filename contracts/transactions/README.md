---
title: Explicit local transaction contract
type: contract
status: Draft
---

# Explicit local transactions

`CIS.Transactions` in Core supplies portable factory, owner and participant
interfaces. The database provides atomicity; participants use the same explicit
context and native connection/transaction. Another connection, request, DI scope
or matching connection string does not establish participation. This module adds no ambient transaction lookup, nested transaction, automatic replay or SQL proxy. Compose [CIS.Operations](../operations/README.md) for durable keys and original-result recovery over this explicit ownership boundary; outgoing delivery remains separate future work.

## Registration and acquisition

`AddCisTransactions(LocalTransactionRuntimeOptions?)` and
`AddCisAdoNetDatabase(name, Func<IServiceProvider, DbDataSource>,
AdoNetDatabaseOptions)` are passive extensions in `CIS.Transactions.Hosting`.
Native database options are in `CIS.Transactions.AdoNet`. Factories resolve the
application's host-lived source through its existing provider only at begin;
RM-02 never disposes that source or clears its pool. It owns each returned
connection lease and native transaction.

Aliases are ordinal, case-sensitive, match
`[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`, and are limited to sixteen per factory.
Duplicate aliases are rejected. Isolation declarations explicitly provide a
nonempty supported subset of ReadCommitted, RepeatableRead and Serializable,
including their concrete default. Options and collections are copied at
registration. Equivalent repeated runtime registration is idempotent; conflicting
runtime options are rejected. Registration performs no connection opening,
migration, readiness probe or background work.

Begin validates the alias, isolation, ambient `Transaction.Current` exclusion and
caller cancellation before source resolution/native acquisition. It opens one
lease and begins one native transaction at the selected isolation, without
fallback or an extra acknowledgement query. A failed begin returns no owner,
attempts acquired-resource cleanup and preserves the original exception or
cancellation; secondary cleanup diagnostics contain predefined safe facts.

## Participation and admission

`ILocalTransaction.Context` is a separate `ILocalTransactionContext`, without
completion or disposal authority. `IAdoNetTransactionContext` provides public
mockable native participation. `UseAdoNetAsync` forwards through this capability;
a portable-only context throws `NotSupportedException` before invoking work.

Exactly one callback is admitted per owner. Overlap and recursive native use are
rejected without a queue. Callbacks receive the exact native connection,
transaction and operation token, and must finish their child work and dispose
commands/readers/temporary ORM contexts before returning. Do not retain native
handles, return a live reader/deferred enumeration, complete the transaction,
switch databases or perform independent SQL transaction control. Native handles
cannot technically prevent deliberate violations of these supported-use rules.

An admitted pre-canceled callback is never invoked and marks rollback-only.
Escaping faults and the post-return cancellation checkpoint poison the unit
before admission is released, then propagate the original exception. Automatic
reasons are `transaction.work_failed` and `transaction.work_cancelled`; an
unrelated cancellation exception is a work failure unless the supplied token is
actually canceled. Ordinary business return values are never inspected.

Tokens belong to the operation that accepts them. Canceling a begin token after
the owner was returned does not cancel that owner or later calls. Canceling a
work token after its callback and final checkpoint completed does not
retroactively poison the transaction. Pass the intended operation token explicitly
to each begin, native callback and commit. Cancellation never substitutes a
completed wait for an admitted callback or native invocation's real settlement.

`MarkRollbackOnly` preserves the first nonempty printable ASCII reason of at most
128 characters. It shares synchronization with completion ownership, so an
accepted mark cannot be lost before commit. An application deliberately catching
a native database failure must mark explicitly before swallowing it. PostgreSQL
can acknowledge commit of an already-aborted transaction as rollback; successful
native return is qualified by this participant obligation, not a SQL probe.

## Completion and cleanup

Active owners admit work; Completing closes admission; Completed publishes an
immutable `TransactionCompletion` after every owned cleanup attempt settles.
Commit/explicit rollback during a callback, another completion, completed work
and late marks are misuse. External disposal closes admission, joins the active
callback, then rolls back. That callback may still mark before it settles.
Disposal originating inside the same callback fails promptly using a private
execution marker, without creating ambient transaction selection. Repeated
disposal joins pending cleanup and performs no second native operation.

Rollback-only commit selects NotCommitted/RollbackOnly before considering the
commit token. Observed cancellation at the final pre-commit checkpoint selects
NotCommitted/Cancellation without invoking commit. After native commit is
invoked, every ordinary failed invocation, including synchronous failure or
cancellation, selects Unknown. Successful native commit selects Committed even
if cancellation or resource-disposal failure follows. No commit or application
work is automatically retried.

Explicit rollback and unfinished disposal select NotCommitted with their own
end reasons. If commit was not confirmed, cleanup invokes rollback once with a
fresh independent cooperative token, then disposes the transaction and connection
once each. Earlier failures do not skip later release attempts. The default
rollback budget is thirty seconds; it must be positive, finite and representable
by the native timer (at most 4,294,967,294 milliseconds). Expiry requests
cancellation; actual rollback and tokenless disposal are still awaited. It is
not a hard deadline or a replacement for real settlement.

Completion distinguishes persistence outcome, end reason, invocation/rollback
flags, primary error, at most three ordered cleanup errors and one nullable
nonnegative monotonic duration in seconds. Committed remains committed despite
cleanup errors; successful later rollback never resolves Unknown. Error phases
are Commit, Rollback, TransactionDispose and ConnectionDispose. Codes include
`transaction.rollback_only`, `transaction.cancelled_before_commit`,
`transaction.commit_outcome_unknown`, `transaction.rollback_failed`,
`transaction.transaction_disposal_failed` and
`transaction.connection_disposal_failed`. Error text is safe and bounded to 512
characters; native exceptions, SQL, parameters and credentials are not serialized.
There is no business `Success` property. Operational cleanup failures are recorded
rather than thrown by disposal over an application's in-flight exception.

## Composition and evidence

Applications stop admission, finish/cancel their work, await owned transactions,
then dispose host-owned sources and DI dependencies. No global owner registry or
host-drain service is installed. Pipeline error handling and State Machine
transitions retain their existing meaning; enclose only short database work,
pass the context explicitly and inspect the workflow result before committing.
Remote HTTP/broker/file/cache effects do not participate in this atomic boundary.

Core uses recording native-provider tests for contracts, admission, ownership and
the terminal lifecycle. Controlled holds cover rejected overlap/reentry,
same-callback misuse, external disposal joining callbacks/commit/cleanup,
rollback-only/completion synchronization, exact cancellation cutoffs, original
fault precedence, cooperative rollback-budget settlement and ordered cleanup
failures. Clock failure leaves duration unknown. These tests execute no SQL and
cannot prove database atomicity. The focused lifecycle qualification executed
54 cases on each actual Windows/Ubuntu lane; see the scoped results and candidate
hashes in [CIS-0017 verification](../../docs/changes/CIS-0017/verification.md).
Subsequent owned qualification supplies real PostgreSQL/Npgsql
three-table/isolation/cancellation evidence, operational observations, public
CommonTesting/package consumption and the maintained consumer guide. An EF
interop recipe is not an executed EF adapter, and a simulated lost acknowledgement
is not real network-fault qualification. Report unexecuted platform/provider
acceptance honestly.
