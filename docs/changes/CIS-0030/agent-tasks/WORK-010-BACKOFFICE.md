---
title: "WORK-010-BACKOFFICE Define textual screens, states, actions, and paths (backoffice)"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010-BACKOFFICE
task_type: core.design.wireframe
task_type_version: 1.1
parent_id: none
category: wireframe
complexity: medium
feature_spec_path: "docs/changes/CIS-0030/feature-specification.md"
feature_spec_sha256: "sha256:71b0c63927fe329198cbe964374a5a8420839b0a60df787723923ba1f20b3e6a"
requirement_ids: ["RM09-AC01","RM09-AC02","RM09-AC03","RM09-AC04","RM09-AC05","RM09-AC06","RM09-AC07","RM09-AC08","RM09-AC09","RM09-AC10","RM09-AC11","RM09-AC12","RM09-AC13","RM09-AC14","RM09-AC15","RM09-AC16","RM09-AC17","RM09-AC18","RM09-AC19","RM09-AC20","RM10-AC26","RM10-AC27","RM10-AC28","RM10-AC29","RM10-AC30","RM10-AC35","RM10-AC36","RM10-AC40"]
impact_ids: []
depends_on: []
approval_gate: none
frontend_type: backoffice
story_id: null
instance_key: "core.design.wireframe|backoffice|shared"
work_owner_task_id: null
targets: ["cislib"]
decision_ids: []
verification_obligations: [{"Id":"task-contract","Criteria":["Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid."],"Description":"Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.","EvidenceRoute":"review","EvidenceExpectations":["Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested."],"SharedGateIds":[],"BasisReferences":["RM09-AC01","RM09-AC02","RM09-AC03","RM09-AC04","RM09-AC05","RM09-AC06","RM09-AC07","RM09-AC08","RM09-AC09","RM09-AC10","RM09-AC11","RM09-AC12","RM09-AC13","RM09-AC14","RM09-AC15","RM09-AC16","RM09-AC17","RM09-AC18","RM09-AC19","RM09-AC20","RM10-AC26","RM10-AC27","RM10-AC28","RM10-AC29","RM10-AC30","RM10-AC35","RM10-AC36","RM10-AC40"]}]
imported_required_outputs: ["wireframes.md","Validated screen/action/path coverage","Exact digest bound to design review"]
imported_negative_criteria: ["Do not expand scope beyond accepted impacts and the imported feature specification."]
imported_verification_obligations: [{"Id":"task-contract","Criteria":["Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid."],"Description":"Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.","EvidenceRoute":"review","EvidenceExpectations":["Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested."],"SharedGateIds":[],"BasisReferences":["RM09-AC01","RM09-AC02","RM09-AC03","RM09-AC04","RM09-AC05","RM09-AC06","RM09-AC07","RM09-AC08","RM09-AC09","RM09-AC10","RM09-AC11","RM09-AC12","RM09-AC13","RM09-AC14","RM09-AC15","RM09-AC16","RM09-AC17","RM09-AC18","RM09-AC19","RM09-AC20","RM10-AC26","RM10-AC27","RM10-AC28","RM10-AC29","RM10-AC30","RM10-AC35","RM10-AC36","RM10-AC40"]}]
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-010-BACKOFFICE: Define textual screens, states, actions, and paths (backoffice)

## Objective

Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.
This task is bounded to the `backoffice` frontend surface.

## Required changes

- `RM09-AC01`: CIS produces a visible, versioned design package with complete inspection/change journeys and API mappings
- `RM09-AC02`: An approval survives restart for the unchanged design
- `RM09-AC03`: Production human sign-in reaches management through independently validated human/delegated identity
- `RM09-AC04`: Altering an environment, application ID, cursor, operation reference or URL cannot disclose or change an unauthorized resource
- `RM09-AC05`: Missing or incompatible capability versions are unavailable truthfully
- `RM09-AC06`: Cross-page application filtering uses the documented server query
- `RM09-AC07`: Reported identity, instance presence, historical health and resource-owner observations retain their source/time distinctions
- `RM09-AC08`: Metadata editing affects only supported display fields
- `RM09-AC09`: A mutation commits but its response is lost, then the page reloads
- `RM09-AC10`: Session expiry, sign-out, user switching or grant loss blocks pending writes and clears affected protected data
- `RM09-AC11`: Configuration validation/preview has no publication effect
- `RM09-AC12`: A state or permission change after preview is checked at commit
- `RM09-AC13`: Canary promotion and rollback show every affected default/cohort entry
- `RM09-AC14`: Mixed Applied, AppliedUnchanged, Pending, Rejected, PendingRestart and Faulted observations remain distinct from stale/offline status
- `RM09-AC15`: Management is unavailable while a valid session has a prior snapshot
- `RM09-AC16`: Unauthorized sensitive fields are absent from browser responses, rendered diffs, logs and error messages
- `RM09-AC17`: Large Int64 strings, exact ETags, prefixed URLs, unknown fields/statuses and response limits retain existing cross-language behavior through the UI and Next.js integration
- `RM09-AC18`: Keyboard-only and narrow/zoomed principal journeys preserve labels, target visibility, focus, error recovery and result access
- `RM09-AC19`: Polling, pagination, caches, drafts and operation receipts remain bounded
- `RM09-AC20`: The portal runs against real delivered management and RM-08 APIs for final acceptance
- `RM10-AC26`: In the Scheduled Task editor, change each task-level selector and observe its corresponding tab, owner, field schema and mappings update
- `RM10-AC27`: Render owner-described fields and a calendar editor using trusted descriptors
- `RM10-AC28`: Publish a draft affecting multiple owners and inject a partial failure, uncertain response and concurrent edit
- `RM10-AC29`: Show the default inherited execution identity, reject unsupported alternatives/delegation and revalidate identity after job selection changes
- `RM10-AC30`: Open separate task History and run-attempt details without adding a third contextual configuration tab
- `RM10-AC35`: Reconcile the actual checkout and component ownership, import this specification through supported CIS traceability, and deliver the required adapters, clients, consumer guidance and migration instructions
- `RM10-AC36`: Capture actual application/build/handler metadata before an attempt can enter business execution
- `RM10-AC40`: Activate a newer job execution contract while old runs remain queued or unresolved

## Required outputs

- wireframes.md
- Validated screen/action/path coverage
- Exact digest bound to design review

## Structured test-obligation matrix

| Obligation | Evidence route | Accepted basis and criteria | Required evidence |
|---|---|---|---|
| task-contract | review | RM09-AC01; RM09-AC02; RM09-AC03; RM09-AC04; RM09-AC05; RM09-AC06; RM09-AC07; RM09-AC08; RM09-AC09; RM09-AC10; RM09-AC11; RM09-AC12; RM09-AC13; RM09-AC14; RM09-AC15; RM09-AC16; RM09-AC17; RM09-AC18; RM09-AC19; RM09-AC20; RM10-AC26; RM10-AC27; RM10-AC28; RM10-AC29; RM10-AC30; RM10-AC35; RM10-AC36; RM10-AC40; Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid. | Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.; Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested. |

These rows are the persisted task verification obligations, not an additional keyword-selected gate set. Applicable adopted completion policy remains binding at its declared task, feature or release boundary. Failed, missing, stale and skipped required evidence block completion; justified inapplicability is not a pass. A release obligation does not itself require a new release campaign for each story.

## Constraints and exclusions

- Do not expand scope beyond accepted impacts and the imported feature specification.

## Context and evidence

- Canonical feature specification: `docs/changes/CIS-0030/feature-specification.md` (`sha256:71b0c63927fe329198cbe964374a5a8420839b0a60df787723923ba1f20b3e6a`).
- Governed reference: `planning/management/portal/sources/CISLIB_RM09_Management_Portal_Specification_v1.0.md`.
- Governed reference: `planning/scheduler/sources/CISLIB_RM10_Application_Scheduler_and_Triggers_Specification_v1.1.md`.

## Dependencies and approval gates

- No task dependencies.

## Acceptance criteria

For this core.design.wireframe task, the imported scenario checkboxes below record textual screen/state/action/path coverage. They are not runtime acceptance results. Full RM09/RM10 behavior, actual native execution and screenshot approval remain with the downstream owners and the change-level acceptance matrix. Exact R07 visual authority will be recorded by WORK-020-BACKOFFICE through the default combined design review.

- [x] Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid.
- [x] `RM09-AC01`: CIS produces a visible, versioned design package with complete inspection/change journeys and API mappings. An unapproved or rejected design blocks production UI tasks.
- [x] `RM09-AC02`: An approval survives restart for the unchanged design. A material changed workflow requires approval of that revision; routine presentation fixes do not prompt again.
- [x] `RM09-AC03`: Production human sign-in reaches management through independently validated human/delegated identity. An operator without a grant cannot borrow the portal application's broader privilege. Development identity is rejected by production configuration.
- [x] `RM09-AC04`: Altering an environment, application ID, cursor, operation reference or URL cannot disclose or change an unauthorized resource. Direct server-action/API calls receive the same protection as the visible UI.
- [x] `RM09-AC05`: Missing or incompatible capability versions are unavailable truthfully. Unknown response states never default to success, and opaque inventory detail is rendered safely.
- [x] `RM09-AC06`: Cross-page application filtering uses the documented server query. Cursors bind the query/scope; changing filters resets them. No loaded page is advertised as a fleet-wide count or complete environment list.
- [x] `RM09-AC07`: Reported identity, instance presence, historical health and resource-owner observations retain their source/time distinctions. The portal neither calls another workload's self-control API nor grants/releases execution authority.
- [x] `RM09-AC08`: Metadata editing affects only supported display fields. Concurrent edits preserve the strong ETag contract; the losing edit is reviewed against current state rather than silently overwriting it.
- [x] `RM09-AC09`: A mutation commits but its response is lost, then the page reloads. Recovery uses the original key and validated actor/target, performs one effect and one required audit, and preserves committed/rejected/unknown and cleanup distinctions.
- [x] `RM09-AC10`: Session expiry, sign-out, user switching or grant loss blocks pending writes and clears affected protected data. A revoked lookup cannot reveal the result or cause a replacement mutation; only a same-user minimal receipt may remain. Late responses cannot repopulate the previous user's or environment's view.
- [x] `RM09-AC11`: Configuration validation/preview has no publication effect. Publication preserves the reviewed exact payload, creates immutable content, and release creation pins exact authorized sources without assigning them automatically.
- [x] `RM09-AC12`: A state or permission change after preview is checked at commit. A conflict preserves a permitted draft for revised review and does not resubmit automatically.
- [x] `RM09-AC13`: Canary promotion and rollback show every affected default/cohort entry. Promotion handles the override explicitly; rollback advances assignment generation and retains immutable publication history.
- [x] `RM09-AC14`: Mixed Applied, AppliedUnchanged, Pending, Rejected, PendingRestart and Faulted observations remain distinct from stale/offline status. Zero observed recipients and outdated successful reports are not called converged.
- [x] `RM09-AC15`: Management is unavailable while a valid session has a prior snapshot. The portal dates and marks the snapshot, disables new changes and queues nothing; reconnect refreshes state before a fresh explicit submission.
- [x] `RM09-AC16`: Unauthorized sensitive fields are absent from browser responses, rendered diffs, logs and error messages. A redaction marker cannot be submitted as a preserved hidden value.
- [x] `RM09-AC17`: Large Int64 strings, exact ETags, prefixed URLs, unknown fields/statuses and response limits retain existing cross-language behavior through the UI and Next.js integration.
- [x] `RM09-AC18`: Keyboard-only and narrow/zoomed principal journeys preserve labels, target visibility, focus, error recovery and result access. Representative rendered screens match the approved design.
- [x] `RM09-AC19`: Polling, pagination, caches, drafts and operation receipts remain bounded. Request cancellation and portal restart do not claim to undo an already committed backend operation.
- [x] `RM09-AC20`: The portal runs against real delivered management and RM-08 APIs for final acceptance. Mock-only configuration screens or an inventory-only increment cannot close full RM-09.
- [x] `RM10-AC26`: In the Scheduled Task editor, change each task-level selector and observe its corresponding tab, owner, field schema and mappings update. Keep exactly two contextual tabs, preserve per-definition drafts, flag incompatible mappings and preview the whole task from either tab. Source requirements: UI07–11, UI23.
- [x] `RM10-AC27`: Render owner-described fields and a calendar editor using trusted descriptors. Conflicting schema versions, unsupported mapper/editor identifiers, executable form content and untrusted lookup targets cannot activate invalid configuration. Source requirements: UI01–06, UI17.
- [x] `RM10-AC28`: Publish a draft affecting multiple owners and inject a partial failure, uncertain response and concurrent edit. Recover original successful operations, retain the draft on conflict and show pending/active/adopted states accurately. Shared edits revalidate all affected tasks, including tasks outside the current editor, without overwriting newer edits. New event admission sees a coherent compatible active revision; existing runs retain their captured settings. Source requirements: UI12–18.
- [x] `RM10-AC29`: Show the default inherited execution identity, reject unsupported alternatives/delegation and revalidate identity after job selection changes. Remapping a profile alias cannot change an existing run's captured identity, while credential rotation for the same identity remains supported. Policy publication alone does not pretend that an unrestarted instance has new grants or a new principal. Source requirements: ID05–08, UI11, UI16.
- [x] `RM10-AC30`: Open separate task History and run-attempt details without adding a third contextual configuration tab. Show captured requester/producer/executor identities and actual outcome/freshness, with access-sensitive content protected. Source requirements: UI11, HIST14, HIST18.
- [x] `RM10-AC35`: Reconcile the actual checkout and component ownership, import this specification through supported CIS traceability, and deliver the required adapters, clients, consumer guidance and migration instructions. Record focused acceptance evidence against the qualified commit and disclose material gaps. Preserve the accepted editor; obtain existing RM09 approval only for uncovered visual decisions. Source requirements: UI22, UI24, DEL01–05.
- [x] `RM10-AC36`: Capture actual application/build/handler metadata before an attempt can enter business execution. A forged event version cannot substitute it, a later catalog update cannot rewrite it, and a new build recovering an old result preserves the original effect attribution. History distinguishes mixed-version attempts and explicitly unknown provenance. Source requirements: VER01–03, VER09, HIST04, HIST07.
- [x] `RM10-AC40`: Activate a newer job execution contract while old runs remain queued or unresolved. New runs capture the new active contract; old runs retain theirs. When supporting workers disappear, show a bounded version-related waiting reason without consuming retries or changing run state to a fabricated failure. The Job and History views remain within the accepted layout. Source requirements: VER03–04, VER09, UI14–15.

## Targeted validation

- [x] Primary and alternate flows are represented.
- [x] Empty, loading, failure, denied, responsive, and lifecycle states are covered.
- [x] Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.

## Acceptance verification

- `task-contract` (review): Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested. Criteria: Every affected screen, state, action, condition, side effect, and destination path is explicit and structurally valid. Evidence: Validate screen/action identity, path resolution, state and requirement coverage; exact human authority is recorded with design approval unless an earlier checkpoint is requested.

## Completion evidence

| Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- |
| Textual screen/action/path validation | `.cis/local/configuration-publication-intake/rm09c-wireframes-validate-r07.json` | Passed | Native status wireframes-valid; exact wireframe SHA cab06d2644f958a5dafc01050e1e12e6424245bb1e775d80aa56668fe2538d7e. |
| Independent source/design review | `.cis/local/testing/rm09-completion/wireframe-review-final.md` | Resolved | Publication-history and identity-attribution findings resolved; original snapshot findings retained separately. |
| Coverage boundary | `wireframes.md`, Screen inventory, States and Coverage | Textual coverage checked | All checked imported scenarios mean design coverage only. No production functionality or browser/provider pass is claimed by this task. |
| Exact visual approval | `WORK-020-BACKOFFICE` | Pending, next task | Default combined screenshot review records exact wireframe/renderer/PNG digests; no earlier separate textual approval requested. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2816; failed=365; possibleTokenSavings=591; ledgerDigest=sha256:8be31f6342fd0527f6aca1f9cf22badf26739df6c9dce7f9684621a50c7edc1f |

## Deferrals and residual risk

- None. Any deferral must identify the unmet criterion, reason, owner, follow-up, and approval.

## External issue links

| Provider | Remote ID | URL | Canonical digest | Remote digest | Last synchronized UTC | State |
|---|---|---|---|---|---|---|

## External synchronization decisions

| Provider | Decision | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|

