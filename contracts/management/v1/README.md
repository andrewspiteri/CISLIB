# Management v1 protocol

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
become a remote execution engine. Configuration publication, portal/login,
recurrence/rules and destination Audit persistence remain separate roadmap work.

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
