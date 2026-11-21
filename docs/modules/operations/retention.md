---
title: Explicit operation retention and uncertain-result recovery
type: guide
status: Draft
cis:
  stable_id: cislib.document.modules.operations.retention
---

# Retention and recovery

Result payload and duplicate protection are separate facts. Both retain
indefinitely by default. A finite `OperationRetentionPolicy` stores expiry from
the database recording time when the result is prepared. That time is not an
observed commit time. New definition policy affects new rows; existing persisted
expiry values remain unchanged. Protection cannot expire before full-result
retention. Positive durations round up to PostgreSQL's microsecond precision.

Lookup and matching execution apply logical result expiry even when no
maintenance has run. Expired does not mean absent. Comparable changed input
returns KeyConflict before matching-payload expiry. An unsupported fingerprint
comparison returns Incompatible before either; unsupported result readers or
expected decode failure never authorize a new handler. An unexpectedly committed
incomplete/corrupt row also keeps protection. These outcomes do not implicitly
delete data or start a repair workflow.

Clearing expired bytes leaves a tombstone with full identity and fingerprint.
A persisted protection expiry only makes a row eligible for deliberate removal;
uniqueness continues until the whole row is actually deleted. After that deletion
the old duplicate-protection guarantee has ended and the same scoped key can
represent another protected operation. Configure a retention period that matches
the application's real duplicate look-back, rather than treating it as a cache
TTL. Unsupported decoding does not trigger deletion: only explicit authorized
maintenance and persisted protection eligibility can remove a row.

## One privileged bounded batch

`IOperationRetentionMaintenance` is a separate mockable public port. Select its
PostgreSQL implementation explicitly after registering the actual journal:

```csharp
services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(
    new PostgreSqlOperationOptions { Schema = "cis_operations" }));
services.AddCisOperations();
// AddCisOperation definitions include explicit nonempty MaintenanceCapabilities
// for only the operations whose privileged maintenance is enabled.
services.AddCisPostgreSqlOperationRetentionMaintenance();
```

The last extension uses `CIS.Operations.PostgreSql`. It accepts no second schema,
source or database selection. It requires the single passive selected
`PostgreSqlOperationJournal` instance and uses that instance's captured schema and
command timeout. A changed provider journal or operation metadata snapshot is
rejected. Registration opens no connection, installs no schema and selects no
schedule. A custom non-PostgreSQL journal can supply its own public maintenance
substitute rather than acquiring an implicit SQL backend.

An empty captured `OperationDefinitionOptions.MaintenanceCapabilities` collection
leaves maintenance disabled for that operation. Otherwise RunAsync resolves the
application's actual scope/resource and awaits existing capability authorization
against the captured server application/environment. This happens before SQL,
owner acquisition or count disclosure. Requested scope text, caller projection
and possession of a key grant no permission. Keep a logical scope's resource
binding stable across definitions/deployments; remapping old scope text to an
unrelated resource cannot safely authorize disclosure or deletion of old data.
Authorization, audit and resolver faults propagate normally.

```csharp
OperationMaintenanceResult batch = await maintenance.RunAsync(
    new OperationMaintenanceRequest("Debit", "requested-account", maximumRows: 256),
    cancellationToken);

if (batch.Completion?.Outcome == TransactionOutcome.Committed)
{
    int cleared = batch.ConfirmedPayloadsCleared!.Value;
    int deleted = batch.ConfirmedRowsDeleted!.Value;
    // Use these bounded confirmed counts according to application policy.
}
```

Each call selects exactly one registered operation and trusted scope, filtering
the complete application/environment/scope/operation identity. It acquires its
own authoritative ReadCommitted owner through the existing RM-02 factory and
validates the returned database/isolation before native access. The caller passes
no active write context. The service performs one batch and actually commits or
settles/disposes only that owned transaction.

The bound is1..256 rows, initially256. One SQL statement locks eligible finalized
rows in ordinal key order with `FOR UPDATE SKIP LOCKED`. It clears expired
payload/error fields or removes whole rows whose persisted protection period
permits it. It fetches only two counts, no result payloads. Locked or incomplete
rows remain protected. SKIP LOCKED belongs only to maintenance, never claim or
result lookup. There is no internal batching loop, timer, polling interval,
Heartbeat registration, background worker or automatic retry.

Denied and Disabled disclose no candidate/confirmed counts or completion.
Executed carries CandidatePayloadsCleared, CandidateRowsDeleted and the owned
transaction's actual `TransactionCompletion`. Executed means the call settled,
not that the candidate changes are confirmed. Confirmed count properties are
non-null only for Committed. Unknown keeps both confirmed properties null;
cleanup errors cannot turn Unknown into rollback, and release diagnostics cannot
downgrade a known Committed outcome. An unexpected work failure is preserved
while owned cleanup actually settles. No transaction, handler or SQL statement is
silently retried.

## Uncertain original commit

After Prepared, await the caller's actual CommitAsync. Unknown means native commit
was invoked without confirmed completion. Do not retry commit, substitute a new
key or infer rollback from later cleanup. The original owner must actually settle
before its source and dependencies are released.

Use authorized GetResultAsync through a fresh authoritative owner. Found returns
the original business result. Its read-scope NotCommitted describes that new read
owner and does not revoke the known original result. NotFound alone does not
resolve an uncertain original commit. A deliberate same-key/same-input attempt
using another fresh owner goes through normal uniqueness arbitration; it can
Replay a committed original or execute after an actual rollback. A handler may
therefore run again after rollback, while the guarantee concerns committed
protected effects while the retained row remains.

The [real-provider tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/PostgresOperationRetentionTests.cs)
cover expiry before maintenance, policy permanence, mismatch/version precedence,
tombstones, actual row deletion,257-row boundary, skipped row locks, a maintenance
deletion race with lookup/arbitration, and a real claim deadlock followed by an
explicit fresh transaction. Server blocking relationships and the actual
deadlock counter support the race observations; a delay alone supplies no proof.
The test fixture's advisory-lock triggers are not production journal SQL.

The same tests use a
[thin test-only native commit decorator](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Operations/NativeCommitBoundarySource.cs).
It delegates SQL to the real selected Npgsql connection/transaction, performs a
real native commit and then reports a controlled failure through RM-02's actual
commit boundary. RM-02 consequently reports Unknown; independent committed rows
and fresh lookup recover the original. This is **simulated acknowledgement loss**,
not a real network fault or a fabricated completion. Another controlled failure
after actual native transaction disposal proves Committed remains Committed.
Maintenance counts are qualified against both these outcomes. No Core test hook,
SQL proxy, transport-fault framework or outgoing-delivery implementation is added.

See [Core ownership](core.md), the [PostgreSQL journal](postgresql.md),
[consumer testing](testing.md) and the current
[verification record](../../changes/CIS-0018/verification.md) for actual executed
lanes/candidates and remaining qualification limits. Authored test source and
scripted application outcomes alone are not native recovery or release evidence.
