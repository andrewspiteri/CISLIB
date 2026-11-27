# Jobs: definitions, eligibility and admitted execution

`CIS.Jobs` uses the existing Core assembly. `JobDefinition` is immutable portable
metadata. `JobHandlerBinding` adds the C# handler type and an existing
`IMessageCodec<T>` for scheduled input. A category groups management and local
capacity; it never selects a handler, grants permission or changes a lease key.

## Explicit catalog registration

Use the application's ordinary `IServiceCollection` and
`CIS.Jobs.Registration`. Register categories and the exact handler bindings, then
freeze the catalog once:

```csharp
services.AddCisJobCategory(new JobCategoryDefinition(
    "wallet-service", "maintenance", "Maintenance"));
services.AddCisContinuousJob<WalletCompactionJob>(new JobDefinition(
    "wallet-service", "wallet.compaction", "Wallet compaction", "maintenance",
    [JobExecutionMode.Continuous], ["jobs.execute"],
    resourceScopeDescription: "A stable wallet partition"));

services.AddCisJobCategory(new JobCategoryDefinition(
    "customer-service", "lifecycle", "Lifecycle"));
services.AddCisScheduledJob<DormancyInput, CustomerDormancyJob>(new JobDefinition(
    "customer-service", "customer.dormancy", "Customer dormancy", "lifecycle",
    [JobExecutionMode.Scheduled], ["jobs.execute"]), dormancyCodec);
services.AddCisJobCatalog();
```

The continuous handler implements `IContinuousJobHandler`; the scheduled handler
implements `IScheduledJobHandler<DormancyInput>`. Registration offers the concrete
handler as an ordinary scoped service when the application has not registered it.
Use normal `ValidateOnBuild` and `ValidateScopes` on the actual provider to validate
constructor dependencies. Registration, catalog capture and inspection construct
no handlers, resolve no services, perform no provider I/O and start no work.

There is exactly one binding per ordinal `(ExecutionScope, JobId)`. A job that
supports both modes must use `AddCisCombinedJob<TInput,THandler>` and implement
both handler interfaces. Duplicate registrations are rejected rather than merged.
Every advertised mode must have an invokable matching binding. Definitions added
after `AddCisJobCatalog` require a new composition.

The injectable `IJobCatalog` lookup port exposes the registered count, limits,
exact `Find` and bounded `Inspect`; the default alias shares the concrete catalog.
`JobCatalog.Find(scope, jobId)` returns only the exact matching binding. Unknown
jobs have no fallback handler. Category registration must exist in the same scope.
`Inspect(offset, pageSize)` copies a deterministic bounded page; inspection and
disabled scheduled definitions need no scheduling provider.

## Explicit execution compatibility and worker metadata

`JobExecutionContract` optionally attaches exact execution/recovery declarations
to a `JobHandlerBinding`. It is separate from the scheduled `InputContract`:
changing an input DTO or comparing application release numbers does not establish
the semantics that a worker can start or recover. Each supported list copies at
most sixteen distinct `MessageContract` values. The current contract must appear
in `SupportedStarts`; an empty `SupportedRecoveries` explicitly declares no
original-result recovery support. Matching names/versions are ordinal and exact,
with no newer-version or semantic-version inference. `CheckpointContract` is
optional application-owned continuation metadata.

```csharp
var execution = new MessageContract("customer.dormancy.execution", 3);
var compatibility = new JobExecutionContract(
    execution,
    supportedStarts: [execution],
    supportedRecoveries: [new("customer.dormancy.execution", 2), execution],
    operationName: "CustomerDormancy");
JobHandlerBinding binding = JobHandlerBinding
    .Scheduled<DormancyInput, CustomerDormancyJob>(definition, dormancyCodec)
    .WithExecutionContract(compatibility);
services.AddScoped<CustomerDormancyJob>();
services.AddSingleton(binding);
```

Register this decorated binding once for the exact job before freezing the
catalog. `WithExecutionContract` returns a new binding; it does not alter an
existing registration or catalog. Existing Continuous, Scheduled and Combined
factories and convenience registrations keep their signatures, codec/invocation
behavior and null `ExecutionContract` when unselected. Attaching metadata resolves
no services, constructs no handler and does not encode or decode input.

`OperationName` is the actual consumer-registered native business-operation alias,
using the existing one-to-64-character Operations alias rules. It is never derived
from a job or handler name. Null permits passive legacy inspection and declares
no reliable execution alias; selected scheduler activation must require the
appropriate real operation registration through its separate integration.

`JobWorkerBuildIdentity` copies application version, immutable build identifier
and handler identifier (each at most 512 UTF-8 bytes), plus optional source revision
(128 bytes). `Capture(applicationVersion, handlerType, sourceRevision)` inspects
only already loaded non-dynamic type/module metadata. It identifies the handler
module by MVID, includes its metadata token and hashes the exact closed-type graph
so generic arguments and array shapes remain distinct. It also hashes the actual
Jobs interface-map target method MVID/token and declaring type, preserving an
inherited callback's implementation and detecting an actual override. Capture
admits at most sixteen callback targets, with each captured type graph bounded to
64 nodes and sixteen generic/element levels; it reads no file or custom
attribute and constructs/initializes no handler. The supplied application release
is display metadata, and the result is not binary attestation or authorization.

This passive API does not change dispatch or capture attempt entry. Per-attempt
integration must first resolve and verify the actual runtime handler against its
registered binding, then capture that runtime class before business callback
entry. Inspecting a registered Type alone is not evidence that it executed.
Original build/handler attribution must remain with an established original
outcome when another build later performs recovery.

## Settings and authorization remain separate

`JobConfigurationSnapshot` is a detached complete local revision with explicit
host enablement, category settings, job settings and finite limits. Both host and
new job enablement default to false. The runtime validates the entire snapshot
before adoption; invalid updates must retain the previous accepted revision.

`JobSettings.CategoryId` may reassign grouping within the same registered scope.
It preserves the stable job/resource coordination key, occurrence IDs and original
operation identities. Effective enablement requires host, category and job gates,
compatible registration, and current authorization. A job cannot override category
disablement or its hard concurrency ceiling. Missing category settings inherit the
host ceiling; job timeout may override the category default timeout.

`JobExecutionSettings` copies mode, idle delay, optional cooperative timeout, finite
drain budget, acquisition retry delay, schedule metadata and exclusive overlap.
Mode and scheduled-source binding stay fixed during an activated lifetime; a
change requires bounded drain, deactivation and reactivation. Invocation snapshots
are immutable. No business callback replay policy is selected: unexpected failure
faults that duty locally. Acquisition retry delay applies only to standby or an
attempt known not to have committed, never an indeterminate mutation or business
operation.

The scoped `ICoordinationService` uses existing established authorization. A job's
singleton runtime must not retain a scoped `ICapabilityAuthorizer`. Configure an
application-owned `IJobExecutionScopeFactory`; its `CreateAsync(JobScopeRequest)`
returns independently owned `IJobExecutionScope.Services`. An adapter can wrap the
published `SecurityHostContextScope.CreateAsync` using already-validated native
workload authentication. A copied `CallerContext`, producer header or claimed
capability is not authentication. Each finite iteration owns and drains its scope
before disposal; continuous idle waits retain no scoped handler.

## Finite handlers and original outcomes

`JobExecutionContext` carries the exact definition, explicit resource, managed
lease, independently scoped services, immutable execution settings and, for
scheduled work, `AdmittedRun`. Its grant must match the exact job/resource. The
lease's local state does not replace the ownership guard in the original RM-02
transaction.

A continuous handler returns `ContinuousIterationResult` with `MoreWork`, `Idle`,
`Failed` or `Indeterminate`. The runtime owns repetition and idle waits. Idle keeps
ownership while releasing business capacity; more work must yield before another
admission. A task ignoring cancellation remains observed and consumes capacity
until it actually finishes.

The scheduled result distinguishes `Completed`, `PreviouslyCompleted`, `Busy`,
`Deferred`, `OwnershipLost`, `Cancelled`, `Failed`, `Indeterminate`, `Unsupported`
and `Unauthorized`. Optional original `TransactionCompletion` remains intact.
Unknown commit cannot be constructed as Completed, and NotCommitted cannot be
reported Completed. Nontransactional work can return a completed result without
inventing a transaction. Staged/Prepared work is never established completion.

## Scheduled occurrences and the explicit scheduler boundary

`AdmittedJobRun` retains scope/job/resource, a stable occurrence ID, the original
`MessageOperationReference`, authoritative admission revision, immutable execution
settings, selected source/reference and optional supplied UTC scheduled time.
`Input` is the existing owned `EncodedMessage` with Command semantics and the
same original operation reference. `IMessageCodec<T>` decodes its registered
`MessageContract`; no new serialization format or broker selection is required.
The immutable input must fit the configured `MessagingLimits` before local
dispatch retention. Producer metadata remains a claim.

`IJobRunSource.ValidateAsync` validates the exact admitted target, revision,
snapshot and provider-owned reference. Constructing an `AdmittedJobRun` does not
establish admission. `IScheduledJobDispatcher.DispatchAsync` preserves pending
authoritative work when disabled or full by returning Deferred. It must reserve
immediate business/category capacity before acquiring ownership. Occurrences share
the stable job/resource key rather than an occurrence-only lease key.

`ExternalOccurrences` requires a trusted source advertising
`ExternallyAdmittedOccurrences`. `AutomaticRecurrence` additionally requires a
separate `IJobScheduleEvaluator` that supports the complete descriptor. Catalog
activation rejects missing capabilities only for effectively enabled scheduled
definitions. The controlled trigger demonstrates externally admitted dispatch;
it supplies no calendar engine and advertises no calculated next-run time.

Schedule descriptors validate these forms:

| Form | Required metadata |
| --- | --- |
| FixedDelay | Positive shared completion-relative delay. |
| Daily | Local time and explicit IANA-shaped time-zone identifier. |
| Weekly | Nonempty distinct weekdays, local time and explicit IANA-shaped identifier. |

Metadata capture performs no timezone database I/O or due-time calculation.
`ValidateActivation` checks the actual available IANA zone for effectively enabled
calendar definitions. A recurring evaluator must also support that complete zone
and policy contract before AutomaticRecurrence activation.
Calendar descriptors declare Skip for nonexistent local time and
OnceAtEarlierInstant for repeated local time. Missed-run policy is explicitly
Skip, RunLatestOnce or CatchUpBounded. Bounded catch-up requires a count from one
through 10,000 and a positive window; other policies reject those parameters.
The separate scheduler owns durable occurrence production and revision/enablement
admission. A local accepted configuration notification does not prove instant
cluster-wide pause.

## Published bounds

Strings retain exact ordinal text without trimming, normalization or case folding.
Empty/whitespace-only values, controls and malformed Unicode are rejected before
retention. Limits count UTF-8 bytes.

| Field | Maximum bytes |
| --- | ---: |
| Execution scope | 256 |
| Job/duty ID, category ID, source ID | 128 |
| Resource ID, occurrence ID, admission reference | 512 |
| Display name | 256 |
| Description | 2,048; empty is allowed |
| Resource-scope description | 1,024 |
| Time-zone metadata | 128 |
| Safe outcome/reason text | 128 |

Each job declares one or two distinct supported modes and one through eight
distinct existing Security capability names. Runtime defaults are 1,024
definitions, 64 categories, 32 continuous duties, eight unfinished invocations,
128 pending scheduled dispatches and 100 items per diagnostic page. Configurable
integer limits are positive and at most 65,536. These are local limits, not fleet
quotas. Managed coordination attempts/handles have their separate ownership bound.

Idle delay and acquisition retry delay initially use five seconds; maximum drain
uses thirty seconds; callback timeout is unset. Accepted job durations are positive
and no greater than 49 days, except maximum drain which must fit the managed
lease's one-day maximum and may be shortened by that owner's configured drain
ceiling. The sole supported overlap policy is
`ExclusiveJobResource`; unsupported enum values fail validation.

The [portable jobs contract](../../../contracts/coordination/jobs.md) separates
these metadata and execution facts from provider authority, physical fencing,
transaction completion and actual native evidence.
