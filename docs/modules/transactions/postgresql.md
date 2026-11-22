---
title: PostgreSQL local transactions
type: document
status: draft
cis:
  stable_id: cislib.document.modules.transactions.postgresql
---

# PostgreSQL local transactions

The reference integration uses Npgsql **10.0.4** against an automatically owned
Testcontainers PostgreSQL **18** service. Npgsql is a private test/reference dependency, outside
Core's shipping closure. Its selected logging abstraction is the repository's
existing **10.0.12** pin. See the [dependency profile](../../changes/CIS-0017/dependency-profile.md)
and [portable transaction contract](../../../contracts/transactions/README.md).

The compiling reference is
[PostgresAcceptanceConsumer](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions/PostgresAcceptanceConsumer.cs),
with its [real-provider tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions/PostgresTransactionTests.cs).
Its business, operation-result and outgoing tables are acceptance fixtures;
they do not implement production RM-03/RM-04 storage or delivery.

## Application-owned data source

Create one host-lived `NpgsqlDataSource` from private application configuration.
Register the existing source without opening a connection or building another
provider inside the registration callback:

```csharp
services.AddCisAdoNetDatabase(
    "AcceptanceDatabase",
    _ => applicationDataSource,
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

The [fixture's composition](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Transactions/PostgresTransactionFixture.cs)
compiles this registration against the actual public API. An explicit owner
lends its connection and transaction to each awaited `UseAdoNetAsync` callback.
Each store creates a command, assigns `command.Transaction`, uses parameters and
disposes the command before returning. The three stores run sequentially outside
one another's callbacks. Commit is requested once, after all participants settle.

The consumer explicitly chooses whether to write business/outgoing rows. A
declined business result can therefore persist just its result row while the
transaction still completes `Committed`. A caught native error must be marked
rollback-only before returning normally. An escaping native failure is marked by
RM-02. Neither application branch adds transaction replay.

Drain every owner before the application disposes its source. RM-02 disposes the
owned native transaction and connection lease; it does not dispose the source or
clear its pool. Npgsql normally returns disposed connection leases to that
source's pool. [Npgsql data sources and pooling](https://www.npgsql.org/doc/basic-usage.html)

## Automatically owned native tests

The integration assembly lazily starts its pinned PostgreSQL 18.7 image through
Testcontainers for .NET. Docker chooses the host port, and a typed owner supplies
the generated credentials and actual mapped endpoint. A selected native test
fails when Docker, its image or authenticated readiness is unavailable. It never
selects an existing developer database or requires a manually written connection file.

Normal cases share one service within the integration run and retain independent
schemas, data sources and transaction owners. The generated connection keeps
these bounds; credentials never belong in command arguments, tracked settings or output:

```text
Pooling=true;Minimum Pool Size=0;Maximum Pool Size=4;
Timeout=15;Command Timeout=30;Cancellation Timeout=2000;
Enlist=false;No Reset On Close=false;
SSL Mode=Disable;GSS Encryption Mode=Disable
```

The disabled native encryption modes apply only to this owned loopback profile.
Non-loopback PostgreSQL Docker endpoints fail clearly and require a separately
qualified transport-security profile. The fixture does not select a passfile, certificate or
operator Kerberos credential. Npgsql defines command/open timeouts in seconds
and cancellation timeout in milliseconds; these are native cooperative limits,
not a claim that RM-02 can forcibly terminate an uncooperative provider.
[Npgsql connection settings](https://www.npgsql.org/doc/connection-string-parameters.html)

From `implementations/csharp`, run the ordinary test project with the repository's
pinned SDK/runtime and restored lock files:

```text
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~PostgresTransactionTests
```

The class is not parallelized. Each case owns one fresh GUID schema, exactly
three ordinary logged tables, at most two small rows per table and payloads
bounded to 1 KiB. Finally cleanup drops only that created schema and then disposes
the owned source. Its lease keeps the service alive until client cleanup settles.
Integration-only assembly cleanup disposes started services and their owned
volumes, with the enabled Resource Reaper as fallback. Fast checks exclude
`RequiresDocker` and do not start or contact Docker. See the
[canonical testing commands](../../../implementations/csharp/tests/README.md).
The service verifies `fsync`, `full_page_writes` and `synchronous_commit` are all
enabled; the Testcontainers module's nondurable defaults are explicitly replaced.

The eleven cases cover identical native handles across the three participants,
independent-session visibility before/after commit, complete rollback after a
later write fails, caught-error marking, declined-result persistence, explicit
rollback/uncommitted disposal, three actual server isolation settings, and source
survival across independent owners. Active cancellation first obtains its own
advisory blocker and confirms the query/backend relationship using an independent
`pg_blocking_pids` observation. Only then does it cancel and actually await the
native callback. The blocker and every pending task are released/joined in
`finally`; polling delay alone is never evidence of an active query.
[PostgreSQL blocking-session information](https://www.postgresql.org/docs/18/functions-info.html)

Actual executed lanes and exact candidates belong in the
[current verification record](../../changes/CIS-0021/verification.md); the
[original RM-02 record](../../changes/CIS-0017/verification.md) remains historical. Authored source is
not passing evidence. Scripted uncertain-commit tests elsewhere do not qualify a
real lost-acknowledgement/network failure. An actual `Unknown` must remain unknown;
later rollback or disposal does not establish whether the original commit
persisted and does not make replay safe.

## EF participation recipe — unexecuted

No EF adapter or EF package is delivered or qualified here. A future application
using a compatible relational EF provider can construct its short-lived context
inside `UseAdoNetAsync`, configure the borrowed connection with connection
ownership disabled, and await `Database.UseTransactionAsync(transaction, token)`.
Await its database work and dispose that context before returning from the
callback. Keep completion on the CIS owner; the EF context must not begin,
commit, roll back or dispose the borrowed native transaction/connection.

Do not enable an implicit retrying execution strategy around this explicit
transaction, retain the context beyond the callback, or call another
transaction-aware service from inside that callback. EF can use savepoints, but
swallowing an error still requires explicit RM-02 rollback-only marking. This is
an integration recipe requiring its own chosen-provider validation, not executed
EF evidence. [EF relational transaction sharing and execution-strategy limits](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
