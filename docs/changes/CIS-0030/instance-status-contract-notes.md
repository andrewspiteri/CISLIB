# Instance status — WORK-168 continuation handoff

Status: source reconciliation and proposed implementation handoff. The UI preview
decision remains pending; this note authorizes no production edit and changes no
existing WORK-168 acceptance criterion or native contract.

Use the shared main and inline table **Instance · Configuration version · Status**.
Rename user-facing Configuration adoption to **Instance status**. Retain the
existing row details panel and collapsed diagnostics for desired/reported/
historical references, assignment generations, local revision, application
version, freshness, failure codes and exact observation/receipt times. Their
removal from the main table must not erase these distinct facts.

## Configuration version identity and optional labels

The reported version in use is only `reported.applied.releaseId`, with recognized
report state/update mode and `liveStateValid=true`. Do not substitute server
desired, consumer desired, `lastSuccessful`, application version or assignment
generation. An older accepted version remains distinct from a newer desired
version during a pending update/restart. Missing applied data is **Not reported**;
unknown or unsafe live semantics are **Current version unknown**. Stale/offline
observations must remain qualified as last reported, not guaranteed current use.

A saved version is an immutable release pair. Its native immutable note/date can
be used only when independently authorized metadata matches its exact release
ID, application, Configuration, schema and environment. Reuse the existing saved
version label/date formatting. Never invent v1/v2 ordinals, infer a latest entry,
or call an assignment generation the Configuration version. A rollback can reuse
the same release at a newer generation.

Inline status reuses the parent's already loaded bounded metadata. Standalone
status may issue **one optional existing `getReleases` page, at most 100 rows**.
Do not follow history cursors automatically, fetch metadata per row or request
publication JSON. Metadata outside this page, denied or unavailable, yields
**Version details unavailable**, with the actual reported release ID retained
only in Technical details. No prominent GUID is needed.

The adoption read renders independently, without waiting for metadata. Metadata
403/404/unsupported/outage must not hide its rows or add DefinitionsRead,
ApplicationsRead or ContentRead as an admission requirement. The native metadata
endpoint still independently authorizes the binding and each release. Existing
401/session/context invalidation remains global; optional metadata is not a
way to accept expired authentication. Ignore foreign metadata tuples rather than
borrowing a label from them.

## Status meanings

| Main status | Required meaning |
| --- | --- |
| **Up to date** | Recognized native `applied` convergence; recognized Applied/AppliedUnchanged report and update mode; valid live-state observation; both registration/report freshness facets fresh; server desired, reported desired and applied match by exact release ID **and generation strings**. |
| **Needs restart** | Recognized native `pendingRestart` with recognized report semantics. Show its reported accepted version, not the pending desired version. |
| **Failed** | Recognized native rejected/faulted convergence with recognized report semantics. Preserve rejection/fault detail and any historical success separately. |
| Neutral **Pending/Unknown** | Pending assignment, stale/offline/earlier observations, missing reports, unsupported/future tokens or ineligible targets cannot be forced into the three normal states or shown green. Preserve precise native reasons in details. |

Do not promote historical Faulted/Applied report state over native unknown or
pending convergence. Notification failure alone is not failed Configuration
acceptance. A native `noInstances` target is an empty/target observation, not a
fabricated instance or successful status. One loaded page establishes neither
fleet convergence nor a distinct process count.

## Bounded implementation and checks

Share a pure presentation/accepted-version-label helper between
`configuration-adoption.tsx` and VersionPage's inline status. Reuse the existing
private allowlisted observations, protected request/session owners, pagination,
row identity and panel keyboard/focus behavior. No service endpoint, portable
DTO, dependency, schema, store, framework or grant change is needed.

Focused pure cases should cover fresh Applied/AppliedUnchanged, same release at
a newer rollback generation, PendingRestart with an older accepted release,
fault/rejection without current applied values, stale/offline/missing/future
tokens, notification failure, noInstances, exact metadata tuple/ID matching,
denied/off-page labels and preservation of full panel facts.

Use a read-only continuation of the existing real Management/Auth.js/PostgreSQL
fixture, opening the exact status route directly. Reuse its native report seeds
and verify the three columns, optional metadata denial for an AdoptionRead-only
actor, panel keyboard/close behavior, bounded pages, session/selection change
clearing and unchanged complete map/publication/release/Audit state. Do not enter
the retired assignment wizard to reach this view. Existing historical
Applied/Faulted seeds intentionally may have pending/unknown convergence; assert
their actual native result. Fresh normal statuses require genuinely admitted,
matching reports via existing provider helpers, not relabeled historical rows.
Pure/provider proof and actually observed browser states must remain distinct.

Relevant existing sources are `configuration-adoption-model.ts`,
`configuration-adoption.tsx`, VersionPage's `ConfigurationInstanceStatus`,
`ManagementEndpoints.Configuration.BindingReads.cs`,
`PostgreSqlManagementStore.Configuration.Adoption.cs`,
`configuration-adoption-model.test.mjs`, and the existing native adoption/provider
fixture helpers. Earlier failed runs and pending acceptance remain preserved;
source preparation alone establishes no new passing outcome.
