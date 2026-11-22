# PostgreSQL ownership authority

`CIS.Coordination.PostgreSql.PostgreSqlCoordinationProvider` implements the public
`ICoordinationProvider` and `ICoordinationGuard` ports in Core using BCL ADO.NET.
Core has no Npgsql dependency. The application selects its native provider and
owns its data source/pool, deployment credentials and eventual disposal.

## Install and select one authority

Version one requires PostgreSQL 18 or later, UTF8 server encoding and a block
size of at least 8 KiB. It uses one authoritative writer, ordinary logged tables
and a logged positive BIGINT generation sequence. Production durability requires
durable acknowledged writes and retained generation history; use the established
`fsync`, `full_page_writes` and `synchronous_commit` production settings.

Generate an authority UUID once for the deployment, retain it in trusted
configuration, and run the explicit installation SQL with deployment credentials:

```csharp
string sql = PostgreSqlCoordinationSchema.GetInstallationSql(
    configuredAuthorityId, "cis_coordination");
// Execute sql deliberately on the selected authoritative database.
```

The packaged SQL is also available at
`contentFiles/any/any/CIS.Coordination/PostgreSql/install-v1.sql`. Its schema and
authority placeholders are replaced by `GetInstallationSql`; do not execute the
unexpanded resource. The installer is one atomic PostgreSQL statement. It refuses
an existing installation, including one whose allocator was deleted or damaged.
It never resets a sequence, substitutes a new identity or silently repairs
acknowledged ownership history. Version one has no automatic migration worker.

Register the existing explicit transaction database and select the provider in
the application's one DI container:

```csharp
services.AddCisAdoNetDatabase("Ledger", _ => applicationOwnedDataSource, new()
{
    SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
});
services.AddSingleton(provider => new PostgreSqlCoordinationProvider(
    provider.GetRequiredService<ILocalTransactionFactory>(), "Ledger", new()
    {
        AuthorityId = configuredAuthorityId,
        Schema = "cis_coordination",
        CommandTimeoutSeconds = 15
    }));
services.AddSingleton<ICoordinationProvider>(provider =>
    provider.GetRequiredService<PostgreSqlCoordinationProvider>());
services.AddSingleton<ICoordinationGuard>(provider =>
    provider.GetRequiredService<PostgreSqlCoordinationProvider>());
```

Use `CIS.Transactions.Hosting`, `CIS.Coordination`,
`CIS.Coordination.PostgreSql` and the ordinary Microsoft DI extensions. Register
the managed coordination/job composition using the [hosting guide](hosting.md).
Do not register a second transaction factory or service provider. Construction
copies options and performs no source resolution, installation or database I/O.
Startup calls the provider's `ValidateAsync` without acquiring a duty.

`AuthorityId` is required and nonempty. Schema identifiers are one to 63 ASCII
letters/digits/underscores, beginning with a letter or underscore. The native
command timeout is one to 300 seconds, initially fifteen seconds. This native
cooperative timeout is distinct from the managed client's two-second request
timeout. Cancellation requests cannot discard unfinished native work: the
operation remains owned and observed until its transaction and cleanup settle.

Every physical operation verifies the configured authority UUID, schema version,
writer status, logged grant table and allocator properties. Metadata mismatch,
unsupported schema/backend or nontransient SQL faults fail clearly; they do not
become Busy. Temporary native infrastructure failures return the appropriate
Unavailable/Indeterminate fact. Invalid options are rejected before store access.

The guard also checks the caller's exact database alias and ReadCommitted
isolation. A grant for another installed authority is rejected even when the
schema/key text happens to match. An independently installed database receives
its own authority UUID. A cloned database retaining that UUID must not operate
as a second authority. Supported failover must preserve acknowledged grant and
generation history; split writers or lossy restoration invalidate the guarantee.
No consensus, disaster-recovery or remote fencing system is supplied here.

## Atomic grant operations

The full environment/scope/duty/resource key is stored in separate `COLLATE "C"`
columns. The complete primary key uses at most 1,920 UTF8 bytes under the portable
limits. Equality remains ordinal and case-sensitive with no normalization.
Owner application and process instance are separate fields, never part of the
key. Acquisition ID is also GrantId in version one.

Acquire inserts or performs a no-op update of the exact unique key. That command
serializes first creation, an existing row, and recreation after pruning. Only
after its row lock has been obtained does a separate `clock_timestamp()` command
determine eligibility. An eligible acquisition obtains `nextval` under that same
serialization, writes its exact owner/acquisition/expiry and commits a short
RM-02 owner. Unrelated keys need no deployment-wide lock. Busy polling still
causes a small row update; use the bounded managed standby policy.

The independent sequence is `AS bigint INCREMENT 1 MINVALUE 1 CACHE 1 NO CYCLE
OWNED BY NONE`. Gaps are permitted. CACHE 1 preserves increasing allocation
across pooled sessions. Allocator exhaustion refuses acquisition and rolls back
the placeholder; it never wraps. Ordinary release, pruning and schema migration
must retain this allocator. Changing/resetting it with administrative SQL is
outside the supported authority contract.

Renewal locks and matches the full key, owner, instance, acquisition and
generation, then checks current authority time. Expired/released grants cannot
be renewed. Release conditionally matches only that same grant; an old release
cannot modify a successor. Releasing an exact expired record is safe, but cannot
revive it. A confirmed renewal preserves all immutable identity and returns an
updated observed expiry and positive remaining validity.

`ResolveAsync` observes only the requested exact acquisition on the writer. A
matching live committed row can support bounded managed recovery. `NotObserved`
describes that instant; it does not establish that an earlier mutation cannot
commit later. The client must not replay an indeterminate acquire mutation.
Busy optionally contains a bounded observed owner without granting ownership.
Snapshots reuse retained observations and perform no database access.

Coordination transactions reuse RM-02's actual completion facts:

| Native fact | Coordination result |
| --- | --- |
| Confirmed committed acquisition/renewal/release | Acquired/Succeeded, including cleanup diagnostics |
| No native commit invoked | Unavailable or the original caller cancellation after cleanup |
| Native commit invoked without confirmed success | Indeterminate; observe the exact acquisition where applicable |
| Current competing grant | Busy with an optional observed-owner snapshot |
| Exact grant missing, released, expired or superseded | NotCurrent for the applicable exact mutation |

No automatic commit retry is performed. Coordination uncertainty is separate from
the application's business transaction outcome. A later cancellation or rollback
does not erase an Unknown commit, and cleanup errors do not erase confirmed
success. Remaining-validity acknowledgements are authority observations; managed
clients subtract request elapsed time and their safety margin.

## Guard the original business transaction

Use the application's original explicit participant. The guard performs no hidden
transaction, commit or recursive native callback:

```csharp
OperationExecution<Receipt> execution = await operations.ExecuteAsync<Input, Receipt>(
    owner.Context, originalOperationRequest, async (context, input, token) =>
    {
        CoordinationGuardResult admission = await guard.ValidateAsync(
            context, lease.Grant, token);
        admission.ThrowIfRejected();

        // Use context.UseAdoNetAsync for protected database changes.
        // Stage any outgoing records through the same original context.
        return await PerformProtectedWorkAsync(context, input, token);
    }, cancellationToken);
// Prepared is not committed. The application completes and inspects owner.
```

The established order is RM-04 inbox where present → RM-03 journal claim →
coordination grant row → application rows → outgoing records. RM-03 invokes its
handler outside native callbacks, allowing the guard and subsequent native work
to run sequentially. Do not wrap `ExecuteAsync` or the guard inside another
`UseAdoNetAsync` callback. Each participant uses the same original context;
applications await the whole execution before completing its owner.

The guard locks the key with `SELECT … FOR UPDATE`, then obtains current time in
a separate command after any wait. It validates exact authority/owner/acquisition/
generation, current state and expiry. Rejection marks that original context
rollback-only even when the caller catches the structured refusal. A successful
guard retains the row lock until application transaction completion. A takeover
must wait; a transaction admitted while valid may therefore commit after nominal
lease expiry before the successor is granted its higher generation.

Keep these transactions short. Do not hold the grant-row lock across an idle wait
or a long external call. Preserve the original RM-03 operation key through
takeover/redelivery. Fresh authorized read-only result recovery remains available
after ownership loss; any new protected effect needs valid guard admission.
Committed RM-04 outgoing intent remains deliverable after ownership ends.

A generation attached to an external request is not enforcement. Another resource
must atomically enforce its own fencing/idempotency contract. A remote owner check
followed by an unrelated database write cannot provide this adapter's atomicity.

## Explicit exact-record cleanup and permissions

`PostgreSqlCoordinationProvider.PruneAsync(exactGrant)` locks the key and checks
post-lock time. It deletes only matching released/expired owner, acquisition and
generation. It leaves a successor unchanged and retains the allocator and
authority metadata. A live record returns Unavailable with
`coordination.prune_live`; no cleanup mutation occurred. Version one does not
select a background sweep or permanent command/renewal history.

Use separate deployment and runtime credentials. Ordinary runtime access needs
the selected schema, grant reads/writes, metadata reads and sequence usage;
explicit pruning additionally needs DELETE. Guarded application transactions
need the row-lock permissions on the grant table as well as their application
permissions. Direct database users with unrestricted table/sequence access can
bypass application authorization or corrupt authority history. UUIDs,
generations, categories and claimed instance identities are not bearer
credentials. Managed scope authorization remains in the existing RM-01 path.

## Focused native evidence

The existing typed, isolated PostgreSQL Testcontainers service supports the new
`Coordination/PostgreSql` cases. Each owns its schema and data-source clients.
They exercise contention, independent keys, exact stale operations, different
pooled sessions, pruning/recreation, allocator damage/exhaustion, cross-authority
rejection, lock-wait expiry and guard/commit/takeover serialization. Blocking is
confirmed using PostgreSQL's actual `pg_blocking_pids` graph. Controlled expiry
updates in test-owned transactions isolate the lock-order risk without claiming
that client timing physically stops a callback.

The existing native commit-boundary seam runs real SQL and commit, then reports
one controlled acknowledgement/disposal failure. It establishes correct mapping
and exact recovery around those real commits; it is not proof of a particular
network fault or deployment failover. The restart case performs a clean stop/start
of one owned container and retained volume, then constructs a fresh provider over
the authority. It is not power-loss, replication or split-brain proof. Read the
change's verification record for actual executed platforms and results;
authored tests and guides do not imply those runs passed.
