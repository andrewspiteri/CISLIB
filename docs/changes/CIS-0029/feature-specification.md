---
title: "RM-09 Management Portal feature"
type: feature-specification
status: Proposed
feature_id: RM09
targets: [cislib]
stack: [Next.js, TypeScript, CSharp]
references:
  - "planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md"
---

# RM-09 Management Portal feature

This CIS import adapter maps the unchanged supplied acceptance scenarios. The preserved source remains the full requirements contract; this adapter does not approve an unseen UI.
Source SHA256: `1574ec2624247a95945b68f318e67d0ea4595b8fbd8e116eff149059ad502633`.
Selected implementation baseline: `6c70cee807f12caddf40cd1b443e4a20045d35aa`.

## Requirements

| ID | Frontend type | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| RM09-AC01 | backoffice | CIS produces a visible, versioned design package with complete inspection/change journeys and API mappings | CIS produces a visible, versioned design package with complete inspection/change journeys and API mappings. An unapproved or rejected design blocks production UI tasks. |
| RM09-AC02 | backoffice | An approval survives restart for the unchanged design | An approval survives restart for the unchanged design. A material changed workflow requires approval of that revision; routine presentation fixes do not prompt again. |
| RM09-AC03 | backoffice | Production human sign-in reaches management through independently validated human/delegated identity | Production human sign-in reaches management through independently validated human/delegated identity. An operator without a grant cannot borrow the portal application's broader privilege. Development identity is rejected by production configuration. |
| RM09-AC04 | backoffice | Altering an environment, application ID, cursor, operation reference or URL cannot disclose or change an unauthorized resource | Altering an environment, application ID, cursor, operation reference or URL cannot disclose or change an unauthorized resource. Direct server-action/API calls receive the same protection as the visible UI. |
| RM09-AC05 | backoffice | Missing or incompatible capability versions are unavailable truthfully | Missing or incompatible capability versions are unavailable truthfully. Unknown response states never default to success, and opaque inventory detail is rendered safely. |
| RM09-AC06 | backoffice | Cross-page application filtering uses the documented server query | Cross-page application filtering uses the documented server query. Cursors bind the query/scope; changing filters resets them. No loaded page is advertised as a fleet-wide count or complete environment list. |
| RM09-AC07 | backoffice | Reported identity, instance presence, historical health and resource-owner observations retain their source/time distinctions | Reported identity, instance presence, historical health and resource-owner observations retain their source/time distinctions. The portal neither calls another workload's self-control API nor grants/releases execution authority. |
| RM09-AC08 | backoffice | Metadata editing affects only supported display fields | Metadata editing affects only supported display fields. Concurrent edits preserve the strong ETag contract; the losing edit is reviewed against current state rather than silently overwriting it. |
| RM09-AC09 | backoffice | A mutation commits but its response is lost, then the page reloads | A mutation commits but its response is lost, then the page reloads. Recovery uses the original key and validated actor/target, performs one effect and one required audit, and preserves committed/rejected/unknown and cleanup distinctions. |
| RM09-AC10 | backoffice | Session expiry, sign-out, user switching or grant loss blocks pending writes and clears affected protected data | Session expiry, sign-out, user switching or grant loss blocks pending writes and clears affected protected data. A revoked lookup cannot reveal the result or cause a replacement mutation; only a same-user minimal receipt may remain. Late responses cannot repopulate the previous user's or environment's view. |
| RM09-AC11 | backoffice | Configuration validation/preview has no publication effect | Configuration validation/preview has no publication effect. Publication preserves the reviewed exact payload, creates immutable content, and release creation pins exact authorized sources without assigning them automatically. |
| RM09-AC12 | backoffice | A state or permission change after preview is checked at commit | A state or permission change after preview is checked at commit. A conflict preserves a permitted draft for revised review and does not resubmit automatically. |
| RM09-AC13 | backoffice | Canary promotion and rollback show every affected default/cohort entry | Canary promotion and rollback show every affected default/cohort entry. Promotion handles the override explicitly; rollback advances assignment generation and retains immutable publication history. |
| RM09-AC14 | backoffice | Mixed Applied, AppliedUnchanged, Pending, Rejected, PendingRestart and Faulted observations remain distinct from stale/offline status | Mixed Applied, AppliedUnchanged, Pending, Rejected, PendingRestart and Faulted observations remain distinct from stale/offline status. Zero observed recipients and outdated successful reports are not called converged. |
| RM09-AC15 | backoffice | Management is unavailable while a valid session has a prior snapshot | Management is unavailable while a valid session has a prior snapshot. The portal dates and marks the snapshot, disables new changes and queues nothing; reconnect refreshes state before a fresh explicit submission. |
| RM09-AC16 | backoffice | Unauthorized sensitive fields are absent from browser responses, rendered diffs, logs and error messages | Unauthorized sensitive fields are absent from browser responses, rendered diffs, logs and error messages. A redaction marker cannot be submitted as a preserved hidden value. |
| RM09-AC17 | backoffice | Large Int64 strings, exact ETags, prefixed URLs, unknown fields/statuses and response limits retain existing cross-language behavior through the UI and Next.js integration | Large Int64 strings, exact ETags, prefixed URLs, unknown fields/statuses and response limits retain existing cross-language behavior through the UI and Next.js integration. |
| RM09-AC18 | backoffice | Keyboard-only and narrow/zoomed principal journeys preserve labels, target visibility, focus, error recovery and result access | Keyboard-only and narrow/zoomed principal journeys preserve labels, target visibility, focus, error recovery and result access. Representative rendered screens match the approved design. |
| RM09-AC19 | backoffice | Polling, pagination, caches, drafts and operation receipts remain bounded | Polling, pagination, caches, drafts and operation receipts remain bounded. Request cancellation and portal restart do not claim to undo an already committed backend operation. |
| RM09-AC20 | backoffice | The portal runs against real delivered management and RM-08 APIs for final acceptance | The portal runs against real delivered management and RM-08 APIs for final acceptance. Mock-only configuration screens or an inventory-only increment cannot close full RM-09. |

## UX scope

All portal screens are backoffice. One coherent design covers application inspection, metadata confirmation and original-key recovery, exact configuration publication, release creation, whole-map assignment/promotion/rollback, mixed adoption and session/outage states. Include desktop and narrow layouts, keyboard focus, visible environment/target, synthetic demo labels and API prerequisites. Production UI depends on the exact approved wireframe and rendered design revision.

## Backend and client prerequisites

Reuse the implemented RM07/RM08 authority, native provider, typed clients and original operation/Audit paths. Add bounded application search and authorized environment discovery, independently validated human/delegated identity, narrowly scoped configuration write/recovery eligibility and trusted configured-cohort discovery. Preserve omitted-query compatibility and query/scope-bound cursors. Known-release lookup is sufficient for initial rollback; add no global history or general permission framework.

## Implementation and verification boundaries

Deliver Next.js as a separate application. Keep React/session/UI dependencies out of portable TypeScript clients and Core. Each implementation task owns its focused tests, documentation and independent review; principal real-service/browser journeys and deployment guidance are a concrete integration deliverable. Preserve all twenty source scenarios, exact payload and Int64/ETag semantics, resource bounds, actual authorization and unknown outcomes. Live Entra deployment proof requires actual configured infrastructure and remains separately identified from local validation.

## Non-goals

No portal database copying fleet state, UI schema engine, arbitrary commands, restart controls, automatic rollout, audit archive, global operation history, new DI/testing framework or broad qualification campaign. Design generation uses synthetic data and performs no real mutations.
