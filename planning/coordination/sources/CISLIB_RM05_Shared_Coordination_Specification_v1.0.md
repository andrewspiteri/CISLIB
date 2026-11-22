# CISLIB RM-05 Shared Coordination Implementation Specification

**Version:** 1.0  
**Date:** 22 November 2026  
**Target:** C# on .NET 10, with portable contracts for subsequent language bindings  
**Status:** Implementation specification  
**Audience:** CISLIB implementation agents, maintainers, application developers, and reviewers

## 1. Purpose and delivery outcome

RM-05 provides temporary exclusive ownership of named duties shared by several application instances. Eligible instances can acquire, renew, lose, release, and hand over ownership. Every acquisition has a distinct grant identity and an increasing generation. Protected database work validates ownership in the same transaction as its effects.

The module also supplies the job integration needed to organize hundreds of duties: stable job definitions, categories, explicit handler eligibility, continuous execution, and execution of scheduled occurrences supplied by a scheduler.

Completion means that two application instances can compete for the same eligible duty, recover ownership after failure, and reject a former owner's guarded database operations. Wallet compaction must always resolve to its registered compaction handler; customer dormancy must always resolve to its own handler.

The guarantee is one authoritative current grant per coordination key. Cancellation requests cooperative stopping. It does not prove that a paused or uncooperative process has stopped executing. Correctness-sensitive effects must participate in the ownership guard, recipient-enforced fencing, or the applicable durable-operation contract.

In this specification, “must” is a requirement; “should” permits a documented reason for a different implementation that preserves the stated behavior. API names below describe required concepts. Reuse equivalent published CISLIB APIs instead of creating competing abstractions.

## 2. Required implementation scope

| Area | RM-05 deliverable |
| --- | --- |
| Coordination | Portable contracts, exclusive leases, automatic renewal, terminal ownership-loss notification, conditional release, diagnostics, and bounded local state. |
| Persistence | One production PostgreSQL adapter using application-supplied ADO.NET connections, plus an ownership guard for the existing RM-02 transaction. |
| Job catalog | Stable job identity, application/execution scope, categories, supported modes, configuration descriptors, and explicit local DI handler bindings. |
| Continuous work | A bounded runner for finite iterations, retaining ownership across iterations and idle waits. |
| Scheduled work | An execution interface accepting an already-admitted occurrence from a scheduler or trigger provider; eligibility, capacity, ownership, handler invocation, and completion reporting. |
| Operational integration | Existing startup, Configuration, Logging, Metrics, Healthcheck, and CommonTesting integration; consumer documentation and repository agent guidance. |

### 2.1 Scheduler boundary

The separate scheduler owns calendar calculation, durable next-due state, occurrence creation, authoritative schedule-revision admission, missed-run selection, and catch-up production. RM-05 defines the integration and executes its admitted occurrences.

Registering a schedule descriptor does not itself arrange recurring execution. Scheduled activation explicitly selects ExternalOccurrences or AutomaticRecurrence. ExternalOccurrences requires a trusted admitted-run source; AutomaticRecurrence additionally requires a compatible evaluator for the configured schedule form. Missing required capabilities must fail activation clearly. Catalog inspection and disabled scheduled definitions remain usable without those providers.

Deliver one controlled scheduled-trigger example and test adapter that proves the integration. Identify it as a demonstration, not a production recurring scheduler. Do not conceal a second scheduling engine inside the coordination module.

The scheduling semantics in Section 12 are binding requirements for the integration contract and its eventual scheduler provider. Automatic recurrence is not an RM-05 completion claim.

### 2.2 Initial handover behavior

The first implementation supports graceful release followed by acquisition by any eligible successor, and takeover after expiry. It does not require directed placement on a named instance, administrative forced transfer, a cluster membership service, fairness guarantees, or distributed category quotas.

Central management remains a separately hosted CISLIB consumer. RM-05 must operate without it. A future remote authority must disclose its enforcement guarantees; an API ownership check cannot replace a same-database transaction guard.

## 3. Project constraints and existing module integration

1. Target .NET 10 and preserve the repository's dependency policy. Add no external runtime NuGet requirement to portable/Core contracts.
2. Reuse the existing application-supplied database provider and transaction approach. The application owns its data source/pool. Follow existing reference/test arrangements for PostgreSQL provider packages.
3. Keep language-neutral IDs, results, duration units, and ownership semantics common. .NET DI, Type, DbConnection, DbTransaction, CancellationToken, and hosting details belong in the C# binding/integration boundary.
4. Preserve published APIs and established compatibility behavior. Additive integration must not change unrelated startup or module semantics.
5. Use human-readable configuration, errors, diagnostics, and examples.
6. Use the existing native verification and CIS workflow. Do not create another test harness, mutation system, approval sequence, or evidence archive.

| Existing component | Required use |
| --- | --- |
| RM-01 identity/security | Trusted application/environment/instance context and permissions for execution scopes. A claimed capability is not authorization. |
| RM-02 transactions | Application-owned commit; guard, business changes, original result, and outgoing records can share the existing connection and transaction. Preserve Committed/NotCommitted/Unknown. |
| RM-03 durable operations | Preserve the logical operation ID and recover the original result across ownership changes. |
| RM-04 messaging | Preserve valid committed outgoing intent and existing delivery/duplicate handling. |
| Configuration | Validated snapshots and change notifications; no independent configuration framework. |
| Heartbeat/lifecycle | Reuse suitable scheduling/lifetime facilities while preserving timely lease renewal independently of job execution. |
| Type Manager and DI | Resolve explicitly registered handlers. Discovery alone must not activate every discoverable job type. |
| Logging/Metrics/Healthcheck | Automatic bounded instrumentation at owned boundaries. Standby contention is normal. |
| CommonTesting | Public service interfaces and controllable substitutes usable by consumers. |

The implementation agent must inspect the actual checkout, current instructions, dependency policy, RM-02/03/04 APIs, and examples before choosing public names and file locations. Do not derive implemented API signatures from older planning snippets.

## 4. Identities and portable data contract

### 4.1 Coordination key

The logical key contains four separate components:

| Component | Meaning |
| --- | --- |
| Environment | Existing authoritative environment identity. |
| Execution scope | The responsible application by default; an explicitly authorized shared group when needed. |
| Duty ID | Stable responsibility, such as wallet.compaction or customer.dormancy. |
| Resource ID | The protected subdivision, such as partition-12; use a stable default value for an unpartitioned duty. |

Use separate storage columns or an unambiguous canonical encoding. Joining arbitrary strings with a separator is insufficient unless escaping is specified. Equality is ordinal and case-sensitive; no culture-dependent transformations are allowed. Validate bounded UTF-8 lengths and reject empty/control-character values before store access. Publish exact limits and normalization rules in the portable contract.

The application/job adapter supplies the environment and allowed execution scope from trusted configuration/identity. A caller cannot gain permission merely by supplying another application's scope.

Category, display name, schedule, configuration revision, instance identity, and lease generation must not change the base coordination key.

### 4.2 Ownership grant

A grant contains:

- The full coordination key.
- Owner application/execution identity and a unique identity for the running process lifetime.
- A client-generated acquisition ID identifying this acquisition request.
- A unique grant/lease ID; the acquisition ID may serve this role if the published contract makes that equivalence explicit.
- A positive increasing generation, comparable within the authority and key.
- Authoritative expiry and enough remaining-validity information for conservative client deadlines.
- Provider/authority identity sufficient to prevent applying a grant to a different coordination authority.

Generation is an ordering value, not an authentication credential. Serialize its integer value losslessly across languages, for example as a decimal string on JSON boundaries. Do not rely on floating-point numeric precision.

A new process gets a new instance identity. A fresh acquisition, including by the same process, gets a new acquisition identity and higher generation. Renewal preserves the current generation.

### 4.3 Other identities remain separate

A scheduled occurrence has a stable occurrence/business-operation ID. A continuous batch may have its own application-owned durable operation ID or checkpoint.

Takeover changes the grant and generation; it must not change the logical operation ID when retrying the same work. Never derive an RM-03 operation ID from a lease ID or generation.

### 4.4 Required interface roles

Provide injectable, mockable interfaces for:

- Consumer coordination operations and managed lease acquisition.
- An ownership handle with identity, state, loss reason, cancellation signal, and asynchronous release/disposal.
- Provider operations, including exact-acquisition observation.
- Transaction-bound ownership enforcement.
- Job catalog lookup and local handler binding.
- Continuous-job execution and externally admitted scheduled-run dispatch.

Use the smallest set of concrete interfaces consistent with repository conventions. Separate provider implementation details from the consumer facade. Do not expose unrestricted database access through an ownership handle.

## 5. Acquisition, renewal, release, and uncertainty

### 5.1 Acquisition

Acquisition must be atomic per key:

1. Validate request shape, trusted caller scope, limits, and provider availability.
2. Serialize against competing grants for the same key.
3. Check the authoritative current record and current time.
4. Grant only when no live grant exists.
5. Allocate the new generation under the same serialization.
6. Persist the new owner, acquisition/grant identity, and expiry.
7. Return success only after the coordination transaction has committed.

Unrelated keys may progress independently. Do not acquire a deployment-wide lock for ordinary per-key operations.

Expected results must distinguish Acquired, Busy, Unauthorized/InvalidRequest, Unavailable, CapacityExceeded, and Indeterminate. “Busy” means another current grant exists; it must not conceal a failed or uncertain store operation.

Unavailable means the provider knows that this acquisition did not commit. If the mutation may have reached the authority and its commit cannot be determined, return Indeterminate. Do not route that uncertainty through an ordinary failed-acquisition retry path.

Inspection is diagnostic. A cached ownership read does not authorize later effects.

### 5.2 Acquisition with a lost response

A timed-out acquisition might have committed. It must not trigger the business callback.

The managed client may recover by observing the authoritative row for the exact key, owner, and acquisition ID for a bounded period. A matching live grant can be adopted only while that acquisition is still locally eligible and its actual remaining validity leaves a safe execution budget.

A read finding no matching grant proves only that it was not observed at that instant. It does not prove that an earlier timed-out mutation cannot still commit.

When recovery is abandoned:

- The local acquisition attempt becomes permanently terminal.
- Late responses cannot start work.
- No renewal is issued for the abandoned attempt.
- An orphaned grant can occupy the key until expiry.
- Exact-grant best-effort cleanup is allowed if the grant becomes known.

Do not add a permanent coordination-command history merely to claim exactly-once acquisition requests. The initial managed client must not blindly replay an indeterminate acquire mutation. Retry only when a preceding attempt is known not to have committed, or start a deliberately new acquisition after abandonment with the preceding uncertainty still handled safely.

### 5.3 Renewal

Renewal must atomically match the exact current key, owner, grant/acquisition identity, and generation, and require that the grant is still unexpired.

- Renew only the current live grant.
- An expired or released grant cannot be revived.
- Evaluate current time after obtaining the required store serialization.
- Return success only after commit.
- Serialize renewal attempts per local handle.
- Bound request timeouts and retries by the remaining confirmed budget.
- Do not extend the local deadline based on an unconfirmed response.
- An explicit expired/superseded rejection makes the handle lost immediately.

A late successful renewal may have extended the store record. It must not revive a local handle already declared lost; the unrenewed record will eventually expire.

### 5.4 Release

Release conditionally matches the exact current grant. A delayed release from a former owner must never release its successor's grant.

A released local handle is terminal even if the remote release result is uncertain. A retry may target only that same old grant. If a different grant exists, report a terminal not-current outcome without modifying it.

Graceful release must occur after the consumer's admitted work drains. Disposal of a handle cannot prove that independently spawned work has finished. The managed execution adapters own and await their callbacks; direct low-level consumers are responsible for their own work lifetime.

### 5.5 Error and cancellation contract

Expected contention, ownership loss, capacity refusal, and uncertain infrastructure outcomes are structured results with stable machine-readable codes and useful messages. Preserve existing CISLIB conventions for programmer errors and caller cancellation.

Request cancellation does not prove that a store mutation or business commit was rolled back. Keep coordination uncertainty separate from RM-02's business transaction outcome.

## 6. Managed handle state and timing

The managed lifecycle distinguishes Acquiring, Owned, Draining, Lost, and Released. Acquiring can terminate without becoming Owned. Lost and Released are permanent terminal states.

A draining handle stops new business admissions but may continue renewing for already-admitted work until its finite drain deadline. It cannot accept new iterations or scheduled callbacks.

Notify ownership loss once and publish a stable loss reason, such as Expired, Superseded, RenewalDeadlineExceeded, AuthorityUnavailable, or ShutdownDeadlineExceeded. User callbacks must not run under internal state/store locks.

### 6.1 Timing rules

Authority time determines persisted expiry. Local monotonic elapsed time determines a conservative stop deadline.

For an acknowledgement or recovery observation, derive the local deadline from request timing and the returned remaining validity. Do not start a new full lease duration when the response is received. Discard a response whose usable remaining budget has already expired.

Illustrative default settings for the first provider:

| Setting | Default |
| --- | --- |
| Lease duration | 30 seconds |
| Renewal interval | 10 seconds |
| Coordination request timeout | 2 seconds |
| Local safety margin | 5 seconds |
| Maximum graceful drain | 30 seconds |

Validate that renewal interval plus request timeout plus safety margin is less than lease duration. Validate actual remaining time on every acknowledgement. These settings are configurable; they do not guarantee recovery in exactly 30 seconds.

Client timing and cancellation are not the enforcement boundary. Process pauses and clock changes can invalidate local assumptions; protected operations still require the guard.

### 6.2 Local non-overlap after loss

The runtime must not restart or reacquire the same duty/resource locally while its previous callback is still executing.

If a callback ignores cancellation, retain observation of its task, continue charging its active execution slot, stop renewal at the defined deadline, and report the stuck condition. Do not remove its local occupancy merely because cancellation was requested.

Another instance may take over after expiry. The protected-resource guard is what prevents superseded effects in that case.

## 7. PostgreSQL reference provider and transaction guard

### 7.1 Store requirements

Use short committed coordination transactions against one authoritative writer. Do not make authority decisions from lagging replicas.

Persist at least the key components, current grant/owner identities, generation, expiry, and released/current state. Enforce a unique constraint over the complete canonical key, with database equality matching the portable key rules. Serialize first creation and recreation after pruning as well as updates; attempting to lock an absent row is insufficient. Acquire, renew, and release must use conditional updates and appropriate transaction serialization; a read followed by an unconditional write is invalid.

Use a logged, ascending BIGINT PostgreSQL sequence with CACHE 1 and NO CYCLE, allocated only after obtaining per-key grant serialization. Session caches larger than one can return values out of order across pooled connections [6]. Gaps are allowed. Never wrap, reset, recreate, or reuse the allocator during ordinary cleanup or migration. An allocator failure or exhausted range must refuse acquisition. Do not return the generated value as a usable grant before the grant transaction commits.

Released/expired records may be pruned only when recreation cannot reuse generations and cleanup cannot race a newer grant. Use exact-grant conditional cleanup and retain the durable allocator. Do not store a permanent row for every renewal.

Supported storage failover must preserve acknowledged ownership and generation history. Split authorities or a lossy restoration cannot silently retain the original guarantee. Document this deployment prerequisite; do not build a new consensus or disaster-recovery subsystem.

### 7.2 Transaction-bound ownership guard

The PostgreSQL guard must use the application's existing RM-02 connection and transaction:

1. Lock the ownership row by the immutable key.
2. After obtaining the lock, evaluate actual current authority time.
3. Validate the exact grant, owner, generation, and unexpired/current state.
4. Reject a missing, released, expired, or superseded grant.
5. Retain the lock until the application's transaction completes.
6. Permit protected business reads/writes only after validation succeeds.

PostgreSQL row locks can serialize competing changes until transaction end [1]. Current-time evaluation must account for the fact that CURRENT_TIMESTAMP/now() are fixed at transaction start; clock_timestamp() returns current time [2]. An expiry predicate evaluated before a lock wait is not sufficient.

A failed guard must prevent the protected operation and leave the transaction unable to commit those protected effects, using the existing RM-02 rollback-only/completion rules.

Do not open a hidden second transaction, commit on the application's behalf, or invoke the existing ADO.NET transaction callback recursively. Follow the actual RM-02 non-reentrancy contract. Document one consistent lock ordering with RM-03 operation records and application locks to avoid introducing inverse acquisition paths.

### 7.3 Handover boundary

A transaction admitted while the grant was valid may commit after nominal lease expiry. Its ownership-row lock delays takeover until that transaction commits or rolls back. The successor then obtains the next generation.

This is the intended serialization boundary. Do not also claim that every already-admitted transaction must commit before the wall-clock expiry.

Keep business transactions short. Never hold the ownership-row lock for the entire lifetime of a continuous duty, across idle waits, or while awaiting a long external API call.

## 8. Effects outside the coordination database

A separate receiving service must enforce the ownership information with the effect it protects. A token attached to a request is insufficient if the recipient ignores it.

A resource enforcing the highest generation it has accepted rejects older generations after the newer generation is installed or observed there. That does not provide instantaneous revocation when a separate coordination authority changes owners [3].

A remote “still owner” check followed by a write to another database is not atomic. Document the supported enforcement boundary for each adapter. For a recipient without suitable fencing or idempotency, do not promise one external effect.

### 8.1 RM-03 and RM-04 integration

Preserve the existing application-owned transaction model:

- Guarded business changes, the original durable-operation result, and outgoing records can commit together.
- Takeover of the same business work retains its original RM-03 key.
- Unknown business commit remains Unknown until resolved through the established recovery contract.
- Read-only recovery of an existing RM-03 result is allowed after ownership loss and does not require reviving the old lease. Any new protected effect still requires valid ownership.
- Losing a lease, receiving cancellation, or failing to release does not prove rollback.
- A valid committed outbox record remains deliverable after the originating lease expires or is released.
- Do not discard committed outgoing intent or demand that its originating owner remain current at delivery time.
- A genuinely lease-bound command needs a separately documented recipient-enforced contract; that is not the default for all outbox messages.

RM-05 does not create a second idempotency coordinator, operation-result store, inbox, or outbox.

## 9. Job catalog, eligibility, and categories

### 9.1 Definition and handler binding

A portable JobDefinition contains stable JobId, display name, description, execution scope, category ID, supported execution mode, and resource-scope description. Keep .NET handler Type and assembly names in the C# registration adapter.

Each local registration maps one job ID to its correct DI handler. Reject duplicate bindings for the same scope/job, missing handlers, invalid categories, and unsupported modes during activation.

Examples:

| Execution scope | Job | Handler binding | Category |
| --- | --- | --- | --- |
| wallet-service | wallet.compaction | WalletCompactionJob | maintenance |
| customer-service | customer.dormancy | CustomerDormancyJob | lifecycle |

Only instances with the compatible registered handler and permission for that execution scope may participate. A self-advertised capability grants no permission.

A host can register both jobs when it is authorized for both. Each dispatch must resolve the exact matching handler. Unknown duties are rejected before acquiring ownership or invoking unrelated code.

### 9.2 Categories

Use Application/Execution scope → Category → Job for the primary management hierarchy. One primary category per job is sufficient in v1.

Categories group jobs for discovery, diagnostics, enablement, policy defaults, and local capacity limits. They are not ownership identities, handler selectors, or automatic permission grants.

Changing category or display name must preserve the job's identity, lease keys, occurrence IDs, and committed results. Jobs in one category have separate ownership keys and can execute concurrently within their limits.

Category settings are application-scoped by default. Cross-application administrative groupings require an explicit shared scope. Pausing wallet-service/maintenance does not implicitly pause customer-service/maintenance.

### 9.3 Definition and runtime state

Keep the registered definition separate from runtime state. Expose bounded snapshots showing mode, effective enabled state, local state, observed owner, configuration revision, last outcome, and scheduler-supplied last/next occurrence where available.

Identify diagnostic owner/expiry values as observations, not authority for subsequent work. Continuous Idle, scheduled Waiting, and lease Standby are normal states. Do not invent next-run times without a scheduling provider.

## 10. Job configuration and control semantics

Use the existing Configuration module. Registration declares capabilities and safe defaults; configuration controls enabled state and permitted operational settings.

Job settings include supported mode selection, schedule descriptor for scheduled work, idle delay for continuous work, timeout where appropriate, bounded failure policy, overlap behavior, and capacity settings. Validate a complete snapshot before applying it.

New definitions are inspectable without activation. Enablement is explicit in deployment configuration. An enabled scheduled definition requires the provider capabilities selected by its ExternalOccurrences or AutomaticRecurrence activation setting.

Category defaults may supply timeouts and failure delays. A job may override defaults, but cannot override a category disable or exceed a hard category concurrency ceiling.

Effective enablement requires host enabled, category enabled, job enabled, compatible registration, and authorization. Reducing a capacity limit does not terminate an already-running invocation; it prevents further admission until the limit is respected.

### 10.1 Update boundaries

- Continuous iteration settings change between iterations.
- Execution mode and scheduled-source binding remain fixed while activated. Changing them requires bounded drain, deactivation, and reactivation under a supported mode/binding. Preserve the stable coordination key, and do not reactivate locally while the old callback still runs.
- Disabling a continuous job/category stops new iterations and initiates bounded drain.
- Existing scheduled occurrences keep their admitted identity and configuration snapshot.
- Schedule edits affect future occurrence admission.
- Invalid updates are reported and not partially applied; preserve the last valid snapshot and expose the rejected revision.
- Changing a schedule or category must not mint a new business-operation identity for a retry.

RM-05's ConfigurationChanged handling provides local convergence when a valid update is observed. It does not promise instantaneous cluster-wide pause or rejection of every stale configuration.

The scheduler/central-management authority must enforce its current revision and enabled state atomically with durable occurrence admission. A local change notification alone does not provide that guarantee. The scheduled-run contract must retain an admission revision and immutable snapshot so this boundary remains explicit.

## 11. Continuous execution

### 11.1 Invocation model

A continuous handler performs a finite iteration and reports MoreWork or Idle, or an explicit failure/uncertain outcome. The runtime owns repetition and idle waiting. Resolve handler dependencies through DI using the appropriate scope for each iteration; dispose that scope when its actual work finishes.

The runner must:

1. Resolve the exact registered job/resource and effective local configuration.
2. Observe ownership and capacity bounds before attempting acquisition.
3. Acquire the managed lease and start independent renewal.
4. Obtain an execution slot before each active iteration.
5. Invoke the handler with its ownership context, configuration snapshot, and linked cancellation.
6. Release the execution slot only when that iteration actually finishes.
7. Yield fairly before another MoreWork iteration, or await the configured delay when Idle.
8. Recheck local configuration and stop conditions between iterations.
9. Drain and release according to Section 14.

Retain ownership during idle waits while releasing business execution capacity. A continuously backlogged job must yield admission opportunities to other eligible work; it must not monopolize one scheduler callback indefinitely.

A default idle delay of five seconds is suitable for the example and is configurable. Require a positive idle delay or a genuine asynchronous input-wait mechanism. Do not translate “continuous” into a 10 ms polling loop across the catalog.

### 11.2 Recovery and failures

The handler owns durable checkpoints and stable operation identities for its work units. RM-05 does not invent a checkpoint schema or wrap an arbitrary infinite loop in an exactly-once claim.

The default unexpected handler failure faults that duty locally and drains/releases its grant after the callback ends. Do not automatically replay a business callback after an exception or uncertain commit. Explicit opt-in retry requires a documented repeatable unit or RM-03 recovery, bounded attempts, and a nonzero backoff.

A local retry limit is not a deployment-wide retry limit. Another eligible instance may acquire the duty under normal failover rules. Fleet-wide retry admission or quarantine belongs to an authoritative scheduler/application policy.

A failed job must not stop unrelated jobs by default. Keep its latest bounded diagnostic and expose its state. Preserve observation of incomplete tasks as required by Section 6.2.

## 12. Scheduled occurrence integration and schedule contract

### 12.1 Scheduled-run input

The scheduling/trigger provider supplies an admitted run containing:

- Execution scope, JobId, and ResourceId.
- A stable occurrence ID and the corresponding durable business-operation identity.
- Scheduled time in UTC where applicable.
- Admission/configuration revision and an immutable execution-settings snapshot.
- Bounded handler input using the existing application/message serialization contract.
- A provider-owned admission reference or equivalent trusted context that can be validated.

A future remotely supplied admission must be authenticated and authorized through the established service-security path. An arbitrary request body declaring itself admitted is insufficient.

The scheduler owns the durable occurrence and may redeliver it. RM-05 must not acknowledge completion before the execution outcome required by that contract is established.

### 12.2 Dispatch sequence

The execution adapter must:

1. Validate admission, target scope, registered handler, supported mode, and permission.
2. Check whether local execution is currently enabled. A local disable before callback admission returns Deferred and preserves the durable occurrence rather than silently completing it.
3. Reserve immediate local/category capacity, or return Deferred without acquiring a lease.
4. Acquire the correct duty/resource lease.
5. Execute the exact registered handler with the admitted snapshot and stable operation identity.
6. Preserve any RM-02/03 original-result recovery and commit outcome.
7. Drain the callback and conditionally release ownership.
8. Report a precise execution result to the scheduling provider.

Distinguish Completed, PreviouslyCompleted, Busy, Deferred, OwnershipLost, Cancelled, Failed, and Indeterminate, plus unsupported/unauthorized input. Reuse existing result types where they preserve those meanings.

Prepared or otherwise uncommitted business work is not Completed. A lost acknowledgement or ownership change is not proof that the run failed to commit. The scheduler decides whether and when to retry, using the same occurrence ID.

### 12.3 Overlap and scope

The initial job adapter permits one current ownership grant for the same job/resource across occurrences. It uses the stable job/resource coordination key, not an occurrence-only lease key. Participating effects are serialized by the transaction guard; a stale callback in another process may still execute after loss. Section 6.2 additionally forbids a second local callback until the first actually finishes.

Different explicit resources/partitions remain independent and may run concurrently. The occurrence ID remains distinct for durable-operation recovery.

Expose this as the exclusive job/resource ownership policy and document the callback limitation above. Reject a requested parallel-occurrence policy as unsupported in v1 rather than quietly changing key construction. More flexible policies can be added with the scheduler when their resource and configuration-transition semantics are defined.

### 12.4 Configurable schedule descriptors

Define portable descriptors for these initial integration forms:

| Form | Required settings and semantics |
| --- | --- |
| FixedDelay | Positive delay measured from completion of the previous logical occurrence; shared completion state belongs to the scheduler, not each replica's clock. |
| Daily | Local execution time and explicit IANA time-zone ID. |
| Weekly | Nonempty set of weekdays, local execution time, and explicit IANA time-zone ID. |

A provider must distinguish the capability to deliver externally admitted occurrences from the capability to evaluate recurring schedules. An evaluator declares which schedule forms it implements and rejects unsupported AutomaticRecurrence activation. RM-05 validates descriptor shape and dispatch integration; the separate scheduler computes due times. An ExternalOccurrences source may carry validated schedule metadata for context without advertising evaluation of it. A general cron parser or another external scheduling dependency is not required by this delivery.

For calendar schedules, the contract must state behavior for invalid local times and repeated local times. The initial descriptor defaults are Skip for a nonexistent local time and OnceAtEarlierInstant for a repeated time. A scheduler provider must implement these declared policies before accepting the descriptor. Store admitted occurrence instants in UTC; never silently inherit a machine's local time zone.

Missed-run behavior must be explicit:

- Skip: omit missed occurrences.
- RunLatestOnce: create at most one catch-up occurrence representing the latest relevant missed schedule.
- CatchUpBounded: retain distinct required occurrences with an explicit catch-up bound/window and durable pending state.

A maintenance example may use RunLatestOnce. Do not silently apply that policy to a business job requiring every accounting period. The scheduling provider owns catch-up production and backlog handling.

A supported scheduling implementation must enforce the effective configuration revision and enabled state atomically with occurrence admission. Already-admitted occurrences retain their identities and snapshots. RM-05's controlled example must document which admission guarantees its test trigger provides.

### 12.5 Controlled integration example

Provide a small example trigger that declares only the ExternallyAdmittedOccurrences capability, uses ExternalOccurrences activation, submits a known stable occurrence through the dispatch interface, and can submit it again to demonstrate recovery. It may display the supplied schedule metadata but must not calculate or advertise recurring execution or a next-run time. Its configuration must not activate AutomaticRecurrence.

The acceptance test verifies correct dispatch, exclusive authority across occurrences, guarded effects, and RM-03/RM-04 behavior. It does not claim that the production recurring scheduler has been delivered.

## 13. Capacity and bounded state

Use explicit per-instance limits. The defaults below are initial configuration values, not cluster-wide guarantees:

| Limit | Initial default |
| --- | ---: |
| Registered job definitions per host | 1,024 |
| Registered categories per host | 64 |
| Managed ownership handles per instance, including unresolved live attempts | 64 |
| Owned continuous duties per instance | 32 |
| Concurrent business invocations per instance | 8 |
| Pending scheduled dispatches retained locally | 128 |
| Diagnostic snapshot page size | 100 |

A category's concurrent-execution ceiling defaults to the host execution ceiling unless a smaller limit is configured. Validate all limits and reject startup catalog overflow clearly. For dynamic workload pressure, return CapacityExceeded/Deferred and preserve authoritative pending work.

Do not create a dedicated thread, timer with unrestricted callbacks, unbounded task, or unbounded retry queue per job. Use asynchronous waits, bounded scheduling, and reusable state. Built-in .NET bounded channels are a suitable implementation option [5].

Business execution saturation must not justify missed renewal deadlines. Use bounded independent renewal capacity or an equivalent mechanism preventing acquisition/dispatch bursts from starving renewal. If the runtime cannot renew in time, lose authority conservatively.

Keep these counts distinct:

- An idle continuous duty retains an ownership slot.
- It does not retain a business execution slot.
- An uncooperative callback continues to consume an active execution slot.
- Pending scheduled work must not hold hundreds of leases while awaiting local execution capacity.

Do not silently drop durable scheduled work when a local queue is full. Return a deferred outcome to the scheduler/transport that owns it. Do not acquire broker acknowledgements or alter RM-04 delivery semantics merely to free local memory.

Expose bounded or paginated resource discovery and diagnostics. Avoid retaining all possible wallet/customer IDs as local coordination state. Prefer stable configured duty/partition keys for maintenance and retain ordinary database serialization for individual ledger operations.

## 14. Startup, readiness, shutdown, and handover

Use the established CISApplication startup stages and startable-service contract. Constructors wire dependencies; startup validates registrations/configuration and starts managed background activity. Starting a continuous runner must not await its entire operational lifetime.

Registration success is distinct from ownership acquisition. A healthy standby instance is ready unless the application explicitly declares that holding a particular duty is a readiness requirement. A job's Idle/Waiting/Standby state is not an error.

Required provider/configuration failures follow the existing host policy and produce a clear diagnostic. Activating scheduled work without its admitted-run source fails validation; activating AutomaticRecurrence without a compatible evaluator also fails. Telemetry failure must not change successful business behavior.

### 14.1 Graceful shutdown

1. Stop admitting new acquisitions, iterations, and scheduled callbacks.
2. Enter Draining for owned duties.
3. Continue renewal while already-admitted work finishes, within the finite drain budget.
4. Persist application-owned progress through its normal guarded transaction.
5. Release the exact grant once the callback and its protected work have finished.
6. Dispose handlers, registrations, and provider resources in the existing host lifecycle.

If drain expires, signal cancellation, stop renewal, keep observing unfinished tasks, and allow expiry. Do not report graceful completion, erase occupancy, or release a successor's grant.

A target-directed transfer is not required in v1. Any future target must pass the same handler-eligibility and authorization checks. No handover migrates arbitrary in-memory execution state.

## 15. Logging, metrics, health, and diagnostics

### 15.1 Logging

Automatically record meaningful transitions: acquired, draining, released, lost, configuration rejected, duty faulted, and drain exceeded. Include job/duty, category where relevant, instance, resource, acquisition/grant identity, and generation in structured diagnostic context.

Use existing Logging filtering, duplicate suppression, and bounded storage. Do not log every successful renewal, every standby poll, or every empty continuous iteration. Rate-limit repeated infrastructure failures and do not log credentials, payloads, or database secrets.

### 15.2 Metric contract

Use the existing CISLIB metric facade and collector/exporter. Do not introduce a second provider, event-history buffer, aggregation engine, or exporter.

The following are new logical contract names; verify and document their actual exported mapping in the current implementation before freezing the module metric catalog:

| Logical name | Instrument / unit | Owned boundary and dimensions |
| --- | --- | --- |
| cis.coordination.operation.duration | Histogram / seconds | One physical provider call, from invocation to returned result; operation acquire/renew/release/resolve and a finite outcome set. |
| cis.coordination.active.leases | Gauge / count | Locally Owned or Draining handles; no I/O during observation. |
| cis.coordination.ownership.losses | Counter / count | One terminal loss transition; finite reason. |
| cis.coordination.guard.rejections | Counter / count | One rejected protected-operation admission; finite rejection reason. |
| cis.jobs.invocation.duration | Histogram / seconds | One completed continuous iteration or scheduled callback invocation; mode and finite final outcome. |
| cis.jobs.active.invocations | Gauge / count | Actual unfinished callbacks by mode, including callbacks ignoring cancellation. |
| cis.jobs.pending.dispatches | Gauge / count | Actual bounded locally pending scheduled dispatches. |

Every exported sample must preserve the existing authoritative application and environment labels. Use monotonic elapsed time for durations. Counters are cumulative for the provider lifetime; no per-second reset or local rate computation.

Keep outcomes finite and document success, failure, cancelled, ownership_lost, indeterminate, and provider-specific classifications. A callback that has not finished has no completed-duration observation. Do not fabricate zero durations.

Default metrics omit JobId, ResourceId, instance ID, lease ID, generation, and arbitrary category values. The catalog may contain hundreds of jobs; those identifiers belong in diagnostics. Optional category dimensions require an explicit bounded allowlist and a cardinality calculation within the existing Metrics budget.

Use the established histogram profiles and stream limits. Avoid a separate total-count counter where histogram count already measures the same event. Instrumentation failures must preserve the original application result.

### 15.3 Health and inspection

Expose bounded snapshots of active/standby/faulted jobs, observed owner, configuration revision, last failure, and latest execution facts. Scrapes and health reads must not query the database or run business work.

Authority unavailability and stuck required duties can affect the existing configured health policy. Contention and normal idle periods must not. Mark unavailable observations as unavailable; do not substitute zero activity or a fabricated owner.

## 16. Security and authorization

Validate trusted application/environment scope before managed job acquisition or dispatch. Keep identity verification in the existing RM-01 path.

For a future remote coordination endpoint, enforce permitted execution scopes server-side. For direct database access, use appropriately restricted credentials/procedures and document that a caller with unrestricted table access can bypass application-level checks.

Grant generations are not secrets or bearer credentials. Authentication and authorization remain necessary. Do not trust arbitrary instance IDs, category names, capability advertisements, or admission claims as proof of permission.

Scope shared cross-application duties explicitly. Keep independent environments isolated by default. Bound keys, descriptors, diagnostic fields, and dispatch input before allocating runtime state.

## 17. Consumer recipes and examples

### 17.1 Wallet compaction

Register wallet.compaction in wallet-service, category maintenance, Continuous mode, with stable partition resource IDs.

Each eligible wallet instance can contend for a partition. The owner processes finite batches, acquires the transaction-bound ownership guard before protected business work, preserves its durable checkpoint/operation identity, and renews independently between iterations.

Customer-service instances must not resolve or acquire this managed job merely because they share a category label.

### 17.2 Customer dormancy

Register customer.dormancy in customer-service, category lifecycle, Scheduled mode. Configuration can describe a Daily schedule at 02:00 in Europe/Malta with explicit missed-run behavior.

A compatible scheduling provider admits an occurrence and supplies its stable ID. The execution adapter obtains capacity and the job/resource lease, invokes CustomerDormancyJob, and reports the established result. Redelivery/takeover of that occurrence keeps the same RM-03 identity. The RM-05 controlled example uses ExternalOccurrences activation; AutomaticRecurrence of the Daily descriptor requires the later compatible evaluator.

### 17.3 Guarded database work

Use the actual published APIs to implement this conceptual sequence:

~~~text
receive registered job/resource and, for scheduled work, admitted occurrence
obtain a confirmed managed ownership grant
begin the application's RM-02 transaction
lock and validate the ownership grant in that transaction
use the original RM-03 operation identity
perform protected application changes when the operation requires execution
record its original result and any RM-04 outgoing records
complete through the existing application-owned commit
if completion is Unknown, use existing durable-operation recovery
drain the callback and release the exact grant
~~~

Keep the established RM-03 locking/orchestration contract and the documented common lock order. Original-result lookup can recover previously committed work; it must not repeat the business effect merely because ownership changed.

### 17.4 Example configuration responsibilities

| Item | Registration/definition | Configurable behavior |
| --- | --- | --- |
| wallet.compaction | WalletCompactionJob; wallet-service; maintenance; Continuous | Enabled, idle delay, local limits, finite drain policy. |
| customer.dormancy | CustomerDormancyJob; customer-service; lifecycle; Scheduled | Enabled, supported schedule descriptor, explicit missed-run policy, timeout, no-overlap policy. |
| maintenance category | Stable category within an application scope | Enabled, overridable defaults, hard local concurrency ceiling. |

Examples must compile against the delivered API, use correct DI scopes, and demonstrate two independent application instances. Reuse the existing consolidated example structure; avoid another collection of duplicate sample applications.

## 18. Focused acceptance matrix

These are behavioral scenarios, not a requirement for a new harness or one test per sentence. Use deterministic clocks and controllable providers for local-state behavior, plus the existing real PostgreSQL fixture for transaction/concurrency proof.

| ID | Scenario | Required evidence |
| --- | --- | --- |
| AC-01 | Contention and independent keys | Concurrent contenders on one key produce one committed current grant; unrelated keys progress. |
| AC-02 | Expiry and reacquisition | New acquisition has a higher generation, including same-process reacquisition; expired grant cannot renew. |
| AC-03 | Stale operations | Old renewal/release cannot modify a successor; old guarded writes are rejected. |
| AC-04 | Acquire uncertainty | Lost reply starts no work; exact live acquisition can be recovered; abandoned attempt and late reply cannot activate a callback. A missing observation is not treated as proof of no pending commit. |
| AC-05 | Renewal/release uncertainty | Unconfirmed renewal does not extend the local deadline; late responses cannot revive a terminal handle; uncertain release stays locally terminal. |
| AC-06 | Guard admission after a lock wait | Time is checked after obtaining the lock; a grant that expired while waiting is rejected before protected work. |
| AC-07 | Transaction/takeover serialization | A valid admitted transaction completes before competing takeover; successor then gets the next generation. |
| AC-08 | Generation and cleanup | Sequential acquisitions through different pooled sessions increase; restart/release/prune/recreation cannot reset generation; first creation is correctly serialized. Verify allocator settings. |
| AC-09 | Correct handler and authorization | Wallet/customer registrations dispatch only to their matching eligible handler; one authorized host can register both; unknown/missing/unauthorized targets are rejected. |
| AC-10 | Category/configuration | Category changes preserve keys and IDs; category disable cannot be overridden by a job; invalid updates do not partially apply. Local convergence is not reported as instantaneous cluster enforcement. |
| AC-11 | Continuous capacity | Idle duty retains ownership but releases execution capacity; finite iterations yield to other work; renewal remains independent. |
| AC-12 | Drain and uncooperative work | Release follows completed drain; exceeded drain stops renewal; callback remains observed/counts as active; no same-key local restart while it still runs. |
| AC-13 | Scheduled integration and overlap | Missing admitted-run source or required recurrence evaluator fails clearly; controlled external dispatch works without claiming calendar support; occurrences share one current job/resource grant and guarded effects serialize. A stale remote callback is not falsely reported as physically stopped. |
| AC-14 | Durable recovery and committed intent | Takeover/redelivery retains RM-03 identity, recovers original/unknown commit correctly, and leaves committed RM-04 outgoing records deliverable after ownership ends. |
| AC-15 | Bounds and diagnostics | Hundreds of catalog definitions remain within configured bounds; pressure returns Deferred/CapacityExceeded without dropped durable work or unbounded state. |
| AC-16 | Instrumentation and compatibility | One representative real metrics scrape has correct names/units/identity/bounded labels; transition logs avoid renewal flooding; standby health and existing APIs/startup remain correct. |

Use a handful of process/connection-level database cases for the actual concurrency risks. Do not simulate PostgreSQL locking solely with an in-memory fake. Reuse existing Windows/Linux gates when the repository requires them; report a genuinely unavailable environment instead of claiming it was exercised.

Stop optional testing once the changed risks and required gates are covered. Do not add broad mutation campaigns, many near-duplicate timing tests, permanent polling logs, or large evidence bundles.

## 19. Implementation work packages

Use existing CIS feature-input, story, and task conventions. Keep stories no larger than medium and attach the relevant acceptance scenarios to each.

| Package | Outcome | Dependencies / acceptance |
| --- | --- | --- |
| WP-1 Contracts and catalog | Portable IDs/results, lifecycle, provider/guard interfaces, job/category definitions, DI bindings, bounded settings. | Establish first; AC-09/10 shape and compatibility checks. |
| WP-2 PostgreSQL authority | Atomic grant operations, generation allocator, exact-acquisition observation, migrations/cleanup, RM-02 ownership guard. | WP-1; AC-01/02/03/06/07/08. |
| WP-3 Managed ownership | Automatic renewal, conservative deadlines, uncertainty recovery, terminal states, drain, public testing substitute. | WP-1; AC-04/05/12; can proceed alongside WP-2 using a controlled provider. |
| WP-4 Job integration | Continuous runner, capacity controls, configuration adoption, admitted scheduled-run adapter. | WP-1 and WP-3; AC-09/10/11/12/13/15. |
| WP-5 Existing-module integration | RM-02/03/04 consumer proof, lifecycle/health, Logging/Metrics, consolidated example. | Required interfaces stable; AC-14/16 and selected real-store cases. |
| WP-6 Documentation and closeout | Public/module docs, metric catalog, repository skill/routing, acceptance review and concise completion report. | Integrated behavior; include docs/tests alongside each preceding package rather than deferring discovery to the end. |

Parallelize WP-2 and WP-3 after shared contracts are established. Other work may proceed in parallel on agreed nonoverlapping files. One owner coordinates project files, shared contracts, migrations, and builds writing common outputs.

Inspect current native command help and use the installed workflow. Import this as the supplied feature input without replacing the product-wide BRD or recreating the workspace. Reuse already authorized decisions; do not create additional routine approval checkpoints.

## 20. Documentation and agent guidance deliverables

Deliver one coherent consumer guide covering:

- Registration, categories, eligibility, and execution scopes.
- Confirmed acquisition, Busy/Unavailable/Indeterminate outcomes, renewal, loss, and release.
- Ownership guard usage in the actual RM-02 transaction API.
- Stable RM-03 operation identity and RM-04 committed-outbox behavior.
- Continuous finite-iteration behavior and bounded shutdown.
- Scheduled-run integration, descriptor semantics, and the explicit scheduler dependency.
- Actual supported settings/defaults, key limits, provider prerequisites, migration/cleanup rules, and diagnostics.
- Correct and incorrect patterns, including callback work continuing after cancellation, remote check-then-write, local limits mistaken for deployment-wide limits, and category renames changing identity.

Update the portable contract, actual C# API documentation, compiling examples, and module metric catalog together.

Add or update the repository's coordination consumer skill, normally under .github/skills/cislib-coordination/SKILL.md, using the repository's actual convention. Add a short routing entry to the existing AGENTS.md without overwriting managed guidance or unrelated instructions.

The skill must direct future agents to the real implemented API and examples and require them to:

1. Establish the correct duty/resource scope and handler eligibility.
2. Use managed leases and terminal loss semantics.
3. Guard correctness-sensitive database effects in the existing transaction.
4. Preserve business operation/occurrence IDs on retry and takeover.
5. Drain work before release and retain observation of uncooperative callbacks.
6. Keep categories, execution modes, scheduling, and ownership responsibilities distinct.
7. Reuse existing instrumentation with bounded labels and native verification.
8. Avoid adding another scheduler, transaction coordinator, or test/evidence framework.

Validate the repository skill using the currently installed native tooling. Do not install a separate personal assistant skill as a substitute for repository deliverables.

## 21. Implementation-agent handoff

~~~text
Implement CISLIB RM-05 from this specification.

First inspect the current checkout, AGENTS.md, dependency policy, installed CIS
workflow, and the real RM-01/02/03/04, lifecycle, Configuration, Metrics, and
CommonTesting APIs. Preserve the supplied specification as feature input;
do not replace the product-wide BRD or rebuild the workspace.

Establish the portable contracts, job catalog, and file ownership first.
Implement the PostgreSQL authority/transaction guard and managed ownership
lifecycle in parallel where the existing workflow permits. Then integrate
bounded continuous duties and execution of externally admitted scheduled runs.

Preserve the stated scheduler boundary: schedule descriptors do not create
recurring execution by themselves. Deliver a truthful controlled trigger
example and fail unsupported scheduled activation clearly.

Use the same RM-02 transaction for the ownership guard and protected effects.
Preserve the original RM-03 operation identity and Unknown commit semantics.
Committed RM-04 outgoing intent remains valid after ownership ends.

Keep new Core contracts free of external runtime package requirements,
preserve public compatibility, and reuse native modules and test fixtures.
Complete the acceptance matrix with focused evidence. Do not create another
harness, mutation campaign, large artifact collection, or repeated approvals.

Deliver working examples, CommonTesting substitutes, module/metric docs,
repository coordination skill, and AGENTS routing. Report exact behavior,
focused verification, actual limitations, and the implementation commit.
Do not claim the recurring scheduler or central management has been built
as part of this module.
~~~

## 22. Definition of done

RM-05 is complete when the required scope in Section 2 is implemented, the acceptance matrix has grounded results, examples compile, documentation and repository guidance describe the actual API, and compatibility/dependency requirements are met.

The closeout report must identify the implementation commit, the native verification performed, any blocked environment, and concrete limitations. Keep evidence to concise results and necessary checkpoints. Demonstrating one current grant without stale-effect enforcement is not sufficient completion.

## 23. Technical references

These sources support the specific implementation mechanisms; CISLIB behavior and defaults are defined by this specification.

1. [PostgreSQL explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html): row-lock conflict and transaction-lifetime behavior.
2. [PostgreSQL date and time functions](https://www.postgresql.org/docs/current/functions-datetime.html): transaction-start timestamps and actual current time.
3. [Hazelcast FencedLock documentation](https://docs.hazelcast.com/hazelcast/5.7/data-structures/fencedlock): paused owners and recipient-enforced increasing fencing tokens.
4. [Kubernetes leader-election source](https://github.com/kubernetes/client-go/blob/master/tools/leaderelection/leaderelection.go): leader election does not physically fence arbitrary client execution; release must respect critical-work lifetime.
5. [.NET System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels): bounded asynchronous queues available through the shared framework.
6. [PostgreSQL CREATE SEQUENCE](https://www.postgresql.org/docs/current/sql-createsequence.html): logged sequences, NO CYCLE, and CACHE 1 ordering across sessions.
7. [Kubernetes CronJob contract](https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/): independently specified scheduling, time zones, concurrency, and missed-run controls.

No dependency on Hazelcast or Kubernetes is introduced by these references.



