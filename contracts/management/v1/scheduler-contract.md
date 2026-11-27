# Management scheduler portable contract

The additive scheduler profile uses the existing `/management/v1` HTTPS prefix,
native resource authorization, original Transactions/Operations/Audit and Jobs
ownership boundaries. It does not change the existing Management
`Capabilities` response: `implementedApis` and `callerHints` still describe
installed HTTP operations. Application features have their own trusted catalog.

[scheduler.schema.json](scheduler.schema.json) defines the finite data profile;
[literal conformance inputs](../../../conformance/management/v1/scheduler-cases.json)
separate structural schema acceptance from native wire and ownership rules.
These declarations do not advertise an installed scheduler backend. Feature
discovery must reflect actual selected, initialized, qualified handlers. Portable
contracts contain no transport, database, session, handler instance or UI code.

## Values and compatibility

Use canonical lowercase nonempty UUIDs, exact ordinal identifiers and UTC
`yyyy-MM-ddTHH:mm:ss.fffffffZ` timestamps. Do not trim, normalize or case-fold
identities. Stored revisions, generations and sequences are signed-Int64 decimal
**strings**; native local revision zero remains distinct from positive revisions.
`MessageContract` retains its existing ASCII `name` and positive Int32 numeric
`version`; a semantic contract version is not a deployment release label.

Commands are closed and versioned. Reject unknown command members, duplicate
decoded names, forbidden explicit null, malformed Unicode/UTF-8, trailing input
and complete-body/depth violations. Optional null in a local model encodes as
omission, not a wire null. Responses allow compatible additional members and
retain bounded unknown state values without interpreting them as successful.

The existing operation-reference shapes intentionally differ:

| Reference | Fields and lexical boundary |
| --- | --- |
| Administrative `OperationReference` | `name`, `scope`, `key`; route key is 1–128 URI-unreserved ASCII characters, excluding exactly `.` and `..` |
| Existing Messaging `MessageOperationReference` | `operationName`, `requestedScope`, `key`; business key retains the existing 1–128 visible ASCII-byte profile, including punctuation not permitted in an HTTP route key |

Both scopes retain the existing 256-UTF-8-byte bound. Never rename or narrow a
business reference when converting an admitted run to the existing Messaging
Command envelope. Original keys do not confer resource or execution authority.

## Trusted definitions and reported participation

`CapabilityKey` is the exact tuple `ownerApplication`, `kind`, `executionScope`,
`definitionId`, `definitionVersion`. Owner application is bounded by 255 Unicode
scalars; scope and definition ID use 256/128 UTF-8 bytes. Definition version is a
positive Int64 string. Known kinds are `job`, `triggerProvider`, `ruleEngine`.
An exposed rule-engine description does not create a rule engine or language.

`CapabilityDefinition` requires its key, human-readable display name and finite
supported operations. It can declare configuration, input, event and safe result
contracts/shapes, finite form hints and explicit execution support. Trust comes
from independently controlled catalog provisioning. Ordinary inventory or a
later process registration cannot install a schema or replace an accepted
version. Conflicts under one exact accepted key are explicit.

Contract/shape pairs must be complete. Trigger providers declare emitted event
contract/shape. Jobs cannot declare an event or `createConfigured` operation;
manual invocation requires its input contract/shape. `configure` or
`createConfigured` requires a Configuration contract. Execution support belongs
only to a job. Unknown bounded kinds/editors/operations can remain standalone
response metadata with unsupported interpretation; trusted catalog admission
requires a supported description.

Known operations are `inspect`, `configure`, `createConfigured`, `manual`,
`cancel`. They are owner-declared support, not permission grants. Choosing a job
does not register a handler. Providers explicitly distinguish new configured
instances from editing a fixed source or inspection only.

Forms name only declared object-member JSON pointers and fixed editors:
`text`, `number`, `boolean`, `enum`, `duration`, `group`, `calendar`. Groups and
lookups have distinct bounded IDs. A lookup names an authorized registered owner,
scope, lookup name and MessageContract; it supplies no network URL. Units are
`milliseconds`, `seconds`, `minutes`, `hours`, `days`, `bytes`, `count`, `percent`;
duration requires a time unit. Paths, group/lookup references and editor/shape
compatibility are validated against the actual declaration. No scripts,
expressions, HTML, React/CLR selectors or arbitrary components are interpreted.

Each complete encoded definition is at most 64 KiB: 16 operations, 128 fields,
32 groups, 16 lookups; structural shape depth 16, 1,024 nodes, 256 members per
object and 256 enum values of at most 1,024 UTF-8 bytes each. Descriptions are
metadata without defaults or settings values. Existing RM08 value disclosure
and actual typed owner validation remain independent.

`CapabilityParticipation` carries the key, actual registration UUID, inventory
sequence, observed/received times, availability, draining and optional worker
build. It is one reported version/instance observation, never another logical
definition, a current lease, a verified replica identity or activation authority.
Missing participation is not an empty successful feature catalog.

## Worker and semantic compatibility

`WorkerBuildReference` requires `applicationVersion`, `buildId`, `handlerId`
(512 UTF-8 bytes each), with optional `sourceRevision` (128 UTF-8 bytes).
Capture it from actual immutable startup/handler registration before attempt
entry. Authentication establishes who supplied it; it is not binary attestation.
An event payload cannot replace this provenance. Retain actual build/role for
each attempt and unknown original provenance when evidence is unavailable.

`JobExecutionSupport` requires `currentContract`, `supportedStarts` and
`supportedRecoveries`, with optional `checkpointContract` and `operationName`.
Each list has at most sixteen distinct MessageContract entries; the start list
contains the exact current contract. Empty recovery support does not imply
continuation. Omitted operation name permits passive inspection only; managed
activation needs the actual registered business operation alias. Newer release
labels, matching job names or similar JSON do not establish start/recovery
compatibility. Unmanaged Jobs consumers keep their existing behavior.

## Settings and execution authority

Persistent trigger and job settings use RM08 publication, coherent releases,
assignments and owner adoption. Invocation input is separate: exact compatible
event fields, typed constants or installed deterministic transformations. A
Scheduled Task links one configured trigger, one registered job and exactly one
resolved resource. No task, event or attempt ID joins the existing RM05
environment/scope/job/resource ownership key.

An accepted run retains an immutable capsule with original event/task revision,
input Command, business operation, contracts, detached settings and captured
workload binding. The ordinary live Configuration root is not temporarily
replaced during replay. Actual native principal/profile/policy, current managed
admission, local enablement and valid ownership remain independent checks before
callback effects. Configured creator, producer, optional manual requester,
actual executor and reported instance are distinct.

Submission acceptance proves only durable event/selected-run admission, not job
start or success. The internal receipt retains every selected run; a producer
without target-read permission receives no target IDs or count. Omission cannot
be replaced with an empty array or zero implying an actual zero-run event.

## Tasks, occurrences and immutable history

Trigger and Task UUIDs are retained before first creation. Trigger revisions,
Task desired revisions and active revisions are separate. A complete Task draft
selects its Trigger by source application and Trigger UUID, the exact logical Job
key, one resource rule, complete input mappings, published Job settings, optional
published Trigger settings, approved execution profile and explicit retry policy.
Updating desired state does not silently promote active state. No operation
creates a CLR handler or imports caller-supplied executable code.

`SchedulerExecutionSettings` contains exact positive 100-nanosecond tick strings:
`idleDelayTicks`, optional `timeoutTicks`, `maximumDrainTicks` and
`acquisitionRetryDelayTicks`. Existing Jobs duration limits apply: 49 days, with
drain at most one day. These framework settings are distinct from typed business
Job settings. A draft can omit them; admission freezes the actual selected owner
settings or explicit first-release defaults once. Every Run capsule includes the
resulting complete snapshot. Recovery never substitutes a newer worker's live
defaults. Conversion selects existing Scheduled/ExternalOccurrences execution
and ExclusiveJobResource ownership; the configured Trigger owns recurrence.

Task preview assesses the whole definition and can accept candidate settings
JSON without publication, assignment or execution effects. `taskId` identifies
an existing edit when present. A usable valid preview binds its full normalized
definition, SHA-256 draft digest, exact settings selections, current Trigger ETag
and existing Task ETag where applicable. `impactFullyDisclosed` independently
states whether the authorized affected-task view is complete. Hidden Tasks are
never replaced with an apparent complete count. Submission retains the reviewed
digest, expected Trigger ETag, strong Task precondition and original intent;
native validation and authorization run again at the write boundary.

An Event identity is the exact source `(applicationId, triggerId, eventId)`,
including for custom application callbacks. It is never a globally unique Event
UUID. Run summaries and capsules retain their source Trigger reference, so links
cannot silently select another source with the same Event UUID. Event details
and original receipts use that exact source. Produced-run pages authorize each
actual target application, including cross-application subscriptions, before
paging; source-read permission grants neither target visibility nor payload
disclosure. A general history query selects exactly one Task, one Job
`(executionScope, jobId)`, or one Event with its complete source pair. Resource,
time, known state, actual worker application version/build and execution-contract
filters further narrow that selection.

`Run.attemptCount` and `Attempt.number` are lossless Int64 strings, including
recovery attempts. Sixteen is the business retry ceiling, not a limit on all
historical attempts. Run detail includes at most one 500-item attempt page and
an optional continuation; older attempts use the dedicated page API. Attempt
roles are `execution` or `recovery`; admission states are `admitted`, `running`,
`settled`, `interrupted`. An optional `acquisitionId` names the existing
Coordination acquisition observation without granting authority. Worker reports
retain actual build, executor, effective policy and sequence. Optional
`durationMs` is a nonnegative Int64 string from actual monotonic observation;
UTC timestamp subtraction does not establish it. Original business completion,
callback settlement and current cleanup are independent facts.

Calendar forms are `fixedDelay`, `daily`, `weekly`; weekdays are distinct native
0–6 values. Local time is exactly `HH:mm:ss.fffffff`, preserving TimeOnly ticks;
UI input such as `09:30` converts before submission. `delayMs`,
`initialDelayMs` and `catchUpWindowMs` are canonical decimal millisecond strings
with at most four fractional digits, no exponent, leading integer zeros or
trailing fractional zero. `0.0001` preserves one TimeSpan tick. Delay/window are
positive and at most 49 days; an explicit immediate first run alone permits zero
initial delay. Missed-run policy is `skip`, `runLatestOnce` or `catchUpBounded`.
Bounded catch-up retains the existing descriptor ceiling of 10,000 and 49-day
window; selected defaults are 100 occurrences/seven days. Production scanning
still yields after at most 100 admissions. Invalid DST wall times skip; repeated
wall times run once at the earlier instant. Fixed-delay cycles and calendar
catch-up are not interchangeable. Failure policy `pause`/`continue` is separate
from an administrative state intent.

## Fixed optional HTTP groups

The following binding is for the additive typed implementation. All paths are
relative to `/management/v1`; `{a}`, `{t}`, `{k}`, `{r}` and `{e}` below mean
application, Trigger, Task, Run and Event canonical UUIDs, not display labels.
`S` abbreviates `/applications/{a}/scheduler`. No body-supplied application,
environment, actor or effective identity replaces the route/native owner.

| Method/path | Typed intent and response |
| --- | --- |
| `GET S/triggers`, `GET S/triggers/{t}` | Trigger page / exact Trigger |
| `POST S/triggers` | Create the retained Trigger UUID with `SchedulerTriggerCommand` |
| `PUT S/triggers/{t}` | Replace the complete Trigger draft |
| `POST S/triggers/{t}/state` | Fixed Trigger state command |
| `GET S/triggers/{t}/operations/{key}` | Original Trigger decision/rejection |
| `GET S/tasks`, `GET S/tasks/{k}` | Task page / exact Task |
| `POST S/tasks/preview`, `POST S/calendar/preview` | Side-effect-free whole Task / Calendar preview |
| `POST S/tasks`, `PUT S/tasks/{k}` | Create / replace complete Task draft |
| `POST S/tasks/{k}/state` | Fixed Task activation/state command |
| `POST S/tasks/{k}/runs` | Explicit manual occurrence for only this Task |
| `GET S/tasks/{k}/operations/{key}` | Original Task decision/rejection, including manual run |
| `GET S/runs`, `GET S/runs/{r}` | Narrowed logical-run page / detail with first attempt page |
| `GET S/runs/{r}/attempts` | Further attempts for that exact authorized Run |
| `POST S/runs/{r}/cancellation` | Durable cancellation request, not proof of physical stop |
| `GET S/runs/{r}/operations/{key}` | Original cancellation decision/rejection |
| `POST S/triggers/{t}/events` | Retained occurrence admission with `SchedulerEventCommand` |
| `GET S/triggers/{t}/events/{e}/receipt` | Original producer receipt, retaining actual zero/hidden selection |
| `GET S/triggers/{t}/events/{e}` | Exact Event with independently authorized captured content |
| `GET S/triggers/{t}/events/{e}/runs` | Independently authorized produced-run page across target applications |
| `GET /registrations/{registrationId}/scheduler/ready-runs` | Workload-only ready-run page under current registration authority |
| `GET /registrations/{registrationId}/scheduler/runs/{r}/capsule` | Workload-only immutable capsule, separate from operator history |
| `POST S/runs/{r}/attempts` | Exact durable attempt admission, after actual local scope/ownership |
| `PUT S/runs/{r}/attempts/{attemptId}/report` | One original sequenced attempt observation |

Creation requires `Idempotency-Key` and `If-None-Match: *`, retains the command's
UUID and refuses an existing different object; it is not an upsert. Replacement,
fixed Trigger/Task state and manual-run admission require `Idempotency-Key` and
one strong current `If-Match`. Cancellation requires its retained original key
and has no invented mutable Run ETag. Route IDs and command IDs must agree.
Every real administrative mutation has a required nonempty reason. Preview has
no operation key and no administrative effect. Event ingress recovers by exact
source occurrence; worker admission/report uses its exact attempt and sequence
identities, not a caller-chosen administrative replay key.

State commands are context-specific fixed intents: Trigger
`enable`/`disable`/`retire` and `resume` only for a known paused fixed-delay source;
Task `enable`/`disable`/`retire`/`activate`/`resume`;
Run cancellation `cancel`. Unknown intents or a known intent on the wrong
resource refuse admission. Eligibility is derived from actual trusted feature
support, native permission and current object state, then checked again by the
selected handler. Selecting an API does not start any producer or worker.

List/history queries use `pageSize` and opaque `cursor`, with default 100 and
maximum 500. General Run history uses the exact `SchedulerHistoryQuery` member
names; `executionContract` is represented by paired
`executionContractName`/`executionContractVersion` query values. Filters are
applied before paging. Duplicate query names, partial paired filters, malformed
URL percent-encoded UTF-8 and unsupported input refuse the request. Cursors bind
the endpoint, current validated principal/profile/policy, exact filters, order,
page size and source/target scope. Changed authorization/filter state requires a
fresh list; each row remains reauthorized. Pages imply no hidden fleet total,
historical snapshot or raw backend scan token.

Logical catalog operations are separate from the scheduler and HTTP API
Capabilities protocol: `GET /applications/{a}/capability-definitions` and
`.../capability-participation` accept the finite `CapabilityQuery` fields. Exact
`GET /applications/{a}/capability-definition` takes all five `CapabilityKey`
members as query values. Scope and definition text are not arbitrary path
segments. Named lookup descriptions do not install a generic URL dispatcher.

The additive RM08 companions are fixed source/binding action reads and trusted
cohort choices: `GET /configuration/sources/{sourceId}/actions`,
`GET /applications/{a}/configuration/{configurationId}/actions?schemaVersion=...`
and the corresponding `/cohorts?schemaVersion=...` page. They return
`ConfigurationActions` / `ConfigurationCohortChoice` metadata without issuer,
subject membership or hidden settings. `supported` and `allowed` are separate
advisory facts. Existing publication, complete assignment-map, adoption status
and original-key recovery APIs remain the configuration authority.

All client methods remain optional companion interfaces with per-call options,
cancellation and genuine typed Rest exchange/problem/result wrappers. Existing
`IManagementClient` substitutes acquire no compulsory member. Unsupported or
uninitialized backends advertise no scheduler/catalog support. This contract
slice defines data and bindings; later wire/backend qualification establishes
which fixed operations are actually installed.

## Completion and limits

Original administrative result/rejection, original transaction completion and
current attempt cleanup remain separate. A committed rejection is a business
rejection. A recovered original commit is preserved if current lookup cleanup
fails. Timeout, cancellation, lost lease, missing history or NotFound recovery
does not prove rollback or authorize a new-key replay. Calendar pause, task
disable, local job disable and cancellation have distinct authoritative effects;
none claims that entered code physically stopped.

The first-release scheduler ceilings are 1,024 triggers, 4,096 tasks and 10,000
unsettled runs per authority/environment; 64 selected tasks and 64 input mappings;
64 KiB event payload/input/detached settings; 256 KiB complete capsule; 4 MiB
complete persisted event admission; 1 MiB scheduler HTTP body; JSON depth 16;
16 KiB result summary; sixteen maximum business attempts; pages default 100,
maximum 500 where supported; reconciliation batches at most 100. Complete encoded
envelopes and completion reservations must fit too. Stricter enclosing limits
win, and unrelated existing Management/RM08 limits are unchanged.

The schema's `x-cis-*` annotations document native byte, lexical, uniqueness,
cross-reference, authority and lifecycle obligations. The existing structural
fixture evaluator removes these annotations; its passing result proves none of
them. Wire/client/provider tests must establish those facts before claiming
runtime support. No generic schema engine, replacement test harness or inferred
production/deployment qualification is introduced by these files.
