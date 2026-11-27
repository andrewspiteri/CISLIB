# Proposed bounded contract decisions for WORK-151/152

Design mapping only: proposed APIs, not implementation or execution proof.
WORK-151/152 establish contracts and clients under parent pool080; WORK-153/154 own eligibility/cohorts; WORK-157 owns catalog/task activation and WORK-158 owns atomic admission/capsules/receipts. Preserve R07 gating
and CIS-0029 history. No product/native/test/source-CIS change is made.

## Existing anchors and compatible additions

- Service `Backends/IManagementBackend.cs` and `IManagementConfigurationBackend`
  retain their existing members. `IManagementDiscoveryBackend.cs` already defines
  optional query/application-configuration companions: follow that pattern.
- Add optional `IManagementApplicationCatalogBackend` and
  `IManagementSchedulerBackend`, implemented by the selected PostgreSQL adapter.
  Advertise features only when selected, qualified and migrated. Missing support
  is unavailable, not empty success. Resolve objects before native authorization;
  use typed operations, not generic commands.
- Core anchors: `Management/Contracts/{IManagementClient,
  IManagementConfigurationClient,ManagementModels}.cs`; TS:
  `packages/management-client/src/{client,models,configuration-client,
  configuration-models}.ts`. Add separate substitutable application-catalog/
  scheduler ports; existing substitutes keep their original contracts.
- Concrete clients borrow the existing selected REST/auth owner and per-call
  cancellation/deadline; create no transport/identity/polling/DI owner. Extend
  finite codec types; retain distinct HTTP operation IDs but bounded observation
  aliases/request fallback and the 32-pair limit. Portable entries gain no
  Next/React/Node/database dependency.
- Canonical HTTP/schema/conformance stays under `contracts/management/v1/` and
  `conformance/management/v1/`. Current `/applications/{id}/capabilities` and
  `ManagementCapabilities.ImplementedApis/CallerHints` stay protocol diagnostics.

## Trusted logical definitions versus observations

Extend the independently loaded `Configuration/ManagementConfigurationOptions.cs`
manifest/catalog apply path. Keep closed legacy parsing, exact digest, immutable
schema/defaults and Operations/Audit. WORK-151/152 freeze a compatible manifest
revision/known section; presence cannot bootstrap it. Use explicit versioned
migration; readiness does no DDL.

One logical key is `(ownerApplication, kind, executionScope, definitionId)`;
accepted versions coexist. Descriptor fields:

- Stable key/version, trusted display name/description/category and owner scope.
- Exact Configuration definition/schema/owner binding; finite form hints using
  declared field paths, known editor IDs, labels/groups/order/units/read-only.
- Job invocation input contract, trigger emitted event contract, optional bounded
  result-summary/detail contract; all portable names/versions, never CLR names.
- Finite supported operations: inspect, edit a fixed configuration, create a
  configured provider instance, and any explicitly supported operational command.
  Support does not grant the operator permission.
- Job execution/recovery and optional checkpoint contract compatibility. Application
  release and immutable build/handler identity are distinct from these versions.

Conflicts prevent unsafe activation; later registration cannot replace accepted
schema/editor versions. No generic compatibility registry or expression engine.

Participation separately reports registration/label/release, immutable worker/
handler, supported versions, drain facts and source/receipt times. Extend the
bounded inventory module profile; missing reports mean Not reported. Its opaque
`details` never establishes trusted schema authority.

`Jobs/Registration/JobCatalog.cs` / `JobHandlerBinding.cs` already expose passive
scope/job/category/name/modes and `InputContract.Name/Version`; capture without
constructing handlers. `JobsManagementInventorySource` remains whole-catalog
capture/refusal. Extend passive immutable registration for actual execution/build
metadata/contracts, never infer them from desired deployment or `scheduled` mode.
Unmanaged Jobs stays compatible. RuleEngine entries describe actual external
features; create no RuleEngine runtime.

## Settings, associations, input and captured context

| Object | Owned facts and boundary |
| --- | --- |
| Trigger definition | Provider identity/version, event contract, supported operations, settings/form contract |
| Configured trigger | TriggerId/name/owner/environment, exact definition/RM08 settings refs, shared/fixed semantics |
| Job definition | Scoped binding, settings/input/result/execution/recovery contracts, worker compatibility; never clone a handler |
| Scheduled Task | TaskId/name/enabled/lifecycle, selected trigger/job, resource/resolver, typed mappings, approved execution profile/binding |
| Persistent settings | Trigger settings/job defaults use RM08 publication/adoption; only declared task overrides |
| Invocation input | Event fields, constants/defaults or typed mappers; distinct from persistent settings |
| Run capsule | Captured revision/event/job/contracts/resource/input/settings versions/digests, expected workload/binding, original operation |
| Attempt context | Actual native principal/profile/policy/instance/build/handler, RM05 ownership and monotonic timing; never caller-supplied authority |

Lookups name authorized registered targets, not URLs. Mappers/resolvers are
versioned deterministic admission-host adapters: no network/dynamic loading in
fanout. Validate required fields/nullability/format/resource; unsupported mapping
blocks activation. Date derivation needs explicit timezone semantics.

Task selectors are above exactly Trigger/Job tabs; both derive from the same
draft. Switching identity/schema cannot carry values to another definition.
Preview covers the whole task/shared impact. Task disable/edit cannot rewrite a
shared trigger/job. Calendar next times come from the evaluator; external sources
say event-driven. No browser-created execution IDs.

## Proposed finite native operation groups

Paths are relative to the trusted prefix plus `/management/v1`, not R07 UI routes.
WORK-151/152 must freeze DTOs/permissions; these endpoints are not yet implemented.

| Group | Proposed methods and paths |
| --- | --- |
| Trusted catalog | Paged `GET applications/{app}/capability-definitions`; exact `GET applications/{app}/capability-definitions/{definition}?definitionVersion=`; bounded participation reads |
| Configured sources | `GET/POST applications/{app}/scheduler/triggers`; `GET/PUT .../triggers/{trigger}`; supported create versus fixed inspect/edit is defined by its trusted provider |
| Task associations | `GET/POST applications/{app}/scheduler/tasks`; `GET/PUT .../tasks/{task}` for desired revision; `POST .../tasks/{task}/preview`; fixed `.../activate`, `.../disable`, `.../enable`, `.../retire` commands |
| Preview/lookups | `POST .../triggers/{trigger}/preview`; whole-task preview includes identity/mapping/settings/affected tasks; named lookups use trusted owner scope, never proxy fetch |
| Event admission | `POST .../triggers/{trigger}/events` with original occurrence/producer identity; `GET .../triggers/{trigger}/event-receipts/{key}` for sanitized acceptance recovery |
| Manual and cancellation | `POST .../tasks/{task}/runs` admits only that selected task; `POST .../runs/{run}/cancellation` requests a supported cancellation, not a configuration toggle or physical-stop assertion |
| Original task/trigger commands | Resource-specific `.../tasks/{task}/operations/{key}` and `.../triggers/{trigger}/operations/{key}` lookup; fixed family/request identity validated, no arbitrary dispatch |
| Historical reads | `GET .../events/{event}`, `GET .../runs`, `GET .../runs/{run}`, `GET .../runs/{run}/attempts`; input/result/detail permission is separate |

`...` means `applications/{app}/scheduler`. History selects one Task, Job(scope/id)
or Event scope; reject mixed identities. Filters/pages are bounded. One record
set supplies task/job views; polls/Busy create no attempts. WORK-161/162 worker
claim/pre-entry/report ports require native workload authority, never portal identity.

Allocate stable object identity before create, separate from its original key.
Freeze create-only preconditions (proposed `If-None-Match: *`) versus update/
activation `If-Match`. Original arbitration precedes stale-precondition checks.
Commands are closed/versioned; reject duplicates/null ambiguity and preserve bytes.

## Source/binding eligibility and trusted cohorts

Keep existing RM08 reads/writes: `getSource` pages publication metadata;
`getPublication` reads authorized immutable content; `validate` is structural;
`preview` accepts exact published references or a whole assignment. It does not
accept an unpublished source draft. Whole-task candidate preview therefore needs
an explicit RM10 typed companion, not relabelled current RM08 preview output.

Propose `GET configuration/sources/{source}/actions` and
`GET applications/{app}/configuration/{config}/actions?schemaVersion=`. Resolve
actual source/binding using existing HTTP helpers and `ICapabilityAuthorizer`;
return finite permitted operation facts. Publication/release/assignment eligibility
requires the corresponding write AND original-result read permission; other
content/validation/preview grants remain independent. Reauthorize at execution.

Add an optional configuration-administration backend/client companion for
`GET applications/{app}/configuration/{config}/cohorts?schemaVersion=&cursor=`.
Page trusted mappings/frozen sessions/retained pins consistently with assignment
admission. Expose cohort IDs/labels, never subject membership or inferred groups.
Preserve old substitutes; add no generic permission-introspection language.

## Activation, fanout, limits and evidence boundary

WORK-157 keeps desired settings, owner adoption and active task revision distinct.
Preconditions guard promotion; unresolved adoption/validation retains the prior
active revision. Revalidate shared changes without overwriting edits or silently
pinning affected tasks. Source publication is not activation; cross-owner steps
retain independent original keys/preconditions/results, not a distributed commit.

WORK-158 atomically admits the complete authorized fanout from coherent active
revision/settings. Replay preserves event/task/run/original-operation identity;
changed payload/provenance conflicts. Never rewrite captured capsules. Sanitize
receipts when target runs are hidden. Execution authorization, enablement,
managed admission and RM05 ownership remain independent pre-effect gates.

Initial ceilings from RM10 v1.1: 1,024 triggers, 4,096 tasks, 10,000 unsettled runs
per authority/environment; fanout 64; mappings 64; payload/input/captured settings
64 KiB each; whole capsule 256 KiB; complete persisted fanout 4 MiB; scheduler HTTP
body 1 MiB; JSON depth 16; result summary 16 KiB; pages default 100/max 500 where
supported; scans 100 before yielding. Apply stricter enclosing limits and complete
encoded-envelope checks; preserve unrelated Management/RM08 limits. Explicit
positive scheduler quota and conservative completion/attempt reservations are
required; protected responsibility/receipts are not truncated or age-pruned.

Counters remain decimal strings; opaque versions/ETags/IDs use exact equality.
Query/principal/policy/filter cursors stay separate from stable original identity
in `Http/ManagementActorScope.cs`. Unknown remains unknown; cancellation is not
rollback; original commit differs from current cleanup. Health/Metrics read
bounded cached facts; no ID/build dimensions or per-definition threads. Startup
and activation stay sequential.

Owner proofs cover compatibility, authorization/recovery, mapping/activation
races, fanout replay, quota rollback, wire bounds and original Operations/Audit.
Reuse provider/browser fixtures. R07/product proof remains separate; stop at
global ReadyForReview.


