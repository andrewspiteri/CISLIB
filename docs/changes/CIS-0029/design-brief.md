---
title: "RM-09 Management Portal design brief"
type: design-brief
status: Proposed
change_id: CIS-0029
frontend_type: backoffice
design_approval: Pending
baseline: "6c70cee807f12caddf40cd1b443e4a20045d35aa"
---

# RM-09 Management Portal design brief

This brief guides the versioned rendered-screen review package for the
[preserved specification](../../../planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md).
All screens are backoffice. The design is pending explicit user approval; this
brief, the imported specification and the implementation plan do not approve an
unseen experience. The review package must identify its exact revision/content
and bind the principal journeys, state variants and API prerequisites to the CIS
approval checkpoint before production UI implementation.

## Navigation and three principal journeys

Use one operational workspace with **Applications**, **Configuration** and
**Operation receipts** navigation. Applications is the landing view; no fleet
dashboard or unsupported aggregate cards are needed. Keep the selected environment
visible in the shell and repeat the exact environment/application in every change
review. Application identity is separate from editable display metadata. Link the
screen set through numbered journey steps and explicit forward/back destinations;
the review must show how an operator reaches a result and recovers from failure.

| Journey | Connected screens and decisions | Required branch |
| --- | --- | --- |
| 1. Inspect, edit and recover | Applications -> application detail -> Instances/Inventory -> Edit metadata -> Review exact target and before/after fields -> operation receipt -> refreshed detail | Concurrent edit retains a permitted draft for a fresh review. Lost response leads to original-key lookup and a recovered result, including separate cleanup diagnostics. |
| 2. Publish, release and assign | Configuration sources -> exact source JSON -> Validate -> publication review/result -> exact common/application pair preview -> Create release -> whole-map canary assignment review/result -> Adoption | Invalid JSON/structural issues return to the draft. Preview is side-effect free; publication does not activate; release creation does not assign. A changed precondition requires renewed review. |
| 3. Observe, promote and roll back | Adoption -> Promote with explicit canary retention/removal -> whole-map review/result -> Adoption -> Open known earlier release -> verify exact binding -> rollback map review/result | Mixed adoption and restart requirements remain visible. Outage preserves a dated snapshot with writes disabled; session/grant loss removes protected data. Reconnect refreshes authority/state before a fresh explicit action. |

Reuse a small screen family: application list; application detail with Instances,
Inventory and Capabilities sections; metadata edit/review; operation receipt;
configuration source/history/editor; release and assignment review; adoption.
Configuration is reachable from the application context and the source index.
Long administrative reviews are pages, not cramped dialogs. Operation receipts
show this session's incomplete list and can open a known reference; they are not
a global operation history or Audit archive.

## Visual system

Use the CIS default reference palette: white surfaces on `#F8FAFC`, dark navy
text `#0F172A`, secondary text `#334155`, blue primary actions `#2563EB` and
borders `#CBD5E1`. Success, warning and error
use text/icon labels as well as restrained color. Verify contrast for actual
foreground/background pairs; color alone never communicates status.

The preview embeds Inter v4.1, with its SIL Open Font License notice retained.
Use 14-16 px body text, clear 20-24 px page titles,
monospace IDs/JSON/exact timestamps, an 8 px spacing rhythm, approximately 40 px
controls and compact rounded corners. Tables have readable headers and aligned
values; forms have persistent labels, associated errors and a stable action row.
Reserve a persistent feedback region for actionable failures and completed
results. Toasts may supplement it but cannot be the only result record.

The disposable review renderer uses CIS SVG templates and pinned Sharp 0.35.5
(Apache-2.0) in ignored tooling. The proposed production component approach is
Radix Primitives with authored CSS, isolated to the portal host/UI. Radix's
[MIT license](https://github.com/radix-ui/primitives/blob/main/LICENSE) requires
retaining its copyright and permission notice. Verify supported production
versions and compatibility before pinning; the existing client workspace has no
UI toolkit to inherit. No production Next/React/session dependency is installed
by this preview.

## Screen content and truthful capabilities

The current baseline has thirteen foundation and nineteen configuration APIs;
the attachment's older RM-08 absence is not the current implementation state.
Discovery still determines which compatible handlers a deployed service exposes.
Each rendered action must identify its implemented contract or prerequisite.

| Surface | Delivered contract and truthful presentation | Prerequisite or limit |
| --- | --- | --- |
| Application list/environment | `listApplications`; identity, environment and display metadata, with bounded server pages | **Required addition:** bounded server search by environment/application identity/display name, plus authorized environment enumeration. Current `PageRequest` accepts only page size/cursor. Do not substitute loaded-page search or first-page environment discovery. Reset query-bound cursors on filter change. |
| Detail/instances/inventory | `getApplication`, `listInstances`, `listInventory`, `getCapabilities`; identity evidence, presence, historical observations, complete reported inventories and timestamps | No operator-facing admission projection or remote restart. Do not call another workload's self-control API. Reported ownership is a time-qualified observation, not lease authority. Opaque detail is bounded escaped text/JSON. |
| Metadata edit/recovery | `replaceMetadata`, `getMetadataOperation`; display name/description only, strong ETag and original key | Omitted description clears it; explicit null rejects. Require target read, write and lookup eligibility. Show business decision, original completion, result source and current cleanup separately. |
| Source discovery/content | `getDefinitions`, `getSource`, `getPublication`; exact configuration/schema/source/binding and paged immutable publication metadata | Definitions are globally authorized pages, not an automatically complete application-specific list. Defaults/content require separate source authority. Definition descriptors have no declared update mode; show reported per-instance mode or "Not reported". |
| Validation/publication/preview | `validate`, `publishSource`, `preview`, `getPublicationOperation`; preserve reviewed exact payload and immutable references | Preview supplies redaction, path/kind changes, issues, current map and recipient observations, not before/after values. A source-text diff needs authorized original content and is separate from server structural preview. Never compute runtime defaults or promise typed consumer acceptance. |
| Release/map/adoption | `createRelease`, `getRelease`, `getAssignments`, `replaceAssignments`, `getStatus` and matching original-key lookups | **Required addition:** bounded authorized binding-scoped configured cohort discovery for the canary picker. Current pins/live recipients cannot enumerate an offline/new configured canary. Do not accept arbitrary instance labels as groups. |
| Rollback | Open an exact known earlier release ID with `getRelease`, verify its binding, then review the complete replacement map | There is no release-history list API. Label known/current/recorded references honestly; no new history API is needed for this initial flow. Rollback advances generation and retains unrelated deliberate overrides. |
| Human session/action eligibility | Existing per-call credential and server-only projection boundaries | **Required additions:** real Entra human/delegated sign-in independently validated by Management, and narrowly scoped configuration read/write/recovery eligibility. Existing workload Entra support and foundation caller hints do not supply these. No broad permission framework or privilege borrowed from the portal application. |

Keep the portable clients, Next.js server/session host and presentation layers
separate. Current workspace pins Node 24.15.0, npm 11.12.1 and TypeScript 5.9.3;
there is no installed Next/React portal or browser-test framework. Pin supported
host dependencies during implementation without moving them into the client
packages. Every server action and underlying API must authorize the actual target.
Keep credentials server-only and expose minimal authorized display DTOs.

## Synthetic review data

Every plate carries **DEMONSTRATION - synthetic data**, a fixed demo snapshot time
and no claim of actual authorization, server health or deployment. Use six demo
applications across two explicitly demo environments; the main target is
`Demo.CountryData` in `Demo staging`. Stable canonical UUIDs and opaque ETags link
the same target/source/publication/release/receipt across all journey steps.

Use `country.settings`, schema `"1"`, with the maintained CountrySettings shape:
`SourceUrl`, `RefreshInterval`, `RequestTimeout`, `RetryPolicyName`. A synthetic
common source can contain exactly
`{"RefreshInterval":"01:00:00","RequestTimeout":"00:00:10"}`; an application
source can set `SourceUrl` and `RetryPolicyName: null`. No URL is fetched. JSON
content null is separate from forbidden metadata-command null.

Use metadata revision `"12"`, assignment generations `"41"`/`"42"`, three known
immutable release references and `canary` labeled as a synthetic trusted mapping
whose production picker needs the stated prerequisite. Include six recipient rows
covering all required adoption states, plus stale/offline and no-recipient
variants. Do not invent fleet totals, grants, verified process identities or
fresh-convergence percentages. Show a visible CET timezone and preserve the exact
seven-fraction UTC source timestamp in diagnostic detail. Include an Int64 above
2^53 in a state variant to demonstrate exact string handling.

## State, recovery and responsive requirements

Provide loading, no-authorized-data, no-results, denied, unavailable, stale,
validation-error, concurrent-update, unresolved-result and expired-session
variants within the journeys. Initial failure displays unavailable, not fabricated
zeros. A valid-session outage may retain the last snapshot with its fetch and
observation times; disable new changes and queue nothing. On reconnect, refresh
session, capabilities, target state and tokens before writes. Target/identity
switch invalidates prepared confirmations and prevents late-response replacement.

Retain the original key before submission. Uncertainty offers original lookup,
not automatic mutation retry; NotFound does not prove rollback. A synchronous
metadata command has no invented queued/running lifecycle. Recovered commitment
remains committed if lookup cleanup later fails. Cancel waiting does not undo an
accepted effect. Bound same-user payload-free receipts; clear them on sign-out or
user switch. Revoked lookup leaves the outcome unresolved without revealing it.

Sensitive content is removed before browser delivery. Redacted defaults are not
empty defaults, and a mask is not an editable value. Preserve drafts only while
permitted, in memory by default. Keep exact JSON bytes, decimal Int64 strings,
opaque ETags/cursors and exact timestamps; do not round-trip authoritative source
numbers through JavaScript Number. Enforce finite payload/page/cache/receipt/call
limits. Provisional polling is 10 seconds for visible inventory/adoption and a
2-second original-result polling interval for at most 60 seconds, then the normal
10-second interval, subject to provider limits. Pause hidden views and allow
manual refresh. UI timers never determine admission.

Adoption shows desired release/generation, reported applied provenance/local
revision, last success where supplied, observation/receipt times and independent
registration/adoption freshness. Preserve Pending, Applied, AppliedUnchanged,
Rejected, PendingRestart and Faulted; unknown states remain unknown. Faulted does
not claim safe live last-known-good values. Stale historical success and zero
observed recipients do not count as fresh convergence. No unsupported restart
action accompanies PendingRestart or rollback.

Desktop evidence uses an approximately 208 px navigation rail and a flexible
content region with tables and a side-by-side editor/review where useful. Narrow
evidence at approximately 390 px uses a drawer, stacked forms/reviews and visible
environment/target above content. The whole page reflows at zoom; dense tables may
use a labeled local scroll region. Long IDs wrap or expose accessible detail/copy
controls. Target WCAG 2.2 AA: keyboard reachability, visible unobscured focus,
associated labels/errors, non-color status cues, announced status updates and
dialog focus return. Rendered pictures demonstrate intended focus/layout; actual
keyboard and accessibility behavior needs implementation verification.

The review pack must visibly connect the three journeys and include metadata
conflict/recovery, source/release review, complete promotion/rollback maps, mixed
adoption and representative narrow/session/outage states. It must show dependency
labels without inactive future-product navigation. No prototype performs real
administrative mutations, and synthetic/rendered evidence does not establish
real-service acceptance or live Entra deployment.
