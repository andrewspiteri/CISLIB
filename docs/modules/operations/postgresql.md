---
title: PostgreSQL durable operation journal
type: document
status: draft
cis:
  stable_id: cislib.document.modules.operations.postgresql
---

# PostgreSQL journal

`PostgreSqlOperationJournal` in `CIS.Operations.PostgreSql` is the production
`IOperationJournal` adapter in Core. It uses BCL ADO.NET through the original
RM-02 context; Core acquires no Npgsql dependency. Applications select and own
their native `DbDataSource`, credentials, server and transaction completion.
The reference native tests use the existing private Npgsql10.0.4 dependency.

Create the adapter and register it in the application's existing service
collection alongside its operation definitions and established authorization:

```csharp
services.AddCisAdoNetDatabase("Ledger", _ => applicationDataSource,
    new AdoNetDatabaseOptions
    {
        DefaultIsolation = TransactionIsolation.ReadCommitted,
        SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
    });
services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(
    new PostgreSqlOperationOptions
    {
        Schema = "cis_operations",
        CommandTimeoutSeconds = 15
    }));
services.AddCisOperations();
// AddCisOperation captures the application-specific normalizer, result codec,
// trusted scope resolver and explicit capability requirements.
```

The compiling [fixture composition](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationFixture.cs)
uses these actual public contracts. The same explicit schema can be installed in
each selected application database. The adapter uses the supplied context's
actual connection and transaction; it does not select another database, own the
source or add a mapping/connection framework. Schema identifiers are copied,
ASCII letter/underscore led, at most63 characters, and quoted. Commands use
parameters for every identity, fingerprint and result value.

## Explicit installation and ownership

The package contains `contentFiles/any/any/CIS.Operations/PostgreSql/install-v1.sql`
and embeds the same versioned SQL. Obtain the complete script for one validated
schema with `PostgreSqlOperationSchema.GetInstallationSql("cis_operations")`.
Execute it deliberately using application-controlled deployment credentials.
No registration, adapter constructor, Execute or GetResult performs installation
or migration. The adapter checks the installed schema version before journal SQL.

This is a **one-time installer**. It creates its two owned tables with ordinary
`CREATE TABLE`, so existing or partially installed owned tables cause a native
error. It never accepts an existing unlogged/malformed table solely because a
version value says1. Inspect and repair or migrate such a deployment explicitly;
there is no automatic schema auditor or repair lifecycle. An application can run
the script inside its own deployment transaction. The script contains no SQL
transaction control or session mutation.

The installer requires PostgreSQL18 or later, UTF8 server encoding and a block
size of at least8192 bytes. PostgreSQL18 is the selected native qualification
target; later versions are not qualified by that check alone. The two ordinary
logged tables are schema metadata and the operation journal. Business and
outgoing tables belong to the application. Runtime access requires the relevant
SELECT/INSERT/UPDATE permissions; deployment credentials need schema/table DDL.
No provider-specific migration package or elevated application connection is
selected by CISLIB.

The journal's nondeferrable primary key contains all five exact identity columns:
application, environment, trusted scope, operation name and opaque key. Built-in
deterministic `C` collation preserves ordinal case-sensitive equality. Existing
255-scalar application/environment limits allow1020 UTF8 bytes each; scope256,
operation64 and key128 give a2488-byte maximum data tuple. This full identity
uniqueness does not rely on a hash, instance, version, trace or credential value.

## Native sequencing and completion

PostgreSQL v1 execution and lookup use ReadCommitted. The coordinator and adapter
reject RepeatableRead/Serializable before claim or handler effects, without
downgrading the selected owner. This is an explicit supported isolation boundary.

`INSERT ... ON CONFLICT DO NOTHING RETURNING` establishes the sole winner.
On conflict, a separate SELECT obtains a new statement snapshot, because the
INSERT's snapshot need not contain the winning row. If concurrent permitted
maintenance removed that row before the SELECT, the adapter returns bounded
`MissingAfterConflict`; the coordinator returns Contended without invoking a
handler or looping. A same-native-transaction claim is never reported as a
committed prior row. PostgreSQL's statement behavior is described in its
[ReadCommitted contract](https://www.postgresql.org/docs/18/transaction-iso.html)
and [INSERT documentation](https://www.postgresql.org/docs/18/sql-insert.html).

Only an acquired claim reaches application work. The handler runs outside the
journal callback, using the original explicit context. A separate callback
conditionally finalizes exactly one incomplete row with the original full
identity/fingerprint and native `xid8` transaction identity. A fresh transaction
cannot finalize an unexpectedly committed incomplete row. Such a row retains
protection and produces safe unavailable/incompatible outcomes. It is never an
invitation to execute the business handler again.

Finalization records database preparation time and immutable optional expiry
timestamps. These are not observed commit timestamps. Indefinite result and
duplicate retention remain the defaults. Positive finite durations round up to
PostgreSQL's microsecond precision, so a positive sub-microsecond policy cannot
silently become zero retention. Logical result expiry is independent of any
later explicit maintenance.

Prepared means staged inside the caller's transaction. The application awaits
all Execute work before requesting owner commit or disposal, then examines the
actual RM-02 completion. Rollback or unfinished disposal removes the claim,
business and outgoing effects together. Replayed returns the original committed
encoded result without another handler. GetResult uses and settles a fresh
authoritative read owner; a read owner's NotCommitted describes that read scope.
It does not revoke the known original committed result. See the
[Core ownership guide](core.md).

Command timeout defaults to15 seconds and can be configured only from1 to300.
The supplied cancellation token and real native settlement still apply; this is
a cooperative timeout, not forced termination. Production SQL does not set
`lock_timeout`, alter isolation or issue transaction-control statements.
Only claim-INSERT SQLSTATE55P03 maps to Contended;40001/40P01 map to RetryRequired.
These errors first escape `UseAdoNetAsync`, allowing RM-02 to poison the owner.
Later55P03 and unexpected errors retain their original provider exception.
Every retry uses a fresh explicit owner; no statement, handler or commit is
automatically replayed. See [PostgreSQL error codes](https://www.postgresql.org/docs/18/errcodes-appendix.html).

## Focused native consumer

[PostgresOperationTests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationTests.cs)
uses one fresh GUID schema per case on the explicitly provisioned isolated
PostgreSQL18 fixture. `CIS_RM03_POSTGRES_CONNECTION_FILE` must name its private
absolute connection-file path. Missing setup is inconclusive; no default database,
operator credential, in-memory engine or existing database is selected.

The source validates private loopback, explicit database/user/password, no
passfile, pool0..6, native timeouts15/30/2000, Enlist=false and reset-on-close.
Disabled SSL/GSS applies only to that private fixture. The fixture checks server
major version and fsync/synchronous_commit/full_page_writes, and exactly four
ordinary logged tables. Cases remain serialized and bounded to the approved
row/payload/lease profile. Every pending native task is joined before owned
schemas and sources are released.

Competing sessions are observed through `pg_blocking_pids` before winner release,
cancellation or timeout settlement. Polling delay only spaces observation.
Fixture-only `SET LOCAL lock_timeout` selects an actual55P03 case; it is absent
from production adapter SQL. A tiny trigger emits55P03 at terminal write to prove
that later failures propagate rather than masquerade as claim contention.
The maximum2488-byte identity case uses varied supplementary Unicode and forces
PLAIN table storage, confirming actual uncompressed values fit the unique index.

One ordinary end-to-end case writes a bounded `RM03_WORKLOAD` JSON line to its
test output with actual row/payload sizes, committed row shape, concurrency,
elapsed observation, server/OS and executed Core hash. It is a focused workload
observation, separate from installer/component checks and no benchmark campaign.
Actual executed lanes and results belong in the
[verification record](../../changes/CIS-0018/verification.md). Authored source
alone proves no atomicity, contention, recovery, package or release result.

Explicit privileged maintenance and uncertain-commit recovery have their own
WORK-004 acceptance. This journal work does not select a scheduler, background
worker, retry queue, public Pending lifecycle or outgoing-delivery module.
