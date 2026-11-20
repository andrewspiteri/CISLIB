---
title: PostgreSQL local transactions
type: document
status: draft
cis:
  stable_id: cislib.document.modules.transactions.postgresql
---

# PostgreSQL local transactions

The reference integration uses Npgsql **10.0.4** against the explicitly provisioned
PostgreSQL **18** fixture. Npgsql is a private test/reference dependency, outside
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

## Explicit isolated qualification

Provision only the approved fresh local fixture described by the
[implementation plan](../../changes/CIS-0017/implementation-plan.md). Its Docker
image is pinned by digest; the provisioning receipt records the actual server
version, image and resource limits. The test itself neither provisions Docker
nor chooses a default database.

Write the generated connection string into a private file outside tracked
source. Set `CIS_RM02_POSTGRES_CONNECTION_FILE` to that file's absolute path. Do
not put the password in command arguments, committed settings or test output.
The file must specify a loopback host and the fresh fixture's database, user and
password, with these bounded settings:

```text
Pooling=true;Minimum Pool Size=0;Maximum Pool Size=4;
Timeout=15;Command Timeout=30;Cancellation Timeout=2000;
Enlist=false;No Reset On Close=false;
SSL Mode=Disable;GSS Encryption Mode=Disable
```

The disabled native encryption modes apply only to this isolated loopback
qualification target. The fixture does not select a passfile, certificate or
operator Kerberos credential. Npgsql defines command/open timeouts in seconds
and cancellation timeout in milliseconds; these are native cooperative limits,
not a claim that RM-02 can forcibly terminate an uncooperative provider.
[Npgsql connection settings](https://www.npgsql.org/doc/connection-string-parameters.html)

From `implementations/csharp`, run the ordinary test project with the repository's
pinned SDK/runtime and restored lock files:

```text
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PostgresTransactionTests
```

The class is not parallelized. Each case owns one fresh GUID schema, exactly
three ordinary logged tables, at most two small rows per table and payloads
bounded to 1 KiB. Finally cleanup drops only that created schema and then disposes
the owned source. Missing explicit fixture configuration produces a clear
inconclusive check, never an in-memory substitute or a connection to a default
database.

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
[verification record](../../changes/CIS-0017/verification.md). Authored source is
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
