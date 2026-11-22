# PostgreSQL inbox and outbox

`PostgreSqlMessagingStore` implements the existing `IMessagingStore` in Core with
BCL ADO.NET. Applications select and own their `DbDataSource` and native provider;
Core acquires neither Npgsql nor RabbitMQ.Client. The initial qualified target is
PostgreSQL 18, UTF8, with block size at least 8192 bytes and normal durability.
The store performs no broker operation, handler invocation or owner completion.

## Explicit setup

Install the independent messaging schema deliberately with deployment privileges:

```csharp
await using ILocalTransaction install = await deploymentFactory.BeginAsync("Messaging");
await PostgreSqlMessagingSchema.InstallAsync(install.Context, "cis_messaging", cancellationToken);
TransactionCompletion installed = await install.CommitAsync(cancellationToken);
// Inspect Committed / NotCommitted / Unknown; never retry an uncertain commit.
```

The equivalent complete script is returned by
`PostgreSqlMessagingSchema.GetInstallationSql("cis_messaging")`. Core embeds
`CIS.Messaging.PostgreSql.install-v1.sql` and packages the same script at
`contentFiles/any/any/CIS.Messaging/PostgreSql/install-v1.sql`. It is a one-time
installer, not an idempotent migration or automatic repair: existing/partial owned
tables fail. Installation participates in the caller's deployment transaction.
It contains no SQL transaction control or session-setting mutation.

Registration remains passive:

```csharp
var history = new MessageRetentionPolicy(); // indefinite by default
services.AddCisAdoNetDatabase("Messaging", _ => applicationOwnedDataSource,
    new AdoNetDatabaseOptions
    {
        DefaultIsolation = TransactionIsolation.ReadCommitted,
        SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
    });
services.AddCisMessageStore(new MessageStoreDefinition("Messaging", "Messaging"));
services.AddSingleton<IMessagingStore>(new PostgreSqlMessagingStore(
    new PostgreSqlMessagingOptions
    {
        Schema = "cis_messaging",
        CommandTimeoutSeconds = 15,
        Retention = history
    }));
```

The application installs before starting delivery workers. Store construction,
definition registration and the first publish/consume never create schema.
Every store operation checks the installed messaging version before table work.
Identifiers are quoted after strict ASCII validation, values use parameters,
and each command has a captured cooperative timeout of 1–300 seconds. Unsupported
isolation is rejected before native work; it is never downgraded.

## Original transaction and immutable bytes

Pass the original `ILocalTransactionContext` to staging, inbox arbitration,
application stores, Operations and reply/outgoing staging. Await each native
callback sequentially; handlers and broker waits run outside those callbacks.
Only the owner commits/disposes. An outgoing receipt is uncommitted staging.
Store staging failures, including pre-SQL validation and finite capacity failure,
poison that owner while preserving the original fault.

The five installed tables are schema metadata, owner storage mode, outbox,
processing and separate rejections. They are ordinary logged tables. The inbox
primary key is the exact C-collation application/environment/subscription/UUID.
Insertion uses a nondeferrable database unique constraint. A conflicting insert
is followed by a new statement snapshot and row lock before business work.
Digest, original evidence and operation scope/resource comparisons occur under
that lock. Unknown commit never proves rollback; reacquisition arbitrates against
any earlier transaction that can still commit.

Each immutable envelope is stored as exact bytes with SHA-256 and length. The
outbox and inbox may each retain their own physical envelope cell. Metadata
reservation returns no body. The publisher must admit encoded byte capacity
before calling `ReadReservedMessageAsync`; native materialization plus strict
decoding can temporarily own two envelope-sized arrays. Normal runtime staging
and optional adapter copies need their own admitted overlap. Native provider
buffers and PostgreSQL storage overhead are separate from this encoded budget.

## Reserve, publish and settle

`ReserveAsync` selects at most 32 eligible/expired records with `FOR UPDATE SKIP
LOCKED`, using database time, an owner UUID and increasing fencing version.
Commit this short reservation transaction before publication. Read one body with
the matching unexpired fence, settle the actual broker invocation outside SQL,
then call `RecordPublicationAsync` in a new short owner and inspect its completion.

Confirmed becomes Published. Unknown remains recoverable Pending with identical
MessageId/bytes; it cannot be classified as permanent routing evidence. Transient
backoff is separate from scheduled handler ordinals. Permanent configuration or
unroutability becomes retained Blocked with a safe code. Conditional updates reject
stale or expired ownership. They cannot prevent a delayed old sender transmitting;
receiver idempotency protects committed effects when that occurs.

`ReplyDestinationExpired` is the narrow exception for a provider-classified obsolete
reply endpoint. It requires an actual Reply row, records a fixed named retired
disposition and remains inspectable as `outbox.reply_destination_expired`; it is
not Published or business failure. Known retired replies are excluded from pending
responsibility observations. Their payload follows captured finite history policy,
or remains indefinitely by default. Ordinary Blocked work stays unresolved. This
exception never expires commands, retries, processing evidence or Operations results.

Publication facts supplied directly in SQL-store tests are scripted broker-port
outcomes. They do not prove native RabbitMQ confirmation or routing.

## Processing, retries and quarantine

`ClaimProcessingAsync` acquires the canonical identity. `CompleteProcessingAsync`
requires its expected version/ordinal/generation and the same native claim
transaction. A linked reply must exist under the same processing key. All of this
is staged until actual owner commit. A committed unfinished Ready row is corrupt,
not permission to execute. Handled suppresses effects even after payload cleanup.

After actual failed-unit rollback, `SecureDispositionAsync` uses a fresh owner.
The initial failed claim may have disappeared. A failed retry instead restores
its previously committed RetryScheduled state: the current ordinal and handoff
must still match. The store preserves concurrent Handled or later secured winners
and atomically inserts exactly one targeted retry/terminal row. The database unique
handoff index includes logical receiver, purpose, generation and ordinal.

Internal retry/redrive metadata must match its retained outgoing record, original
digest/provenance, generation and operation binding. Stored producer evidence is
audit data, not delegated authority. The dispatcher must first authorize the
current retry service and validate the actual same resolved operation resource.

`RecordRejectionAsync` writes a different rejection UUID plus its quarantine
outbox record. It never inserts, locks or alters the canonical processing identity.
`TargetSubscription` is separate metadata, so rejection can target the affected
consumer without inventing a canonical processing key. Untrusted or conflicting
messages cannot poison legitimate processing. Malformed/oversize bytes use the
provider's verified safe native quarantine path instead of this bounded envelope
store. Failed/unknown responsibility settlement never licenses source acknowledgement.

## Explicit administration and retention

Infrastructure ports contain no implicit authorization grant. The application
administration service must perform current capability checks before calls or
count disclosure. `InspectAsync` returns at most 256 safe metadata records with
UUID pagination, excluding payload, provenance and operation keys/scopes. A route
filter selects outgoing rows; a processing-key filter selects associated records.
`ObserveAsync` reads at most 256 plus one overflow witness. Incomplete counts are
null, not zero. Scrapes consume the later retained observation, never execute SQL.

`RedriveAsync` accepts only retained Terminal work at an inspected version. It
checks both captured and current supported horizons, retains original identity
and bytes, increments the explicit audited generation and secures a new targeted
outbox row. Null horizon means indefinite while protection and payload remain.
Successful/active work is never reopened; missing payload or an ended horizon is
an explicit result. The application must authorize/audit the operator separately.

Retention/horizons are copied for new rows. Payload expiry and protection/terminal
eligibility use database time; later configuration does not shorten those saved
facts. `MaintainAsync` requires the exact selected policy and changes at most 256
rows in one caller-owned transaction, returning candidate counts only. Confirmation
comes from that owner's known Committed completion. It can clear eligible published
payloads, leave digest/identity tombstones, and delete eligible complete protection
or resolved terminal/rejection records after the supported horizons. Actual whole
processing-row deletion ends its duplicate guarantee. Pending, Reserved, Blocked,
scheduled and dependent provenance/reply responsibility is retained. Operations
records and Operations maintenance are never touched. No schedule is selected.

`MaximumRetainedEnvelopeBytes` optionally provides transactional envelope admission
per owner application/environment. Select it before first use. Finite mode locks
one accounting row within the participating transaction, so it serializes finite
quota writers; no preflight count races concurrent staging. Every retained envelope
cell is counted once, including distinct inbox/outbox copies. Rollback restores
capacity; cleanup releases capacity only with its own actual commit. Null mode is
indefinite and avoids that update lock. The persisted mode cannot silently change
over unaccounted history: use an explicit offline deployment migration or new
schema. This envelope quota excludes metadata, indexes, WAL and engine overhead;
applications still own physical database headroom monitoring. Never delete accepted
responsibility to satisfy a quota. Capacity failure is `MessageStorageCapacityException`,
which the client maps to explicit overload after settling the poisoned owner.

The [SQL fixture/tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageStoreTests.cs)
reuse ordinary MSTest, existing test-only Npgsql and the existing narrow real-commit
then failure seam. A simulated acknowledgement loss is not a real network fault.
Store proof is distinct from subsequent actual broker/host/RPC acceptance; see
[CIS-0019 verification](../../changes/CIS-0019/verification.md) for actual results.
