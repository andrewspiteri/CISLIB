# Central configuration contract

RM-08 adds an optional `IManagementConfigurationClient` beside `IManagementClient`.
Existing Management clients, metadata results and ordinary 64-KiB request defaults
remain compatible. The configuration client is a passive transport port; selecting
it does not enroll roots, start polling or select managed execution. Runtime
enrollment and paired source acceptance belong to Configuration's separate
contracts. The canonical [OpenAPI](../../../contracts/management/v1/openapi.json),
[schema](../../../contracts/management/v1/contract.schema.json) and independently
authored [wire cases](../../../conformance/management/v1/configuration-cases.json)
define the portable HTTP protocol. Authored APIs alone are not passing evidence.

All paths are relative to the configured prefix plus `/management/v1`:

| Client method | HTTP path |
| --- | --- |
| `GetDefinitionsAsync` | `GET /configuration/definitions` |
| `ValidateAsync` | `POST /configuration/validate` |
| `PreviewAsync` | `POST /configuration/preview` |
| `GetSourceAsync` | `GET /configuration/sources/{sourceId}` |
| `PublishSourceAsync` | `POST /configuration/sources/{sourceId}/publications` |
| `GetPublicationAsync` | `GET /configuration/publications/{publicationId}` |
| `CreateReleaseAsync` | `POST /applications/{applicationId}/configuration/{configurationId}/releases` |
| `GetReleaseAsync` | `GET /configuration/releases/{releaseId}` |
| `GetAssignmentsAsync` | `GET /applications/{applicationId}/configuration/{configurationId}/assignments` |
| `ReplaceAssignmentsAsync` | `PUT` on the same assignments path |
| `GetRegistrationConfigurationAsync` | `GET /registrations/{registrationId}/configuration/{configurationId}` |
| `ReportStatusAsync` | `PUT /registrations/{registrationId}/configuration-status` |
| `GetStatusAsync` | `GET /applications/{applicationId}/configuration-status` |
| `GetCatalogAsync` | `GET /configuration/catalog` |
| `ApplyCatalogAsync` | `POST /configuration/catalog/apply` |
| `GetCatalogOperationAsync` | `GET /configuration/catalog/operations/{operationKey}` |
| `GetPublicationOperationAsync` | `GET /configuration/sources/{sourceId}/publication-operations/{operationKey}` |
| `GetReleaseOperationAsync` | `GET /applications/{applicationId}/configuration/{configurationId}/release-operations/{operationKey}` |
| `GetAssignmentOperationAsync` | `GET /applications/{applicationId}/configuration/{configurationId}/assignment-operations/{operationKey}` |

Application/configuration and registration/configuration paths require exactly
one positive `schemaVersion` query. Pages retain existing bounded page-size and
authenticated same-endpoint cursor rules. UUIDs, reported labels and discovery
hints grant no authority. Every actual resource and recovery remains subject to
current native authorization.

Definition discovery is paged by actual source resource. Each row contains the
stable `sourceId`, exact environment and `common` or `application` kind beside
immutable definition/schema/shape/default data. Common-only publishers can
discover a common source without an application binding. Application rows also
identify the application, binding UUID and that binding's fixed host scope;
common rows carry none of those three fields. Several application rows can share
one immutable definition/schema while using different host scopes. A definition/
schema pair is therefore not a unique discovery row. The service must filter
against each actual native source resource before paging and bind cursors to the
authorized actor/query. A global page admits at most 500 complete source rows;
the separate per-application enrollment/status bound remains 256.

`defaultsJson` is optional on discovery reads. Include raw defaults only after
actual source-content-read authorization for that row. Omission is redaction,
not an empty defaults value or a redefinition of the immutable schema. A present
value still requires exact bounded valid object JSON; explicit null is rejected.
The trusted deployment manifest continues to require deterministic defaults
internally, and existing local Configuration defaults are unchanged.

An operator discovers source IDs here, obtains a source head's precondition from
`GetSourceAsync`, then publishes exact content. Application rows supply the
application/definition/schema needed to read the assignment-map precondition
before creating a release or replacing assignments. No SQL access or local
service-manifest inspection is needed to find the first resource ID.

`ManagementConfigurationWire` handles only finite declared DTOs. Commands reject
unknown members and explicit null recursively; additive response members are
tolerated. Optional typed null means omission when encoding. The configuration
profile limits each complete encoded body to one MiB and each decoded source
JSON text to 256 KiB, at depth sixteen. Tighter selected limits remain effective.
Large generations, schema versions, sequences and local revisions are exact
decimal Int64 strings; only local revision permits zero. Counts and logical
quota bytes are safe numeric integers. Quota is positive and used bytes cannot
exceed it. Environment and host scope retain exact 255-scalar identities.

Source text is exact `payloadJson`, with lowercase hexadecimal SHA-256 over its
decoded UTF-8 bytes. The codec rejects BOM, malformed Unicode/UTF-8, duplicate
decoded names, trailing input, excessive source/body size and excessive depth.
It does not normalize Unicode or parse and reserialize before hashing. Common
and application source states are explicit: `absent` carries no content identity;
`present` carries source/publication IDs, definition/schema/environment/kind,
exact content and digest, plus the application UUID for application content.
Timeout, authorization failure and unexpected NotFound are never absence.

The finite shape declaration has object members, array/dictionary elements and
primitive kinds with optional exact string values. It contains no CLR type,
converter, executable rule or consumer validator. Definitions retain deterministic
defaults. Structural eligibility does not establish deployed typed or
cross-property acceptance; Configuration remains responsible for that decision.

Preview has two explicit alternatives: an exact common/application source pair,
or one complete assignment command. It rejects incomplete pairs, empty selection
and ambiguous requests containing both alternatives. Every response echoes its
selected source pair or complete proposed assignment, and includes the current
complete assignment map and strong ETag. This exposes promotion, rollback,
override removals and retained pins before mutation. A separately paged recipient
list reports authorized registration/cohort compatibility observations as
`reportedCompatible`, `unknown` or an explicit unknown future value. It does not
prove every deployed validator will accept a candidate. Recipient paging uses
the existing query/cursor rules; page controls are not added to the body.

`DecodeSnapshot` receives an independently trusted
`ManagementConfigurationSnapshotTarget`. It validates the complete bounded outer
JSON, desired identity, exact authority/application/environment/registration/
cohort/enrollment/host binding and strong ETag before retaining desired context.
It then checks both source scopes, exact schema, JSON and digest inside the native
decoder's deadline. An identified content failure returns a safe code and the
validated desired release/generation, allowing an honest `Rejected` observation.
Malformed outer data, invalid ETag and foreign/ambiguous identity return no
trusted desired state. They cannot manufacture a rejected release. Successful
decoding completes integrity work before the call returns. A conditional 304 is
usable only with an exact retained snapshot for that session; it supplies no new
authority or freshness by itself.

The runtime client method takes that independently trusted target and an optional
complete previously validated snapshot. It derives the conditional ETag internally
only after checking the retained target/session/schema. It does not accept a bare
caller-provided conditional token or perform content verification after return.

The public `ManagementConfigurationCallResult` can retain a payload-free
`ContentRejection` containing only independently validated desired identity and a
bounded safe code. It accompanies a failed native exchange without server Problem
Details. A local content decode failure preserves the actual HTTP 200, failure
category, timing and cleanup facts; it does not replace them with an invented HTTP
error. Successful exchanges cannot carry a rejection. Public substitutes use the
same boundary, allowing an adapter to report identified `Rejected` without a
dependency on wire-codec result types. Unidentified failures leave it absent.

Catalog status and guarded manifest application are fixed bootstrap operations.
The apply command selects a digest of the operator-controlled local manifest;
it never accepts an arbitrary HTTP manifest. Empty initialized catalogs and
unassigned maps have stable IDs and readable strong ETags. A release creation
uses the current assignment-map ETag as its guarded binding precondition. It
creates an immutable reference pair without changing that map's ETag or
generation and without activating the release. Assignment mutation replaces the
complete default/override map with explicit `replace`, `promote` or `rollback`
intent. Its first generation is positive, later decisions advance it, and
exhaustion refuses rather than wrapping. There are at most sixteen distinct
cohort overrides; defaults require an explicit empty release.

Four fixed durable names preserve the original Transactions/Operations/Audit
graph: `management.configuration.catalog.apply`,
`management.configuration.source.publish`,
`management.configuration.release.create` and
`management.configuration.assignment.replace`. Mutations carry the unchanged
original idempotency key, strong original precondition and bounded reason. Replay
arbitration precedes checking the old precondition. Recovery uses four typed
endpoints; it is not arbitrary command dispatch. Matching replay repeats neither
effect nor Audit. Uncertainty and NotFound never authorize a new-key rerun.

`ManagementConfigurationOperationResult` separates the original metadata-only
decision or genuine native rejection, original completion, current result source
and current attempt cleanup. Assignment results retain intent and the complete
resulting map, so promotion and rollback recovery preserve retained overrides.
Results carry references/digests and safe decision metadata, never payloads;
pruning unreferenced content does not erase or recreate the original decision.
Found/replayed results preserve their original committed fact despite a current
read owner's rollback or cleanup error. Unknown values retain explicit unknown
interpretations rather than becoming successful defaults.

Configuration status is a separate complete sequenced stream with at most 256
unique definition/schema entries. Desired, current applied and historical
last-successful versions remain distinct. `Faulted` cannot advertise intact live
state or a current applied value. Candidate and notification failure codes are
separate, and reports contain no payload, effective checksum or source equality
token. Current adoption joins the server's current target with retained reports;
an older `Applied` observation does not prove current convergence. Registration
and adoption freshness are independent. `reportedInstanceId` is metadata, and
no-instance cohorts are not successful adoption. Unknown future states remain
unknown through the public interpretation helpers.

Validation and preview admit at most 64 complete issues; configuration Problem
Details admits at most 1,024 detail scalars. Values beyond either boundary are
rejected as complete values. Recorded assignment intent retains a bounded future
string with an explicit unknown interpretation; submitted map commands retain
the three recognized intents.
