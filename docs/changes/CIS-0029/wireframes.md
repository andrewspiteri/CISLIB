---
title: "CIS-0029 textual wireframes — R02"
type: textual-wireframes
status: Draft
change_id: CIS-0029
design_revision: R02
approval_status: NotReviewed
authority: human-reviewed
---

# Textual wireframes — R02

R02 is a proposed two-screen **desktop backoffice** review: find an application,
then inspect its retained instance observations and reported application
versions. It follows the user's narrower step-by-step request. Mobile is no
longer required. See the [design brief](design-brief.md).

R01 was rejected for revision. R02 is pending human review; the CIS design
checkpoint remains **PausedForReview**. These textual wireframes neither record
approval nor authorize production UI. Any later approval must identify the exact
rendered R02 revision and this partial scope. Configuration, metadata editing,
operation recovery, release/assignment/adoption and other broader specification
work remain deferred; they are not silently removed or accepted.

## Screen inventory

| Screen ID | Frontend type | Name | Route/path | Platform | Actors/access | Entry points | Purpose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RM09-APPLICATIONS | backoffice | Applications reported to Management | /applications | Web desktop only | Current operator with authorized application reads | Existing host entry, detail return | Find authorized known applications by name/identity and environment, then select one. |
| RM09-INSTANCES | backoffice | Application instances and reported versions | /applications/11111111-1111-1111-1111-111111111111/instances | Web desktop only | Current operator with exact application and instance read access | Application name/row, authorized direct link | Show retained instance observations, reported application versions, raw presence and last-seen evidence for the exact selected application. |

There are no sign-in, metadata, configuration, operations, assignment or adoption
screens in R02, and no placeholder navigation for those areas. A simple
Management header and Applications destination supply context. Any Deployments
section label is accompanied by truthful source wording: applications reported
to or known to Management, rather than verified live deployments.

## Shared interaction and data rules

Every frame says **DEMONSTRATION — synthetic data** and uses fixed snapshot
`2026-11-25T10:00:00.0000000Z` (25 Nov 2026, 11:00 CET). All identities, labels,
rows and observations are synthetic. Proposed portal paths/query state are not
Management API routes or proof of implemented server filters.

The main application is `Demo.CountryData`, display name
`Country data service`, in `Demo staging`, application ID
`11111111-1111-1111-1111-111111111111`. Application display metadata, canonical
identity and environment remain distinct. Never resolve a target by editable
display name.

`Instance.applicationVersion` is required reported registration text, 1–512
characters. Use exact values such as `2.4.2+build.18` and `2.4.1`; do not
parse or rank them. `metadataRevision`, runtime/CIS versions and configuration
revisions are not substitutions. There is no application-wide version or instance
count in the application list response, and R02 invents neither.

`reportedInstanceId` is an unverified label. `registrationId` identifies the
process-incarnation registration. Presence displays Active, Stale or Closed for
the supplied `active`, `stale` or `closed` value. Last seen displays optional
`lastSeenAtUtc`; when absent, show **Not reported**. It does not update the
immutable version report. Supporting copy
states **Version reported by this process when it registered**. Presence does
not claim health or verified process liveness.

P2 remains an explicit prerequisite: bounded authorized server search by
application name/identity and environment, plus authorized environment
enumeration. Current `listApplications` accepts only pageSize/cursor; current
instance paging also has no environment, presence or version filter. A complete
synthetic fixture may demonstrate intended search/filter interaction, but
production may not silently search only loaded rows or discover environments from
the first page. No new version API is needed.

All reads authorize their exact target and query/cursor scope. Preserve opaque
IDs/cursors and exact source timestamps. Show dated CET times with accessible
exact UTC source values. Do not infer totals, deployment state or fleet health
from a page. A known application may remain after registrations are purged; an
empty instance response therefore does not prove the application is undeployed.

Retain list filters, cursor history, scroll and originating application focus
across detail navigation. Clear query-bound cursors when filters change. Late
responses from previous targets/queries cannot replace the current view.
Protected data is cleared on session/read-access loss; valid-session outages may
retain an explicitly dated snapshot. There are no writes, offline commands,
operation receipts or automatic action replay in R02.

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
- Page/section title **Instances**, plus Refresh and dated fetch feedback.

Keep this a single inspection view without tabs for configuration, metadata or
operations. The selected application is resolved by application ID and authorized
again. Direct entry needs no fabricated prior list selection; Back returns to a
safe initial authorized applications query when no prior context exists.

| Column | Content and behavior |
| --- | --- |
| Instance | Reported instance label, clearly described as unverified; opaque registrationId as readable secondary text. |
| Reported app version | Exact required applicationVersion. Long values wrap without losing content. |
| Presence | Active, Stale or Closed for known source values, with text and non-color cues. Preserve unknown future value text and mark it unknown. |
| Last seen | Dated lastSeenAtUtc in CET when supplied, with accessible exact source UTC timestamp; **Not reported** when absent. This is separate from the registration-time version report. |

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
| RM09-I01 | Back to applications | Applications navigation is available | Activate Back | Restore prior list filters, query-bound cursor history, scroll and originating link focus; revalidate access as needed | Preserved /applications query, or /applications for direct entry | RM09-APPLICATIONS | If the originating row disappeared, focus the list heading and explain the refreshed result. Cached context does not imply fresh data. |
| RM09-I02 | Refresh | Exact application/instance reads are permitted | Activate Refresh | Reread selected application and current bounded instance page | Current /applications/11111111-1111-1111-1111-111111111111/instances page | RM09-INSTANCES | Retain a dated permitted snapshot on network failure; remove data on access loss. |
| RM09-I03 | Next | Same-target continuation cursor exists | Activate Next | Read bounded next instance page | Current instance route with opaque next-page state | RM09-INSTANCES | Failed paging does not replace observations with an empty success state. |
| RM09-I04 | Previous | Same-target previous cursor history exists | Activate Previous | Read prior bounded instance page | Current instance route with prior page state | RM09-INSTANCES | Invalid/changed cursor restarts the selected target's query with feedback. |
| RM09-I05 | Retry | A selected-target read failed and read access remains valid | Activate Retry | Retry the visible application/instance read | Current instance route/page | RM09-INSTANCES | Keep unavailability explicit and never reveal an inaccessible or previously selected target. |

#### States

| State ID | State | Visible behavior |
| --- | --- | --- |
| RM09-I-LOADING | Initial target read | Selected target context/loading feedback; no fabricated version or instance rows. |
| RM09-I-READY | Successful populated page | Per-instance reported versions, raw presence, last seen and bounded paging. Different versions remain separate. |
| RM09-I-EMPTY | Successful empty instance response | **No retained instance observations**. Explain that this does not establish whether a process is currently deployed or running. |
| RM09-I-UNAVAILABLE | Initial target/instance read unavailable | **Instances unavailable**, persistent feedback and Retry; no zero-instance claim. |
| RM09-I-STALE | Refresh failed with a valid permitted snapshot | **Snapshot may be out of date**, last successful fetch time, original source last-seen values and Refresh/Retry. Raw Active remains an old observation, not a current health assertion. |
| RM09-I-DENIED | Target missing/inaccessible or session/read access lost | Remove protected observations; show safe unavailable/access feedback and Back where permitted. No sign-in screen is added. |
| RM09-I-TARGETCHANGE | New target or obsolete request completes | Clear old target rows; accept only the current target's response. |
| RM09-I-INCOMPATIBLE | Required record fields missing or unsupported response shape | Explain that the response cannot be displayed safely. Do not manufacture a version, identity or Active state. |

## Desktop review frame inventory

All proposed frames use desktop layout at exactly 1600 × 1000. There are
two screen templates; state variants do not add navigation destinations.

| Frame ID | Screen ID | State | Review purpose |
| --- | --- | --- | --- |
| R02-F01 | RM09-APPLICATIONS | RM09-L-READY | Search/name/identity, authorized environment filter, application selection and bounded paging. |
| R02-F02 | RM09-INSTANCES | RM09-I-READY | Selected application context, differing reported versions, unverified labels, Active/Stale/Closed and last seen. |
| R02-F03 | RM09-APPLICATIONS | RM09-L-NOMATCH | Successful filtered empty result and Clear filters. |
| R02-F04 | RM09-INSTANCES | RM09-I-EMPTY | No retained observations, without claiming no deployed processes. |
| R02-F05 | RM09-INSTANCES | RM09-I-STALE | Dated retained snapshot during refresh failure. |
| R02-F06 | RM09-APPLICATIONS | RM09-L-UNAVAILABLE | Initial unavailability, Retry and no fabricated rows/totals. |

Loading, denied/session-loss, incompatible data and obsolete-response states are
specified above; the six pictures do not claim all runtime behavior has been
implemented or tested. No mobile/narrow frame is required. Desktop keyboard,
focus return, labels, zoom, non-color status and persistent/announced feedback
remain implementation requirements.

## Journey and requirement coverage

| Requirement/exclusion | Screen IDs | Action IDs | States/evidence | Coverage and limits |
| --- | --- | --- | --- | --- |
| Latest step 1: filterable applications | RM09-APPLICATIONS | RM09-L01, RM09-L02, RM09-L04, RM09-L05 | R02-F01, R02-F03 | Desired whole-query search and authorized environments remain P2 prerequisites; current paging alone does not deliver them. |
| Latest step 2: select application and see instances/version | Both | RM09-L03, RM09-I01 | R02-F01, R02-F02 | Exact target, per-instance reported applicationVersion and preserved back context. |
| Truthful application/version evidence | Both | RM09-L03, RM09-I02 | R02-F02, R02-F04 | Registration-time version is distinct from metadataRevision and Last seen; no app version/count/health aggregate. |
| RM09-AC01, RM09-AC02 | Both | No approval action in prototype | Proposed R02, pending exact rendered decision | Partial desktop review only; rejection history and production gate remain authoritative. |
| RM09-AC04, RM09-AC05 | Both | All read actions | Denied, target/query change, incompatible response | Exact target/query authorization and capability/response compatibility remain required. |
| RM09-AC06 | RM09-APPLICATIONS | RM09-L01, RM09-L02, RM09-L04, RM09-L05 | Ready/no matches | P2 remains required. No loaded-page global search or first-page environment discovery. |
| RM09-AC07, RM09-AC17 | RM09-INSTANCES | RM09-I02, RM09-I03, RM09-I04 | Ready, empty, incompatible | Only instance/version inspection is in scope; no admission/control or complete inventory journey is claimed. Preserve opaque identity and exact version/timestamp text. |
| RM09-AC10, RM09-AC15 | Both | Refresh/Retry/Back | Unavailable, stale, denied | Only read/session-loss behavior is in scope; no receipt/recovery or mutation coverage is claimed. |
| RM09-AC18, RM09-AC19 | Both | All declared controls | Desktop/keyboard/zoom, bounded pages | Mobile is no longer required by the user. Pictures do not establish real keyboard/accessibility behavior. No unbounded fetch or polling is implied. |
| Deferred broader scope | Outside these two screens | No deferred controls/navigation | Future work | RM09-AC03 human sign-in UI; AC08–AC14 mutation/configuration/recovery/adoption; AC16 sensitive configuration content; remaining inventory/capability and cross-cutting portions need later review and implementation. |
| RM09-AC20 | Both | Read journey only | Synthetic review | Does not provide real-service/browser acceptance or close RM-09. |
| Initial exclusions | Both | No mutation/control action | Inspection only | No metadata editing, configuration, operation navigation, restart, scheduling, execution, automatic rollout, fleet transaction or mobile screen. |

Coverage is limited to the described parts of each requirement. A cross-reference
does not mark the full requirement complete.

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Rationale |
| --- | --- | --- | --- | --- |
| Pending Approval — R02 | No human R02 decision recorded | Not decided | Not sealed; CIS must bind the exact rendered review revision/content | Proposed two-screen desktop inspection slice. R01 rejection history is retained separately; production remains gated and broader RM-09 work remains deferred. |

