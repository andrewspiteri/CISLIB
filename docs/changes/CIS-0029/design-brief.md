---
title: "RM-09 application and instance inspection design brief — R02"
type: design-brief
status: Proposed
change_id: CIS-0029
frontend_type: backoffice
design_revision: R02
design_approval: Pending
baseline: "6c70cee807f12caddf40cd1b443e4a20045d35aa"
---

# RM-09 application and instance inspection design brief — R02

R02 follows the user's request to proceed step by step: first show a filterable
list of applications, then open an application to see its instances and their
reported application versions. This is a **desktop-only, read-only backoffice
review slice** with two screen templates. Mobile is no longer required by the
latest instruction.

The broader [preserved specification](../../../planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md)
remains future scope. Configuration, metadata editing, operation recovery,
release/assignment/adoption journeys and their prerequisites are deferred beyond
R02; this smaller review does not discard them or satisfy their acceptance
criteria. R02 has no configuration, metadata-edit, operations or sign-in screens,
and no future-placeholder navigation.

R01 was rejected for revision. These proposed R02 documents do not record human
approval, replace the rejection history or resume production work. The CIS design
checkpoint remains **PausedForReview** until an explicit decision binds the exact
rendered R02 content and its limited scope. Approval of this slice would not
approve the broader portal or close RM-09.

## Journey and screen scope

| Step | Screen | Purpose | Forward/back behavior |
| --- | --- | --- | --- |
| 1 | RM09-APPLICATIONS — /applications | Find applications reported to or known to Management, using name/identity and an authorized environment filter. | Select an application name or row to open its exact application ID. |
| 2 | RM09-INSTANCES — /applications/11111111-1111-1111-1111-111111111111/instances | Inspect the selected application's retained instance observations, reported application versions, raw presence and last-seen times. | Back restores the list's filters, query-bound page/cursor history, scroll and originating focus. |

A simple desktop header supplies the Management context and current page.
Applications is the only navigation destination in this slice; there is no
multi-area rail. On detail, keep the selected application's display name,
canonical application identity and environment visible above the instance table.
Refresh acts on the current view.

The product may use **Deployments** as a section heading, with **Applications**
as the list title. Source copy must make clear that these are applications
reported to or known to Management. A stored application is not proof that a
process is currently deployed or running; an empty instance response is not proof
that nothing is deployed.

## Applications screen

Place a persistent **Search applications** field, described as **Name or
identity**, beside an **Environment** selector. **Apply filters** commits the
query; **Clear filters** restores the unfiltered authorized query and resets its
cursor. The environment selector contains authorized environments only. Do not
add version, presence or health filters in R02.

The table contains application display name, canonical identity and environment.
The application name is a keyboard-accessible link; clicking its row opens the
same destination without making text selection or keyboard focus ambiguous.
Resolve the target by its application ID, never by editable display name.
The list contract does not supply instance counts, current versions, fleet totals
or deployment health, so the table and surrounding page show none of these.
Do not derive rollups from the currently loaded page.

Use bounded pages with Previous/Next controls only when their corresponding
query-bound cursor/history exists. Do not imply a total page or application
count. Filter changes invalidate old cursors; obsolete responses cannot overwrite
the newly selected query.

## Instances screen and version meaning

The table contains **Instance**, **Reported app version**, **Presence** and
**Last seen**. Under Instance, distinguish the reported instance label from the
opaque instance identity. A reported label is unverified display text; it is not
a verified host, process identity or address.

Use the delivered required `Instance.applicationVersion` string, bounded to
1–512 characters. Preserve the exact reported text; do not parse it as a semantic
version or choose the greatest-looking value. Supporting copy says:

> Version reported by this process when it registered.

The registration captures the version once. A later last-seen observation updates
presence evidence; it does not reread the version or prove the same version is
executing now. Label the value **Reported app version**, rather than presenting
it as a verified live/current version.

Different instances may report different versions. Display each row faithfully;
do not invent a single application-wide current version. `metadataRevision`
belongs to display metadata and is not an application version, runtime version or
CIS/library version. Keep metadata revision and ETag out of this slice's main UI.
A missing required applicationVersion is not a valid normal record to render as
a made-up version.

Presence displays **Active**, **Stale** or **Closed** for the supplied `active`,
`stale` or `closed` value, with text and
a restrained visual cue. These describe registration observations, not health.
Show optional `lastSeenAtUtc` as Last seen, with a dated CET display and accessible
exact source timestamp, or **Not reported** when absent. Do not recalculate
presence from a browser timer. Unknown future values retain their exact text and
remain explicitly unknown; they never fall back to Active.

The table is bounded and independently paged. No total, healthy count, convergence
percentage, restart, deployment control or configuration tab appears.

## Contract map and prerequisites

| Surface | Existing evidence | Required boundary |
| --- | --- | --- |
| Application rows | `listApplications`: identity, environment, display metadata, revision and ETag; bounded `pageSize`/`cursor`. | No application version, instance count, fleet total or current deployment-state aggregate. |
| Requested application filters | Current application paging accepts only `pageSize` and `cursor`. | **P2: bounded authorized server search by application name/identity and environment, plus authorized environment enumeration, remains required.** Do not claim current paging implements these filters. |
| Selected application | `getApplication` for the exact application ID. | Authorize the resolved target. List context or an old snapshot does not establish current read access. |
| Instance rows and versions | `listInstances`: required `applicationVersion`, opaque process-incarnation `registrationId`, unverified `reportedInstanceId`, presence and optional `lastSeenAtUtc`; bounded paging. | No new version API is needed. Existing instance paging has no version, environment or presence filter. |
| Human/session boundary | Existing per-call/server-only boundaries remain relevant. | Real human/delegated authority remains a production prerequisite; R02 does not add or approve a sign-in screen. Protected data must be removed on session/access loss. |

The rendered synthetic review may demonstrate the intended filters over its
complete demo fixture, with P2 identified in the review notes. Production may not
substitute loaded-page search or first-page environment discovery for the
requested whole-query behavior. Proposed portal query state is not evidence of
existing Management API query parameters.

## State and return behavior

Both templates distinguish loading, successful empty data, no filter matches,
access loss and initial unavailability. Initial failure shows unavailable with
Retry, never fabricated zero applications or instances.

A valid-session refresh failure may retain a clearly dated last successful
snapshot. Mark it **Snapshot may be out of date**, expose the last successful
fetch time and failure feedback, and offer Refresh/Retry. Preserve the source
last-seen times and raw presence values; do not recolor old Active rows as proof
of current activity. An instance table's empty response says **No retained
instance observations** and does not say no deployed or running processes.

A denied/deleted target does not reuse a prior application's rows. Session or
read-access loss clears protected content. This slice has no offline queue,
mutation, original-operation receipt or automatic action replay.

Back restores the selected list query, cursor history and scroll position, then
returns keyboard focus to the originating application link when it still exists.
If it no longer exists, focus the list heading and explain the refreshed result.
Recheck access and refresh data as needed; restored navigation context must not
make a cached snapshot appear fresh. Target/query switches invalidate in-flight
responses that belong to the previous view.

## Visual system and desktop evidence

Retain the CIS palette: white surfaces on `#F8FAFC`, dark navy `#0F172A`,
secondary text `#334155`, blue `#2563EB` and borders `#CBD5E1`. Use Inter,
14–16 px body text, 20–24 px titles, an 8 px spacing rhythm and clear table
headers. Keep source IDs and exact timestamps readable. Long identities and
versions wrap without losing their full value. Status must use text as well as
color.

Propose six desktop frames at exactly 1600 × 1000, all using these two
templates:

| Frame | Screen | Evidence |
| --- | --- | --- |
| R02-F01 | RM09-APPLICATIONS | Populated list, name/identity search, environment filter and bounded paging. |
| R02-F02 | RM09-INSTANCES | Selected application with differing reported versions, Active/Stale/Closed presence and last-seen evidence. |
| R02-F03 | RM09-APPLICATIONS | Successful query with no matching applications and Clear filters. |
| R02-F04 | RM09-INSTANCES | Successful response with no retained instance observations. |
| R02-F05 | RM09-INSTANCES | Refresh unavailable with a clearly dated retained snapshot. |
| R02-F06 | RM09-APPLICATIONS | Initial service unavailability with Retry and no fabricated rows or totals. |

Loading, denied/session-loss and obsolete-response behavior are specified in the
wireframes; these six pictures must not be described as complete implementation
or end-to-end acceptance evidence. No mobile/narrow frames are required. Desktop
zoom, keyboard reachability, visible focus, labels, non-color status and announced
feedback remain relevant. A picture cannot establish working keyboard or
accessibility behavior.

## Synthetic review data and approval boundary

Every frame carries **DEMONSTRATION — synthetic data** and fixed snapshot
`2026-11-25T10:00:00.0000000Z` (25 Nov 2026, 11:00 CET). Use
`Demo.CountryData`, display name `Country data service`, in `Demo staging`,
application ID `11111111-1111-1111-1111-111111111111`. Other list rows may use
the existing synthetic application fixture and a second explicitly demo
environment; no live service or identity is implied.

Use exact illustrative reported versions such as `2.4.2+build.18` and
`2.4.1` in different instance rows. Reported instance labels are explicitly
synthetic/unverified. Preserve the distinction between snapshot fetch time,
registration-time version evidence and last-seen observation time.

The review is proposed R02 content only. No production UI, host dependency,
administrative call or deployment is authorized by these files. The eventual
human decision must identify the exact rendered revision/content and the
two-screen desktop scope; broader RM-09 acceptance remains deferred.

