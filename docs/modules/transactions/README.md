---
title: Explicit local transactions
type: document
status: draft
cis:
  stable_id: cislib.document.modules.transactions.core
---

# Local transactions

`CIS.Transactions` in Core gives one application operation an explicit local
database transaction. Stores receive its participant context and use the same
native connection/transaction. The owner alone completes it. The database makes
ordinary transactional writes atomic; HTTP, broker, file, cache and in-memory
effects remain outside that guarantee.

Read the [portable contract](../../../contracts/transactions/README.md),
[public interfaces](../../../implementations/csharp/src/CIS.Core/Transactions/Contracts/ILocalTransactionFactory.cs),
[PostgreSQL reference](postgresql.md) and [observations](observability.md).
The compiling three-store consumer is
[PostgresAcceptanceConsumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions/PostgresAcceptanceConsumer.cs).
Its three tables are acceptance fixtures, with no production result store or
outgoing dispatcher. CountryData remains the maintained tutorial application.

## Register an application-owned source

Use ordinary host service configuration and its existing provider. The
application supplies a host-lived `DbDataSource` and its provider dependency;
Core includes neither Npgsql nor EF. Register that existing source passively:

```csharp
using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;

services.AddCisTransactions(new LocalTransactionRuntimeOptions
{
    RollbackTimeout = TimeSpan.FromSeconds(30),
    LogCalls = false
});
services.AddCisAdoNetDatabase("LedgerDatabase",
    provider => provider.GetRequiredService<DbDataSource>(),
    new AdoNetDatabaseOptions
    {
        DefaultIsolation = TransactionIsolation.ReadCommitted,
        SupportedIsolationLevels =
        [
            TransactionIsolation.ReadCommitted,
            TransactionIsolation.RepeatableRead,
            TransactionIsolation.Serializable
        ]
    });
```

Extensions are in `CIS.Transactions.Hosting`; database options and native helpers
are in `CIS.Transactions.AdoNet`. Registration copies options and an explicit
nonempty supported isolation subset containing its default. It opens no
connections and builds no second provider. Equivalent repeated runtime options
are idempotent; conflicting options and duplicate aliases fail. Up to sixteen
ordinal, case-sensitive aliases match `[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`.
No data source is chosen by default. Unknown aliases, unsupported isolation and
ambient `System.Transactions.Transaction.Current` fail before source resolution/I/O.

## Own completion and lend work

Inject `ILocalTransactionFactory`. Begin with an alias and token; optionally pass
`new LocalTransactionOptions { Isolation = ... }` for that owner. Failed begin
releases acquired resources and propagates the original exception/cancellation.
A successful owner is Active, with a separate Context lacking completion authority.

The reference consumer invokes business, result and outgoing stores sequentially
with the same context. Each uses `UseAdoNetAsync`, assigns `command.Transaction`,
and disposes commands/readers inside its awaited callback. This command boundary
uses the implemented public API; the application supplies factory, token and SQL:

```csharp
await using ILocalTransaction owner = await factory.BeginAsync("LedgerDatabase", token);
await owner.Context.UseAdoNetAsync(async (connection, transaction, workToken) =>
{
    await using DbCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = applicationSql;
    // Add the application's parameters before execution.
    await command.ExecuteNonQueryAsync(workToken);
}, token);
TransactionCompletion completion = await owner.CommitAsync(token);
```

See the compiling reference for complete participation and independent-connection
assertions. Run one callback at a time. Nested services may share the context,
but invoke native helpers sequentially outside one another's callbacks. Overlap,
recursive native use and completion inside an admitted callback fail promptly.
Never retain handles, return a live reader/deferred enumeration, switch databases,
issue independent transaction control or dispose borrowed native handles.
External owner disposal closes admission and joins actual work before cleanup.

An escaping callback fault/cancellation marks rollback-only, then propagates the
original exception. A participant catching a failure must explicitly call
`MarkRollbackOnly("app.safe_reason")` if it should prevent commit. The first
bounded printable ASCII reason wins. Business values are not inspected: a
declined result can intentionally commit just its result row, as the reference shows.

## Decide from persistence evidence

Valid commit/rollback requests return TransactionCompletion after actual cleanup.
Inspect persistence separately from the business result and cleanup diagnostics:

| Outcome | Application decision |
| --- | --- |
| Committed | Native commit returned successfully. Preserve this fact even when resource disposal reports errors. |
| NotCommitted | Native commit was never invoked. Inspect EndReason and the primary error for refusal, cancellation or explicit noncommit. |
| Unknown | Commit was invoked without confirmed success. It may have committed; reconcile using application-owned durable identifiers and do not automatically replay or retry commit. |

RollbackConfirmed never resolves Unknown. Error describes the primary problem;
up to three CleanupErrors separately describe rollback, transaction disposal and
connection disposal in attempt order. Later release attempts still run. There is
no business Success convenience flag. Completion remains null until cleanup settles,
then stays immutable and inspectable. Unfinished disposal selects noncommit and
records operational cleanup faults without replacing an application exception.

Begin/work/commit tokens belong to their operations. Canceling a completed begin
or callback token creates no lifetime cancellation. Rollback-only refusal wins
over the commit-token checkpoint: cancellation before invocation is NotCommitted;
ordinary failure/cancellation after invocation is Unknown. Independent positive
RollbackTimeout defaults to thirty seconds. Expiry requests cancellation and still
awaits the real provider; it neither detaches work nor forcibly terminates it.

Applications close entry points, finish/cancel work, await every transaction
owner, then dispose sources and host/DI dependencies. RM-02 never disposes the
application source or clears its pool. It installs no global owner registry,
host-drain service, scheduler or ambient current transaction.

## Workflows and EF boundary

Enclose the short database portion of a workflow. Pipeline error/compensation
does not imply database rollback. Inspect its existing result/report, including
failures returned without throwing, and explicitly mark/complete the database
unit. State-machine state publication and database commit are different boundaries.
A later workflow failure cannot undo committed database or external effects.

An EF recipe can use the public native callback: construct a temporary context
using the supplied connection with connection ownership disabled, enlist using
`UseTransactionAsync` and the supplied transaction, await EF work, then dispose
the context before returning. The CIS owner retains commit/rollback/disposal.
Avoid nested helpers, independent EF transaction ownership and automatic replay.
This is an unexecuted recipe, not a shipped or qualified EF adapter/dependency.

## Portable consumer tests

The optional [CommonTesting package](../common-testing.md) supplies
ScriptedLocalTransactionFactory. Queue and retained-owner capacities default to
sixteen each. Exhaustion throws before consuming the next script, without eviction.
Enqueue() scripts clean commit, Enqueue(completion) scripts immutable commit facts,
and EnqueueBeginFailure(exception) preserves the original begin fault. Use a new
factory when retained recordings are full.

```csharp
using CIS.CommonTesting;
using CIS.Transactions;

var scripts = new ScriptedLocalTransactionFactory();
scripts.Enqueue();
ILocalTransactionFactory factory = scripts;
await using ILocalTransaction owner = await factory.BeginAsync("LedgerDatabase");
// Pass owner.Context to the application's fake stores.
owner.Context.MarkRollbackOnly("app.refused");
TransactionCompletion result = await owner.CommitAsync();
bool refused = result.Outcome == TransactionOutcome.NotCommitted && !result.CommitAttempted;
```

CreatedTransactions exposes the context, status/completion and CommitRequested,
RollbackRequested and DisposeRequested flags after completion. Marking or a
pre-canceled commit refuses a scripted Committed result. Explicit rollback and
unfinished disposal normalize noncommit facts while retaining applicable cleanup
diagnostics. Native use throws NotSupportedException before invoking work.
These logical scripts execute no SQL, native acquisition, timers or atomicity proof.
Scripted DurationSeconds is a supplied fact, defaulting to null, rather than a
measured native lifetime.

The existing [outside-friend consumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting/CommonTestingPackageTests.cs)
compiles substitutions against actual Core/CommonTesting packages. Authored source
is not passing evidence. Consult [current qualification](../../changes/CIS-0017/verification.md)
for executed provider/platform groups, candidate hashes and remaining limits.
Broader release qualification remains separate.

## Durable keys and committed-result recovery

Compose the [Operations coordinator](../operations/core.md) for a durable scoped
identity, original-result replay and fresh authorized recovery. Pass the original
context and await the complete Execute before requesting owner completion.
Prepared is staged business data; inspect the actual transaction completion
separately. After Unknown, an independent lookup can recover the original result;
a missing lookup does not resolve persistence and commit is never retried.
