---
title: "RM09 current-contract reconciliation"
type: reference
status: Draft
change_id: CIS-0029
---

# RM-09 source reconciliation and capability map

This is a read-only source assessment of CISLIB baseline
`6c70cee807f12caddf40cd1b443e4a20045d35aa`, preceding production implementation.
The [preserved RM-09 specification](../../../planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md)
has SHA-256 `1574ec2624247a95945b68f318e67d0ea4595b8fbd8e116eff149059ad502633`.
Its inspected pre-RM-08 revision is historical: current source already contains
the RM-08 configuration service and portable clients. Source presence is not a
new test result or deployment qualification. No native tests/builds, service
access, database access or production changes were performed for this assessment.
The visible design and its explicit user approval remain pending.

## R02 current review slice

The latest user requested desktop-only applications and instance versions,
step by step. R02 therefore reviews only a filterable applications list and a
selected application's instance page. Other screen mappings below are retained
future assessment, not the current UI approval scope. Mobile is excluded.

The existing `Instance.applicationVersion` is required registration-time text
(1–512 characters). It comes from the application's selected registration
version, normally the entry application's service version. Show it per instance
as **Reported app version**; different instances may report different values.
Later presence updates do not refresh this version assertion. Use the raw
`active`/`stale`/`closed` presence and separate optional `lastSeenAtUtc` (displayed
as Not reported when absent). A reported instance label is unverified; opaque
`registrationId` identifies the process-incarnation record.

No version API addition is needed. Application rows expose neither instance
counts, versions nor current deployment-state aggregates. Known applications can
outlive retained instances. P2 server filtering/authorized environments and the
production human/native authorization boundary remain relevant prerequisites;
P3/P4 configuration/cohort work is deferred with those screens.

## Current delivered boundary

Use the canonical [OpenAPI](../../../contracts/management/v1/openapi.json),
[schema](../../../contracts/management/v1/contract.schema.json),
[portable contract](../../../contracts/management/v1/README.md) and exact-text
cases under `conformance/management/v1/`. Actual typed client surfaces are
[Management models](../../../implementations/typescript/packages/management-client/src/models.ts),
[Management client](../../../implementations/typescript/packages/management-client/src/client.ts),
[Configuration models](../../../implementations/typescript/packages/management-client/src/configuration-models.ts)
and [Configuration client](../../../implementations/typescript/packages/management-client/src/configuration-client.ts).
The [C# contracts](../../../implementations/csharp/src/CIS.Core/Management/Contracts/)
and service handlers implement the same portable boundary; reflection does not
replace that contract.

RM-07 inspection, metadata replacement and original-key lookup are delivered.
RM-08 adds nineteen configuration APIs, trusted catalog provisioning, immutable
publication/release references, complete guarded assignment maps, preview,
runtime snapshots, adoption and bounded retention. Configuration is explicitly
selected, and discovery advertises only the catalog subset until the backend
reports an initialized catalog. PostgreSQL is the complete selected backend;
this work does not introduce another authority or adapter. See the
[service guide](../../../implementations/csharp/applications/CIS.Management.Service/README.md),
[backend guide](../../../implementations/csharp/applications/CIS.Management.Service/Backends/README.md)
and [configuration handler selection](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.Configuration.cs).

The current TypeScript [Next server example](../../../implementations/typescript/examples/next-server/)
is a compile-only boundary example. It installs neither a production Next.js
runtime nor human login/session handling. Those remain RM-09 host responsibilities.

## Screen, field and action mapping

| Proposed screen or journey | Delivered API and facts | Gap or presentation condition |
| --- | --- | --- |
| Sign-in/session | Security's native extension can project independently validated users and delegation. | Production Entra authorization-code/PKCE login, server session ownership and independently validating management-audience human/delegated handler/projector are prerequisites. Existing `entra` reference mode is workload-only. |
| Overview | `getServiceInfo`: protocol versions, implemented APIs, maximum inventory request bytes. | Show actual fetch/connection facts. No fleet aggregate, global health or operator admission projection exists. |
| Applications | `listApplications`: opaque application ID, trusted application/environment, editable display metadata, metadata revision/ETag. | Only `pageSize` and `cursor` exist. Required global search/environment filtering and authorized environment enumeration need an additive extension. |
| Application detail | `getApplication`, `listInstances`, `listInventory`, `getCapabilities`: instance evidence, presence, lifecycle/health observations and times, complete modules/categories/jobs, supported APIs and base operation hints. | Distinguish reported and verified identity. Inventory/owner observations do not grant lease or execution authority. No remote restart or operator-facing admission field. |
| Metadata edit and recovery | `replaceMetadata`, `getMetadataOperation`: display name and optional description, strong precondition, original key, decision, result source, original completion and attempt cleanup. | Omission clears description; explicit null is rejected. Read/write/recovery permissions are separate. A synchronous mutation has no invented queued/running lifecycle. |
| Configuration directory/source | `getDefinitions`, `getSource`, `getPublication`: exact configuration/schema/source/binding/environment identity, shape, optional captured defaults, source ETag and paged immutable publication metadata. | Definitions are globally authorized and paged. A first-page application filter is not a complete application directory. Definition metadata has no declared host update mode; show reported adoption mode or unknown. |
| Author/validate/publish | `validate`, `publishSource`: exact `payloadJson`, structural issues, source ETag, reason and original key. | Preserve reviewed source text. Structural validity does not prove every consumer's typed acceptance. Payload/default disclosure requires the actual source's content permission. |
| Preview and coherent release | `preview`, `createRelease`, `getRelease`: exact source references or explicit absence, current map/precondition, payload-free changes/issues and recipients; immutable release actor/time/reason. | Preview supplies path/kind changes, not before/after values. An optional value diff requires separately authorized content. Release creation uses the current map ETag and activates nothing. |
| Assign/promote/rollback | `getAssignments`, `replaceAssignments`: complete default/cohort map, ETag, increasing generation and explicit `replace`/`promote`/`rollback` intent. | One binding changes atomically. Inspect an explicit known earlier release with `getRelease` for initial rollback; no release-history API is needed for this bounded flow. |
| Configured canary selection | Existing map overrides and preview/adoption recipient cohorts reveal those exact observed IDs. | No configured-cohort enumeration exists. A configured offline/new canary cannot be discovered from those observations; a promised configured-canary picker needs the bounded lookup below. |
| Adoption | `getStatus`: desired version, reported desired/applied/last-successful versions, local revision, update mode/state, live-state validity, safe failure codes, independent registration/adoption freshness, convergence and observation/receipt times. | Keep current convergence separate from historical acceptance. Unknown, stale, offline and no-instance facts differ. Do not calculate fleet ratios from incomplete pages. |
| Operation detail | `getMetadataOperation` and configuration catalog/publication/release/assignment original-key lookups. | Retain same-operator payload-free receipts and original target/key. This is an incomplete session reference list, not a global operation history, Audit query or cancellation facility. |

Actual inspection/metadata routing is in
[ManagementEndpoints.cs](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.cs)
and [ManagementEndpoints.Metadata.cs](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.Metadata.cs).
Configuration routing and authority are in
[catalog/publication handlers](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.Configuration.cs),
[distribution handlers](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.Distribution.cs)
and [adoption handlers](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementEndpoints.Configuration.Adoption.cs).
The [distribution guide](../../../docs/modules/management/configuration-distribution.md)
and [adoption guide](../../../docs/modules/management/configuration-adoption.md)
explain those existing meanings.

## Proposed bounded prerequisites

These are proposed scopes for the RM-09 plan, not claims of implementation or
independent execution authority.

1. **Application search and environments.** Add optional bounded environment and
   application identity/display-name query fields to an application-specific
   request, plus bounded authorized environment enumeration. Specify matching,
   limits and stable ordering in canonical OpenAPI/schema/conformance first.
   Preserve omitted-filter behavior and ordinary paging for other endpoints.
   Extend the selected service/backend/PostgreSQL reads and C#/TypeScript clients
   without requiring the browser to enumerate the fleet. Filter inaccessible
   resources before response items/cursors; bind the normalized query and
   appropriate current authorization scope into cursors. Changing filters resets
   pagination. The existing
   [page reader](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementHttp.cs)
   rejects fields other than `pageSize`/`cursor`; the existing
   [store read](../../../implementations/csharp/applications/CIS.Management.Service/Persistence/PostgreSqlManagementStore.Reads.cs)
   only selects by application UUID position. Do not change original operation
   actor identity merely to add query-cursor binding: the existing
   [actor scope](../../../implementations/csharp/applications/CIS.Management.Service/Http/ManagementActorScope.cs)
   deliberately excludes mutable grants/credentials for recovery.
2. **Production human/delegated identity.** Integrate maintained Entra OIDC/PKCE
   login and server-only sessions in the portal host. Use the service's compiled
   native provider extension to independently validate management token issuer,
   audience, subject, applicable trusted calling client and resource mapping.
   Preserve the human initiator separately from the calling application and use
   complete native read/write/recovery grants. No workload-profile relaxation,
   arbitrary actor header, application-credential borrowing or development
   fallback is permitted. Existing native signed fixtures already exercise
   human/delegated actor isolation and Audit attribution; they do not supply a
   production Entra reference integration. See
   [Security native integration](../../../docs/modules/security/README.md),
   [workload Entra scope](../../../docs/modules/security/entra.md),
   [ManagementSecurityOptions](../../../implementations/csharp/applications/CIS.Management.Service/Security/ManagementSecurityOptions.cs)
   and [native fixture projector](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Management/ManagementHostFixture.cs).
3. **Configuration journey eligibility.** Base Management hints already cover
   metadata write and lookup separately. Configuration APIs are advertised as
   implemented, but the current application capability response does not emit
   source/binding permission hints. Use the reference deployment's complete
   operator grant bundle or add small scoped hints to existing actual
   source/binding reads so the host can avoid enabling a journey without its
   required recovery permission. Reauthorize submission and lookup in existing
   authoritative handlers. Do not introduce permission administration or a
   general introspection framework. Scope source publication/recovery to the
   actual source and release/assignment/recovery to the actual binding.
4. **Configured cohort lookup.** If the approved design includes choosing a
   configured canary absent from current recipients/pins, add a bounded
   binding-scoped authorized cohort-ID enumeration using the existing trusted
   mapping, admitted session and retained map authority. Return no mapping
   subject/credential data. Do not create cohorts or infer membership in the
   portal. Existing
   [assignment validation](../../../implementations/csharp/applications/CIS.Management.Service/Persistence/PostgreSqlManagementStore.Configuration.Assignments.cs)
   already rejects unknown cohort IDs. A view limited to currently observed
   cohorts must identify that narrower scope explicitly.

No release-history endpoint is proposed: `getRelease` plus a known earlier
reference supports a truthful initial rollback. No definition-filter endpoint is
required for an explicitly paged authorized directory. No domain command engine,
scheduler, restart action, Audit archive or copied portal management database is
needed.

## Redaction, results and bounds to retain

Existing disclosure is source-scoped: definitions omit `defaultsJson` without
content-read permission, publication payloads require that permission, and
preview sets `redacted` while suppressing value-derived changes when the caller
lacks relevant source content authority. There is no field-level mask-editing
contract. Do not infer that omitted defaults are empty, generate runtime defaults
in JavaScript, or publish a displayed redaction as a value.

Persisted success requires the original committed fact and an applied business
decision. A committed rejection remains rejected; a recovered committed result
survives a later lookup cleanup failure. NotFound, expiry, timeout or cancelled
waiting does not establish rollback or permit a new-key retry. Retain exact
Int64 decimal strings, timestamp text, ETags, operation keys and source bytes.

Reuse current limits: ordinary default requests/responses are 64 KiB/1 MiB;
complete inventory requests/responses are 4 MiB/8 MiB. Configuration envelopes are
at most 1 MiB, exact source text 256 KiB, depth sixteen, complete status/enrollment
256 entries and cohort overrides sixteen. Paging defaults to 100 and caps at
500. The client defaults to a 30-second entry deadline including preparation,
credentials, transport, decode and joined cleanup. Writes never retry
automatically. UI-specific page/receipt/cache/concurrency/polling limits remain
finite plan decisions within those service/client bounds. See
[ordinary wire limits](../../../implementations/typescript/packages/management-client/src/wire-limits.ts),
[configuration limits](../../../implementations/csharp/src/CIS.Core/Management/Wire/ManagementConfigurationWireLimits.cs)
and the [TypeScript ownership guide](../../../implementations/typescript/README.md).

## Existing verification and maintainer routes

- [C# Management units](../../../implementations/csharp/tests/CIS.Core.UnitTests/Management/)
  cover clients, wire, bootstrap, reporting, configuration stores and admission.
- [Native Management integration fixtures](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Management/)
  cover PostgreSQL/TLS/identity, metadata and configuration authorization,
  concurrency, commit uncertainty, cleanup, original-key recovery and required
  Audit. Reuse `ManagementMetadataTests`, `ManagementSecurityProfileTests`,
  `ManagementConfigurationCatalogTests`, `ManagementConfigurationDistributionTests`,
  `ManagementConfigurationCompletionTests` and `ManagementConfigurationAdoptionTests`
  for affected boundaries.
- [TypeScript tests](../../../implementations/typescript/tests/) include native
  `node:test` client/wire/cancellation/configuration cases, package exports,
  browser compile consumers and real-service consumers. The workspace pins Node
  24.15.0, npm 11.12.1 and TypeScript 5.9.3. Portable packages currently have no
  external runtime dependency; Next/session/component dependencies belong in the
  independent host/UI layer.
- [RM-08 acceptance](../CIS-0027/acceptance.md) and
  [verification](../CIS-0027/verification.md) retain actual prior evidence and
  external-deployment limits. They are not new RM-09 execution. Controlled local
  identity evidence is not live Entra federation; staged Audit/outbox delivery is
  not an Audit destination archive; focused passes are not release qualification.

The next reviewable outcome is the visible versioned design package and bounded
implementation plan. Production screens and write wiring depend on explicit
approval of that design revision through the retained CIS checkpoint.
