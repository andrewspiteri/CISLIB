---
title: "RM-09 application detail tabs design brief — R05"
type: design-brief
status: Proposed
change_id: CIS-0029
frontend_type: backoffice
design_revision: R05
design_approval: Pending
baseline: "6c70cee807f12caddf40cd1b443e4a20045d35aa"
---

# RM-09 application detail tabs design brief — R05

Andrew Spiteri approved R04 and requested item details, suggesting a side panel.
R05 adds one consistent read-only right-side panel to Instances, Configuration,
Capabilities and Scheduled jobs rows. The application list retains its approved
page transition; the four tabs, default Instances and five screen definitions
remain. Mobile and broader write/content/recovery workflows remain deferred.

R04's exact native approval is preserved in Git commit `ed65c3f1` and native
history. Only the new row-details interaction awaits screenshot review. It uses
already returned authorized data and introduces no details API, content/default
fetch, API invocation, scheduler or execution control. Normal scope/plan/owner
reconciliation still precedes production work.
## Journey and screen scope

| Step | Screen | Purpose | Forward/back behavior |
| --- | --- | --- | --- |
| 1 | RM09-APPLICATIONS — /applications | Find applications reported to or known to Management, using name/identity and an authorized environment filter. | Select an application name or row to open its exact application ID. |
| 2 | RM09-INSTANCES — /applications/11111111-1111-1111-1111-111111111111/instances | Inspect retained instance observations, reported application versions, raw presence and last-seen times. | Default detail tab; Back restores list filters, query-bound page/cursor history, scroll and originating focus. |
| 3 | RM09-CONFIGURATION-LIST — /applications/11111111-1111-1111-1111-111111111111/configurations | Inspect application-kind configuration definition metadata for the exact application/environment. | Configuration tab retains the application and return context; no content or write journey opens. |
| 4 | RM09-CAPABILITIES — /applications/11111111-1111-1111-1111-111111111111/capabilities | Inspect implemented Management API descriptors and current-caller hints. | Capabilities tab retains the application and return context; no execution control opens. |
| 5 | RM09-SCHEDULED-JOBS — /applications/11111111-1111-1111-1111-111111111111/jobs | Inspect reported scheduled-support definitions and resource observations in one selected registration inventory snapshot. | Scheduled jobs tab preserves the application and Back context; no execution or scheduling action. |

A simple desktop header supplies Management context. Applications remains the
only top-level navigation destination; there is no multi-area rail. All detail
screens share Back to applications, the selected application's display name,
canonical identity and environment, followed by exactly four linked tabs:
**Instances**, **Configuration**, **Capabilities**, **Scheduled jobs**. The active tab is clear and
keyboard reachable. Each tab loads its own read surface; Refresh acts on the
current tab. Keep per-tab bounded page state within the same application.

The product may use **Deployments** as a section heading, with **Applications**
as the list title. Source copy makes clear that these are applications reported
to or known to Management. A stored application is not proof of a currently
deployed/running process; an empty instance response does not prove nothing is
deployed.

## Applications screen — accepted reference

Retain the R02 filter row: **Search applications**, described as **Name or
identity**, **Environment**, **Apply filters** and **Clear filters**. Apply
commits a new bounded query; Clear resets filters and cursor history. Environment
choices are authorized only. Do not add version, presence or health filters.

Retain application display name, canonical identity and environment in the
table. The application name is a keyboard-accessible link; its row opens the same
exact application ID. Do not resolve targets by editable display name. There are
no instance counts, current versions, fleet totals, deployment-health cards or
rollups derived from the loaded page.

Previous/Next use only corresponding server cursors and same-query history.
Filter changes invalidate old cursors. Do not imply total applications/pages;
obsolete responses cannot overwrite the current query. R03 does not redesign
this accepted list.

## Instances tab — accepted content under new tabs

Retain **Instance**, **Reported app version**, **Presence** and **Last seen**.
Distinguish the unverified reported instance label from opaque `registrationId`,
which identifies the process-incarnation registration. A label is not a verified
host, process identity or address.

Use the delivered required `Instance.applicationVersion` string, 1–512
characters, without semantic-version parsing or greatest-version selection.
Supporting copy remains:

> Version reported by this process when it registered.

Registration captures the version once. Later last-seen observations update
presence evidence, not the version report. Different instances may report
different versions; do not invent an application-wide current version.
`metadataRevision` belongs to display metadata, not the application, runtime or
CIS/library version. Metadata revision and ETag remain outside the main UI.
A missing required applicationVersion is an incompatible record, not a made-up
normal version.

Display **Active**, **Stale** or **Closed** for source `active`, `stale` or
`closed`, with text and non-color cues. These describe registration observations,
not health. Last seen uses optional `lastSeenAtUtc`, with dated CET display and
accessible exact source UTC, or **Not reported** when absent. Do not recompute
presence from browser time; unknown future values retain their text and remain
explicitly unknown.

The accepted table remains bounded and independently paged. The R03 addition is
the shared tab navigation. There is no restart, deployment control, healthy count,
convergence percentage or configuration write action.

## Configuration tab — read-only definition metadata

Show only application-kind definitions matched to the exact application ID and
environment through the proposed scoped query. Fields are `configurationId`,
`schemaVersion`, `sourceId`, `environment`, `kind`, plus application-kind
`applicationId`, `bindingId` and `hostApplicationName`. Group application
scope fields where useful and wrap long identities. The descriptor's required
shape remains contract data; this slice displays metadata, without defaultsJson
values or content.

The existing `getDefinitions` contract is a globally authorized paged directory
with pageSize/cursor only. **A bounded application/environment-scoped definition
query is required for the proposed complete application list.** Loaded-page
filtering cannot establish completeness. Synthetic rows may demonstrate the
intended scoped response with this prerequisite identified; they do not establish
that the current endpoint provides it. Do not conceal the gap through an
unbounded global scan or invent totals.

Do not infer that common definitions belong to this application or combine common
and application sources. There are no content/defaults, effective runtime values,
publication/release/adoption rows, editors or write controls.

## Capabilities tab — Management APIs and current-caller hints

Use `getCapabilities(applicationId)`. Show actual `implementedApis` fields:
`operationId`, `version`, `method`, `path` and
`requiredCapabilities`. These describe implemented **Management APIs**, not
instance-reported inventory or executable actions. Preserve advertised relative
paths as display text; do not manually construct paths or API execution links.

Join `callerHints` by exact operationId and label them **Current caller hint**.
A supplied true/false hint may say **Allowed**/**Not allowed**, explicitly as a
hint for the current caller. An absent hint says **Not evaluated**, never Allowed
or Denied. A hint does not create an implemented API row, imply permission from a
capability name or replace authorization of an actual read. Listing an API that
can write does not expose a write workflow.

## Shared item details panel

Item labels within the four detail lists become keyboard-reachable links/buttons;
row activation opens the same panel. Reuse the governed dialog as a right-aligned
desktop variant around 600 px wide, with a light backdrop, selected-row context,
Close details, independent body scrolling and a clear item title. Background
controls are inert. Move focus into the dialog, keep it contained, and support
Close, Escape and backdrop dismissal. Restore list/report/page/scroll and item
focus; if the row disappeared, focus the list heading with feedback.

Use exact retained response identity, never display name or row index. Instance
selection uses application + registrationId; optional last seen remains Not
reported. Configuration uses exact application/environment + sourceId, retaining
configuration/schema/binding identity, with definition metadata only and no
payload/default values. Capabilities uses application + operationId and the full
returned descriptor; paths stay descriptive and absent hints stay Not evaluated.

Jobs pins application + registration + inventory sequence + executionScope/jobId.
Its selected observation additionally includes resourceId + mode. Show definition
name/scope/category/supported modes and the actual resource observation, outcome,
optional occurrence times and dated report context. Support is not observed mode,
enablement or authority; no cadence or run time is inferred. Do not add desired
configuration, ownership controls or extra live reads to these panels.

Refresh must not silently retarget a panel. Close obsolete selection when the
row/report, application, tab, registration or sequence changes. A permitted stale
snapshot retains its original dated basis; session/read-access loss clears the
panel and protected background. No new API is required for panel opening.

Primary new preview: Scheduled jobs details. Three additional detail-state
previews demonstrate the same pattern for Instance, Configuration and Capability
metadata. Pictures do not prove working click/focus/dialog behavior.
## Scheduled jobs tab — reported inventory only

Use existing listInventory(applicationId). Cursor pages contain whole
registration InventoryItems, each with a complete bounded catalog. Select one
reported registration snapshot; do not merge registrations into a fleet total.
The source context shows registrationId, reported applicationVersion, inventory
sequence and dated observed/received times. A matching reported instance label
is unverified; the registration ID remains authoritative record identity.

List definitions whose supportedModes contains scheduled. The key is
(executionScope, jobId), with category also scoped by executionScope. Declared
support is not actual active mode, enablement, schedule, lease or execution
authority. Show actual scheduled resourceId/mode/state observations and optional
open lastOutcome codes. A definition without such observations remains visible
as No scheduled observation; never invent Idle/Waiting or a successful outcome.

Occurrence timestamps are optional. Display supplied lastOccurrenceUtc and
nextOccurrenceUtc as dated source values, otherwise Not reported independently.
The current native snapshot does not populate these fields; no cron/cadence
contract is transported. Do not extract the example's daily schedule from source
and present it as reported runtime data.

The synthetic row uses the real opt-in CountryData definition Customer dormancy,
customer-service/customer.dormancy, category lifecycle. Its illustrative
observation is partition-eu, scheduled, released, jobs.completed; both occurrence
times are Not reported. Continuous-only wallet.compaction is excluded. Selection
is demo-eu-01 / a1111111-1111-4111-8111-111111111111, version 2.4.2+build.18,
inventory sequence 41, observed 25 Nov 2026, 10:58:30 CET and received 10:58:32 CET.
These are inventory report times, not occurrence times. All data remain synthetic.

Previous/Next browse inventory pages, not individual jobs or fleet totals. No
Run/Pause/Enable/Edit schedule action appears. Loading, no inventory, no scheduled
definition, no observation, stale, denied and unavailable remain distinct. No
new read API is needed for this bounded view.
## Contract map and prerequisites

| Surface | Existing evidence | Required boundary |
| --- | --- | --- |
| Application rows | `listApplications`: identity, environment, display metadata, revision and ETag; pageSize/cursor. | No application version, count, fleet total or live deployment-state aggregate. |
| Requested application filters | Existing application paging has no search/environment query. | **P2:** bounded authorized name/identity/environment search and authorized environment enumeration remain required. No loaded-page global search. |
| Selected application | `getApplication` for the exact ID. | Authorize the resolved target; prior list context is not current access authority. |
| Instance rows/versions | `listInstances`: required applicationVersion, process-incarnation registrationId, unverified reportedInstanceId, presence and optional lastSeenAtUtc; bounded paging. | No new version API. No instance environment/presence/version filters are delivered. |
| Configuration list | `getDefinitions`: globally authorized descriptor pages; application-kind scope fields exist. | Required addition: bounded exact-application/environment filtering. No loaded-page completeness, common relationship or content/default-value claim. |
| Capabilities | `getCapabilities(applicationId)`: implementedApis and callerHints. | Exact descriptor display and operationId hint matching; missing hint is Not evaluated. No instance inventory or execution console. |
| Human/session boundary | Per-call/server-only boundaries remain relevant. | Real human/delegated authority remains a production prerequisite. No sign-in screen is added or approved here; remove protected data on access loss. |

A synthetic complete fixture may demonstrate the desired application filters and
scoped configuration query. Review notes identify their API gaps. Proposed portal
query state is not proof of existing Management query parameters.

## State and return behavior

All templates distinguish loading, successful empty data, access loss and initial
unavailability. Applications also distinguishes no filter matches. Initial
failure shows unavailable with Retry, never fabricated zeros.

A valid-session read failure may retain a dated last successful snapshot. Mark
**Snapshot may be out of date**, expose the last successful fetch time and offer
Refresh/Retry. Preserve source last-seen times/raw presence rather than asserting
current activity. Successful empty instances say **No retained instance
observations**, not no deployed processes.

A successful empty scoped configuration response says **No matching application
configuration definitions**. An empty capability response says **No implemented
API descriptors returned**; neither proves health, permission or absent runtime
configuration. Session/read-access loss clears protected content.

Back restores list query, cursor history, scroll and originating link focus;
otherwise focus the list heading with refreshed-result feedback. Recheck access
and freshness. Tab changes keep the application fixed and preserve applicable
per-tab page state. Target/query/tab changes prevent late obsolete responses from
replacing the active view. Reads remain bounded; tabs start no background crawl,
offline queue, mutation or operation-recovery workflow.

## Visual system and desktop evidence

Retain white surfaces on `#F8FAFC`, navy `#0F172A`, secondary text
`#334155`, blue `#2563EB` and borders `#CBD5E1`. Retain Inter,
14–16 px body text, 20–24 px titles and the 8 px spacing rhythm. Long identities,
versions and API paths remain readable. Status uses text as well as color.

Plan up to thirteen desktop frames at exactly **1600 × 1000**. The accepted R02
Applications reference stays unchanged. R05 adds four shared item-details panel states to the approved R04 presentation.

| Frame | Screen | Evidence |
| --- | --- | --- |
| R02-F01 | RM09-APPLICATIONS | Unchanged accepted list reference: filters, selection and bounded paging. |
| R03-F01 | RM09-INSTANCES | Instances selected; accepted version/presence/last-seen content under the shared tabs. |
| R03-F02 | RM09-CONFIGURATION-LIST | Configuration selected; application-kind scoped definition metadata with query prerequisite. |
| R03-F03 | RM09-CAPABILITIES | Capabilities selected; actual Management API descriptors and caller hints including Not evaluated. |
| R02-F03 | RM09-APPLICATIONS | Preserved no-matching-applications state and Clear filters. |
| R03-F04 | RM09-INSTANCES | Preserved no-retained-observations state under shared tabs. |
| R03-F05 | RM09-INSTANCES | Preserved dated-snapshot refresh failure under shared tabs. |
| R02-F06 | RM09-APPLICATIONS | Preserved initial unavailable state with Retry. |
| R04-F01 | RM09-SCHEDULED-JOBS | Approved jobs list with row-details affordance. |
| R05-F01 | RM09-SCHEDULED-JOBS | Job definition and selected resource observation in the shared right-side panel. |
| R05-F02 | RM09-INSTANCES | One exact registration record in the shared panel. |
| R05-F03 | RM09-CONFIGURATION-LIST | Definition metadata only in the shared panel. |
| R05-F04 | RM09-CAPABILITIES | Full Management descriptor and advisory hint in the shared panel. |

Other loading/access/incompatible-response states remain specified textually;
more PNGs are not required to claim a complete state machine. No mobile/narrow
frames are required. Desktop keyboard, visible focus/return, labels, zoom,
non-color cues and announced persistent feedback remain implementation
requirements. Pictures do not establish working accessibility or real-service
acceptance.

## Synthetic data and approval boundary

Every frame carries **DEMONSTRATION — synthetic data** and fixed snapshot
`2026-11-25T10:00:00.0000000Z` (25 Nov 2026, 11:00 CET). Retain
`Demo.CountryData`, display name `Country data service`, `Demo staging`,
application ID `11111111-1111-1111-1111-111111111111`, and accepted synthetic
list/instance data. Reported versions such as `2.4.2+build.18` and `2.4.1`
remain registration-time text, separate from snapshot and last-seen timestamps.

The Configuration fixture uses `country.settings`, schemaVersion string
`"1"`, kind `application`, source
`55555555-5555-5555-5555-555555555555`, binding
`99999999-9999-9999-9999-999999999999`, the selected application/environment
and hostApplicationName `Demo.CountryData`. IDs/data are synthetic, not actual
deployment or authorization evidence.

Capabilities examples use actual operation IDs/descriptors, all version 1:
getApplication and listInstances with management.applications.read and synthetic
Allowed hints; replaceApplicationMetadata with
management.applications.metadata.write and a synthetic Not allowed hint;
getMetadataOperation with management.operations.read and a synthetic Not allowed
hint; getConfigurationDefinitions with
management.configuration.definitions.read and no hint (**Not evaluated**).
These are descriptor rows only, never workflow controls or live grants.

R04 is approved; the additive R05 item-details design is pending exact rendered approval. No production UI, host dependency,
administrative call or deployment is authorized by these files. The decision must
identify the accepted reference and new tab additions within the five-screen
desktop scope; broader RM-09 acceptance remains deferred.
