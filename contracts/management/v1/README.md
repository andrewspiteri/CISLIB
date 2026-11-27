# Management v1 protocol

The optional unpublished Configuration candidate-preview companion uses exact
source/map preconditions and explicit baseline references without creating a
publication identity or effect. See [candidate preview](../../../docs/modules/management/configuration-candidate-preview.md),
ConfigurationCandidatePreviewCommand and ConfigurationCandidatePreview.

The optional exact Configuration editor-context read returns one actual binding,
immutable shape and independently authorized selected-source declared defaults.
It preserves source absence, redaction and disabled/unavailable states without
adding mutation or runtime authority. See [binding editor context](../../../docs/modules/management/configuration-editor-context.md)
and the canonical ConfigurationEditorContext schema and conformance vectors.

The additive v1.3 discovery surface supplies literal application queries,
authorized environment pages and exact-application metadata-only Configuration
definitions. See [discovery semantics and cursor boundaries](../../../docs/modules/management/discovery.md).
Existing unfiltered APIs remain supported; unsupported optional backends do not
advertise the additions.

Use `/management/v1` relative to the configured HTTPS base prefix. Preserve that
prefix when appending the typed API paths. Bootstrap trust and credentials are
independent of this service. Every business API uses a selected native
authentication profile; operational listeners retain the existing restricted
health/metrics policy and expose no business-route bypass.

[OpenAPI 3.1.1](openapi.json) references the [canonical JSON schemas](contract.schema.json).
These descriptions are maintained independently of the C# and TypeScript
implementations. The fixed major is compatible across client package releases.

## Portable values

- UUIDs use canonical lowercase `D` text and are not bearer credentials.
- Observation/inventory sequences, metadata revisions and ownership generations
  are canonical positive Int64 decimal strings; native Jobs configuration
  revision additionally permits `"0"`. Values above 2^53 are never JavaScript
  numbers. Counts and millisecond durations are bounded safe integers.
- Timestamps retain exact UTC `yyyy-MM-ddTHH:mm:ss.fffffffZ` text. Reported
  timestamps are distinct from authoritative server database receipt time.
- Application/environment use native exact comparison and255 Unicode-scalar
  bounds. Reported process labels permit the existing1024-scalar identity bound.
  No Unicode normalization or case folding merges identities.
- Reported application and service versions permit the native512-scalar bound.
- Administrative metadata uses256/2048 Unicode-scalar display/description limits.
  Jobs scope256, job/category128, display256, description2048,
  resource-scope description1024 and observed resource512 are UTF-8 **byte**
  bounds. These limits are not interchangeable.
- Requests reject forbidden null, duplicate decoded keys at any depth,
  malformed Unicode/UTF-8, trailing input and unknown members in closed objects.
  Omitted description clears it; explicit null rejects. Response objects,
  including nested metadata and inventory, tolerate optional additions.
- Response statuses retain their raw strings and an explicit recognized/unknown
  interpretation. Unknown values never become successful defaults. Unknown
  inventory module data remains bounded observation data, never executable code.
- Operation keys use1–128 URI-unreserved ASCII characters
  `[A-Za-z0-9._~-]`, excluding exactly `.` and `..` so URI dot-segment normalization cannot change the recovery route. Strong ETags are opaque, at most256 characters including
  quotes; cursors are opaque same-endpoint ASCII tokens of at most2048 characters.
  A weak, wildcard or multiple metadata precondition is invalid.

The `x-cis-*` and `x-known-values` schema annotations retain runtime byte,
calendar, identity, uniqueness, reference, source and outcome obligations. The
existing JSON Schema evaluator intentionally does not implement those annotations.
Its focused tests remove only those annotations and establish structural schema
facts. Actual runtime/client tests establish the remaining lexical and semantic
facts. A JSON Schema selection alone is not full protocol or native proof.

Schemas express supported absolute ceilings; `x-cis-default-*` annotations retain
the initial runtime limits. A host can deliberately select lower bounds or raise
them within the public finite ceilings: 1/16 MiB ordinary/inventory requests,
8/32 MiB ordinary/inventory responses, depth 32, 1,024 modules and 65,536
categories/jobs/total observations. Client and server limits remain independent;
unsupported input fails visibly. The service defaults below are unchanged.

## Registration, reports and complete inventory

The v1.1 addition is `GET /registrations/{registrationId}/control?attemptId=...`.
The query has exactly one canonical nonempty UUID for the current exchange.
Authentication, trusted self mapping and retained registration ownership are
required. The service consults its authoritative backend and returns
`Cache-Control: no-store`; registration, TCP reachability and presence do not
substitute for this exchange.

`ControlConfirmation` contains required `authorityId`, `registrationId`,
`controlRevision`, `admissionAllowed`, `validForMs` and `attemptId`. The IDs use
the existing UUID profile. The revision remains exact opaque control-free text
of 1–256 Unicode scalars; it is never sorted or parsed as an integer. The required
boolean can explicitly refuse admission. Lifetime is an integer token from
1 through 15,000 milliseconds. Both SDKs check registration/attempt echoes inside
their existing decoding deadline and disable automatic read retries for control,
including a caller's generic read policy. Every later control exchange gets a
fresh attempt ID; it is unrelated to tracing and durable operation keys.

Usable freshness starts at the request's local monotonic entry, including
authentication, HTTP, decoding and actual cleanup. Response arrival cannot begin
another full window. A retained confirmation does not itself authorize later
work: selected managed execution checks current expiry, registration, authority,
attempt generation and local reconciliation before admission. Existing metadata
replay and original-result rules remain unchanged.

Service-info optionally adds `maximumInventoryRequestBytes`, an integer token
from 1 through 16 MiB describing the effective accepted provider/wire bound.
Older v1 service-info remains readable with the field absent; absence does not
invent a provider limit. New authoritative providers advertise their actual bound.
These additions preserve existing v1 paths and compatible response evolution.

`PUT /registrations/{registrationId}` binds immutable process facts to the
validated issuer/subject and exact mapped application/environment. Matching
registration content returns its original receipt; changed content or foreign
ownership fails. New process incarnations use a fresh random UUID.

Observation and inventory each have an independent sequence. Higher replaces;
equal-identical replays without updating receipt/freshness. Obsolete input fails
with 409 `management.sequence.obsolete`; equal changed input conflicts. A retained
close is terminal. Unknown/purged reports create no registration. Presence uses
the latest accepted observation receipt (initially the registration receipt);
inventory receipt is separate. Stale presence changes no RM-05 lease or authority.

An inventory is one complete replacement: modules, distinct native
`(executionScope, categoryId)` definitions and `(executionScope, jobId)`
definitions with separate resource-specific observations. Preserve actual
`ownerApplication`, `instanceId`, generation, expiry and observation time only
when the existing owner observation supplies them. A reported process label is
separate from native NotProvided/Reported/Verified evidence. Unknown schedule and
ownership facts remain absent; category grouping grants no authority.

Defaults are32 modules / 64 categories / 1024 jobs/4096 total job observations and a 4 MiB
request. Rejection preserves the previous complete snapshot. Inventory pages
admit complete snapshots into an 8 MiB response, potentially fewer than the
requested items; no partial catalogs or eager100×4MiB page allocation. Ordinary
request/response limits are64 KiB / 1 MiB, JSON depth 16 and error-prefix capture 64 KiB.
Default page size 100, maximum 500. Opaque continuation is bound to the endpoint,
filter, ordering, page size and current validated actor. Paging is a reauthorized
live view, not a historical snapshot, and exposes no hidden total or scan cursor.

## Authorization and metadata result recovery

The service's own application/environment is the native management target.
`management.application/{applicationId}` is an exact resource stored by that
service. Registration/report requires TargetOnly registration permission plus
trusted self mapping and bound ownership. Read/list/hints, mutation, replay and
lookup authorize their actual resources afresh; inaccessible apps appear absent.

Workload profiles do not imply human authority. A configured Native integration
must validate an operator/delegation independently. User headers, copied cookies
and serialized caller objects grant no permission. Application automation remains
an application actor. Result scope hashes a versioned length-prefixed tuple of
the full validated actor/delegation/resource identity, without tokens, roles,
correlation or reported instance IDs. Both execution and lookup derive it
identically; the public reference carries the real operation name/scope/key.

The metadata command replaces the complete display metadata under
`Idempotency-Key` and one strong `If-Match`. It cannot change identity, grants,
configuration or jobs. Its fingerprint includes commandVersion 1, target, exact
semantic strings and original precondition. Native RM-03 Bytes retain exact
UTF-8 here; its normalizing Text representation is inappropriate for these facts.

A newly recorded decision uses the original RM-02 transaction for metadata,
original RM-03 result and exactly one required RM-06 administrative decision
Audit. A rejection's before/after facts describe unchanged observed metadata,
not an applied effect. Audit failure prevents commit. Matching replay returns
the existing result before reevaluating the old ETag and repeats neither effect
nor Audit. Presence updates never invalidate the metadata ETag.

`ManagementMetadataResult` separates the unchanged business decision,
`prepared|replayed|found` result source, original completion and current attempt
completion/cleanup. Prepared needs this owner's known commit before reporting an
applied persistent mutation. Replayed/Found establish the original committed
result even if the new read/arbitration owner rolls back or reports cleanup
diagnostics. Original metadata/revision may differ from the current resource.

A lost response or Unknown retains the original key. Explicit same-key,
same-input resubmission may use established replay; no SDK automatically
resubmits mutations, invents another key, retries commit or infers rollback from
a later NotFound. Problem Details uses actual HTTP status and safe stable
`code`, optional correlation and genuine operation/result facts; it exposes no
credentials, stack trace, raw SQL or connection text.

## Transport and extension

The additive v1.4 [Scheduler/catalogue contract](scheduler-contract.md) defines
34 fixed optional operations, its [bounded schema](scheduler.schema.json) and
independent [literal vectors](../../../conformance/management/v1/scheduler-cases.json).
Logical application capabilities and reported participation are separate from
the unchanged HTTP API `Capabilities` protocol. Scheduler selection is passive;
installed native handlers, current authority and existing Jobs ownership remain
required. Creation, desired-state changes, manual occurrences, cancellation and
original-key recovery preserve their distinct guarded effects. Contracts and
clients alone advertise no qualified scheduler backend.

Both SDKs have a 30-second default total deadline covering credentials, HTTP,
selected read retries and decoding. Retry defaults None; deliberate read policy
has at most two retries. Cancellation signals actual work, stops later attempts
and joins actual owned cleanup; it does not establish remote rollback.
Authenticated redirects and arbitrary pagination URLs are forbidden.

The C# client preserves its exact native `RestResult<T>`; normal management
problem decoding occurs inside the existing response mapper/codec deadline.
Security-bound authentication is never replaced. TypeScript portable imports
use DOM/ES modules and per-call credentials, with no Node/React/Next dependency
or global user state. Host-specific access remains explicitly server-only.

A new module installs explicit typed handlers, schemas, actual resource and
capability requirements, finite limits and documented result/retry behavior.
Feature discovery derives from installed handlers. Presence and inventory never
become a remote execution engine or confer publication authority. The configuration
addition below uses its own fixed resource-scoped APIs; portal/login,
recurrence/rules and destination Audit persistence remain separate roadmap work.

## Central configuration contract addition

RM-08 adds nineteen fixed typed operations to the same canonical OpenAPI and
schema. The addition is being implemented under CIS-0027; discovery continues to
describe actually installed, initialized handlers. A schema or client method is
not evidence that an uninitialized backend can publish configuration. The portal,
rollout automation, secret resolution and destination Audit persistence remain
separate work.

Explicit portable configuration IDs and cohort IDs use the existing safe
operation-key character profile, including rejection of the complete dot
segments. Schema identities, assignment generations and adoption sequences are
canonical positive Int64 strings. Local accepted revisions additionally allow
zero. No central generation replaces Configuration's local revision.

Each source retains exact decoded `payloadJson` UTF-8 and its lowercase SHA-256.
Whitespace, non-ASCII text and Unicode normalization are not rewritten. The
digest covers the decoded source string, not its escaped transport envelope.
Invalid JSON/Unicode, BOM, duplicate decoded names and excess depth/bytes refuse
the content. Configuration payload properties retain the mapper's existing
unknown-member policy; administrative command wrappers are closed.

The configuration profile permits at most one MiB per complete encoded command
or resolved snapshot and 256 KiB per decoded source. Ordinary Management remains
64 KiB by default. Escaping and both sources count toward the snapshot limit.
Configuration clients use one `configuration` metric operation alias plus their
ordinary `request` fallback; wire operation IDs and authorization actions remain
distinct. Two existing Management profiles and two configuration profiles fit the
unchanged 32-pair composition ceiling.

Publications and releases are immutable. Release creation takes the current
binding assignment-map ETag, including its initialized unassigned token, under
the original binding lock after operation arbitration. It changes neither the
assignment ETag nor generation and activates nothing. A complete assignment
command deliberately replaces the default and named cohort entries under its
own original key/precondition. Promotion and rollback record their complete map
and intent; rollback advances the generation. Original results retain identifiers
and decisions without payloads, including after eligible content pruning.

Preview has two fixed forms: an exact common/application source pair, or a
complete proposed assignment command. Mixing them refuses the request. The
response retains the selected pair or complete proposed map, current assignment
and its precondition, and a page of recipient observations. Reported compatibility
is advisory, not proof of every local validator. Continuation binds the actual
actor, binding, selected intent and current-map token; a changed map requires a
fresh preview. The later mutation independently reauthorizes and checks its ETag.

A conditional runtime read is specific to a registration, trusted cohort,
authority, fixed host scope and exact enrolled schema. A 304 is usable only with
the complete matching retained session snapshot. Source-content failures can
identify a rejected release only after the complete desired identity and outer
metadata pass independent expected-target checks. Unidentified failures preserve
previous desired/applied facts. Neither content nor a 304 renews managed-work
authority.

The separate complete configuration-status stream preserves original sequences,
receipt freshness and uncertainty. Desired, actually accepted, pending restart,
rejected, terminal fault and connectivity remain separate facts. A historical
acceptance is not proof of current convergence or intact state after a broken
live setter. Status and original results carry no payload or typed checksum;
metric labels contain only fixed dimensions and application/environment identity.

The independent [configuration vectors](../../../conformance/management/v1/configuration-cases.json)
separate schema acceptance from runtime integrity, lexical, target and native
authority obligations. Known optional fields are omitted rather than null;
responses permit additive unknown members and retain explicit unknown status
values. These vectors establish no database, deployment or lifecycle proof.

Optional exact-binding reads add `GET /applications/{applicationId}/configuration/{configurationId}/releases`
and the sibling `/status`, both with required `schemaVersion` and bounded
`pageSize`/opaque `cursor`. Release pages contain existing immutable metadata in
creation-time/UUID descending order, with unique release IDs and actual binding
plus per-release DefinitionsRead before paging. Their authorized scan stops at 4096
rows; exhaustion yields 503 without a partial page. Scoped status retains the
application's AdoptionRead authority and restricts the exact binding/schema
before recipient expansion; the earlier app-wide status route is unchanged.
Both require clean qualified native reads and bind continuation to the current
actor/profile/policy/principal and exact scope. Missing optional handlers return 501
after current resource authorization. See the
[binding-read guide](../../../docs/modules/management/configuration-binding-reads.md).

`GET /configuration/definitions` pages authorized source-qualified descriptors.
Each row has its actual source ID, environment, kind and shared immutable schema/
defaults. Application rows also identify the actual application, stable binding
and fixed host scope. Common rows expose none of those application facts. Thus
an authorized operator can discover a source, read its current catalog ETag and
publish through HTTP; source IDs do not require SQL or private service-file access.
The same schema can serve bindings with different host scopes. Native resource
authorization filters each row before pagination, and IDs confer no authority.
Raw `defaultsJson` is optional in this view and requires separate actual source
content-read authority. Metadata discovery works without it. Omission redacts
values; it never means that the immutable schema/defaults contract is empty or
changed. Trusted deployment provisioning still validates the complete defaults.

## Native rejected results

A metadata result contains exactly one nonnull `result` or `rejection` branch.
The opposite branch may be omitted or null. A typed result retains its original
metadata, revision, ETag and decision time. Native RM-03 Rejected retains only
its original `{ code, message }`; recovery never fabricates the absent metadata
or decision time. The common operation reference, result source, original
completion and later attempt cleanup remain present and independent.

The rejection code preserves the native one through128 printable-ASCII bound
and cannot be all whitespace. Its non-whitespace message preserves valid Unicode,
at most1024 UTF-8 bytes, including controls other than NUL. These are native
OperationError bounds, distinct from administrative metadata scalar rules.
Known typed decision values and unknown additive response values remain readable.

## Optional application Configuration creation and summaries

`GET /applications/{applicationId}/configuration-creations/context` returns one
authorized generic-v1 creation context with the actual application/environment,
fixed host scope, schema version `"1"` and opaque strong creation ETag.
`POST /applications/{applicationId}/configuration-creations` accepts only
`{commandVersion:1,configurationId,intent,reason}`, where intent is explicitly
`createGeneric` or `reuseGeneric`. The original `Idempotency-Key` and current
`If-Match` creation ETag are required. This operation uses the existing native
`management.configuration.create` family, with Create and OperationsRead on the
resolved application resource. Its generic schema is fixed by the native owner:
nonnullable object, no declared members and `{}` defaults. Browser payloads never
define a schema. Creation performs no publication, coherent release or assignment.

The `configurationCreated` decision identifies the exact application,
Configuration/schema, source, binding and host, plus distinct source and creation
ETags. `GET /applications/{applicationId}/configuration-creation-operations/{key}`
recovers that same original under current OperationsRead without an AppRead or
content prefetch. Its scope retains the existing native form
`management.configuration.v1/management.configuration.create/{applicationId}/management.actor.v1.{digest}`.
NotFound, a failed current read or cleanup, and a known original committed
decision remain separate facts; no replacement key or automatic replay follows.

`GET /applications/{applicationId}/configuration-summaries` returns bounded
metadata-only application binding rows. AppRead and actual source/binding
DefinitionsRead are checked before visible paging. Summary rows contain exact
source/binding/Configuration/schema/environment identities, host scope, an origin
observation and `hasCohortOverrides`; they contain no payload, defaults or merged
values. Origins are `shared`, `applicationOverride`, `applicationOnly`,
`defaultsOnly`, `unassigned`, `mixed`, `protected` or `unavailable`. Unknown future
origins remain explicit unknown observations. `mixed` means assigned source pairs
differ across default/cohort map entries; it is not consumer convergence.
`defaultsOnly` describes an assigned pair whose two source references are absent.
Release metadata authorization can yield `protected`; it never fabricates an
empty source. Continuations are opaque and bound to current native caller,
policy/profile and the exact application/page request. No fleet/history scan is
implied. Within a page, source IDs, binding IDs and Configuration/schema pairs are
unique. Unsupported optional backends return a typed failure, not an empty page.

The optional `modifiedAtUtc` summary field is the exact precise-UTC creation time
of the default-assigned selected immutable publication: Application when present,
otherwise Common. It is not the assignment time, a cohort maximum, the newest
source history entry or a mutable current-value pointer. Defaults-only and
unassigned rows have no such publication time. Source and release metadata
authority remain independent: a selected Common publication time also requires
DefinitionsRead on that actual Common source. Withheld or unavailable dates are
omitted. Existing positional C# constructors/deconstructors remain intact.

The additive TypeScript `ManagementConfigurationCreationClientPort` exposes these
four methods on the existing administration client. Previous optional ports gain
no compulsory members. Requests use the existing one-deadline transport owner,
fresh per-call credentials and joined decode/cleanup. Writes never retry
automatically; configured read retries apply only to the existing read policy.
