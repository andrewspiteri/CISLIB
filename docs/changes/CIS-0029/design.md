---
title: "CIS-0029 design approval"
type: design-approval
status: ReadyForReview
change_id: CIS-0029
approval_status: ReadyForReview
gate_status: PausedForReview
authority: human-reviewed
---

# Design approval

**Pending Approval.** This is an initial review record, not an approval or a
completed render. The supported CIS draft scaffold/render path will bind the
textual wireframes, governed guidelines/UI direction, renderer and generated PNG
manifest before the user sees one coherent package. No renderer, PNG, verified
hash or reviewer decision is claimed here. Existing product-wide no-UI authority
is unchanged; the human review must explicitly cover the RM09 backoffice portal
direction and exact design. Production UI and task import remain blocked.

For UI-bearing work, a completed renderer and PNG pack set `gate_status` to
`PausedForReview`. Explicit approval is retained through the existing CIS
checkpoint; rejected or materially changed flows require revised review. This
Draft/Inactive record does not set or bypass that gate. Routine accessibility,
spacing/text corrections preserving approved behavior need no repeated approval.

## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0029/wireframes.md` | `sha256:91fbd5ef855326bb33f8129e55074e8af6ec7ab68e24f51bb1a7b11b0b1bbdee` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0029/assets/render-rm09-portal-screens.mjs` | `sha256:067570696e12a698fd9ec14fa3191bb95a166d3686c2438729d9bda3d025fcd6` | v24.15.0 | 0.35.5 | 8.18.7 |

High-level UI direction: `docs/design/ui-direction.md` at `sha256:25ef386c6a84bc747b1bd87ab3d7ebe67fdd3de2034c20e4ee8f6fefed8012cc`.
## Application shell and component templates

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |
| `shell.standard-app` | 1.0 | shell | CIS built-in | Dark application rail, top bar, route context, and governed content canvas. | Selected |
| `component.page-header` | 1.0 | component | CIS built-in | Consistent title, description, status, and primary action hierarchy. | Selected |
| `component.button` | 1.0 | component | CIS built-in | Primary, secondary, destructive, icon, disabled, loading, and focus-visible button states. | Selected |
| `component.text-input` | 1.0 | component | CIS built-in | Labeled text input with helper, placeholder, required, focus, disabled, and validation states. | Selected |
| `component.select` | 1.0 | component | CIS built-in | Labeled select/dropdown with placeholder, selected, open, disabled, and validation states. | Selected |
| `component.textarea` | 1.0 | component | CIS built-in | Multiline input with label, helper, character count, resize affordance, and validation states. | Selected |
| `component.checkbox` | 1.0 | component | CIS built-in | Checkbox with checked, unchecked, indeterminate, focus, disabled, and error states. | Selected |
| `component.tabs` | 1.0 | component | CIS built-in | Route-aware tabs with active, hover, focus, disabled, and overflow behavior. | Selected |
| `component.breadcrumbs` | 1.0 | component | CIS built-in | Consistent hierarchical path, current-page semantics, and truncation behavior. | Selected |
| `component.pagination` | 1.0 | component | CIS built-in | Result range, page navigation, page size, disabled boundaries, and loading state. | Selected |
| `component.dialog` | 1.0 | component | CIS built-in | Modal confirmation/form shell with title, description, focus boundary, actions, and destructive variant. | Selected |
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
| `component.form` | 1.0 | component | CIS built-in | Labeled fields, helper text, validation, and action row. | Selected |
| `component.empty-state` | 1.0 | component | CIS built-in | Consistent empty, denied, error, and recovery-state composition. | Selected |
| `component.timeline` | 1.0 | component | CIS built-in | Audit/activity timeline with status-aware markers and metadata. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `RM09-SIGNIN`, `RM09-APPLICATIONS`, `RM09-APPLICATION`, `RM09-METADATA`, `RM09-OPERATION`, `RM09-CONFIGURATION`, `RM09-ASSIGNMENT`, `RM09-ADOPTION` | `shell.standard-app` | RM09-SIGNIN:Render required, RM09-APPLICATIONS:Render required, RM09-APPLICATION:Render required, RM09-METADATA:Render required, RM09-OPERATION:Render required, RM09-CONFIGURATION:Render required, RM09-ASSIGNMENT:Render required, RM09-ADOPTION:Render required |
## Principal review journeys

1. **Inspect/edit/recover:** Applications -> detail/instances/inventory -> metadata
   edit -> exact review -> original receipt -> uncertain lookup -> recovered
   result/current state. Concurrent edit branches into current-versus-draft review.
2. **Publish/release/assign:** Source directory -> exact JSON validation -> source
   publication review/result -> immutable pair preview -> inactive release ->
   complete-map canary review -> assignment receipt -> mixed adoption.
3. **Observe/promote/roll back:** Mixed adoption -> explicit canary retention or
   removal -> map review/result -> known earlier release lookup -> rollback map
   review -> new generation/adoption. Outage and session loss remain part of this
   journey, with distinct data-retention and recovery behavior.

The screen set must visibly label step numbers and forward/back destinations so
the three journeys can be followed without a gallery of unrelated mockups.
Within each template, rendered panels/state variants demonstrate relevant steps.

## Capability prerequisites and limits

Current baseline delivers RM07 inspection/metadata recovery and nineteen RM08
configuration APIs. Their presence is not deployed identity/permission evidence.
The screen/action map in wireframes.md and reconciliation.md is authoritative for
what each proposed interaction needs. Clearly label these proposed prerequisites:

- **P1:** production Entra human/delegated session plus independently validating
  native Management identity integration; workload Entra and portal login alone
  cannot grant an operator management authority.
- **P2:** bounded application identity/display-name/environment server query and
  authorized environment enumeration; current paging cannot perform global search.
- **P3:** complete scoped configuration read/write/recovery eligibility from the
  reference grant bundle or small source/binding hints; implemented feature
  discovery and foundation caller hints alone are insufficient.
- **P4:** bounded trusted configured-cohort discovery for an absent/offline canary;
  current pins/observed recipients do not enumerate all configured cohorts.

No release-history API is invented: rollback opens an exact known release via
`getRelease`. Definitions are explicitly paged global authorized discovery, not a
complete application-filtered list. Update mode is per reported instance, not a
definition field. Preview returns paths/kinds/redaction/issues/recipient metadata,
not value diffs or deployed typed acceptance. Separate authorized local source
text review from that preview. No admission projection, restart, job/lease control,
global operation history, automatic rollout or Audit archive is offered.

## Required states

These unchecked items are review/implementation obligations, not completed tests.

- [ ] Primary flows for all eight backoffice screen templates.
- [ ] Distinct no-authorized-data, no-results, no-instance and no-history states.
- [ ] Loading/pending browser waits without invented queued/running backend lifecycle.
- [ ] Validation, precondition conflict and revised explicit review.
- [ ] Uncertain original result, NotFound unresolved, recovered commitment and separate cleanup warning.
- [ ] Permission denied, expired session, user switch and revoked result lookup.
- [ ] Valid-session stale snapshot/outage, disabled changes and authority/state refresh on reconnect.
- [ ] Redacted content/defaults and unavailable unsafe hidden-value editing.
- [ ] Pending, Applied, AppliedUnchanged, Rejected, PendingRestart, Faulted; independent stale/offline/noInstances and unknown future status.
- [ ] Complete promotion/rollback map with retained/removed canary and unrelated retained overrides.
- [ ] Desktop, approximately 390 px narrow layout and zoom/reflow without whole-page horizontal scrolling.
- [ ] Keyboard/focus/error association, status announcements, non-color cues and dialog focus return.

## Planned render coverage

This is a proposed evidence plan, not the generated PNG manifest. Cover every
screen ID; reuse templates for states rather than expanding the screen inventory.

| Screen ID | Representative rendered content | Journey relationship |
| --- | --- | --- |
| RM09-SIGNIN | Reference sign-in plus expired-session/error variant | Entry and session-loss branch; no protected content. |
| RM09-APPLICATIONS | Server-query proposal and populated authorized page; unavailable/no-results variant | Journey 1 begins, with P2 prerequisite shown. |
| RM09-APPLICATION | Identity, instance evidence and reported inventory | Journey 1 inspection context; no authority/control invention. |
| RM09-METADATA | Exact before/after confirmation and conflict/current-versus-draft variant | Journey 1 deliberate change. |
| RM09-OPERATION | Unresolved original receipt and recovered committed result with cleanup warning | Journey 1 recovery; reused by publication/release/assignment results. |
| RM09-CONFIGURATION | Exact source validation/publication review and immutable pair/release review | Journey 2 distinct publish/create/activate facts, P3 identified. |
| RM09-ASSIGNMENT | Current/proposed full map for canary, promotion and rollback | Journeys 2/3 preserve all affected and retained entries, P4 identified. |
| RM09-ADOPTION | Six-state mixed recipient view, stale/offline/no-recipient and known-release rollback candidate | Journey 3; history/freshness distinct and no fleet ratio. |

Representative narrow evidence must include exact metadata confirmation and
adoption/full-map review; every template's description also specifies narrow
behavior. Use a roughly 208 px rail on desktop and drawer/single-column forms on
narrow layouts. Keep environment/target above change actions, wrap IDs, and allow
only local labeled scrolling for dense tables/editor content. Rendered focus cues
express intended behavior; actual keyboard/accessibility needs later verification.

## Synthetic data and exact-value policy

All plates say **DEMONSTRATION - synthetic data**. Use the stable sample target,
source/release references and three connected journeys from wireframes.md. The
maintained CountrySettings shape provides realistic bounded content; six demo
recipient rows demonstrate the six known states. No actual grant, verified
identity, live health, current authority or full-fleet count is implied. Keep exact
JSON text, Int64 decimal strings, opaque ETags and seven-fraction UTC timestamps;
format a copy with visible CET timezone. No credentials or configuration payload
are persisted in receipts, no renderer fetches network data and no UI submits a
real administrative change during design review.

## Reused approved PNGs

No approved source PNG is claimed or reused in this draft. Exact reuse must be
registered and verified by CIS if later selected.

| Target screen ID | Frontend type | Source change | Source screen ID | State | Viewport | Path | Compatibility | Dimensions | SHA-256 | Source wireframe approval SHA-256 | Source wireframe content SHA-256 | Source renderer SHA-256 | Source manifest SHA-256 | Reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |

## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `rm09-adoption` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-adoption--primary--desktop.png` | 1600x1000 | `sha256:fc5276e3fb10b946ab1c60c56678a407aed83e6004379bae08b799dd8e235963` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-application` | `backoffice` | denied | desktop | `docs/changes/CIS-0029/assets/rm09-application--denied--desktop.png` | 1600x1000 | `sha256:c1718883941859b8ececd24ceefce2e2557d9dae4962455ab3b308ca1eb5ae02` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-application` | `backoffice` | outage | narrow | `docs/changes/CIS-0029/assets/rm09-application--outage--narrow.png` | 390x1840 | `sha256:394e20606cc9f6d0848f5155b2fb60d6e1e704f31d8d0c40a5c73b197aec7cd3` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-application` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-application--primary--desktop.png` | 1600x1000 | `sha256:c5332119b8f7601d97c6d273fba561a6fa7abea811886ec523fbd822edbe448a` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-applications` | `backoffice` | empty | desktop | `docs/changes/CIS-0029/assets/rm09-applications--empty--desktop.png` | 1600x1000 | `sha256:53aeeb373d9e5d4385fbd7a3fc0809fe403e1dc22267e18f5fd303a60192ca40` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-applications` | `backoffice` | loading | desktop | `docs/changes/CIS-0029/assets/rm09-applications--loading--desktop.png` | 1600x1000 | `sha256:d4c71ee73d571d68997ef370517962f41301d05ae7a352b03f84121330f0a35b` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-applications` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-applications--primary--desktop.png` | 1600x1000 | `sha256:aadf4e03bf280b0a7de4d01cda51cb77f77848c67592686faf854c28714e5052` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-assignment` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-assignment--primary--desktop.png` | 1600x1000 | `sha256:c3d0b88a961d23cad300ecdac9616e3d69800fbbb56742569441023d5389fc19` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-assignment` | `backoffice` | promote | desktop | `docs/changes/CIS-0029/assets/rm09-assignment--promote--desktop.png` | 1600x1000 | `sha256:c44e56a90e34b95ae4000b99541f1956719b5bafd8ee9d21f9055d77c91169c6` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-assignment` | `backoffice` | rollback | desktop | `docs/changes/CIS-0029/assets/rm09-assignment--rollback--desktop.png` | 1600x1000 | `sha256:5dc8a626f1793d525165a239ef54fface42a73c3b39b379245316670a03ffe69` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-assignment` | `backoffice` | rollback | narrow | `docs/changes/CIS-0029/assets/rm09-assignment--rollback--narrow.png` | 390x1840 | `sha256:6728363cf24776a02f5c00827b09744695a3122743fa7a31f5d23550484d828c` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-configuration--primary--desktop.png` | 1600x1000 | `sha256:2d52d65f03c6161e5a5dddbbd67397c7f194d19cdfa2b2497cc109da8062c0f0` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration` | `backoffice` | release-review | desktop | `docs/changes/CIS-0029/assets/rm09-configuration--release-review--desktop.png` | 1600x1000 | `sha256:852705eb23016b57c67c818f6d65b1334a645dce20a86ba243b243aef6435f94` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration` | `backoffice` | validation | desktop | `docs/changes/CIS-0029/assets/rm09-configuration--validation--desktop.png` | 1600x1000 | `sha256:a01124078b2d20447b8473301e1fd9a0686ff35eff5602c4c4747a03fd7d7b6d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-metadata` | `backoffice` | conflict | desktop | `docs/changes/CIS-0029/assets/rm09-metadata--conflict--desktop.png` | 1600x1000 | `sha256:b7cd3314903ab20c083311937eba606c5e55f99ef545a849c0d521e7ece9e40b` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-metadata` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-metadata--primary--desktop.png` | 1600x1000 | `sha256:99835842798ef20610438fa5376804a69c978ea2a44accdb0d222f16ce839d0f` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-operation` | `backoffice` | recovered | desktop | `docs/changes/CIS-0029/assets/rm09-operation--recovered--desktop.png` | 1600x1000 | `sha256:590d7671664a21554b77316820612d1cf842ddcb5cefa5b1c651ac2ca9d0456c` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-operation` | `backoffice` | uncertain | desktop | `docs/changes/CIS-0029/assets/rm09-operation--uncertain--desktop.png` | 1600x1000 | `sha256:f7909013a02d5892b5b50d7ab1d097c484aaf08013c322a391fdb42ee80fa90f` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-signin` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-signin--primary--desktop.png` | 1600x1000 | `sha256:f0e5cddc2da5c063d053e1f8caef56e223b5bec32adde6555402a47af9cd975a` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-signin` | `backoffice` | session-expired | desktop | `docs/changes/CIS-0029/assets/rm09-signin--session-expired--desktop.png` | 1600x1000 | `sha256:1ff755b75148bc2f1a33427c0bc4c0f6f6bcd177d4bc52427143f63598279ccb` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |
| Governed UI direction/guidelines | Supported scaffold must bind exact source digests; existing direction record unchanged by this draft | Awaiting scaffold and human review of RM09 direction | None granted. |
| Visible environment/target and synthetic marker | Every protected shell and every change review | Specified; rendering unverified | None granted. |
| WCAG 2.2 AA principal journeys | Contrast, focus, labels, reflow, non-color status and persistent feedback | Intent specified; rendering/behavior unverified | None granted. |
| Exact values, redaction and truthful outcomes | Separate source preview, immutable release, whole map, adoption freshness and original receipt | Specified against delivered contracts/prerequisites | None granted. |

## Rejected revisions

No review decision or rejected PNG is recorded. Rejected files must be removed
while their manifest hashes and review findings remain in CIS history.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Pending Approval | No human decision recorded | Not decided | Not sealed | Not generated | Not generated | Draft only. Present one exact rendered package covering RM09 backoffice UI direction, all three journeys and P1-P4. Explicit human approval is required before production UI/task import; this record changes no authority. |
