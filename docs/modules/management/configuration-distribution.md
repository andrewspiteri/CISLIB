# Coherent releases, assignment maps and preview

Use the [catalog/publication recipe](configuration-service.md) to load the trusted
deployment manifest, apply it through the authenticated native operator and
discover actual source and application-binding IDs. Release creation, map changes
and original-key recovery use that same backend, authority and original
Transactions/Operations/Audit graph. Selecting portable DTOs installs no handler.

## Release creation does not activate content

Read the complete map with
`GET /applications/{applicationId}/configuration/{configurationId}/assignments?schemaVersion=1`.
Even its initialized unassigned state has a stable readable ETag, no generation,
no default and no overrides. POST the binding's `/releases?schemaVersion=1` child
with that current ETag, one original key, a reason and an exact common/application
pair. Each reference is either a publication UUID or explicit absence. Both
absent is a real defaults release, rather than an unavailable assignment.

The server resolves the actual stored binding and checks native
`management.configuration.releases.create` on
`management.configuration.binding/<bindingId>`. After RM-03 arbitration it takes
the catalog and binding/map locks, checks the original map precondition and
protects the exact referenced publications. A common reference must match the
definition/schema/environment; the application reference must additionally match
the binding's actual application/source. Neither opaque IDs nor body assertions
grant that authority.

The immutable release records its exact pair, actor, native time and reason. It
changes neither map ETag nor generation and activates nothing. Publishing newer
common content cannot retarget it. Its FK references protect admitted publications
until eligible release history is removed by
[bounded retention](configuration-retention.md).

Both source texts are checked before composition. The finite declarative shape
uses captured defaults, then common, then application: nested objects merge
present known members; arrays and dictionaries replace; false, zero, empty values
and nullable null remain values. Unknown source members retain their ignore
policy. Service structural eligibility does not promise a consumer's typed or
cross-property acceptance, and the service loads no consumer assemblies.

Creation checks the complete encoded resolved envelope using the real source
payloads and maximum supported identity/session/cohort/generation/ETag overhead.
An oversized pair is refused before any release is installed. Assignment checks
this eligibility again. Individual publication limits and a short current host
name are insufficient proof that a future complete runtime response will fit.

## Explicit whole-map decisions

PUT the binding's `/assignments?schemaVersion=1` endpoint with one strong current
map ETag, original key and a complete command:

```json
{
  "commandVersion": 1,
  "intent": "promote",
  "defaultReleaseId": "11111111-1111-1111-1111-111111111111",
  "overrides": [],
  "reason": "Promote the selected release and remove named pins"
}
```

`replace`, `promote` and `rollback` are deliberately recorded intents over the
same complete replacement. Operators choose the earlier release for rollback;
the server does not guess a target from UUID order or silently preserve/drop a
cohort pin. Submit retained overrides explicitly, at most sixteen unique named
cohorts. Current trusted mappings, frozen admitted cohorts and retained map pins
bound permissible cohort identities; request labels do not create membership.

Native `management.configuration.assignments.write` covers the actual binding.
Every selected release must belong to that binding. Catalog → binding/map →
UUID-ordered reference locks serialize competing writers and reference cleanup.
One original precondition yields one winner across replicas. Each accepted new
decision advances the positive generation and map ETag, including an identical
complete map; exhaustion refuses without wrapping. Rollback therefore advances
the decision sequence while selecting older immutable content.

The effect, original result and required payload-free Audit stage together on
the original owner. Audit retains the actual before/after maps and original
operation identity. Known commit is required for newly prepared success. Matching
replay precedes old-ETag/reference checks, returns the original complete map or
release ID, and repeats neither effect nor Audit. GET the binding's
`release-operations/{key}?schemaVersion=1` or
`assignment-operations/{key}?schemaVersion=1` for fresh authorized recovery.
`management.configuration.operations.read` resolves the same native actor and
binding scope. Unknown completion and NotFound never authorize another key.

Release metadata reads use `management.configuration.definitions.read` on the
actual `management.configuration.release/<releaseId>` resource. Map metadata
reads use that capability on the binding. Value disclosure remains separately
source-scoped; a common publisher gains no application retargeting authority.

## Scoped side-effect-free preview

POST `/configuration/preview` with either the exact source pair or a complete
assignment proposal, never both. Native `management.configuration.preview`
authorizes the actual binding. The response echoes that selection, complete
current map and precondition, bounded payload-free changes/issues and a recipient
page. Without separate content-read authority on the actual source streams,
value-derived changes are redacted; no source/default payload appears in preview.
Target pin additions, removals and retentions remain explicit metadata.

Recipient records come from retained live native registrations whose frozen
binding metadata matches the actual application/binding. Reported instance labels
are not distinct physical-process proof. Deployment binding capture alone cannot
establish typed compatibility: this stage reports `unknown` until actual consumer
reporting supplies qualifying evidence. Preview never promises deployed acceptance.

Page cursors bind current actor, authority/recovery epoch, binding, complete
selected intent, current map token and page size. Another actor/proposal or a
changed map requires a fresh preview. The authoritative read also fences the
observed map token before returning. Complete change lists that exceed finite
count/path/response bounds are refused rather than silently truncated. Preview
creates no release, generation, operation result or Audit effect.

## Runtime snapshots

Runtime snapshots resolve
`/registrations/{registrationId}/configuration/{configurationId}?schemaVersion=...`
only for the exact live native owner. The original registration captures trusted
cohort and supported binding/host/schema; reported instance/version labels cannot
select a cohort. Catalog → registration locks and one joined authoritative
map/release/both-publication observation prevent mixed pairs. A real assigned empty
release returns two explicit absent sources (defaults); unassigned, missing, closed,
unsupported or failed reads return failure.

Strict exact UTF-8 content/digest/scope validation and final response bounds run
before successful content or conditional 304. Exact `If-None-Match` can produce
bodyless 304, ETag and `no-store` only after original rollback/disposal actually
joins. No snapshot read, ETag or 304 renews registration-control authority.

## Evidence boundary

Focused tests use the existing isolated PostgreSQL/RabbitMQ/HTTPS fixtures for
release pinning/no activation, original-key recovery, full-map generation races,
promotion/rollback/pin removal, source/binding denial, worst-envelope refusal,
scoped/redacted preview and recipient continuation. The existing native commit
boundary covers release/map acknowledgement loss and late cleanup while retaining
original results. Snapshot tests cover real TLS actor denial, frozen cohort,
coherent pairs/defaults, original cleanup faults and held owner drain. Authored
tests are not passing evidence; the active change records actual serialized runs.
These local proofs establish neither deployment nor release qualification.
