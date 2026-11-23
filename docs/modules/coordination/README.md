# Shared coordination

`CIS.Coordination` and `CIS.Jobs` are additive namespaces in `CIS.Core`.
Applications own their database source, business transactions and host lifetime.
The [portable contract](../../../contracts/coordination/README.md) defines grant,
uncertainty, local-state and fencing semantics; [jobs](jobs.md) describes explicit
catalog registration and the separate scheduled-occurrence boundary.

Selected Central Management execution adds a small optional
[new-work admission boundary](managed-admission.md) to Jobs. Unmanaged use
retains its existing behavior; independent ownership, fencing and actual
settlement remain authoritative in both modes.

The scoped `ICoordinationService` resolves a `CoordinationRequest` through the
application's trusted `ICoordinationTargetResolver`, then awaits the existing
scoped `ICapabilityAuthorizer` against the complete actual resource. A requested
scope or constructed `CoordinationTarget` alone grants no permission. The singleton
managed engine receives detached authorized acquisition data and retains no
scoped handler, authorizer or provider.

Use `LocalApplicationIdentity` and the existing explicit
`SecurityHostContextScope` for background native authority. Jobs own a fresh
appropriate scope per finite iteration or admitted scheduled callback; they do
not inherit an ambient HTTP caller. Inspect the exact registered job/resource
before ownership acquisition. Discovery alone does not activate handlers.

## Choose the owned boundary

| Responsibility | Implemented API and guide |
| --- | --- |
| Trusted target and current scoped permission | `ICoordinationTargetResolver`, `ICoordinationService`, existing `ICapabilityAuthorizer`; [portable contract](../../../contracts/coordination/README.md) |
| One PostgreSQL authority and original transaction guard | `PostgreSqlCoordinationProvider`, `PostgreSqlCoordinationSchema`, `ICoordinationGuard`; [installation, permissions and locking](postgresql.md) |
| Independent bounded renewal and terminal local ownership | `CoordinationRuntime`, `ICoordinationLease`; [managed timing and uncertainty](managed.md) |
| Exact registered handler, category and mode | `JobCatalog`, `JobHandlerBinding`, `JobConfigurationSnapshot`; [catalog and API](jobs.md) |
| Local occupancy, business capacity and owned callback scopes | `JobRuntime`; [shared admission](runtime.md), [finite continuous iterations](continuous.md) |
| An already admitted immutable occurrence | `IScheduledJobDispatcher`, `IJobRunSource`; [scheduled dispatch](scheduled.md) |
| Whole local configuration revisions | `JobConfigurationSubscription<T>`; [coordinated capture and adoption](configuration.md) |
| Sequential activation and dependency-preserving shutdown | `AddCisCoordination`, `AddCisJobs`; [existing host integration](hosting.md) |
| Cached health, quiet diagnostics and seven conditional streams | [Observations](observability.md), [metric catalog](../../../contracts/metrics/coordination.md) |
| Consumer substitutes and actual native proof | [Managed testing](testing.md), [controlled scheduled input](scheduled-testing.md), [durable consumers](consumers.md) |

Register the selected provider and original-context guard, trusted resolver and
ordinary application dependencies before selecting hosting. Freeze explicit job
categories/bindings with `AddCisJobCatalog`, then supply the application's
`IJobExecutionScopeFactory` and detached configuration. Registration is passive;
the authority is validated at sequential startup. Installation is an explicit
deployment operation using `PostgreSqlCoordinationSchema.GetInstallationSql`.
Startup never creates or repairs an authority or its allocator.

Both the jobs host and individual jobs start disabled. No continuous resources
are discovered or activated automatically. Enabled scheduled execution requires
a selected admitted-run source; automatic recurrence additionally requires a
capable evaluator. Fixed-delay, Daily and Weekly descriptors preserve policy but
do not generate due occurrences. This module supplies no recurring scheduler.

## Keep ownership and effects distinct

The stable key is `(Environment, ExecutionScope, DutyId, ResourceId)`. Components
are ordinal, case-sensitive UTF-8 without normalization; categories, mode,
configuration revision, occurrence, generation and instance are excluded. A grant
contains the installed authority, exact acquisition and a positive Int64
generation. Generation JSON uses a decimal string to preserve all its bits.

Only confirmed `Acquired` supplies a usable managed lease. `Busy` is ordinary
standby; `Unavailable` is a known noncommitted acquisition. The managed runtime
performs bounded exact-acquisition recovery before returning `Indeterminate`.
That result supplies no lease or acquisition ID for a consumer to resolve; it
permits no business callback or blind mutation replay. A missing observation cannot
prove that a pending mutation will never commit. Unconfirmed renewal cannot extend
the local deadline, and a late response cannot revive terminal ownership.

For correctness-sensitive database effects, begin the existing RM-02 transaction
and pass its **original context** plus the grant to `ICoordinationGuard`. The
PostgreSQL guard locks the current ownership row and checks authority time after
the lock wait. Rejection makes that same owner rollback-only. Keep the guard,
business changes, durable checkpoint and transactional outgoing intent in that
transaction. It owns no hidden commit and offers no fencing for another database
or an arbitrary remote side effect.

When using RM-03, retain the original operation identity across retry and takeover.
The lock order is inbox when present, operation claim, coordination guard,
application rows, outgoing records. Prepared work remains separate from the
owner's actual `Committed`, `NotCommitted` or `Unknown` completion. Recover an
unknown result with the existing fresh authorized lookup. Already committed RM-04
outgoing intent remains deliverable after the producer loses ownership.

Drain the callback **and its owned scope** before releasing its lease. Loss,
cancellation or a drain deadline cannot prove that work physically stopped. An
uncooperative callback keeps local key occupancy and business capacity until
actual settlement. The known host barrier preserves dependencies during that
join; it does not turn local limits into fleet-wide quotas.

| Incorrect pattern | Required boundary |
| --- | --- |
| Read the owner remotely, then write later | Guard the original business transaction and retain its ownership-row lock. |
| Replace an operation ID with the new generation | Preserve the original RM-03 operation and occurrence identities. |
| Release or restart a key when cancellation is requested | Join the actual callback and scope; retain observation and occupancy meanwhile. |
| Put category or schedule changes into the key | Keep management grouping and execution policy separate from duty identity. |
| Treat a local concurrency limit as deployment-wide admission | Apply local bounds honestly; shared exclusivity comes from the authority and guard. |

## Defaults and consumer entry points

Coordination defaults are a 30-second lease, 10-second renewal interval,
2-second request timeout, 5-second safety margin and 30-second maximum graceful
drain. There are at most 64 managed/unresolved acquisitions, eight shared physical
acquire/resolve/release requests and eight independently reserved renewal requests. Job defaults allow
1,024 definitions, 64 categories, 32 continuous duties, eight business reservations,
128 pending dispatches and bounded pages of 100. [Managed ownership](managed.md)
and [runtime admission](runtime.md) document validation and exact scope of each
bound. Invalid whole configuration revisions retain the previous accepted state.

The compiling [CountryData registrations](../../../implementations/csharp/examples/CountryData/Coordination/CountryMaintenanceJobs.cs)
bind [wallet compaction](../../../implementations/csharp/examples/CountryData/Coordination/WalletCompactionJob.cs)
only to its finite Continuous handler and
[customer dormancy](../../../implementations/csharp/examples/CountryData/Coordination/CustomerDormancyJob.cs)
only to its Scheduled handler. Wallet work uses a direct durable checkpoint. If
an application adds Operations there, claim the journal before the guard, rather
than invoking Operations inside the already-guarded work port. Their work, transaction source and native workload
authentication are explicitly injected; the normal CountryData application stays
database-free until an application selects that composition. Start with the
dedicated [public-helper unit examples](../../../implementations/csharp/examples/CountryData.UnitTests/Coordination/MaintenanceRecipeExamples.cs), then
read the [real transaction, recovery and process proof](consumers.md).

[CIS-0022 verification](../../changes/CIS-0022/verification.md) records exact current
native checks and retained failures. Controlled substitutes establish logical
consumer behavior, not SQL authorization or durability. Focused Windows/Linux
observations do not establish power-loss, failover, cloud or release qualification.
