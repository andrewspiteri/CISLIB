# CISLIB RM-02 Local Transaction Integration

## Implementation specification

| Field | Value |
| --- | --- |
| Version | 1.0 |
| Date | 20 November 2026 |
| Audience | Implementation agents, reviewers, and CISLIB application developers |
| Roadmap item | RM-02 — Local transaction integration |
| First implementation | C# on the repository's pinned .NET 10 toolchain |
| Reference database | PostgreSQL, accessed through Npgsql |
| Repository baseline inspected | `andrewspiteri/CISLIB`, `main`, commit `539a05d184432b5b8c131caf8454a44d0134c692` |
| Delivery status | Feature specification and implementation handoff; product implementation and native verification are subsequent work |

## 1. Purpose and governing decisions

RM-02 lets application database changes, a stored operation result, and outgoing records participate in one explicit local database transaction. One application operation owns the transaction. Application repositories and CISLIB storage integrations borrow its context. The owner commits the complete unit or finishes without committing it.

The database provides atomicity. CISLIB supplies consistent ownership, native integration, failure handling, observations, and a portable consumer contract. ADO.NET already exposes connection-bound local transactions; the C# implementation must use that facility. [S06]

The following decisions are normative for version 1:

1. Ship the portable contract and the dependency-free .NET integration in `CIS.Core`, under explicit `CIS.Transactions` namespaces.
2. Use one application-configured `DbDataSource`, one connection lease, and one `DbTransaction` for each transaction owner.
3. Start transactions explicitly. Pass the transaction context explicitly to participants.
4. Permit transaction completion only through the owner. Completion releases its owned transaction and connection resources.
5. Provide one small C# native-work helper that lends the connection and transaction to a callback and observes that callback's completion.
6. Distinguish `Committed`, `NotCommitted`, and `Unknown`. Keep database outcome separate from business outcome and resource-cleanup diagnostics.
7. Perform no automatic commit retry or application-work replay.
8. Qualify the first implementation against PostgreSQL using Npgsql, without adding Npgsql to Core's shipping dependencies.
9. Reuse the existing Logging, Metrics, CommonTesting, native-test, and repository-governance facilities.
10. Keep acceptance tied to the behaviours in this specification. Documentation, tests, review, and completion evidence are part of the implementation tasks that own those behaviours.

**MUST**, **MUST NOT**, **SHOULD**, and **MAY** express required, prohibited, recommended, and permitted behaviour. API declarations below specify new APIs to implement; they are not claims that these APIs exist at the inspected baseline.

The current repository instructions and dependency policy remain applicable. Preserve accepted requirements from this handoff when importing it into the existing workflow. Do not replace the product BRD, restart historical task plans, reuse completed work owners, or invent approval/allocation records. [S01, S02]

## 2. Scope and roadmap boundaries

### 2.1 Required RM-02 capabilities

- Named, bounded database registrations and explicit asynchronous transaction creation.
- An owning transaction interface and a separate participant context.
- Native ADO.NET participation using the same connection and transaction throughout the unit.
- Explicit isolation selection and an application-configured default.
- Commit, rollback, rollback-only marking, cancellation, disposal, and immutable completion reporting.
- Sequential native-work admission and clear rules for overlapping work and disposal.
- Automatic bounded logging and metrics.
- Public testing support and a real three-table database acceptance fixture.
- Portable behavioural documentation, a C# usage guide, and checked-in agent instructions.

### 2.2 Responsibilities retained by other owners

| Owner | Responsibility |
| --- | --- |
| Application | Schema, migrations, SQL, ORM models, permissions, locking, concurrency checks, business invariants, and operation boundaries. |
| RM-03 | Durable operation identifiers, duplicate detection, stored result contracts, replay policy, and reconciliation of uncertain outcomes. |
| RM-04 | Production outgoing-record schema, polling/dispatch, acknowledgements, delivery retries, and broker/HTTP integration. |
| RM-06 | Audit record contracts and delivery policy. |
| Workflows | Application sequencing and compensation logic, where explicitly configured. |
| Native database/provider | Transaction execution, connection pooling/reset, native I/O, and database-specific isolation behaviour. |

The three acceptance tables in this specification are test fixtures. They MUST NOT become premature production implementations of RM-03 or RM-04.

Version 1 does not introduce distributed or cross-database transactions, ambient transaction propagation, nested transactions, public savepoint orchestration, a generic repository/query API, a durable transaction journal, or an execution-retry framework. It does not adopt an already-running external native transaction. The supported path creates one CISLIB-owned native transaction and lets application persistence code participate in it.

HTTP requests, broker publication, file writes, caches, and arbitrary in-memory changes do not participate in the atomic guarantee. An outgoing record is a row written in the local transaction; its eventual delivery belongs to RM-04. A post-commit callback is not a reliable delivery mechanism.

### 2.3 Atomicity boundary

Every participating database command MUST use the owner's connection and transaction. Another connection to the same database is a different local transaction boundary. A matching connection string, database alias, HTTP request, or DI scope does not establish participation. [S06]

This guarantee applies to ordinary transactional database work under the supported native-use contract. Participants MUST NOT switch databases, perform independent transaction control, or invoke database mechanisms that make their effects independent of the supplied transaction.

## 3. Architecture, packages, and ownership

### 3.1 Repository placement

| Area | Destination |
| --- | --- |
| Portable C# contracts and owner implementation | `implementations/csharp/src/CIS.Core/Transactions/` |
| Native ADO.NET capability and helper | Same project, `CIS.Transactions.AdoNet` namespace |
| Registration and passive composition | Same project, `CIS.Transactions.Hosting` namespace |
| Public test helpers | `implementations/csharp/src/CIS.CommonTesting/` |
| Portable contract | `contracts/transactions/README.md` |
| Metric contract | `contracts/metrics/transactions.md` |
| Consumer guide | `docs/modules/transactions/README.md` |
| Agent skill | `.github/skills/cislib-transactions/SKILL.md` |

A namespace is not a requirement for another package. `System.Data.Common` and the required native runtime APIs do not justify adding an external dependency to Core. Npgsql belongs in the reference consumer/integration-test dependency closure. An application using this module supplies its own provider dependency. Preserve the approved package separation. [S02]

An EF Core runtime adapter package is not required for this delivery. The native borrowing contract must permit a future adapter without changing transaction ownership. Section 11 defines the integration boundary and the limits of the version-1 claim.

### 3.2 Ownership table

| Resource | Owner | Rule |
| --- | --- | --- |
| `DbDataSource` and its pool | Application/host | RM-02 borrows it and never disposes it or clears its pool. |
| Connection lease opened for one transaction | Transaction owner | Never shared with another transaction owner; released through normal provider disposal. |
| Native `DbTransaction` | Transaction owner | Only the owner completes and disposes it. |
| Commands, readers, and temporary ORM contexts in a callback | Participant | Finish and dispose before the callback returns. |
| Transaction context | Transaction owner | Participants borrow it; it cannot transfer native ownership. |
| Application services and existing DI scopes | Application/DI | RM-02 does not construct a second provider or dispose borrowed services. |
| Logger, metrics binding, and clock | Existing application composition | Reuse the established contracts and lifetime rules. |

Factory registration MUST be passive. It must not open connections, run migrations, execute database probes, or start background work. The runtime must be usable with the ordinary .NET host/DI facilities and must not depend on `CISApplication`, CIS tooling, or the collector package. [S01]

### 3.3 One owner per unit of work

The application starts one transaction and passes its context through nested service calls. Those services can perform several sequential native callbacks. They do not open substitute transactions to write their portion of the unit.

The factory MAY create independent transactions concurrently. RM-02 does not globally infer nesting from call stacks or request scopes. It exposes no ambient "current transaction" service.

## 4. Public C# contract

### 4.1 Portable interfaces

The following signatures define the required consumer shape. Follow existing repository file, XML-documentation, and nullable conventions when implementing them.

```csharp
namespace CIS.Transactions;

public interface ILocalTransactionFactory
{
    Task<ILocalTransaction> BeginAsync(
        string databaseName,
        CancellationToken cancellationToken = default);

    Task<ILocalTransaction> BeginAsync(
        string databaseName,
        LocalTransactionOptions options,
        CancellationToken cancellationToken = default);
}

public interface ILocalTransaction : IAsyncDisposable
{
    ILocalTransactionContext Context { get; }

    LocalTransactionStatus Status { get; }

    TransactionCompletion? Completion { get; }

    Task<TransactionCompletion> CommitAsync(
        CancellationToken cancellationToken = default);

    Task<TransactionCompletion> RollbackAsync();
}

public interface ILocalTransactionContext
{
    string DatabaseName { get; }

    TransactionIsolation Isolation { get; }

    bool IsRollbackOnly { get; }

    string? RollbackReasonCode { get; }

    void MarkRollbackOnly(string reasonCode);
}

public enum TransactionIsolation
{
    ReadCommitted,
    RepeatableRead,
    Serializable
}

public enum LocalTransactionStatus
{
    Active,
    Completing,
    Completed
}

public enum TransactionOutcome
{
    Committed,
    NotCommitted,
    Unknown
}

public sealed record LocalTransactionOptions
{
    public TransactionIsolation? Isolation { get; init; }
}
```

An omitted isolation uses the named database's captured default. Every successful owner/context has a concrete isolation value. Native .NET connection, transaction, isolation-enum, ORM, and provider types MUST NOT appear in the portable transaction interfaces or completion models.

`RollbackAsync` deliberately has no application cancellation token: it is cleanup and uses the independent rollback budget in section 9. `CommitAsync` accepts the caller token because cancellation before native commit changes whether commit is attempted.

`Completion` is null until completion and all owned cleanup attempts have settled. Once published, the snapshot and its outcome are immutable. Metadata and completion remain inspectable after completion; the context can no longer admit work.

### 4.2 Completion model

Implement an immutable `TransactionCompletion` with these fields:

| Field | Type/meaning |
| --- | --- |
| `Outcome` | `TransactionOutcome`. |
| `EndReason` | `TransactionEndReason` enum: `CommitRequested`, `RollbackRequested`, `UncommittedDisposal`, `RollbackOnly`, or `Cancellation`. This explains why the completion path was chosen. |
| `CommitAttempted` | Whether the owner crossed the native commit invocation boundary. |
| `RollbackAttempted` | Whether the owner explicitly invoked native rollback during cleanup. |
| `RollbackConfirmed` | Whether that rollback invocation returned successfully. It is not permission to rewrite an unknown commit outcome. |
| `Error` | Optional immutable error for the primary completion problem. |
| `CleanupErrors` | Immutable bounded list for failed rollback and resource-disposal attempts. |
| `DurationSeconds` | Nullable nonnegative `double` measured using the monotonic clock; null only when measurement is unavailable. |

Use a portable `TransactionError` containing a `TransactionErrorPhase`, stable code, and bounded safe message. Phase values are `Commit`, `Rollback`, `TransactionDispose`, and `ConnectionDispose`. A safe provider code MAY be retained when available without parsing free-form exception messages. Do not serialize native exceptions, SQL, parameters, connection strings, or credentials into this model.

The first rollback-only reason is preserved on the context. Do not accumulate a list of participant failures. Reason/error codes are nonempty printable ASCII, at most 128 characters; safe messages are at most 512 characters. Invalid caller-supplied reason codes throw `ArgumentException`. Native details must not be truncated into a supposedly safe message; use predefined safe text and bounded diagnostic fields.

At most one explicit rollback, one native transaction disposal, and one connection disposal are attempted by the owner. Therefore `CleanupErrors` contains at most three entries, in actual attempt order. A single cleanup failure is not duplicated into multiple list entries.

`Outcome == Committed` remains the decisive persistence fact even if `CleanupErrors` is nonempty. A business-facing `Success` convenience property is deliberately absent from this model: it would conflate persistence outcome with the business result.

### 4.3 Required stable completion error codes

| Code | Meaning |
| --- | --- |
| `transaction.rollback_only` | Commit was refused because a participant marked the unit rollback-only. |
| `transaction.cancelled_before_commit` | The caller token was observed before native commit invocation. |
| `transaction.commit_outcome_unknown` | Native commit was invoked but did not return a confirmed successful completion. |
| `transaction.rollback_failed` | An explicit rollback attempt failed or was cancelled by its cleanup budget. |
| `transaction.transaction_disposal_failed` | Native transaction disposal failed. |
| `transaction.connection_disposal_failed` | Connection-lease disposal failed. |

Use `transaction.work_failed` and `transaction.work_cancelled` as automatic rollback-only reasons for an admitted callback's escaping fault or observed cancellation. Application participants may supply their own bounded reason code when explicitly marking a caught failure.

### 4.4 Exception and result division

| Entry point | Required outward behaviour |
| --- | --- |
| `BeginAsync` | Throw argument/configuration errors before I/O. If allocation or native begin fails, attempt partial cleanup and propagate the original exception/cancellation. No owner is returned. |
| Native work helper | Reject misuse before invoking the callback. For admitted work, mark rollback-only on an escaping fault/cancellation, then propagate the original exception. |
| Valid `CommitAsync` / `RollbackAsync` | Return a completion snapshot for operational native failures and cancellation. |
| Lifecycle misuse | Throw `InvalidOperationException`; do not execute another native operation. |
| `DisposeAsync` | Await cleanup. Capture/report operational cleanup failures without replacing an in-flight application exception. Invalid disposal from inside the same native callback is misuse and must fail promptly. |

Do not catch fatal runtime failures as ordinary transaction outcomes. Do not convert application programming exceptions from native callbacks into successful results. Begin/work exception preservation does not require putting arbitrary exception objects in portable completion snapshots.

## 5. Database registration and isolation

### 5.1 Registration surface

Provide the following registration concepts in `CIS.Transactions.Hosting`:

```csharp
services.AddCisTransactions();

services.AddCisAdoNetDatabase(
    "LedgerDatabase",
    serviceProvider => serviceProvider.GetRequiredService<NpgsqlDataSource>(),
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

The registration factory returns `DbDataSource`; the Npgsql type above belongs to the consumer, not to Core. It resolves the application's existing data source and must not open a connection during registration. The public options type belongs to the C# integration namespace.

`AddCisTransactions` is idempotent. Named database registration is explicit and rejects duplicate aliases rather than silently replacing a different registration. Preserve existing provider registrations. Never build a service provider inside these methods.

Database aliases are ordinal, case-sensitive strings matching `[A-Za-z0-9][A-Za-z0-9_.-]{0,63}`. A factory admits at most 16 aliases, including with metrics disabled. These are startup configuration names, not tenant names or values derived from requests. Reject invalid/unknown aliases before obtaining a connection.

Capture immutable registration/options snapshots during normal composition. Mutating an original collection or options object after registration cannot alter existing or later transactions from that factory. Live configuration reload does not rebuild data sources or change an existing transaction's isolation.

Expose `LocalTransactionRuntimeOptions`, accepted as an optional argument to `AddCisTransactions`, with `RollbackTimeout` defaulting to 30 seconds and `LogCalls` defaulting to false. The timeout must be positive and finite; section 9 defines its cooperative meaning. Capture an immutable copy. Normal successful transaction logs are disabled by default; enabling `LogCalls` enables the terminal Debug summary. Error/failure reporting remains available when normal call logging is disabled. Conflicting repeat runtime options are rejected; an equivalent repeated registration is idempotent.

### 5.2 Supported isolation levels

`SupportedIsolationLevels` is an explicitly supplied nonempty subset of the three portable values and must include `DefaultIsolation`. The application/provider registration declares what its selected database supports; the qualification fixture verifies the declaration for Npgsql/PostgreSQL.

| CIS isolation | .NET mapping | PostgreSQL reference behaviour |
| --- | --- | --- |
| `ReadCommitted` | `IsolationLevel.ReadCommitted` | `read committed` |
| `RepeatableRead` | `IsolationLevel.RepeatableRead` | `repeatable read` |
| `Serializable` | `IsolationLevel.Serializable` | `serializable` |

Do not expose `Unspecified`, `Chaos`, `ReadUncommitted`, or `Snapshot` in the version-1 portable enum. This avoids asserting equivalence for provider aliases. PostgreSQL treats read-uncommitted as read-committed; a provider's reported enum alone is not proof of the server's isolation semantics. Verify the qualified mappings with the database's native isolation query. [S13]

An unsupported request fails before application work begins. If native begin rejects a declared supported level, release partial resources and surface the failure; do not retry with another isolation level. A future provider must document and verify its own mapping before claiming support.

Isolation does not implement ledger-specific locking or invariants. Applications still own conditional updates, concurrency tokens, lock ordering, and conflict handling.

### 5.3 Ambient transaction exclusion

Check `System.Transactions.Transaction.Current` before acquiring/opening the connection. A non-null value is rejected. RM-02 must not auto-enlist, suppress the caller's ambient scope, or replace it silently.

The application's data source must supply a fresh logical connection lease without an existing local transaction or external enlistment. The reference Npgsql setup explicitly uses `Enlist=false`; Core does not rewrite arbitrary connection strings. Npgsql otherwise supports automatic ambient enlistment, so this setup rule must be visible in the usage guide. [S14]

## 6. Native ADO.NET participation

### 6.1 Public capability and convenience helper

Provide a public mockable native capability, rather than requiring a cast to an internal implementation:

```csharp
namespace CIS.Transactions.AdoNet;

public interface IAdoNetTransactionContext : ILocalTransactionContext
{
    Task UseAsync(
        Func<DbConnection, DbTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);

    Task<T> UseAsync<T>(
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
```

Public extension methods named `UseAdoNetAsync` on `ILocalTransactionContext` forward to that capability. If the context does not provide it, throw `NotSupportedException` before invoking user work. Do not use reflection, internal-friend access, or concrete-runtime casts as the extension mechanism.

These methods lend the native handles for the duration of one callback. They do not add SQL generation, query interception, repository registration, prepare callbacks, or distributed enlistment.

### 6.2 Admission and callback rules

For each call, the implementation MUST:

1. Validate the delegate and context and reject an unavailable or rollback-only context.
2. Atomically admit at most one native callback for the owner.
3. Observe cancellation before invoking the callback. An admitted cancelled call marks the unit rollback-only and throws cancellation without calling the delegate.
4. Pass the exact connection and transaction objects belonging to the owner.
5. Await the callback's actual task, including all work it owns.
6. On an escaping exception, mark rollback-only before rethrowing the original exception.
7. On normal callback return, perform a cancellation checkpoint. If the supplied token is now cancelled, mark rollback-only and throw cancellation; otherwise return the callback value.
8. Release callback admission in all paths and notify any waiting disposal.

The callback must use the supplied cancellation token for its native I/O. The helper MUST NOT implement cancellation using `Task.WhenAny`, `WaitAsync`, or another mechanism that abandons an in-flight callback.

Returning a business object with `Success == false` is a normal return. The helper does not inspect arbitrary return values or infer whether a business rejection should commit.

### 6.3 Participant obligations

Inside the callback, participants MUST:

- Assign the supplied native transaction to commands that require an explicit transaction property.
- Finish all database tasks and dispose commands/readers before returning.
- Dispose temporary ORM contexts without closing/disposal ownership of the borrowed connection or native transaction.
- Propagate database/flush exceptions, or call `MarkRollbackOnly` before intentionally catching and consuming them.
- Use native handles directly for that callback's database work.

Participants MUST NOT:

- Invoke native or SQL `BEGIN`, `COMMIT`, `ROLLBACK`, participant-managed savepoint orchestration, or independent transaction completion.
- Call owner commit, rollback, or disposal from inside the callback.
- Recursively invoke `UseAdoNetAsync` on the same owner.
- Run concurrent commands on the borrowed connection, or return before child work has finished.
- Return or retain the borrowed connection/transaction, a live reader, unfinished task, deferred enumeration, or ORM object that retains these resources.
- Switch the database or detach the intended work from the supplied transaction.

Compose transaction-aware services outside the callback. Each service can enter its own sequential callback. Inside one callback, call helpers that work directly with the supplied native handles, rather than services that re-enter the gate.

Native handles expose methods beyond the portable participant interface. These obligations define supported use; the library cannot technically prevent deliberate bypass after a native object has been lent.

An ORM may use internal savepoints as an implementation detail. They do not clear RM-02 rollback-only state, enable participant-managed partial recovery, or permit further application work after the unit is poisoned.

### 6.4 Why caught database errors must mark rollback-only

A native provider can return from commit even when an earlier, suppressed statement error caused the server to abort the transaction. PostgreSQL documents that committing an aborted transaction completes as rollback. Therefore normal return from native commit is meaningful only under the supported failure-handling contract. [S11]

The guarded helper handles exceptions that escape its callback. It cannot detect an exception deliberately swallowed inside the callback. Documentation and acceptance must cover a caught error followed by explicit rollback-only marking. Do not claim protection against unmarked swallowed native failures, and do not add a SQL proxy or a per-commit probe to imply that protection.

## 7. Beginning work and lifecycle

### 7.1 Begin sequence

After validating registration, options, and the ambient-transaction exclusion:

1. Observe the begin token before resource acquisition.
2. Open one connection lease using the application's registered data source and the supplied token.
3. Begin a native transaction with the selected concrete isolation and token.
4. If cancellation was observed before the owner can be returned, release acquired resources and throw cancellation.
5. Create the owner/context, capture its instrumentation binding, and return the owner in `Active` state.

No partially initialized owner escapes a failed begin. If open/begin throws after acquiring a resource, release only resources actually acquired. Preserve the original exception; report secondary cleanup errors through bounded diagnostics.

`Active` means a usable provider transaction handle exists. It does not promise that a separate `BEGIN` round trip has already been acknowledged. Do not force another round trip merely to make the state name stronger.

### 7.2 Lifecycle operations

| Current condition | Requested operation | Required behaviour |
| --- | --- | --- |
| Active, no callback, not rollback-only | Native callback | Admit it. |
| Active, callback already admitted | Another native callback | Reject; do not invoke the second delegate or queue it. |
| Active, rollback-only | Native callback | Reject; only completion/inspection remains available. |
| Active, callback admitted | Commit or explicit rollback | Reject as misuse without completing the transaction. |
| Active, no callback | Commit or explicit rollback | Atomically take completion ownership and set `Completing`. |
| Active, callback admitted | Disposal from outside that callback | Close admission, wait for the callback to settle, then roll back and release resources. |
| Completing | Another commit or rollback | Reject; do not issue a second native completion. |
| Completing | Disposal | Join the existing completion/cleanup. |
| Completed | Commit, rollback, or native work | Reject; inspect `Completion` instead. |
| Completed | Disposal | Return without native I/O. |

Repeated disposal is harmless and joins the same pending cleanup when necessary. There is no hidden work queue and no task abandoned to make a timeout appear complete.

`MarkRollbackOnly` and the claim of completion ownership MUST use the same admission synchronization. A mark accepted before commit claims ownership must be observed by that commit. Once commit or explicit rollback claims completion, reject new marks; do not leave a gap between checking the flag and admitting completion. An already admitted callback is the limited exception: it may still mark rollback-only while external disposal closes admission and waits for that callback to finish, before disposal proceeds to native cleanup.

Disposal identified as originating from the executing callback must fail promptly instead of waiting for itself. A small internal async execution marker MAY be used solely to detect this misuse. It must not become ambient transaction selection, a global registry, or a request-to-transaction lookup feature. Deliberately suppressing execution-context tracking and constructing a callback-to-disposal dependency cycle is unsupported; no global task-dependency detector is required.

Short internal synchronization may protect state/admission. Do not hold a monitor across user callbacks or database I/O. The factory may be shared; transaction owners isolate their mutable state.

## 8. Commit, rollback, and outcome classification

### 8.1 Commit procedure

For a valid commit request with no active callback:

1. Close admission and enter `Completing`.
2. If already rollback-only, skip native commit, select `NotCommitted`, set `EndReason=RollbackOnly` and the refusal error, then perform rollback cleanup.
3. Otherwise, if the caller token is cancelled at the final pre-commit checkpoint, skip native commit, select `NotCommitted`, set `EndReason=Cancellation`, and perform rollback cleanup.
4. Otherwise, set `CommitAttempted=true` immediately before invoking the native commit method, and pass the caller token.
5. If native commit returns successfully under the supported-use contract, select `Committed`. Do not apply a post-commit cancellation checkpoint that changes this outcome.
6. If that invocation throws, including cancellation or a synchronous provider error, select `Unknown` and `transaction.commit_outcome_unknown`.
7. Await the cleanup sequence in section 9, publish the immutable snapshot, and enter `Completed`. Make the terminal observation attempts without allowing instrumentation failure to prevent publication or change the returned snapshot.

The generic version-1 adapter conservatively classifies every failed invocation after the commit boundary as `Unknown`. It does not require a provider exception-classification framework. Validation/cancellation rejected before that boundary is a different case.

### 8.2 Exact outcome meanings

| Outcome | Required evidence and meaning |
| --- | --- |
| `Committed` | Native commit returned successfully under the qualified native-use contract. All participant work must have followed section 6. |
| `NotCommitted` | No commit was issued by the owner under supported use. An explicit rollback may or may not have been acknowledged; inspect cleanup fields separately. |
| `Unknown` | The owner invoked native commit but did not obtain confirmed success. The database may have committed. |

`NotCommitted` does not assert that the server has already released every lock when cleanup failed. `Unknown` does not mean "cleanup failed"; it specifically preserves uncertainty after a commit attempt.

When connectivity fails during commit, treating a retry as if rollback were certain can duplicate changes. RM-02 therefore reports the uncertainty and does not replay the operation. [S08]

### 8.3 Explicit rollback and uncommitted disposal

Explicit `RollbackAsync` selects `NotCommitted` and `EndReason=RollbackRequested`. Disposal of an unfinished owner selects `NotCommitted` and `EndReason=UncommittedDisposal`. These paths never invoke native commit.

They then perform rollback and resource cleanup. Failed cleanup is reported in `CleanupErrors`; it does not invent a commit attempt. On these two paths, `Error` remains null because any operational cleanup problem is represented in that list. A callback's original exception remains the exception propagated by its own call.

### 8.4 Outcomes cannot be rewritten by cleanup

- Confirmed commit followed by a disposal error remains `Committed`.
- Failed/cancelled native commit followed by a successful rollback attempt remains `Unknown`.
- Unknown commit followed by connection closure remains `Unknown`.
- No commit attempt followed by failed rollback remains `NotCommitted`, with unconfirmed cleanup.

A rollback acknowledgement is not evidence that an earlier commit did not take effect. PostgreSQL permits rollback outside an active transaction without making it a recovery query for the earlier commit. [S12]

The owner must not retry native commit, issue a new application transaction, or query an invented transaction-status table to resolve uncertainty. Future RM-03 integration can use its durable operation identifier and authoritative database state.

## 9. Cancellation, cleanup, and shutdown

### 9.1 Cancellation boundaries

Tokens apply to the operation accepting them: begin, a native callback, or commit. The application normally passes the same request/operation token through all three. The factory does not retain the begin token as an implicit lifetime token for future calls.

| Cancellation point | Result |
| --- | --- |
| Before or during open/native begin | Clean acquired resources; preserve the original begin cancellation; return no owner. |
| Before an admitted callback executes | Mark rollback-only, do not call the delegate, throw cancellation. |
| During callback/native I/O | Pass the token through and await actual settlement; an escaping cancellation or cancelled post-work checkpoint marks rollback-only. |
| Before native commit invocation | Skip commit and complete through rollback as `NotCommitted`. |
| After native commit invocation fails/cancels | Complete as `Unknown`. |
| After native commit returns successfully | Complete as `Committed`, even if the token is now cancelled. |

Cancellation is cooperative. A callback that ignores its token can delay disposal. Native base methods may also use synchronous implementations. Do not promise a hard wall-clock timeout for arbitrary providers or user callbacks. [S09]

### 9.2 Cleanup sequence

After admitted work settles, the owner performs these attempts sequentially:

1. If commit was not confirmed and a usable native transaction handle was obtained, attempt explicit rollback once. Use a fresh token governed by `RollbackTimeout`, independent of the request token.
2. Dispose the native transaction once.
3. Dispose the connection lease once.

A failure in one attempt does not skip later resource-release attempts. A provider may internally reset/roll back during disposal; those native internals are not additional RM-02 retries. Do not clear a shared pool, dispose a data source, or force application-wide cancellation to clean one transaction.

The rollback budget requests cancellation of the rollback call. It is not a deadline for tokenless native disposal or arbitrary provider code. Await each admitted native operation to actual settlement, including after budget expiry. Retain cleanup diagnostics separately from the persistence outcome and from any original application exception.

Completion methods perform this cleanup before returning their snapshot. Consequently, `await using` after an awaited commit/rollback adds no second round of native cleanup and cannot turn a returned confirmed commit into a disposal exception.

### 9.3 Application shutdown

The application must stop accepting operations, cancel or finish active work according to its normal policy, await each owned transaction, then allow data sources and DI services to be disposed. Document this ordering in the consumer guide.

RM-02 does not need a global live-transaction registry, background rollback scheduler, or a second host-drain framework. Ownership remains local and explicit. Destroying an application-owned data source while transactions still borrow it violates the resource contract.

## 10. Application usage and result handling

### 10.1 Successful unit of work

This is the intended application shape. The business/result/outgoing store names are illustrative application or future-module interfaces, not RM-02 production store deliverables.

```csharp
await using var transaction = await transactions.BeginAsync(
    "LedgerDatabase",
    cancellationToken);

var context = transaction.Context;

var result = await ledger.ApplyAsync(command, context, cancellationToken);

await operationResults.StoreAsync(
    operationId,
    result,
    context,
    cancellationToken);

await outgoingRecords.AppendAsync(
    outgoingRecord,
    context,
    cancellationToken);

var completion = await transaction.CommitAsync(cancellationToken);

switch (completion.Outcome)
{
    case TransactionOutcome.Committed:
        return result;

    case TransactionOutcome.NotCommitted:
        return MapNotCommitted(completion);

    case TransactionOutcome.Unknown:
        return MapUncertainOutcome(operationId, completion);

    default:
        throw new InvalidOperationException("Unexpected transaction outcome.");
}
```

Do not send an outgoing message or publish a successful business response before commit is confirmed. After `Unknown`, the application must communicate uncertainty without asserting that retrying the business action is safe. RM-02 does not choose an HTTP status or invent RM-03 recovery behaviour.

### 10.2 Ordinary repository implementation

```csharp
public Task<int> InsertAsync(
    string recordId,
    ILocalTransactionContext context,
    CancellationToken cancellationToken)
{
    return context.UseAdoNetAsync(
        async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO rm02_example_record (id) VALUES (@id)";

            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = recordId;
            command.Parameters.Add(id);

            return await command.ExecuteNonQueryAsync(token);
        },
        cancellationToken);
}
```

The example table is fixture-only. The final repository guide must use a compiling example backed by the real acceptance fixture and the implemented API.

### 10.3 Business rejection versus failed database work

A business rejection can be committed as a durable result when no rejected business writes remain in the unit. For example, an insufficient-funds decision made before mutation may save a declined result without an outgoing success event.

If business writes were made and must be undone, mark rollback-only and roll them back. Version 1 does not support rolling back part of the unit and then committing a rejection in that same unit. Any later transaction used to persist a rejection is an explicit application/RM-03 decision.

The transaction layer must not inspect a generic result's `Success`, translate ledger states, or apply domain-specific exception rules. Neither a transaction completion snapshot nor an in-memory operation result is itself a durable RM-03 record.

### 10.4 Pipeline composition

Create the transaction around the short database portion of a pipeline and pass the context through the pipeline's application context. Inspect the pipeline outcome before requesting commit. If the intended unit must fail, explicitly mark rollback-only or roll back.

Pipeline `Error` methods retain their existing meaning. They do not replace database rollback. Remote calls and long-running workflow waits should be outside the local database transaction. Preserve the current workflow contract and original exception handling. [S05]

## 11. Provider and ORM integration

### 11.1 PostgreSQL reference setup

Use an application-owned `NpgsqlDataSource` and normal provider pooling. Each owner obtains its own connection lease. Npgsql's data source is the native configuration/pooling boundary; RM-02 must not recreate it for every operation. [S10]

The reference configuration must include explicit finite connection, command, and cancellation-response timeouts, `Enlist=false`, and normal connection reset on return to the pool. A suitable fixture baseline is 15 seconds for opening, 30 seconds for commands, and 2 seconds for cancellation response. These are fixture defaults, not universal production SLAs. Document the actual selected configuration; do not log secrets. [S14]

Use ordinary durable PostgreSQL tables and normal durability settings for acceptance. Record the exact database and Npgsql versions actually used. Select/pin a supported Npgsql version compatible with the existing .NET 10 toolchain through the current dependency workflow; do not upgrade the solution SDK/runtime as a side effect. Test/provider packages must not enter Core's shipping package graph.

### 11.2 EF Core interoperability boundary

EF Core can participate in an externally created relational transaction when its context uses the same connection and is attached through `UseTransactionAsync`. [S07]

Within this module's supported borrowing boundary, an EF consumer would create/configure a context against the lent connection, attach the lent transaction, perform and explicitly flush its work, and dispose that temporary context before the callback returns. It must configure ownership so context disposal does not complete or dispose the owner resources.

A long-lived scoped `DbContext` retaining the lent handles between callbacks is outside the version-1 usage contract. EF's internal savepoints do not grant participants permission to recover and commit after an RM-02 rollback-only mark. An EF exception that escapes the callback poisons the whole RM-02 unit.

Implicit execution-strategy replay must not run inside this integration. Any future retry integration must own the complete transaction attempt, its fresh resources, and the business replay decision; it belongs to an explicitly scoped follow-up. [S08]

Version 1 delivers the ADO.NET participation contract and the PostgreSQL proof. An EF package and executed EF consumer qualification are not acceptance prerequisites. Documentation must distinguish the native interoperability recipe from a tested EF adapter. If an EF sample is included as executable evidence, its extra dependencies and actual execution must be recorded honestly.

## 12. Automatic logging and metrics

### 12.1 Logging

Use the existing logger with category `CIS.Transactions`. Carry existing application/environment identity and correlation through the current context mechanism; do not create a second identity source or confuse request identity with a durable RM-03 operation identifier.

Record at most one normal terminal summary per transaction. It includes database alias, isolation, outcome, end reason, duration when known, commit/rollback flags, and safe error codes. `LogCalls=false` suppresses routine successful summaries; `LogCalls=true` enables them at Debug. Failures/unknown outcomes remain eligible for Warning/Error reporting through existing filters and suppression. Expected caller cancellation is not an error-level event by itself; an unknown commit still needs visible diagnostic treatment.

Do not log every SQL command, serialize application results, or include SQL, parameters, raw native messages, connection strings, tokens, or certificates. Reuse the logger's bounded state and filtering. Telemetry failure must not change transaction admission, persistence outcome, cleanup, or application exceptions. Per-request log context must not be retained by the factory after completion.

### 12.2 Metric contract

Use the implemented `CIS.Metrics` facade and reusable instrument handles. Create definitions once per instrumentation owner. When the owner already measures elapsed time for the completion report, send that same measurement to `ITimer.Record`. Do not time the same interval again. [S03]

| Logical name | Instrument/unit | Dimensions | Normal combinations |
| --- | --- | --- | --- |
| `cis.transaction.duration` | Timer / Seconds | `database`, `outcome` | 16 × 3 = 48 |
| `cis.transaction.active` | Up/down counter / Count | `database` | 16 |
| `cis.transaction.failures` | Counter / Count | `database`, `phase` | 16 × 4 = 64 |

`database` is a registered alias. `outcome` is exactly `committed`, `not_committed`, or `unknown`. `phase` is exactly `begin`, `commit`, `rollback`, or `dispose`. Do not add transaction IDs, operation IDs, request IDs, error codes, exception types, SQL, tenant names, or business outcomes as dimensions.

Measurement rules:

- The active interval begins when a successful begin publishes an owner and ends after all owner cleanup attempts settle. It excludes failed begins and connection acquisition before an owner is published.
- The duration histogram measures that same interval, including application time between callbacks and actual terminal cleanup. It records once with the final persistence outcome.
- Begin failure records only an appropriate native failure count; it does not manufacture a completed transaction or a zero duration.
- The failures counter counts native open/begin, commit, explicit rollback, and native-disposal invocations that fail, including cancellation returned by that invoked native operation. A cancellation checkpoint that prevents native invocation does not increment this counter.
- An escaping application callback exception is not inferred to be a native failure by this counter. Its effect is visible in the transaction outcome and safe diagnostics.
- Two separate native disposal failures can produce two `phase=dispose` increments, but there is still only one transaction completion observation.
- Validation failures, rejected overlap, and second completion attempts do not add lifecycle observations.
- If elapsed time is unavailable, balance active work and publish the completion without inventing a duration sample.

Expected exported families are `cis_transaction_duration_seconds` with `_bucket`, `_sum`, and `_count` children; `cis_transaction_active`; and `cis_transaction_failures_total`. Verify actual exposition through the existing collector tests. Use the histogram count for transaction totals rather than adding a duplicate completion counter.

Use the existing standard seconds buckets: `0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10`. Retain the existing global capacities and scrape limits. The three conditional streams reserve 128 normal combinations in total; SDK special/overflow points remain additional. This is a cardinality budget, not a total-process memory guarantee. [S03]

### 12.3 Collector composition

Update the existing optional collector's `MetricsStreamCatalog.cs` and related options/catalog integration. `AddCisTransactions` supplies passive opt-in metadata; the collector attaches the exact instrumentation owner belonging to that host, in either registration order. Preserve `CollectCoreModules=false`, disabled/no-op behaviour, and hosts without transaction registration.

Capture one complete binding for an accepted owner so active increments/decrements cannot go to different hosts. Operations begun before attachment remain unobserved and are not replayed. Native begin failures may use the binding available to their attempted begin; this must not create an unmatched active decrement.

Keep authoritative `application` and `environment` labels on every exported sample, including generated histogram and overflow samples. Validate stream-capacity overrides against the configured domains; do not expand global limits to hide a bad registration. The current workflow catalog provides the composition pattern to follow. [S03]

Intended queries include completed transaction rate from the duration count, outcome distribution including unknown outcomes, latency from histogram buckets, active owners, and native failure rate by phase. Metrics do not claim to identify durable business success.

## 13. Public testing support and acceptance

### 13.1 CommonTesting deliverable

Add a small `ScriptedLocalTransactionFactory` in `CIS.CommonTesting`, implementing the portable public interfaces. It must support:

- Scripted begin failure or success.
- Scripted commit outcomes, including unknown completion and cleanup-error snapshots.
- Recording created contexts, whether commit/rollback/disposal was requested, and the eventual completion. Participating fake stores can record the context they received.
- Realistic rollback-only behaviour: a script cannot force committed success after a rollback-only mark.
- Ordinary application tests with mocked/fake stores that receive the same portable context.

The helper simulates lifecycle/outcome behaviour; it is not a SQL engine or proof of database atomicity. It need not implement the native ADO.NET capability. The extension must fail clearly when native use is attempted on a portable-only test context.

Bound pending scripts and retained recordings explicitly; use small finite defaults and fail clearly on exhaustion. Do not add a mocking library, test framework dependency, SQL parser, or in-memory database to the public helper. Preserve CommonTesting's existing package separation. [S04]

### 13.2 Real database fixture

Create a small fixture in the existing integration-test structure with three ordinary tables:

| Fixture table | Purpose |
| --- | --- |
| Business record | A demonstrable application mutation. |
| Operation result | A stored result correlated with that mutation. |
| Outgoing record | A record requiring later delivery, with no dispatcher. |

Use disposable fixture identifiers and isolate its schema/data. Verify results from an independent connection after terminal completion. Do not use the transaction's own reads as proof that other consumers can observe committed data.

Prove all-three commit, all-three absence after a later write failure and rollback, and a business rejection that commits only its result record. Include the reference isolation mapping and cancellation of a genuinely active native query. Use controllable test coordination instead of timing-dependent sleeps.

Keep database provisioning/setup instructions concise. Credentials are supplied through test configuration, never committed. Missing database infrastructure is a reported unexecuted integration check, not a passing or substituted in-memory result. CountryData remains the sole maintained tutorial; this fixture is a named database acceptance consumer, not a new tutorial application. [S01]

### 13.3 Acceptance matrix

These acceptance groups define meaningful behaviours. They are not a requirement for exactly one test method per row or a new test harness.

| ID | Required observation | Appropriate evidence |
| --- | --- | --- |
| AC-01 | Core exposes portable public contracts; its shipping dependency closure contains no Npgsql, EF, or collector dependency added by this module. | Build/API/package inspection. |
| AC-02 | Registration is passive, bounded, immutable, and idempotent where specified; duplicate/unknown aliases and unsupported isolation fail before I/O. | Focused native tests with a recording data source. |
| AC-03 | An ambient transaction is rejected before the connection factory opens a connection. | Focused native test. |
| AC-04 | Begin failure/cancellation returns no owner, releases acquired resources, and preserves the primary exception. | Scripted native-provider tests. |
| AC-05 | Sequential participating stores receive the same native connection and transaction objects; separate owners use independent leases. | Native fixture plus recording provider test. |
| AC-06 | All three table writes commit and are visible through an independent connection. | Real PostgreSQL integration. |
| AC-07 | A later statement failure rolls back all three writes, with no partial persistence. | Real PostgreSQL integration. |
| AC-08 | An escaping callback fault marks rollback-only before the caller catches it; subsequent commit never invokes native commit. | Focused gate/owner test. |
| AC-09 | A deliberately caught database error followed by explicit marking cannot commit earlier changes. | Real PostgreSQL integration. |
| AC-10 | A business rejection can commit a result row without business changes or a success event; no `Success` reflection controls the transaction. | Real PostgreSQL integration/application fixture. |
| AC-11 | Disposal without commit and explicit rollback both return/preserve `NotCommitted` and release owner resources. | Real integration plus resource-ownership test. |
| AC-12 | Pre-cancelled native work does not invoke its delegate; an active native query is cancelled and awaited before cleanup. | Gate test and a focused PostgreSQL cancellation test. |
| AC-13 | Cancellation observed before commit sends no native commit and uses an independent rollback token. | Scripted provider test. |
| AC-14 | Native commit success followed by caller cancellation remains `Committed`. | Scripted provider test. |
| AC-15 | A lost-acknowledgement simulation returns `Unknown`; subsequent successful rollback/disposal does not downgrade it; commit/work is not retried. | Scripted provider test that records native calls. |
| AC-16 | Native disposal failure after confirmed commit leaves outcome committed and reports cleanup separately. | Scripted provider test. |
| AC-17 | Failed rollback without a commit attempt remains `NotCommitted` with unconfirmed cleanup; later release attempts still run. | Scripted provider test. |
| AC-18 | Overlap, recursive native use, and completion from a callback are rejected without executing rejected work. | Deterministic coordination test. |
| AC-19 | External disposal closes admission and joins an admitted callback/commit; same-callback disposal with normal execution-context flow fails promptly; repeated disposal is harmless. | Deterministic coordination test. |
| AC-20 | Rollback-only marking and completion claim are synchronized; no accepted late mark can be lost before commit. | One focused controlled race test. |
| AC-21 | Completed contexts reject work and second completion; snapshots remain immutable and inspectable. | Public API behaviour test. |
| AC-22 | Each advertised isolation maps to the expected server isolation; native begin failure never falls back to another level. | PostgreSQL native query plus failure test. |
| AC-23 | RM-02 never disposes the application's data source; one application data source survives multiple independent owners. | Ownership/reference consumer test. |
| AC-24 | Metrics record one duration/outcome, balance active work, distinguish phase failures, and preserve configured dimensions and mandatory identity. | Existing recorder/collector tests and actual scrape. |
| AC-25 | Collector registration order, no-collector use, disabled Core collection, and late attachment behave as specified without replay. | Existing composition test infrastructure. |
| AC-26 | Logging/metrics failures do not alter transaction outcome, cleanup, or the callback exception; diagnostic fields exclude fixture secrets and SQL. | Focused failure/redaction tests. |
| AC-27 | An outside-friend consumer can use the actual Core/CommonTesting packages and public interfaces without reflection or copied library source. | Existing package-consumer mechanism. |
| AC-28 | Consumer snippets compile; documentation and agent guidance match the actual API and distinguish PostgreSQL proof from the EF integration recipe. | Compiling fixture and concise review. |

Run applicable native tests on the repository's required Windows and Ubuntu lanes, recording what actually ran and the exact candidate. Reuse valid existing evidence and impact-based selection. A lane not executed must remain identified as such; source inspection is not execution evidence.

No new mutation campaign, blanket full-suite run, broad provider matrix, performance harness, or large evidence archive is required solely by this feature. Existing mandatory repository gates still apply. Broaden checks only for a concrete uncovered risk or an applicable gate.

## 14. Implementation stories and tasks

Allocate the next legitimate change identity through the existing workflow. The task labels below are handoff labels, not reserved CIS identifiers. Each task includes its own tests, documentation, review, and evidence. Do not add separate approval/documentation/test-preparation tasks around them.

### RM02-01 — Contracts, configuration, and transaction creation

**Story:** An application can configure named databases and explicitly obtain an owned local transaction using public CISLIB contracts.

Implement sections 3–5 and the begin path in section 7. Establish immutable completion/error types, alias/isolation validation, passive DI composition, partial-begin cleanup, and the native lifecycle seam used by subsequent work. Write the portable contract alongside the implementation. Keep partially delivered APIs clearly identified until terminal behaviour is complete.

**Acceptance:** AC-01–04 and the applicable registration/ownership portion of AC-23. Verify no resource acquisition occurs from registration or metadata inspection.

**Dependencies:** None within RM-02. Inspect the current repository and preserve its selected toolchain and package policy.

### RM02-02 — Native participation and terminal lifecycle

**Story:** Repositories can share native work safely, and the owner receives an accurate completion result after cleanup.

Implement the public native capability and helper, rollback-only marking, admission/reentrancy, commit/rollback, cancellation, and resource release. Add focused provider doubles for uncertain commits and cleanup failures in existing test support, not the public consumer helper. Finish the portable lifecycle/error documentation.

**Acceptance:** AC-05's native identity check; AC-08; AC-13–21; applicable AC-11/12 and AC-26. Verify the exact native call counts and the synchronization between marking and completion.

**Dependencies:** RM02-01. The transaction-owner source has one writer; do not concurrently edit its lifecycle in multiple tasks.

### RM02-03 — PostgreSQL integration and reference consumer

**Story:** A real application pattern writes business, result, and outgoing records atomically with the selected native provider.

Select and lock the reference provider dependency outside Core. Build the three-table fixture, verify real commit/rollback/decline/cancellation and isolation, and provide compiling repository examples. Verify application-owned data-source lifetime and the Core package dependency boundary. Document PostgreSQL setup and the bounded EF integration recipe.

**Acceptance:** AC-05–07, AC-09–12, AC-22–23, and the corresponding portion of AC-28. Record the exact database/provider versions and actual platform observations.

**Dependencies:** RM02-02. Coordinate changes to shared package/project files through one owner.

### RM02-04 — Automatic operational observations

**Story:** Applications receive transaction observations through existing CISLIB logging and metrics without adding calls to every repository.

Implement section 12, update the portable metric catalog and optional collector integration, and verify normal/disabled/late-attachment composition. Reuse the same measured elapsed time for reports and metrics. Keep instrumentation failures isolated from native work and cleanup.

**Acceptance:** AC-24–26 and the diagnostic aspects of AC-16/17. Include actual exported names, identity, and outcome values in the focused collector evidence.

**Dependencies:** RM02-01/02 contract stability. May proceed alongside RM02-03 with separate file ownership and coordinated builds.

### RM02-05 — Public test support and consumer integration

**Story:** Application developers and future agents can test and use the transaction contract through published interfaces.

Implement the bounded CommonTesting factory, package-consumer coverage, final consumer guide, agent skill, and root routing hook. Link the real compiling fixture and implemented contract. Reconcile any documentation/API mismatch discovered by consumer use without creating a second implementation or copied tutorial.

**Acceptance:** AC-27–28 and public fake behaviour in section 13.1. Confirm the guide correctly handles committed outcomes with cleanup errors and unknown outcomes without replay.

**Dependencies:** Stable RM02-01/02 public contracts; final integration against RM02-03/04. The public helper implementation is the owned deliverable; documentation/review is included with it.

Tasks must remain no larger than the repository's medium task size. If actual scope requires a split, split by a distinct implementation outcome, not by adding another approval stage. Reuse existing user decisions and approved execution authority; do not ask again merely because work moves from a story to a task. New dependencies or genuine scope changes follow the existing decision process without inventing approvals.

## 15. Documentation and agent deliverables

### 15.1 Required maintained content

Update the repository's existing module indexes and deliver the paths in section 3.1. Keep one canonical portable transaction contract and one consumer guide; extra files need an actual readership or purpose.

The guide must contain:

- Database registration and data-source ownership.
- A complete compiling commit example and a rollback/failure example.
- A transaction-aware repository using `UseAdoNetAsync`.
- Explicit nested-service composition outside native callbacks.
- Business rejection versus transaction completion.
- `Unknown` handling without blind retry.
- Caller cancellation, independent rollback cleanup, and cooperative timeout limits.
- PostgreSQL configuration/qualification and the narrower EF recipe.
- CommonTesting usage and the limits of simulated outcomes.
- Workflows composition and application shutdown order.

### 15.2 Repository skill

During implementation, create `.github/skills/cislib-transactions/SKILL.md` using the current repository skill conventions. Its description must trigger for implementing, consuming, reviewing, or diagnosing local transaction integration and native database participation.

The skill must direct future agents to:

1. Read root instructions, the current transaction contract, actual APIs, dependency policy, and compiling fixture.
2. Start one explicit owner and pass the context through participants.
3. Use the public native capability; do not obtain handles through internal reflection or hidden globals.
4. Keep native work sequential and non-reentrant; finish/dispose borrowed resources inside the callback.
5. Propagate native failures or mark rollback-only before swallowing them.
6. Keep business rejection separate from persistence outcome.
7. Preserve unknown commit outcomes and never add automatic replay by inference.
8. Preserve data-source ownership, independent rollback cancellation, and actual cleanup settlement.
9. Use existing Metrics and CommonTesting guidance instead of recreating those systems.
10. Run focused native and real-provider checks tied to acceptance, and report unexecuted scope honestly.

Link to authoritative repository content instead of duplicating this whole specification into the skill. Add a concise Transactions routing section to `AGENTS.md` outside managed CIS markers. Validate the checked-in skill with the installed skill/CIS workflow and commit it with the implementation; do not install or change a personal assistant skill as an incidental action.

### 15.3 Completion report

The implementation report must identify the delivered public APIs, behavioural changes, actual provider/package versions, changed repository paths, executed acceptance groups and platforms, and material gaps. It must distinguish implementation, source tests, package-consumer evidence, real PostgreSQL execution, and broader release qualification.

Preserve unknown outcomes and incomplete evidence. Do not report a scripted lost-ack test as an actual network-failure qualification, or the ADO.NET extension point as an executed EF adapter.

## 16. Implementation-agent handoff

Use the following text with this specification:

> Implement CISLIB RM-02 Local Transaction Integration from this specification. Inspect the current checkout, `AGENTS.md`, actual module APIs, dependency policy, and installed CIS workflow first. Preserve this document as feature input within the existing project, and allocate the current legitimate change/task identities without restarting historical work or inventing approvals.
>
> Implement the portable owner/context contract in `CIS.Core`, the guarded public ADO.NET participation capability, passive named-database registration, and the defined cancellation/completion/cleanup semantics. Reuse native `DbDataSource`, `DbConnection`, and `DbTransaction`. Keep the three persistence outcomes distinct, and never automatically replay a transaction or downgrade an unknown commit after later cleanup.
>
> Qualify the real three-table pattern using PostgreSQL/Npgsql outside Core's shipping dependency closure. Keep production operation-result and outgoing-delivery implementations in their later roadmap items. Add bounded automatic Logging/Metrics, the public CommonTesting helper, a compiling consumer fixture, current module documentation, the checked-in transaction skill, and its root routing hook.
>
> Follow the five implementation outcomes in section 14, including meaningful tests, documentation, review, and evidence in their owning tasks. After shared contracts stabilize, parallelize only work with separate file ownership. Coordinate solution/package changes and builds. Use impact-based native verification and the existing public package-consumer mechanism; do not create a new test framework, broad qualification campaign, or large evidence bundle.
>
> Verify the acceptance matrix and preserve actual evidence limits. Resolve routine implementation details from the accepted design and existing authorization. Stop only for a genuine unresolved requirement, access limitation, scope/dependency decision, or applicable execution-authority constraint, and explain that specific blocker plainly. Do not claim implementation or qualification until it has actually occurred.

## 17. Source references

Repository links below identify the inspected baseline. Implementation must inspect the actual target checkout for later accepted changes. External references support native behaviour; the normative CISLIB requirements above are the design selected by this specification.

- **S01 — Repository instructions and composition:** [CISLIB AGENTS.md](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/AGENTS.md).
- **S02 — Dependency policy:** [Approved C# dependencies](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/docs/architecture/csharp-dependency-policy.md).
- **S03 — Existing Metrics contract and composition:** [Metrics Core API](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/docs/modules/metrics/core.md), [workflow metric catalog](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/contracts/metrics/workflows.md), and [collector stream catalog](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsStreamCatalog.cs).
- **S04 — Public test support:** [CommonTesting guide](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/docs/modules/common-testing.md).
- **S05 — Existing workflow behaviour:** [Sequential pipelines](https://github.com/andrewspiteri/CISLIB/blob/539a05d184432b5b8c131caf8454a44d0134c692/docs/modules/workflows/pipeline.md).
- **S06 — ADO.NET local transactions:** [Microsoft local transaction documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/local-transactions?view=sql-server-ver17).
- **S07 — EF transaction participation:** [Microsoft EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
- **S08 — Commit uncertainty and replay:** [Microsoft EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#transaction-commit-failure-and-the-idempotency-issue).
- **S09 — Native asynchronous/cancellation contracts:** [DbConnection.BeginTransactionAsync, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbconnection.begintransactionasync?view=net-10.0) and [DbTransaction.CommitAsync, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbtransaction.commitasync?view=net-10.0).
- **S10 — Npgsql data source and transaction usage:** [Npgsql basic usage](https://www.npgsql.org/doc/basic-usage.html).
- **S11 — PostgreSQL commit behaviour:** [PostgreSQL COMMIT](https://www.postgresql.org/docs/current/sql-commit.html).
- **S12 — PostgreSQL rollback behaviour:** [PostgreSQL ROLLBACK](https://www.postgresql.org/docs/18/sql-rollback.html).
- **S13 — PostgreSQL isolation:** [PostgreSQL SET TRANSACTION](https://www.postgresql.org/docs/18/sql-set-transaction.html).
- **S14 — Native configuration:** [Npgsql connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html).



