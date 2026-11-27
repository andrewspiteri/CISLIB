---
title: "RM-09 application detail tabs design brief — R06"
type: design-brief
status: Proposed
change_id: CIS-0029
frontend_type: backoffice
design_revision: R06
design_approval: Pending
baseline: "6c70cee807f12caddf40cd1b443e4a20045d35aa"
---

# RM-09 application detail tabs design brief — R06

R06 adds **View full details** to the Scheduled job panel and dedicated Job,
Scheduled Task, immutable Event and shared Run history screens. The prior
discussion and actual delivered gaps are reconciled in [reconciliation](reconciliation.md).
Job has Overview / Run history / Configuration. Scheduled Task owns its main
trigger/job/resource/input association and has exactly Trigger / Job contextual
configuration tabs, with history a separate action. Event Configuration shows
captured trigger settings rather than editing an event.

Retain the five existing screens, application navigation, four application tabs,
default Instances and all R05 quick-inspection fields. R04 exact native approval
is preserved at `ed65c3f1`; R05 requested revisions at `f947e78c`. R06 is
pending exact rendered UI review across nine screen definitions and twenty-one
desktop frames. New synthetic future-contract flows are not delivered backend
capabilities and must be enabled in production only after their relevant
contracts/authorization exist. No scheduler/backend, write, API execution or
deployment implementation is granted. Mobile remains excluded.
## Journey and screen scope

| Step | Screen | Purpose | Forward/back behavior |
| --- | --- | --- | --- |
| 1 | RM09-APPLICATIONS — /applications | Find applications reported to or known to Management, using name/identity and an authorized environment filter. | Select an application name or row to open its exact application ID. |
| 2 | RM09-INSTANCES — /applications/11111111-1111-1111-1111-111111111111/instances | Inspect retained instance observations, reported application versions, raw presence and last-seen times. | Default detail tab; Back restores list filters, query-bound page/cursor history, scroll and originating focus. |
| 3 | RM09-CONFIGURATION-LIST — /applications/11111111-1111-1111-1111-111111111111/configurations | Inspect application-kind configuration definition metadata for the exact application/environment. | Configuration tab retains the application and return context; no content or write journey opens. |
| 4 | RM09-CAPABILITIES — /applications/11111111-1111-1111-1111-111111111111/capabilities | Inspect implemented Management API descriptors and current-caller hints. | Capabilities tab retains the application and return context; no execution control opens. |
| 5 | RM09-SCHEDULED-JOBS — /applications/11111111-1111-1111-1111-111111111111/jobs | Inspect reported scheduled-support definitions and resource observations in one selected registration inventory snapshot. | Scheduled jobs tab preserves the application and Back context; no execution or scheduling action. |

| 6 | RM09-JOB-FULL — /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | Full Job Overview and reported local Configuration, with shared Run history. | Job panel View full details preserves exact report/return context; related Task opens full Task details. |
| 7 | RM09-RUN-HISTORY — /applications/11111111-1111-1111-1111-111111111111/runs | Future authorized bounded logical runs/attempts with typed job, task or event scope. | Job history retains Job header/nav; Task history retains Task context; Event produced-runs scope retains Event context; Back to details works when unavailable. |
| 8 | RM09-TASK-FULL — /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | Main Scheduled Task association/resource/static mapping and exactly Trigger/Job contextual tabs. | Related Job/Event/history links enter by exact taskId; separate history action includes prior job associations. |
| 9 | RM09-EVENT-FULL — /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104 | Immutable Event Overview, captured Configuration and produced Task runs. | Links reach exact Task/Job details and event-scoped shared history; restore originating detail/history context. |

A simple desktop header supplies Management context. Applications remains the
only top-level navigation destination; there is no multi-area rail. The four
existing application-detail lists share Back to applications, the selected application's display name,
canonical identity and environment, followed by exactly four linked tabs:
**Instances**, **Configuration**, **Capabilities**, **Scheduled jobs**. The active tab is clear and
keyboard reachable. Each tab loads its own read surface; Refresh acts on the
current tab. Keep per-tab bounded page state within the same application.
New entity details use their explicitly named navigation below the application
context; add no inactive top-level placeholders.

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

Retain the R05 Job/Instance/Configuration/Capability panel fields. R06 adds
**View full details** only to Job, preserving exact report/resource and modal
return context. The other three panels remain quick inspection. Pictures do
not prove working click/focus/dialog behavior.
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
## R06 full details and history

The full Job page retains the selected definition and dated registration/resource
report from the panel. Overview shows Customer dormancy /
customer-service/customer.dormancy, category lifecycle, scheduled support and
the same partition-eu scheduled/released/jobs.completed observation; occurrence
times remain Not reported. Back restores the report/list/panel context without
retargeting by label. Job Configuration shows only the reported local
effectiveConfiguration revision **12**, enabled **true**. Label it local observed
configuration; it composes host/category/job gates and grants no authority.
Unprovided application job defaults/settings, binding, cadence and input mapping
say **Not reported**. P8 is required before richer policy/settings or trusted
source binding; never infer a CountrySettings relationship or fabricate payload.

Run history is one future shared record view, not chronology reconstructed from
current snapshots, logs or exact Operations lookups. Its typed portal scope is
one of job (application route + executionScope/jobId), task (application route +
taskId) or event (application route + eventId). Example query suffixes:
**?scope=job&executionScope=customer-service&jobId=customer.dormancy**,
**?scope=task&taskId=task-demo-dormancy**, and
**?scope=event&eventId=event-demo-104**. Reject mixed identities and bind cursors
to exact authorized scope. Job history stays within the same Job title/nav.
Task history has Task context and is a separate action; it includes prior runs
after the task's selected job changes. Event produced-runs links use event scope.
P6 supplies bounded authoritative capture, retention and authorized paged queries
plus clients; current Management has none of this chronology.

The read-only **Scheduled Task — Daily customer check**, task-demo-dormancy,
revision **7**, is a P7 synthetic future-contract preview. Its persistent main
settings select **Daily calendar**, **Customer dormancy** /
customer-service/customer.dormancy, resource **partition-eu**, and input mapping.
Static typed fixture constants are **PartitionId** (string) = **partition-eu**
and **InactiveBefore** (date) = **2026-08-27**; no expression engine is implied.
Exactly two contextual configuration tabs are **Trigger** and **Job**; **View run
history** is separate. Trigger shows **Shared trigger settings**: provider
**Central Management**, trigger revision **3**, daily **02:00 Europe/Malta**.
Job shows selected definition and only dated reported local revision12/enabled
true, with unprovided application job defaults/settings Not reported. Keep
shared trigger settings, Task mapping and application Job defaults distinct.

**Event — event-demo-104** is immutable. Overview shows 25 Nov 2026 **02:00 CET**
(2026-11-25T01:00:00.0000000Z), Daily calendar/revision3, captured Task revision7
and produced logical Task run run-demo-104. Configuration shows captured trigger
settings and references, not current settings or an event editor. Captured task7,
trigger3, localconfig12 and jobcontract3 survive current changes; unprovided full
settings/payload say Not reported. Produced runs link to event-scoped history.
A supplied zero-run reason is shown exactly; absent reason is **Reason not
reported**. Missing history support is **Run history unavailable**, never an
invented successful zero. P7 must deliver typed Task/Event identity, exact
application/environment/association scope and authorized detail/relationship
reads. Task/Event links traverse their full details.

The populated history is visibly **SYNTHETIC FUTURE CONTRACT — history not
delivered**. Logical run **run-demo-104** is **Succeeded — recovered**, **12 s**,
**2 attempts**, preserving original RunId and original business-operation
reference (opaque value Not reported in this fixture).

| Captured attempt | Worker / reported app version / immutable build | Work/result attribution |
| --- | --- | --- |
| 1 | demo-eu-02 / 2.4.1 / build-20261124.17 | Original work executed; completion interrupted/lost, with original committed result. |
| 2 | demo-eu-01 / 2.4.2+build.18 / build-20261125.18 | Recovered original committed result; work was not re-executed. |

Retain captured task7/trigger3/localconfig12/jobcontract3 references and actual
worker release/build per attempt. Current catalog/inventory cannot replace
historical attribution. A 24 Nov row says **Outcome unknown**; unspecified
duration/attempt/reference values are Not reported. Reporting loss/unknown
outcome never implies failure, rollback or fresh permission to execute. Workload
execution identity is separate from task creator, trigger producer and human
requester; absent identities stay Not reported. No global totals appear.

New production flows remain disabled until the relevant P6/P7/P8 contracts and
authorization exist. The unavailable frame says **Run history unavailable**
and preserves **Back to details**. A genuinely successful empty retained-history
query may say No retained runs in this scope with actual coverage/retention
basis. These screens grant no scheduler, event bus, general task engine,
backend or write implementation; prior assistant discussion is evidence, not
standalone authority. Existing owner boundaries remain intact.

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
| Run/attempt history | No delivered retained chronology; current snapshots and Operations lookup are insufficient. | **P6:** bounded authoritative run/attempt capture, protected retention and authorized scoped paged query contracts plus clients. |
| Scheduled Task / Event details | No delivered typed Task/Event Management identities/details/relationships. | **P7:** typed stable identity, exact application/environment/association scope, authorized detail/relationship reads and captured configuration references. |
| Job configuration | Optional local effectiveConfiguration revision/enabled only. | **P8:** explicit job policy/descriptor projection and trusted source/binding mapping before richer settings or writes; no inferred CountrySettings association. |

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

Plan exactly twenty-one desktop frames at **1600 × 1000** across nine screen
definitions. Preserve the thirteen existing reference/panel frames; R06 adds the
eight states below. The Job panel gains View full details, while the other three
panels retain R05 quick inspection. Canonical filenames use screen--state--desktop.

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

| R06-F01 | RM09-JOB-FULL | Job Overview; rm09-job-full--primary--desktop.png. |
| R06-F02 | RM09-JOB-FULL | Reported local Configuration; rm09-job-full--configuration--desktop.png. |
| R06-F03 | RM09-RUN-HISTORY | Job-scoped populated synthetic history and expanded attempts; rm09-run-history--primary--desktop.png. |
| R06-F04 | RM09-RUN-HISTORY | Run history unavailable with Back to details; rm09-run-history--unavailable--desktop.png. |
| R06-F05 | RM09-TASK-FULL | Main settings/static mapping and Trigger active; rm09-task-full--primary--desktop.png. |
| R06-F06 | RM09-TASK-FULL | Same main settings and Job active; rm09-task-full--job--desktop.png. |
| R06-F07 | RM09-EVENT-FULL | Immutable Event Overview/produced Task runs; rm09-event-full--primary--desktop.png. |
| R06-F08 | RM09-EVENT-FULL | Captured Configuration; rm09-event-full--configuration--desktop.png. |

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

R04's exact approval is historical; R05 requested revisions and the changed R06
body do not inherit it. R06 is pending exact rendered UI approval over the
nine-screen desktop scope and twenty-one-frame inventory. No new approval is
recorded here. Production UI/dependencies, administrative calls, deployment and
RM-10 scheduler/backend implementation are not authorized by these files;
broader RM-09 acceptance remains deferred.
