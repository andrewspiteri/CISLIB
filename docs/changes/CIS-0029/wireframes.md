---
title: "CIS-0029 textual wireframes — R06"
type: textual-wireframes
status: Approved
change_id: CIS-0029
design_revision: R06
approval_status: Approved
authority: human-reviewed
---

# Textual wireframes — R06

R06 adds **View full details** to the Scheduled job side panel, opening a Job
page with Overview, Run history and Configuration. Linked Scheduled Task and
Event details follow the prior discussion reconciled in [reconciliation](reconciliation.md):
the task owns its main association/resource/input settings and has exactly two
contextual configuration tabs, Trigger and Job; history is a separate action.
The other three R05 panels remain quick inspection of their existing fields.
Applications navigation, four application tabs and default Instances remain.

There are nine desktop screen definitions and twenty-one 1600 × 1000 frames:
the thirteen existing frames plus eight R06 states. R04's exact native approval
is preserved in Git commit `ed65c3f1`; R05 requested revisions are preserved
at `f947e78c`. The historical approval row below does not approve this changed
body. R06 is pending exact rendered UI review. It authorizes no scheduler,
backend, write or execution implementation. See the [design brief](design-brief.md).
## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-APPLICATIONS | backoffice | Applications reported to Management | /applications | Web desktop only | Current operator with authorized application reads | Existing host entry, detail return | Find authorized known applications by name/identity and environment, then select one. |
| RM09-INSTANCES | backoffice | Application instances and reported versions | /applications/11111111-1111-1111-1111-111111111111/instances | Web desktop only | Current operator with exact application and instance read access | Application name/row, Instances tab, authorized direct link | Default detail tab: retained instance observations, reported versions, raw presence and last seen. |
| RM09-CONFIGURATION-LIST | backoffice | Application configuration definitions | /applications/11111111-1111-1111-1111-111111111111/configurations | Web desktop only | Current operator with exact application/environment definition-discovery access | Configuration tab, authorized direct link | Read application-kind definition metadata through a bounded scoped query; no content/defaults or edits. |
| RM09-CAPABILITIES | backoffice | Management APIs and current-caller hints | /applications/11111111-1111-1111-1111-111111111111/capabilities | Web desktop only | Current operator with exact application capability read access | Capabilities tab, authorized direct link | Read implemented Management API descriptors and current-caller hints; no API execution. |
| RM09-SCHEDULED-JOBS | backoffice | Reported scheduled jobs | /applications/11111111-1111-1111-1111-111111111111/jobs | Web desktop only | Current operator with exact application inventory read access | Scheduled jobs tab, authorized direct link | Inspect scheduled-support definitions and reported resource observations in one selected registration inventory snapshot; no scheduling controls. |
| RM09-JOB-FULL | backoffice | Full Job details | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | Web desktop only | Exact application/job reads; future relationships/history independently authorized | Job panel View full details; authorized related-job link | Retain selected definition/report facts; Overview, Run history and Configuration. |
| RM09-RUN-HISTORY | backoffice | Scoped logical runs and attempts | /applications/11111111-1111-1111-1111-111111111111/runs | Web desktop only | Future authorized typed-scope paged history read (P6/P7) | Job Run history tab; Task View run history; Event produced-runs link | One underlying run history with job, task or event scope and preserved parent context. |
| RM09-TASK-FULL | backoffice | Scheduled Task details | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | Web desktop only | Future exact application/environment/task identity and relationship reads (P7) | Related Task link in Job/Event/history; authorized direct link | Main trigger/job/resource/input association; exactly Trigger and Job configuration tabs; separate history action. |
| RM09-EVENT-FULL | backoffice | Immutable Event details | /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104 | Web desktop only | Future exact application/environment/event and produced-run reads (P6/P7) | Event link in history; authorized direct link | Event Overview and captured Configuration; produced logical Task runs or recorded zero-run reason. |

Applications remains the sole top-level navigation destination. The four existing
application-detail lists retain Back to applications, application display name,
canonical identity, environment and exactly **Instances / Configuration /
Capabilities / Scheduled jobs** tabs. New entity pages sit beneath that context,
with their own explicit navigation below; no inactive top-level placeholders.

## Shared interaction and data rules

Every frame says **DEMONSTRATION — synthetic data** and uses fixed snapshot
`2026-11-25T10:00:00.0000000Z` (25 Nov 2026, 11:00 CET). All rows, labels and
grants are synthetic. Portal routes/query state do not prove implemented server
filters.

The target remains `Demo.CountryData`, display name `Country data service`,
in `Demo staging`, application ID
`11111111-1111-1111-1111-111111111111`. Resolve by application ID, not editable
display name. A Deployments section heading must retain source wording:
applications reported to or known to Management, not verified live deployments.

Required `Instance.applicationVersion` is exact registration text, 1–512
characters. It is not metadataRevision, runtime/CIS version or a fleet aggregate.
`reportedInstanceId` is unverified display text; `registrationId` identifies the
process-incarnation registration. Display Active/Stale/Closed for source values
`active`/`stale`/`closed`. Optional `lastSeenAtUtc` is separate presence evidence,
not a renewed version measurement; show **Not reported** when absent. Supporting
copy remains **Version reported by this process when it registered**. Preserve
exact strings/timestamps and unknown value text.

P2 remains required: bounded authorized application name/identity/environment
search and authorized environment enumeration. Existing application paging has
only pageSize/cursor; instance paging has no environment/presence/version filters.
Do not present loaded-page search as global.

Configuration requires a bounded exact-application/environment definition query:
current getDefinitions is globally authorized paging with pageSize/cursor only.
A filtered loaded global page is not a complete application list. Match only
application-kind definitions to exact application ID and environment; infer no
common definition relationship. Synthetic complete fixtures may demonstrate
these desired queries with the prerequisites identified, not conceal them.

Capability rows come from implementedApis; callerHints join by exact operationId.
A missing hint is **Not evaluated**. A hint alone does not create an implemented
API or replace actual read authorization. Relative paths remain returned display
text under their advertised prefix, without manual construction/execution links.

Preserve list filters, cursor history, scroll and originating focus through
detail/tab navigation. Each tab keeps its own bounded page state within the
selected application. Filter changes clear query-bound cursors. Late prior
target/query/tab responses cannot replace the active view. Session/read-access
loss clears protected data; valid-session failures may retain dated snapshots.
There are no writes, offline commands or operation-recovery controls. Existing
configuration-list/panel defaultsJson and content boundaries remain unchanged.
R06 captured Task/Trigger settings and input constants are explicitly synthetic
future-contract previews, not an extra current content read.

## Shared application-detail tab navigation

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-T01 | Instances tab | Exact application context exists; reads independently checked | Select Instances | Open default read-only instance surface; preserve application/list return context | /applications/11111111-1111-1111-1111-111111111111/instances | RM09-INSTANCES | Show its own safe loading/denied/unavailable state; never substitute another tab's data. |
| RM09-T02 | Configuration tab | Exact application context exists; scoped read independently checked | Select Configuration | Open bounded application-kind definition metadata; no content read or mutation | /applications/11111111-1111-1111-1111-111111111111/configurations | RM09-CONFIGURATION-LIST | Scoped query prerequisite remains explicit; no loaded-global-page completeness claim. |
| RM09-T03 | Capabilities tab | Exact application context exists; capability read independently checked | Select Capabilities | Read implemented Management APIs/current-caller hints | /applications/11111111-1111-1111-1111-111111111111/capabilities | RM09-CAPABILITIES | Show safe denied/unavailable state; missing hints do not become permission decisions. |
| RM09-T04 | Scheduled jobs tab | Exact application context exists; inventory read independently checked | Select Scheduled jobs | Read a bounded inventory page and inspect one selected registration snapshot | /applications/11111111-1111-1111-1111-111111111111/jobs | RM09-SCHEDULED-JOBS | Distinguish no inventory, no scheduled definitions, no observations, denied and unavailable; never fabricate a current schedule. |

The active route/tab is visually clear and keyboard reachable. Back to
applications is available on each detail tab with RM09-I01 semantics below.
Tab selection neither changes the application nor opens deferred workflows.

## Shared item details side panel

The four application-detail lists give each item name a visible keyboard-reachable
link/button affordance. Clicking its row opens the same read-only details panel;
text selection and nested controls do not trigger duplicate actions. Keep the
existing Applications-to-application page transition unchanged.

Use the governed dialog component as a right-aligned desktop variant, around
600 px wide. The underlying application, active tab and selected row remain
visible with a light backdrop; background controls are inert while open.
The panel has a clear type/title, accessible Close details button and a body
that scrolls independently. Opening moves focus into the named dialog; keyboard
focus stays inside until Close, Escape or backdrop dismissal. Closing preserves
list filters, cursor/report selection and scroll, then returns focus to the
originating item. If that row disappeared, focus the list heading with feedback.
No unsaved-change confirmation is needed because this panel cannot edit data.

Open from the exact already returned authorized row/report, without an extra
API request. Keep these selections distinct:

| Row type | Exact selection identity | Read-only panel content |
| --- | --- | --- |
| Instance | applicationId + registrationId | Unverified reported label, registrationId, exact reported applicationVersion, presence, reported process-start time, registration receipt time, protocol major and optional last-seen/closed observations. Optional last seen says Not reported. |
| Configuration | Exact application/environment + sourceId, retaining configuration/schema/binding identity | configurationId, schemaVersion, kind, environment, optional description and applicationId/bindingId/hostApplicationName for application-kind rows. No defaultsJson, payload, source-content fetch or edit controls. |
| Capability | applicationId + operationId, retaining the actual descriptor | Full operationId/API version/method/descriptive path/required-capability list and matching advisory caller hint. Missing hint remains Not evaluated. No invoke link/action. |
| Scheduled job | applicationId + registrationId + inventory.sequence + executionScope/jobId; selected observation additionally resourceId + mode | Job definition name/description, scoped identity/category, supportedModes and optional resource-scope description; selected resource's mode/state/open outcome/optional occurrence times; registration/version/sequence and dated observed/received report context. No inferred cadence or execution authority. |

Job category lookup uses executionScope + categoryId. Scheduled support is
separate from observed mode. Missing observation fields remain Not reported;
unknown state/outcome strings retain their exact text. Do not show reported
local effective configuration as central desired state, and do not turn
optional observed ownership into current lease authority. Such extra diagnostics
are outside this first panel's visible fields.

Refresh must never rebind a panel by row position or display name. If the
selected row/report changes or disappears, close obsolete details and explain
that the list/report changed. Target, registration, inventory sequence, tab or
filter changes clear obsolete selection. A permitted retained snapshot may stay
open only with its original dated source context and stale feedback. Session or
read-access loss clears the panel and protected background data immediately.
Late obsolete responses cannot populate the panel.

R05's four panel previews retain the same fields and interaction; R06 adds
**View full details** only to the Job panel. Preserve its exact registration,
sequence and resource observation when entering the full page and on return.
The other three panels remain quick inspection. All twenty-one images are
synthetic. Pictures do not establish working focus trapping, click handlers,
dialog accessibility or live reads.

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-D01 | Close details / Escape / backdrop | A read-only details panel is open | Dismiss panel | Preserve the current tab/list/report context and restore originating focus; no mutation | Current /applications/11111111-1111-1111-1111-111111111111 detail-tab route | Current detail screen | Removed row returns focus to heading; access loss clears protected data. |
| RM09-D02 | View full details | Exact Job panel selection; production full-details contracts available, or explicit synthetic preview | Open full Job page | Preserve application/list/report/panel return context and exact scoped job; close modal and focus page heading | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | RM09-JOB-FULL | Never retarget by display name; missing future support stays unavailable and cannot be represented as empty history. |
### RM09-APPLICATIONS: Applications reported to Management

#### Description

Page heading: **Applications**. Supporting copy: **Applications reported to
Management**. No instance-count, current-version, health or fleet-total cards
appear above the list.

Filter row:

- **Search applications** textbox; persistent helper **Name or identity**.
- **Environment** selector containing authorized environments only.
- **Apply filters** and **Clear filters**.

The selected filter values remain visible. Applying filters begins a fresh
bounded query and resets old cursor history. The desired production behavior
depends on P2; a review annotation identifies this prerequisite without creating
future-product navigation.

Table columns:

| Column | Content and behavior |
| --- | --- |
| Application | Display name as a keyboard-accessible link, with canonical application identity as secondary text. Selecting its row opens the same target. |
| Environment | Exact environment from the application row. |
| Identity detail | Opaque application ID remains available as readable secondary text where needed to disambiguate targets; it is not an editable field. |

Application identity may be grouped under Application rather than creating a
redundant third visual column. Display metadata revision and ETag are not main
table content. No application version, instance count or current deployment
state appears.

Previous/Next use only server-supplied cursors and same-query cursor history.
Show neither a global application count nor total pages. Refresh rereads the
current query and retains its visible filters.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-L01 | Apply filters | Authorized query; P2 delivered for production | Enter name/identity, select authorized environment and apply | Read a fresh bounded matching page; reset query-bound cursor history; no mutation | /applications with proposed portal filter state | RM09-APPLICATIONS | Show persistent query error; do not substitute loaded-page search or obsolete results. |
| RM09-L02 | Clear filters | Filters differ from the initial authorized query | Activate Clear filters | Reset search/environment and cursor history; read the initial authorized page | /applications | RM09-APPLICATIONS | Keep the requested cleared state visible and expose Retry if reading fails. |
| RM09-L03 | Application name/row | Authorized row from the displayed query | Open selected application | Preserve list query/page/scroll/focus; read exact application and first instance page | /applications/11111111-1111-1111-1111-111111111111/instances | RM09-INSTANCES | No prior target data is substituted if the target is denied, missing or unavailable. |
| RM09-L04 | Next | Same-query continuation cursor exists | Activate Next | Read bounded next page and retain cursor history | /applications with same filters and opaque page state | RM09-APPLICATIONS | Keep the previous successful snapshot dated; failed page reads do not become empty success. |
| RM09-L05 | Previous | Same-query previous cursor history exists | Activate Previous | Read the preceding bounded page | /applications with same filters and prior page state | RM09-APPLICATIONS | Invalidated cursors restart the current query with clear feedback. |
| RM09-L06 | Refresh | Current application query read is permitted | Activate Refresh | Reread the current bounded query; no mutation | Current /applications query | RM09-APPLICATIONS | Retain a dated snapshot only while access remains valid; initial failure is unavailable. |
| RM09-L07 | Retry | A current query read failed and read access remains valid | Activate Retry | Retry the read for the visible requested query | Current /applications query | RM09-APPLICATIONS | Persistent failure remains unavailable/stale; no fabricated rows or totals. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-L-LOADING | Initial loading | Table skeleton/loading feedback; do not display zero results or fabricated rows. |
| RM09-L-READY | Successful populated page | Authorized rows and visible filters; only available paging directions are enabled. |
| RM09-L-EMPTY | Successful initial authorized query is empty | **No applications available**. Do not disclose inaccessible targets or infer that no applications are deployed. |
| RM09-L-NOMATCH | Successful filtered query is empty | **No applications match these filters**, retained filter values and Clear filters. |
| RM09-L-UNAVAILABLE | Initial query unavailable | **Applications unavailable**, persistent feedback and Retry; no fabricated counts. |
| RM09-L-STALE | Refresh/page read failed with a permitted prior snapshot | **Snapshot may be out of date**, last successful fetch time, retained source data and Refresh/Retry. |
| RM09-L-DENIED | Session or query read access lost | Remove protected rows and environment choices; explain access/session loss without inventing a sign-in screen. |
| RM09-L-QUERYCHANGE | Filter change or obsolete response | Old cursor history is invalidated; only the current query may populate the table. |

### RM09-INSTANCES: Application instances and reported versions

#### Description

Header:

- **Back to applications**.
- **Country data service**, canonical identity **Demo.CountryData**, and
  **Demo staging**.
- **Instances / Configuration / Capabilities**, with Instances selected.
- Page/section title **Instances**, plus Refresh and dated fetch feedback.

The selected application is resolved by application ID and authorized again.
Direct entry needs no fabricated prior list selection; Back returns to a safe
initial authorized applications query when no prior context exists. The table
and version meaning remain accepted R02 content under the shared R03 tabs.

| Column | Content and behavior |
| --- | --- |
| Instance | Reported instance label, clearly described as unverified; opaque registrationId as readable secondary text. |
| Reported app version | Exact required applicationVersion. Long values wrap without losing content. |
| Presence | Active, Stale or Closed for known source values, with text and non-color cues. Preserve unknown future value text and mark it unknown. |
| Last seen | Dated optional lastSeenAtUtc in CET, with accessible exact source UTC timestamp; Not reported when absent. This is separate from the registration-time version report. |

Below the table heading, show **Version reported by this process when it
registered**. Different rows may show different versions. Do not combine them
into one application-level current version, select the highest-looking value, or
treat a recent last-seen receipt as a fresh version measurement.

The normal populated frame includes Active, Stale and Closed rows, with at least
two different reported application versions. They demonstrate raw source data,
not a health summary. Instance paging is bounded; no total/healthy count or
convergence metric appears.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-I01 | Back to applications | Applications navigation is available | Activate Back from any detail tab | Restore prior list filters, query-bound cursor history, scroll and originating link focus; revalidate access as needed | Preserved /applications query, or /applications for direct entry | RM09-APPLICATIONS | If the originating row disappeared, focus the list heading and explain the refreshed result. Cached context does not imply fresh data. |
| RM09-I02 | Refresh | Exact application/instance reads are permitted | Activate Refresh | Reread selected application and current bounded instance page | Current /applications/11111111-1111-1111-1111-111111111111/instances page | RM09-INSTANCES | Retain a dated permitted snapshot on network failure; remove data on access loss. |
| RM09-I03 | Next | Same-target continuation cursor exists | Activate Next | Read bounded next instance page | Current instance route with opaque next-page state | RM09-INSTANCES | Failed paging does not replace observations with an empty success state. |
| RM09-I04 | Previous | Same-target previous cursor history exists | Activate Previous | Read prior bounded instance page | Current instance route with prior page state | RM09-INSTANCES | Invalid/changed cursor restarts the selected target's query with feedback. |
| RM09-I05 | Retry | A selected-target read failed and read access remains valid | Activate Retry | Retry the visible application/instance read | Current instance route/page | RM09-INSTANCES | Keep unavailability explicit and never reveal an inaccessible or previously selected target. |
| RM09-I06 | Instance item/row | Exact authorized instance row exists | Open details | Show the selected registration record in the shared side panel; no new read | /applications/11111111-1111-1111-1111-111111111111/instances | RM09-INSTANCES | No manufactured fields or prior-target reuse; close obsolete selection. |

Shared RM09-T01–RM09-T03 switch tabs without changing the target or return
context. They add no mutation or deferred workflow.

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-I-LOADING | Initial target read | Selected target context/loading feedback; no fabricated version or instance rows. |
| RM09-I-READY | Successful populated page | Per-instance reported versions, raw presence, last seen and bounded paging. Different versions remain separate. |
| RM09-I-DETAILS | Selected row details open | Shared right-side read-only modal panel; exact retained item/report identity, Close/Escape/backdrop and focus return. |
| RM09-I-EMPTY | Successful empty instance response | **No retained instance observations**. Explain that this does not establish whether a process is currently deployed or running. |
| RM09-I-UNAVAILABLE | Initial target/instance read unavailable | **Instances unavailable**, persistent feedback and Retry; no zero-instance claim. |
| RM09-I-STALE | Refresh failed with a valid permitted snapshot | **Snapshot may be out of date**, last successful fetch time, original source last-seen values and Refresh/Retry. Raw Active remains an old observation, not a current health assertion. |
| RM09-I-DENIED | Target missing/inaccessible or session/read access lost | Remove protected observations; show safe unavailable/access feedback and Back where permitted. No sign-in screen is added. |
| RM09-I-TARGETCHANGE | New target or obsolete request completes | Clear old target rows; accept only the current target's response. |
| RM09-I-INCOMPATIBLE | Required record fields missing or unsupported response shape | Explain that the response cannot be displayed safely. Do not manufacture a version, identity or Active state. |

### RM09-CONFIGURATION-LIST: Application configuration definitions

#### Description

Retain the shared application header and four tabs, with **Configuration**
selected. Title: **Configuration definitions**. Supporting copy explains that
this is application-kind definition metadata for the selected application and
environment, without source/default content or runtime values.

| Column | Content and behavior |
| --- | --- |
| Configuration | Exact configurationId, such as country.settings. |
| Schema version | Exact schemaVersion string, such as "1"; not a deployed application version. |
| Source | Opaque sourceId. |
| Environment / kind | Exact environment and application kind. |
| Application scope | Exact applicationId, bindingId and hostApplicationName, grouped as labeled metadata where useful. |

Only application-kind definitions matching the exact application ID/environment
belong in the proposed scoped list. No common relationships are inferred.
Required descriptor shape stays in contract data; no defaultsJson/content values
are displayed. Rows do not open editors or deferred source/publication flows.

The existing getDefinitions directory is globally paged by pageSize/cursor only.
**Required addition: bounded application/environment-scoped definition query.**
A loaded global page filtered in the browser is not a complete application list.
The synthetic preview demonstrates the intended scoped result with that
prerequisite identified, without implying delivered filtering, totals or an
unbounded scan.

Synthetic row: country.settings; schemaVersion string "1"; kind application;
source 55555555-5555-5555-5555-555555555555; binding
99999999-9999-9999-9999-999999999999; selected application
11111111-1111-1111-1111-111111111111; Demo staging; hostApplicationName
Demo.CountryData. This is an illustrative descriptor, not a live source.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-C01 | Refresh | Scoped query exists and exact definition read is permitted | Activate Refresh | Read the current bounded exact-application/environment definition page | Current /applications/11111111-1111-1111-1111-111111111111/configurations page | RM09-CONFIGURATION-LIST | Date a retained permitted snapshot; do not imply complete results from loaded global rows. |
| RM09-C02 | Next | Same-scope continuation cursor exists | Activate Next | Read the next bounded scoped page | Current configuration-list route with opaque next-page state | RM09-CONFIGURATION-LIST | Failed paging is unavailable/stale, not empty success. |
| RM09-C03 | Previous | Same-scope previous cursor history exists | Activate Previous | Read the previous bounded scoped page | Current configuration-list route with prior page state | RM09-CONFIGURATION-LIST | Invalidated cursor restarts the scoped query with feedback. |
| RM09-C04 | Retry | Scoped read failed and exact access remains valid | Activate Retry | Retry the same bounded read | Current configuration-list route/page | RM09-CONFIGURATION-LIST | Retain the prerequisite/access/error boundary; no fallback completeness claim. |
| RM09-C05 | Configuration item/row | Exact authorized application-kind definition exists | Open details | Show only retained definition metadata in the shared side panel | /applications/11111111-1111-1111-1111-111111111111/configurations | RM09-CONFIGURATION-LIST | No content/default fetch; clear on exact-scope access loss. |

Back uses RM09-I01; tabs use RM09-T01–RM09-T03. No row mutation/content action
exists.

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-C-LOADING | Scoped read loading | Loading feedback within the active Configuration tab. |
| RM09-C-READY | Successful scoped page | Only exact application-kind metadata; bounded paging and no totals/content. |
| RM09-C-DETAILS | Selected row details open | Shared right-side read-only modal panel; exact retained item/report identity, Close/Escape/backdrop and focus return. |
| RM09-C-EMPTY | Successful scoped response is empty | **No matching application configuration definitions**; do not claim absent runtime configuration. |
| RM09-C-PREREQUISITE | Scoped query not delivered | Explicit dependency in review; production cannot substitute a partial global-page filter. |
| RM09-C-UNAVAILABLE | Initial read failed | **Configuration definitions unavailable** and Retry, without fabricated rows. |
| RM09-C-STALE | Refresh failed with permitted prior snapshot | Dated snapshot warning and original descriptor data. |
| RM09-C-DENIED | Session/target/scoped read denied | Remove protected rows; safe feedback and Back where permitted. |
| RM09-C-TARGETCHANGE | Target/scope/tab changes or obsolete response arrives | Preserve the active target/tab; ignore obsolete results. |

### RM09-CAPABILITIES: Management APIs and current-caller hints

#### Description

Retain the shared application header and four tabs, with **Capabilities**
selected. Title: **Management capabilities**. Read getCapabilities(applicationId);
show actual implementedApis, not instance inventory or an execution console.

| Column | Content and behavior |
| --- | --- |
| Operation | Exact operationId from implementedApis. |
| API version | Exact descriptor version. |
| Method / path | Exact method and returned relative path, under its advertised prefix; display text, not an execution link. |
| Required capabilities | Exact declared capability names; do not infer grants from them. |
| Current caller hint | Match callerHints by operationId. Supplied true/false means Allowed/Not allowed as a current-caller hint; absent means Not evaluated. |

The synthetic preview uses real descriptor IDs/paths, all API version 1:

| Operation | Method | Relative path | Required capability | Synthetic current caller hint |
| --- | --- | --- | --- | --- |
| getApplication | GET | /applications/{applicationId} | management.applications.read | Allowed |
| listInstances | GET | /applications/{applicationId}/instances | management.applications.read | Allowed |
| replaceApplicationMetadata | PUT | /applications/{applicationId}/metadata | management.applications.metadata.write | Not allowed |
| getMetadataOperation | GET | /applications/{applicationId}/metadata-operations/{operationKey} | management.operations.read | Not allowed |
| getConfigurationDefinitions | GET | /configuration/definitions | management.configuration.definitions.read | Not evaluated — no hint supplied |

These are synthetic examples of a legitimate grant bundle, not live grants.
Configuration APIs can be advertised without source/binding caller hints; an
absent hint must not be relabeled Denied or Allowed. Hint-only entries do not
invent implemented API rows. Every actual read still authorizes independently.
There is no execution button, metadata form or operation-recovery link.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-K01 | Refresh | Exact application capability read is permitted | Activate Refresh | Reread implemented Management API descriptors/current-caller hints | /applications/11111111-1111-1111-1111-111111111111/capabilities | RM09-CAPABILITIES | Retain a dated permitted snapshot on network failure; remove it on read/session loss. |
| RM09-K02 | Retry | Capability read failed and exact access remains valid | Activate Retry | Retry the same application capability read | Current capabilities route | RM09-CAPABILITIES | Persistent failure stays unavailable; do not invent APIs or hint decisions. |
| RM09-K03 | Capability item/row | Exact authorized implemented descriptor exists | Open details | Show full descriptor and advisory caller hint without invoking it | /applications/11111111-1111-1111-1111-111111111111/capabilities | RM09-CAPABILITIES | Absent hint remains Not evaluated; no inferred permission or dispatch. |

Back uses RM09-I01; tabs use RM09-T01–RM09-T03. API descriptors have no
execution action.

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-K-LOADING | Capability read loading | Loading feedback within the active Capabilities tab. |
| RM09-K-READY | Successful descriptor response | Actual implemented APIs/current-caller hints; absent hints remain Not evaluated. |
| RM09-K-DETAILS | Selected row details open | Shared right-side read-only modal panel; exact retained item/report identity, Close/Escape/backdrop and focus return. |
| RM09-K-EMPTY | Successful response without implemented APIs | **No implemented API descriptors returned**; not a health or permission assertion. |
| RM09-K-UNAVAILABLE | Initial read failed | **Management capabilities unavailable** and Retry. |
| RM09-K-STALE | Refresh failed with permitted prior snapshot | Dated snapshot and original hint observations, without asserting current grants. |
| RM09-K-DENIED | Session/target/capability read lost | Remove protected descriptors/hints; safe feedback and Back where permitted. |
| RM09-K-TARGETCHANGE | Target/tab changes or obsolete response arrives | Ignore obsolete responses; retain only active target/tab data. |

### RM09-SCHEDULED-JOBS: Reported scheduled jobs

#### Description

Keep the shared application/environment header, Back and four detail tabs, with
**Scheduled jobs** active. Heading: **Scheduled jobs**. Supporting copy:
**Reported jobs that support scheduled execution**. This is inventory inspection,
not a scheduler or authoritative fleet view.

Read listInventory for the exact application. Its cursor pages contain whole
registration snapshots, not job rows. Select one report using **Reported
instance**; options on this inventory page resolve by registrationId and may show
an unverified reported label from matching instance data. Missing labels fall
back to registrationId, never a fabricated host. Previous/Next browse inventory
pages, not a fleet or unbounded sweep. Refresh rereads the selected report/page.

Show the selected registrationId, InventoryItem.applicationVersion, inventory
sequence, observedAtUtc and receivedAtUtc as dated reporting context. The fixture
selects demo-eu-01 / a1111111-1111-4111-8111-111111111111, reported version
2.4.2+build.18, sequence `41`, observed 25 Nov 2026, 10:58:30 CET
(2026-11-25T09:58:30.0000000Z), received 10:58:32 CET
(2026-11-25T09:58:32.0000000Z). These times are not job occurrence times.

Within that complete bounded catalog, include definitions whose supportedModes
contains `scheduled`. Identity is (executionScope, jobId); category identity
also includes executionScope. Declared support does not establish actual mode,
enablement, an active schedule or ownership. Preserve actual observations by
resourceId and do not collapse matching jobs across registrations into a fleet
row. Definitions with no scheduled resource observation remain visible as
**No scheduled observation**, without fabricated state or outcome.

| Column | Content and behavior |
| --- | --- |
| Job | Display name with exact executionScope/jobId and category as secondary text. |
| Resource | Actual resourceId for the scheduled observation, or Not reported. |
| Observation | Supplied mode and state, with text/non-color cues; absence is No scheduled observation. Unknown future strings retain their exact text and remain unknown. |
| Last outcome | Optional open safe lastOutcome code, or Not reported. Do not classify all future codes as success/failure. |
| Occurrence times | Supplied optional lastOccurrenceUtc and nextOccurrenceUtc as dated CET/accessible UTC; Not reported independently when absent. |

The synthetic row uses the actual CountryData opt-in definition Customer dormancy,
customer-service / customer.dormancy, category lifecycle, supportedModes scheduled.
Its illustrative resource observation is partition-eu, mode scheduled, state
released, lastOutcome jobs.completed. Both occurrence times say Not reported.
The current native runtime does not populate those optional occurrence fields.
No cron/cadence descriptor is transported; do not invent the configured cadence
from source code or infer future timing from current state. Wallet compaction
supports continuous execution and is excluded from this scheduled-support view.

No Run, Pause, Enable, Edit schedule or lease-control action is offered. Keep
**Inventory observations are reports; they do not grant execution authority**
visible. These synthetic data do not claim the normal CountryData host selects
its opt-in maintenance recipe. No new read API is required for this bounded view.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-J01 | Reported instance | Authorized complete inventory items on the current page | Select a registration report | Render its complete catalog with scheduled support; preserve exact application context | Current /applications/11111111-1111-1111-1111-111111111111/jobs with selected registration state | RM09-SCHEDULED-JOBS | Clear old rows if target/report changes; no report means no inventory, not no jobs deployed. |
| RM09-J02 | Refresh / Retry | Exact inventory read remains permitted | Reread current page/report | Perform an authorized bounded read; no mutation | Current jobs route and inventory page | RM09-SCHEDULED-JOBS | Retain only permitted dated snapshots on outage; access loss removes protected data. |
| RM09-J03 | Previous / Next inventory page | Prior cursor history / next cursor exists | Browse reports | Read another bounded page of complete registration reports | Current jobs route with query-bound opaque cursor state | RM09-SCHEDULED-JOBS | Invalidate stale cursor history and obsolete responses; do not imply job/fleet totals. |
| RM09-J04 | Scheduled job item/row | Exact job in the selected registration report exists | Open details | Pin the scoped definition and selected resource/mode observation in the shared panel | /applications/11111111-1111-1111-1111-111111111111/jobs | RM09-SCHEDULED-JOBS | Changed report/sequence clears selection; absent observations remain unknown, not fabricated. |

Shared tab actions RM09-T01–RM09-T04 and Back RM09-I01 apply here as well.

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-J-READY | Selected complete reported catalog | Exact scoped definitions and actual scheduled observations with dated source context. |
| RM09-J-DETAILS | Selected row details open | Shared right-side read-only modal panel; exact retained item/report identity, Close/Escape/backdrop and focus return. |
| RM09-J-LOADING | Initial read or report change | Clear obsolete rows, show loading and retain only safe selected application context. |
| RM09-J-NOINVENTORY | Successful page has no retained report | No inventory reported; this does not establish that no jobs/processes are deployed. |
| RM09-J-NOSCHEDULED | Selected complete catalog has no scheduled-support definition | No scheduled jobs reported in this snapshot; no fleet/global absence claim. |
| RM09-J-NOOBSERVATION | Scheduled-support definition without scheduled observations | Keep definition, show No scheduled observation and absent fields as Not reported. |
| RM09-J-UNAVAILABLE | Initial read failed | Inventory unavailable and Retry, never an empty successful catalog. |
| RM09-J-STALE | Refresh failed with permitted retained report | Snapshot may be out of date; preserve observed/received times and report age separately from job occurrence times. |
| RM09-J-DENIED | Session or inventory read lost | Clear protected snapshot/options and expose safe access feedback/Back where permitted. |
| RM09-J-INCOMPATIBLE | Required inventory shape invalid | Incompatible response; no manufactured definition/state/authority. |
## R06 full-details contract boundary

All new links use exact application/environment and typed entity identities;
neither a label nor a current association is historical authority. The synthetic
future-contract states below demonstrate desired behavior. Production must
enable each new flow only after its relevant contract and authorization are
delivered. Existing inventory, operation lookup and logs cannot substitute.

| Prerequisite | Required boundary |
| --- | --- |
| P6 — run history | Authoritative bounded logical-run/attempt capture, protected retention and authorized paged query contracts plus clients. Preserve original RunId/business-operation identity, captured references and actual worker release/build per attempt. |
| P7 — Task/Event details | Stable typed Task/Event identities; exact application/environment/association scope; authorized detail and relationship reads; immutable captured configuration references. Task history survives changes of the task's selected job. |
| P8 — Job policy/configuration | Explicit policy/descriptor projection and trusted configuration-source/binding mapping before richer job settings or writes. Current inventory reports only optional local effectiveConfiguration revision/enabled. |

No RM-10 scheduler/backend implementation is granted. Prior assistant discussion
is evidence of requested behavior, not standalone implementation authority.
The existing defaultsJson/content boundaries, native owner responsibilities and
read-only scope remain. No snapshot chronology, Operations lookup, log archive,
global total or runtime/write control is presented as history.

### RM09-JOB-FULL: Full Job details

#### Description

Route: /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy.
Keep application **Country data service / Demo.CountryData / Demo staging**.
Use **Back to Scheduled jobs**, title **Customer dormancy**, exact scope
**customer-service / customer.dormancy**, and **Overview / Run history /
Configuration** navigation. These are Job tabs beneath the retained application
context, not additional top-level destinations.

**Overview** retains the selected definition/report basis from the panel:
category lifecycle; supportedModes scheduled; selected resource partition-eu,
mode scheduled, state released, lastOutcome jobs.completed; last and next
occurrence independently **Not reported**. Show registration
a1111111-1111-4111-8111-111111111111, unverified label demo-eu-01, reported app
version 2.4.2+build.18, sequence 41, observed 25 Nov 2026 10:58:30 CET and
received 10:58:32 CET. Do not conflate report times with execution chronology.
A future P7 related-task section links **Daily customer check** /
task-demo-dormancy; clearly distinguish that synthetic relationship from the
current retained inventory. **Run history** opens the shared history screen
with this same Job header and active Job Run history tab.

**Configuration** displays **Reported local effective configuration** for that
same registration/resource snapshot: revision **12**, enabled **true**.
This is an optional observed local result; enabled composes native host/category/
job gates and does not grant execution authority. It is not central desired
configuration. **Application job defaults/settings**, **configuration binding**,
**cadence** and **job input mapping** say **Not reported** when not provided.
Do not bind country.settings/CountrySettings to this job by name, reconstruct
payload/default values, or infer cadence. Rich policy/settings require P8.
A Scheduled Task's input mapping belongs to the task association, not Job defaults.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-JF01 | Back to Scheduled jobs | Safe application return is permitted | Return from full details | Restore exact list/report/scroll and originating Job panel selection/focus when still valid | /applications/11111111-1111-1111-1111-111111111111/jobs | RM09-SCHEDULED-JOBS | Changed report does not reopen obsolete panel; return to list heading with feedback. |
| RM09-JF02 | Overview | Exact job/report is permitted | Select Overview | Show retained definition and selected observation | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | RM09-JOB-FULL | Clear protected facts on access loss; do not substitute another registration. |
| RM09-JF03 | Run history | P6 and relevant P7 reads available; exact job scope authorized; synthetic preview otherwise explicit | Select Run history | Query shared runs by exact application/executionScope/jobId; retain Job header/nav | /applications/11111111-1111-1111-1111-111111111111/runs?scope=job&executionScope=customer-service&jobId=customer.dormancy | RM09-RUN-HISTORY | Run history unavailable, not No runs; usable Back to details. |
| RM09-JF04 | Configuration | Same authorized job/report basis | Select Configuration | Show only reported local revision/enabled and explicit absent fields; no content fetch/write | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy?tab=configuration | RM09-JOB-FULL | Optional absent observation is Not reported; richer unsupported settings remain unavailable. |
| RM09-JF05 | Daily customer check | P7 exact related Task identity/read available; synthetic preview explicit | Open related task | Open the actual referenced Scheduled Task, not a task found by job display name | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | RM09-TASK-FULL | Missing/denied relationship does not disclose or manufacture a task. |
| RM09-JF06 | Refresh / Retry | Relevant exact read remains permitted | Reread current bounded source | Preserve source/report identity and date permitted retained data | Current full Job route/tab | RM09-JOB-FULL | Changed report requires explicit new selection; loss of read clears protected data. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-JF-PRIMARY | Overview | Definition/report facts and explicitly synthetic future related Task link; no inferred schedule/history. |
| RM09-JF-CONFIGURATION | Configuration | Reported local revision 12/enabled true; absent settings/binding/cadence/input mapping Not reported; scope labels visible. |
| RM09-JF-LOADING | Exact detail read loading | Safe target identity and loading; no substituted report or fabricated settings. |
| RM09-JF-UNAVAILABLE | Relevant contract/read unavailable | Full details unavailable with safe Back/Retry; no successful-empty claim. |
| RM09-JF-STALE | Permitted retained report after failed refresh | Dated snapshot warning and original report identity/times. |
| RM09-JF-DENIED | Session/job/report access lost | Clear protected details and relationships; safe Back where permitted. |
| RM09-JF-INCOMPATIBLE | Required shape/identity invalid | Incompatible response; preserve no invented revision, state or association. |

### RM09-RUN-HISTORY: Scoped logical runs and attempts

#### Description

Base route: /applications/11111111-1111-1111-1111-111111111111/runs.
This is one shared underlying run-record view with a typed scope, not separate
Job/Task histories that duplicate records. Proposed portal query forms are:

| Typed scope | Required identity within the exact application route | Context retained |
| --- | --- | --- |
| job | scope=job&executionScope=customer-service&jobId=customer.dormancy | Same Job title and Overview / Run history / Configuration nav, Run history active. |
| task | scope=task&taskId=task-demo-dormancy | Scheduled Task title/id and Back to Task details; history remains a separate action, never a third contextual configuration tab. |
| event | scope=event&eventId=event-demo-104 | Immutable Event title/id and Back to Event details; produced-run scope. |

These are proposed typed query states, not delivered Management parameters.
Reject mixed/missing identities; keep cursor/filters bound to the authorized
scope. Job scope contains applicationId plus executionScope/jobId. Task scope
queries captured task identity across all prior job associations: changing the
task's selected job must not filter out older runs. Event scope shows only runs
recorded as produced by that event. No unbounded scan or global totals.

Title **Run history**. Columns: occurred/started time, logical RunId, Task,
Event, Job scope/id, resource, outcome, duration and attempts. Use exact captured
records; expandable run detail presents attempts and captured references without
creating another screen. Task/Event names are links to their exact full details.
Separate workload execution identity from task creator, trigger producer and
human requester; absent identities say **Not reported**.

The populated preview is **SYNTHETIC FUTURE CONTRACT — history not delivered**.
Event **event-demo-104**, 25 Nov 2026 **02:00 CET**
(2026-11-25T01:00:00.0000000Z), produced logical Task run **run-demo-104**
for task-demo-dormancy / Daily customer check, customer-service/customer.dormancy,
resource partition-eu. Outcome **Succeeded — recovered**, duration **12 s**,
**2 attempts**. Preserve original RunId and original business-operation reference;
the opaque original-operation value is **Not reported** in this fixture.

| Attempt | Actual worker / reported app version / immutable build | Recorded work/result |
| --- | --- | --- |
| 1 | demo-eu-02 / 2.4.1 / build-20261124.17 | Executed original work; completion interrupted/lost. Original committed result exists; reporting loss does not imply rollback. |
| 2 | demo-eu-01 / 2.4.2+build.18 / build-20261125.18 | Recovered the original committed result; original work was not re-executed. |

Captured task revision **7**, trigger revision **3**, local effective configuration
revision **12** and job contract revision **3** remain associated with this run.
Historical app/build values are captured per attempt; current inventory/catalog
values must not overwrite them. Another row on **24 Nov 2026** has outcome
**Outcome unknown**; do not infer failure, rollback or permission to execute
again. Its unspecified duration/attempt/reference fields say **Not reported**.

If P6 is absent or a history read fails, show **Run history unavailable** with
**Back to details** and Retry where meaningful. Do not show **No runs** for missing
support. A successful authorized retained-history query with no rows may say
**No retained runs in this scope**, with its actual bounded coverage/retention
basis; it is not proof no execution ever occurred.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-RH01 | Back to details | Safe exact parent context known | Return | Restore originating Job tab, Task details or Event details and preserved focus/page state | Job /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy; Task /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy; Event /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104 | RM09-JOB-FULL / RM09-TASK-FULL / RM09-EVENT-FULL according to typed scope | Back remains usable when history unavailable; recheck parent access. |
| RM09-RH02 | Run row / attempts | Exact authorized logical run returned | Expand run | Show captured references and per-attempt attribution/work-versus-recovery facts in place | Current /runs typed scope | RM09-RUN-HISTORY | Missing fields stay Not reported; no original-result API is misrepresented as chronology. |
| RM09-RH03 | Task link | P7 exact captured Task read available | Open Task details | Navigate by recorded taskId, preserving history return context | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | RM09-TASK-FULL | Missing/denied target is explicit; never resolve by today's selected job. |
| RM09-RH04 | Event link | P7 exact captured Event read available | Open Event details | Navigate by recorded eventId | /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104 | RM09-EVENT-FULL | Missing/denied event does not substitute a current trigger or another event. |
| RM09-RH05 | Previous / Next | Same authorized scope prior/next cursor exists | Browse retained history | Read a bounded page of the same run records | Current /runs with same typed scope and opaque page state | RM09-RUN-HISTORY | Scope changes invalidate cursors; no total/page-count inference. |
| RM09-RH06 | Refresh / Retry | P6 available and scoped read permitted | Retry current bounded query | Read exact visible scope | Current /runs typed scope | RM09-RUN-HISTORY | Unsupported/failed read remains Run history unavailable; access loss clears protected rows. |
| RM09-RH07 | Job Overview / Configuration | Job-scoped history with exact Job context | Select Job tab | Return to same Job details state | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy or ?tab=configuration | RM09-JOB-FULL | Safe parent unavailable/denied state; preserve exact scope. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-RH-PRIMARY | Job-scoped synthetic populated history | Same Job header/nav; recovered logical run with two attempts expanded, captured references and 24 Nov Outcome unknown row. |
| RM09-RH-UNAVAILABLE | P6 absent or initial scoped history unavailable | Run history unavailable; safe Back to details; no No runs or zero count. |
| RM09-RH-TASK | Authorized task scope | Task context and captured task identity across prior job associations; separate history action. |
| RM09-RH-EVENT | Authorized event scope | Event context; only recorded produced runs and supplied zero-run reason. |
| RM09-RH-LOADING | Scoped read loading | Preserve safe scope heading; no fabricated run rows or counts. |
| RM09-RH-EMPTY | Successful bounded retained query is empty | No retained runs in this scope with reported coverage/retention basis; not no execution ever. |
| RM09-RH-STALE | Failed refresh with permitted prior history page | Dated prior page and warning; captured historical attribution unchanged. |
| RM09-RH-DENIED | Session/scope read lost | Remove protected runs/attempts and relationship links. |
| RM09-RH-INCOMPATIBLE | Invalid typed scope or required record shape | Safe invalid/incompatible feedback; no mixed-scope query or invented record. |

### RM09-TASK-FULL: Scheduled Task details

#### Description

Route: /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy.
Title **Scheduled Task — Daily customer check**, exact taskId
**task-demo-dormancy**, application/environment and task revision **7**.
Keep a safe Back to originating details/history link. This entire fixture is
**SYNTHETIC FUTURE CONTRACT — Task reads not delivered** (P7), read-only.

The **main Scheduled Task settings** own the association and remain visible above
both contextual tabs: selected trigger **Daily calendar**, selected job **Customer
dormancy** / customer-service/customer.dormancy, resource **partition-eu**, and
input mapping. Show read-only selection summaries, not edit/save controls.
Mapping uses explicitly static typed fixture constants: **PartitionId** (string)
= **partition-eu**; **InactiveBefore** (date) = **2026-08-27**. These values are
synthetic; no expression engine or mapping execution is implied.

Exactly two contextual configuration tabs: **Trigger** and **Job**. Default
**Trigger** shows shared trigger settings: Daily calendar, provider **Central
Management**, captured/reported trigger revision **3**, daily at **02:00**,
timezone **Europe/Malta**. Label these as **Shared trigger settings** rather
than application job defaults. **Job** shows the selected scoped Job definition
and a clearly dated **Reported local effective configuration** reference:
revision **12**, enabled **true**. **Application job defaults/settings** and any
unprovided full settings/binding say **Not reported**; no CountrySettings
association or fabricated payload/default values. Task input mapping stays in
the main settings and is not relabeled as Job defaults.

**View run history** is a separate action, outside the Trigger/Job tab set.
It opens task-scoped shared history, including runs captured under previously
selected jobs after this association changes. The main settings link to full
Job details; captured Event links from history open immutable Event details.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-TF01 | Back to originating details/history | Safe retained parent context | Return | Restore exact parent selection/page/focus | Preserved authorized Job, Event or /runs typed-scope route | RM09-JOB-FULL / RM09-EVENT-FULL / RM09-RUN-HISTORY | Direct entry falls back to safe Scheduled jobs context; no fabricated parent. |
| RM09-TF02 | Trigger | P7 exact Task/trigger read permitted | Select Trigger | Show shared trigger settings beside persistent main association/mapping | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | RM09-TASK-FULL | Unavailable settings stay explicit; no fabricated schedule. |
| RM09-TF03 | Job | P7 exact selected Job reference permitted | Select Job | Show selected definition and only reported local config facts; main settings remain | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy?tab=job | RM09-TASK-FULL | Unprovided defaults/settings Not reported; no inferred source binding. |
| RM09-TF04 | View run history | P6/P7 task-scoped history delivered and permitted; synthetic preview explicit | Open history | Query by captured taskId across prior jobs; retain Task context | /applications/11111111-1111-1111-1111-111111111111/runs?scope=task&taskId=task-demo-dormancy | RM09-RUN-HISTORY | Run history unavailable with Back to details; not No runs. |
| RM09-TF05 | Customer dormancy | Exact referenced Job is permitted | Open full Job details | Preserve Task return context; use selected executionScope/jobId | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | RM09-JOB-FULL | Safe missing/denied Job state; no lookup by display name. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-TF-PRIMARY | Trigger configuration | Persistent main settings/mapping; exactly Trigger/Job tabs; shared daily trigger settings; separate history action. |
| RM09-TF-JOB | Job configuration | Same main settings; selected Job/local revision12/enabled true, absent application job defaults Not reported. |
| RM09-TF-LOADING | Exact Task read loading | Safe identity/loading; no substituted association or mapping. |
| RM09-TF-UNAVAILABLE | P7 or exact read unavailable | Task details unavailable and safe Back; not an empty successful association. |
| RM09-TF-STALE | Failed refresh with permitted prior Task snapshot | Date original task/trigger references; no silent association change. |
| RM09-TF-DENIED | Session/Task read lost | Clear protected settings and links. |
| RM09-TF-INCOMPATIBLE | Required typed identity/reference invalid | Incompatible response; do not infer Trigger/Job/resource identities. |

### RM09-EVENT-FULL: Immutable Event details

#### Description

Route: /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104.
Title **Event — event-demo-104**, application/environment and safe Back.
Tabs **Overview / Configuration**. This is an immutable historical event,
not a trigger editor. Show **SYNTHETIC FUTURE CONTRACT — Event reads not
delivered** (P7); produced-run reads additionally need P6.

**Overview** shows exact eventId **event-demo-104**, occurred 25 Nov 2026
**02:00 CET** (2026-11-25T01:00:00.0000000Z), trigger **Daily calendar** /
revision **3**, provider **Central Management**, associated Scheduled Task
**Daily customer check** / task-demo-dormancy / captured task revision **7**.
The **Produced Task runs** section links logical run **run-demo-104**,
**Succeeded — recovered**, 12 s, 2 attempts, to the event-scoped shared history.
It also links Task full details and scoped Job full details. All links use
recorded relationships, not current names. If an authoritative event has zero
produced runs, display its supplied recorded reason; absent reason says
**Reason not reported**. Missing history support says **Run history unavailable**,
not a zero-produced-runs success.

**Configuration** shows **Captured trigger configuration at this event**:
Daily calendar, provider Central Management, trigger revision **3**, daily
**02:00 Europe/Malta**, captured task revision **7**, local effective configuration
revision **12** and job contract revision **3** as references. Values are captured
for this event/run relationship and are not overwritten from today's trigger,
Task or Job settings. Do not imply a captured full payload when only references
were supplied: remaining settings/payload say **Not reported**. No edit, replay,
retry, execute or write control appears.

#### Actions and paths

| Action ID | Label/control | Available when | User action | Result/side effect | Destination path | Destination screen | Failure/denied behavior |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-EF01 | Back to originating details/history | Safe retained parent context | Return | Restore originating typed scope/page/focus | Preserved authorized /runs scope or Task/Job detail route | RM09-RUN-HISTORY / RM09-TASK-FULL / RM09-JOB-FULL | Direct entry uses safe application return; access rechecked. |
| RM09-EF02 | Overview / Configuration | P7 exact Event read permitted | Switch Event tab | Read immutable Event overview or captured settings/references | /applications/11111111-1111-1111-1111-111111111111/events/event-demo-104 or ?tab=configuration | RM09-EVENT-FULL | Missing captured fields Not reported; never substitute current trigger settings. |
| RM09-EF03 | Produced Task runs / run-demo-104 | P6/P7 recorded event-run relationship delivered and permitted; synthetic preview explicit | Open produced runs | Query shared history by exact eventId; preserve Event context | /applications/11111111-1111-1111-1111-111111111111/runs?scope=event&eventId=event-demo-104 | RM09-RUN-HISTORY | Run history unavailable with Back to Event details; missing support is not zero runs. |
| RM09-EF04 | Daily customer check | P7 exact captured taskId readable | Open Scheduled Task | Navigate to full Task details and preserve Event return context | /applications/11111111-1111-1111-1111-111111111111/tasks/task-demo-dormancy | RM09-TASK-FULL | Safe missing/denied Task state; captured revision remains historical. |
| RM09-EF05 | Customer dormancy | Exact recorded Job relationship readable | Open full Job details | Navigate by application/executionScope/jobId | /applications/11111111-1111-1111-1111-111111111111/jobs/customer-service/customer.dormancy | RM09-JOB-FULL | No inference from label/current task association; missing relationship stays explicit. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-EF-PRIMARY | Immutable Event Overview | Captured event/trigger/task identity and produced logical run; related Task/Job and event-scoped history links. |
| RM09-EF-CONFIGURATION | Captured Configuration | Captured trigger settings/revisions and configuration/contract references, read-only; no current-setting substitution. |
| RM09-EF-ZERORUNS | Authoritative event produced zero runs | Supplied recorded reason, or Reason not reported; no inference of error, success or rollback. |
| RM09-EF-HISTORYUNAVAILABLE | Produced-run contract/read unavailable | Run history unavailable in Produced Task runs; Event facts may remain if independently permitted. |
| RM09-EF-LOADING | Exact Event read loading | Safe identity/loading, no fabricated event or produced runs. |
| RM09-EF-UNAVAILABLE | P7 or exact Event read unavailable | Event details unavailable and safe Back; no successful-empty event. |
| RM09-EF-DENIED | Session/Event read lost | Remove protected event/configuration/relationship facts. |
| RM09-EF-INCOMPATIBLE | Required typed event/captured reference invalid | Safe incompatible response; no inferred immutable record. |

## Desktop review frame inventory

All frames use exactly **1600 × 1000** desktop layout. Preserve the thirteen
existing reference/panel frames. R06 adds eight frames over four new screen IDs,
for twenty-one images over nine screens. The Job panel gains View full details;
the other three panels retain their R05 quick-inspection fields. R06 filenames
below are canonical screen--state--desktop PNG names.
| Frame ID | Screen ID | State | Review purpose |
| --- | --- | --- | --- |
| R02-F01 | RM09-APPLICATIONS | RM09-L-READY | Unchanged accepted list reference: filters, selection and bounded paging. |
| R03-F01 | RM09-INSTANCES | RM09-I-READY | Instances active; accepted versions/presence/last seen beneath shared tabs. |
| R03-F02 | RM09-CONFIGURATION-LIST | RM09-C-READY | Configuration active; exact application-kind descriptor metadata and scoped-query prerequisite. |
| R03-F03 | RM09-CAPABILITIES | RM09-K-READY | Capabilities active; actual Management APIs and caller hints, including absent hint = Not evaluated. |
| R02-F03 | RM09-APPLICATIONS | RM09-L-NOMATCH | Preserved no-matches/Clear filters state. |
| R03-F04 | RM09-INSTANCES | RM09-I-EMPTY | Preserved no-retained-observations state beneath tabs. |
| R03-F05 | RM09-INSTANCES | RM09-I-STALE | Preserved dated-snapshot refresh failure beneath tabs. |
| R02-F06 | RM09-APPLICATIONS | RM09-L-UNAVAILABLE | Preserved initial unavailable/Retry state. |
| R04-F01 | RM09-SCHEDULED-JOBS | RM09-J-READY | Approved scheduled jobs list with row-details affordance. |
| R05-F01 | RM09-SCHEDULED-JOBS | RM09-J-DETAILS | Main new preview: selected job and same report/resource observation in a right-side panel. |
| R05-F02 | RM09-INSTANCES | RM09-I-DETAILS | Same panel pattern for one exact registration record. |
| R05-F03 | RM09-CONFIGURATION-LIST | RM09-C-DETAILS | Same panel pattern for definition metadata only. |
| R05-F04 | RM09-CAPABILITIES | RM09-K-DETAILS | Same panel pattern for a Management descriptor and advisory hint. |
| R06-F01 | RM09-JOB-FULL | RM09-JF-PRIMARY | Full Job Overview, exact retained report and related Task link; rm09-job-full--primary--desktop.png. |
| R06-F02 | RM09-JOB-FULL | RM09-JF-CONFIGURATION | Reported local revision12/enabled true; missing settings Not reported; rm09-job-full--configuration--desktop.png. |
| R06-F03 | RM09-RUN-HISTORY | RM09-RH-PRIMARY | Job context; recovered logical run/two attempts/captured references and Outcome unknown row; rm09-run-history--primary--desktop.png. |
| R06-F04 | RM09-RUN-HISTORY | RM09-RH-UNAVAILABLE | Same Job context, Run history unavailable and usable Back to details; rm09-run-history--unavailable--desktop.png. |
| R06-F05 | RM09-TASK-FULL | RM09-TF-PRIMARY | Persistent main association/static mapping; Trigger active, Job second tab and history separate; rm09-task-full--primary--desktop.png. |
| R06-F06 | RM09-TASK-FULL | RM09-TF-JOB | Same main settings with selected Job/local config and absent defaults; rm09-task-full--job--desktop.png. |
| R06-F07 | RM09-EVENT-FULL | RM09-EF-PRIMARY | Immutable Event and produced logical Task run, related details/history links; rm09-event-full--primary--desktop.png. |
| R06-F08 | RM09-EVENT-FULL | RM09-EF-CONFIGURATION | Captured trigger configuration/references without event editing; rm09-event-full--configuration--desktop.png. |

Other states are textually specified; further PNGs are not required to represent
a complete state machine. No mobile/narrow frame is required. Desktop keyboard,
focus return, labels, zoom, non-color status and persistent/announced feedback
remain implementation requirements. Pictures do not establish accessibility or
real-service/browser acceptance.

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States/evidence | Coverage and limits |
| --- | --- | --- | --- | --- |
| Accepted filterable application list | RM09-APPLICATIONS | RM09-L01–RM09-L07 | R02-F01, R02-F03, R02-F06 | Preserve accepted reference; P2 server search/authorized environments remain required. |
| Accepted instance/version inspection and return | Both original screens | RM09-L03, RM09-I01–RM09-I05 | R03-F01, R03-F04, R03-F05 | Accepted version/presence semantics remain; approved shared tabs receive the additive Scheduled jobs destination. |
| Requested four detail tabs | All four detail screens | RM09-T01–RM09-T04 | R03-F01–R03-F03, R04-F01 | Add Scheduled jobs to approved Instances, Configuration, Capabilities; same application, default Instances and preserved Back context. |
| Item details | Four application-detail screens | RM09-I06, RM09-C05, RM09-K03, RM09-J04, RM09-D01, RM09-D02 | R05-F01–R05-F04 | Preserve existing panel fields/identity/focus; Job alone adds View full details, subject to the R06 contract boundary. |
| Full Job details/configuration | RM09-JOB-FULL | RM09-JF01–RM09-JF06 | R06-F01, R06-F02 | Retained report facts; local revision/enabled only; missing settings Not reported; no inferred binding/cadence/defaults (P8). |
| Shared scoped history | RM09-RUN-HISTORY | RM09-RH01–RM09-RH07 | R06-F03, R06-F04; Task/Event scopes specified | P6 bounded authoritative records/attempts; typed job/task/event scopes; historical versions/recovery attribution; unavailable is not empty. |
| Scheduled Task model | RM09-TASK-FULL | RM09-TF01–RM09-TF05 | R06-F05, R06-F06 | P7 main association/resource/static mapping; exactly Trigger/Job contextual tabs, history separate and spans prior jobs. |
| Immutable Event details | RM09-EVENT-FULL | RM09-EF01–RM09-EF05 | R06-F07, R06-F08; zero-run state specified | P7 captured settings/references, P6 produced runs and recorded zero-run reason; Task/Job/history traversal without editing. |
| Scheduled job inspection | RM09-SCHEDULED-JOBS | RM09-T04, RM09-J01–RM09-J03 | R04-F01 and specified negative states | Existing inventory API, explicit registration report, scoped definitions/observations; no cron/cadence, inferred timing, fleet aggregate or control authority. |
| Read-only application configuration list | RM09-CONFIGURATION-LIST | RM09-C01–RM09-C04 | R03-F02; scoped/empty/error states | Required bounded scoped query; no global-page completeness, common binding inference or default/content values. |
| Management capabilities/current caller | RM09-CAPABILITIES | RM09-K01, RM09-K02 | R03-F03; absent-hint state | Actual implementedApis and exact operationId hint matching. Not evaluated is distinct from Allowed/Not allowed. |
| RM09-AC01, RM09-AC02 | All nine | No approval action in prototype | R04 historical approval; R05 requested revisions; R06 pending | Exact changed-body/rendered screenshot decision remains pending; prior approval/history are retained. |
| RM09-AC04, RM09-AC05, RM09-AC17 | All nine | All reads/tab actions | Denied, incompatible, target/tab change | Exact scoped authorization, response compatibility, opaque identity and exact strings/timestamps remain required. No executable server schema. |
| RM09-AC06 | RM09-APPLICATIONS | Filter/page actions | Accepted list reference | P2 remains required; no loaded-page global search. |
| RM09-AC07 | RM09-INSTANCES, RM09-CAPABILITIES | Instance/capability reads | R03-F01, R03-F03 | Limited inspection only; Management APIs are distinct from instance inventory/admission/control. |
| RM09-AC10, RM09-AC15 | All nine | Refresh/Retry/Back | Unavailable, stale, denied | Read/session-loss behavior only; no receipt/recovery/mutation coverage. |
| RM09-AC18, RM09-AC19 | All nine | All declared controls | Desktop/keyboard/zoom, bounded pages | Mobile no longer required. No unbounded scan/polling or claim pictures verify runtime accessibility. |
| Deferred broader scope | Outside this slice | No deferred workflow controls | Future work | Sign-in UI, metadata/configuration writes, content/defaults, operation recovery, release/assignment/adoption and remaining inventory/control acceptance need later review. |
| RM09-AC20 | All nine | Read journey only | Synthetic review | Does not provide real-service/browser acceptance or close RM-09. |
| Initial exclusions | All nine | No mutation/execution action | Inspection only | No write/login/metadata/recovery flow, restart, deployment control, automatic rollout, extra inferred tabs or mobile screen. API descriptor text does not enable those workflows. |

Coverage is limited to the described parts of each requirement. A cross-reference
does not mark the full requirement complete.

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-11-25T17:04:52Z | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | Andrew Spiteri explicitly selected Approve connected design for the exact R06 package (renderer6375438db766) including quick panels, View full details, Job Overview/Run history/Configuration and linked Scheduled Task/Event details. Approval covers the read-only desktop UI design and current scope; history/event/task synthetic examples still depend on named P6/P7/P8 RM-10 contracts. It does not claim a delivered scheduler/history backend or authorize a separate RM-10 runtime implementation. Earlier R04 authority remains preserved; normal CIS current-scope/plan/owner reconciliation precedes production work under the existing delegated bounded allocation. |
