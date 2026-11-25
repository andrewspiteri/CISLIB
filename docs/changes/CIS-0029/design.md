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

**R04 is approved; the additive R05 item-details side panel awaits review.**
Andrew Spiteri approved the Scheduled jobs view and requested item selection to
open details, suggesting a side panel. The exact native R04 checkpoint is
preserved in Git commit `ed65c3f1`, its event and the approval row below.
Earlier approval is not rejected or extended to the unseen interaction.

R05 adds one read-only right-side modal panel to the four application-detail
lists. It preserves Applications navigation, four tabs/default Instances and
all data boundaries. No panel-specific API, content/default disclosure, write,
scheduler or execution control is added. Normal scope/plan reconciliation still
precedes production work.
## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0029/wireframes.md` | `sha256:d55d6857e738aa933e4e6c958f6988946c8d76e4a72d8da48dcade8934253ff6` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0029/assets/render-rm09-item-details-r05-screens.mjs` | `sha256:c7bdad057ddab20d8a547386f6ad9b3b26ceeb1a4057ac0e899707487b7c41e5` | v24.15.0 | 0.35.5 | 8.18.7 |

High-level UI direction: `docs/design/ui-direction.md` at `sha256:25ef386c6a84bc747b1bd87ab3d7ebe67fdd3de2034c20e4ee8f6fefed8012cc`.
## Application shell and component templates

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |
| `shell.standard-app` | 1.0 | shell | CIS built-in | Dark application rail, top bar, route context, and governed content canvas. | Selected |
| `component.page-header` | 1.0 | component | CIS built-in | Consistent title, description, status, and primary action hierarchy. | Selected |
| `component.button` | 1.0 | component | CIS built-in | Primary, secondary, destructive, icon, disabled, loading, and focus-visible button states. | Selected |
| `component.text-input` | 1.0 | component | CIS built-in | Labeled text input with helper, placeholder, required, focus, disabled, and validation states. | Selected |
| `component.select` | 1.0 | component | CIS built-in | Labeled select/dropdown with placeholder, selected, open, disabled, and validation states. | Selected |
| `component.tabs` | 1.0 | component | CIS built-in | Route-aware tabs with active, hover, focus, disabled, and overflow behavior. | Selected |
| `component.breadcrumbs` | 1.0 | component | CIS built-in | Consistent hierarchical path, current-page semantics, and truncation behavior. | Selected |
| `component.pagination` | 1.0 | component | CIS built-in | Result range, page navigation, page size, disabled boundaries, and loading state. | Selected |
| `component.dialog` | 1.0 | component | CIS built-in | Modal confirmation/form shell with title, description, focus boundary, actions, and destructive variant. | Selected |
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.filter-bar` | 1.0 | component | CIS built-in | Search, filters, applied-filter chips, and result count. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
| `component.empty-state` | 1.0 | component | CIS built-in | Consistent empty, denied, error, and recovery-state composition. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `RM09-APPLICATIONS`, `RM09-INSTANCES`, `RM09-CONFIGURATION-LIST`, `RM09-CAPABILITIES`, `RM09-SCHEDULED-JOBS` | `shell.standard-app` | RM09-APPLICATIONS:Render required, RM09-INSTANCES:Render required, RM09-CONFIGURATION-LIST:Render required, RM09-CAPABILITIES:Render required, RM09-SCHEDULED-JOBS:Render required |
## Principal review journeys

1. Filter applications and open one using the accepted R02 list.
2. Inspect per-instance reported versions on the default **Instances** tab.
3. Select **Configuration** to inspect that application's definition metadata,
   or **Capabilities** to inspect Management APIs and advisory caller hints.
4. Return to Instances without losing the exact application/environment, or
   Back to the preserved application list filters, page and focus.

This revision adds read-only list navigation. No configuration editing,
publication, assignment, metadata editing or operation recovery flow is added.

## Capability prerequisites and limits

The existing required `Instance.applicationVersion` remains the per-registration
reported version. Optional Last seen says Not reported when absent. Display
known presence values as Active/Stale/Closed without inferring live health.

The existing `getCapabilities` supplies implemented Management APIs and advisory
caller hints. Configuration features currently have no caller hints; absence
means Not evaluated. These API records are distinct from reported module/Job
inventory. Native authorization remains required for actual requests.

The Configuration tab lists only application-kind definition metadata scoped to
the exact selected application/environment. No payload/default values are
exposed. A bounded application/environment definition query is needed for a
complete scoped list: existing `getDefinitions` is globally paged. Arbitrary
common definitions cannot be associated with the selected app.

P2 application search/authorized environments and production human/native
resource authority remain prerequisites. Do not substitute loaded-page filters
or claim production integration based on these synthetic previews.

## Required states

These are review/implementation obligations, not completed functional tests.

- [ ] Accepted application filters, selection and Back context remain intact.
- [ ] Four exact detail tabs preserve application/environment; Instances is default.
- [ ] Item activation opens one read-only side-panel pattern; exact row identity and focus/list context survive dismissal.
- [ ] Instance versions, optional last seen and raw presence meanings are preserved.
- [ ] Configuration list is exact-scope metadata without content/default disclosure.
- [ ] Capabilities distinguishes advisory true/false/absent hints and actual authority.
- [ ] Per-tab loading, empty, denied, stale and unavailable results remain distinct.
- [ ] Jobs distinguishes no inventory, no scheduled definitions and no observations; absent occurrence times remain Not reported.
- [ ] Target/query switches invalidate obsolete requests; access loss clears data.
- [ ] Desktop keyboard tabs, focus return, zoom and non-color status remain required.

## Planned render coverage

Thirteen desktop frames at 1600 × 1000. The main new preview is Scheduled jobs details;
the same side-panel pattern appears on Instances, Configuration and Capabilities.

| Screen ID | States | Review purpose |
| --- | --- | --- |
| RM09-APPLICATIONS | Populated, no matches, unavailable | Preserve accepted list/filter behavior. |
| RM09-INSTANCES | Populated, no retained observations, stale | Add tab row while preserving instance facts. |
| RM09-CONFIGURATION-LIST | Populated | Inspect selected application definition metadata. |
| RM09-CAPABILITIES | Populated | Inspect Management API discovery and caller hints. |
| RM09-SCHEDULED-JOBS | Populated, details panel | Inspect one registration inventory snapshot and exact job/resource observation. |

No mobile layouts or future-placeholder tabs are included. Static pictures do
not establish working tab selection, native service access or accessibility.

## Synthetic data and exact-value policy

All frames say **DEMONSTRATION — synthetic data**. Use the fixed sample snapshot
25 Nov 2026, 11:00 CET and the exact synthetic application/instance targets in
wireframes.md. The selected application is Country data service,
Demo.CountryData, Demo staging. Versions such as `2.4.2+build.18` and `2.4.1`
remain per-instance registration reports, not semantic-version rankings.

No real identity, grant, live health or full-fleet total is implied. Renderer
assets are local and no screenshot submits a real administrative action.

## Reused approved PNGs

No approved source PNG is claimed or reused in this draft. Exact reuse must be
registered and verified by CIS if later selected.

| Target screen ID | Frontend type | Source change | Source screen ID | State | Viewport | Path | Compatibility | Dimensions | SHA-256 | Source wireframe approval SHA-256 | Source wireframe content SHA-256 | Source renderer SHA-256 | Source manifest SHA-256 | Reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |

## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `rm09-applications` | `backoffice` | no-matches | desktop | `docs/changes/CIS-0029/assets/rm09-applications--no-matches--desktop.png` | 1600x1000 | `sha256:019d8d34a046b684193201d77756d9ab9519da78a3ac88220ba44fc7e3981fcf` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-applications` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-applications--primary--desktop.png` | 1600x1000 | `sha256:bfe9387fd1a425980832733d473a40bfcb0139d31c24ce3a0538c6c63ec37134` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-applications` | `backoffice` | unavailable | desktop | `docs/changes/CIS-0029/assets/rm09-applications--unavailable--desktop.png` | 1600x1000 | `sha256:d5c8c0625303b15351743d3e23753032e5917bbf6af7c0dec39b4a38bd6ea8f0` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-capabilities` | `backoffice` | details | desktop | `docs/changes/CIS-0029/assets/rm09-capabilities--details--desktop.png` | 1600x1000 | `sha256:ab97b2b27fbe290fc6ceee0072bd90e158447561b976d1a01ab0218f659d8f16` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-capabilities` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-capabilities--primary--desktop.png` | 1600x1000 | `sha256:527e544a3d170f158c7df0f87c1f71a6c853dab92bd9271d2d369aa29e3070f9` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration-list` | `backoffice` | details | desktop | `docs/changes/CIS-0029/assets/rm09-configuration-list--details--desktop.png` | 1600x1000 | `sha256:bbf127a6971dee00fbfbde691563c8b67fedb32f81ccfe8fa7d973b6eff62629` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration-list` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-configuration-list--primary--desktop.png` | 1600x1000 | `sha256:7a6289bfee17e58420c143996da259160f95ecb440dcea354da7ce13fbcf2e07` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | details | desktop | `docs/changes/CIS-0029/assets/rm09-instances--details--desktop.png` | 1600x1000 | `sha256:89a95d0e0d9dbaf8d7ca4b732fd81b1e0385e96c778bf463ec0f503f954fc1f2` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | empty | desktop | `docs/changes/CIS-0029/assets/rm09-instances--empty--desktop.png` | 1600x1000 | `sha256:8ee7dda00312619ca01a4b6ab379a3bc0e6dc35a4f78fe4485215623c0c026df` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-instances--primary--desktop.png` | 1600x1000 | `sha256:b4247169f4f12d6b83b75eda65a36a5e30268cddeefde5b2a70bca5952bd00b7` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | stale | desktop | `docs/changes/CIS-0029/assets/rm09-instances--stale--desktop.png` | 1600x1000 | `sha256:53e1205cbba315139c922c9833909824e8a0459ad321ae2596006d320c17b6f4` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-scheduled-jobs` | `backoffice` | details | desktop | `docs/changes/CIS-0029/assets/rm09-scheduled-jobs--details--desktop.png` | 1600x1000 | `sha256:08cd68e08743224c8b10c95982e98d13e619203cd0bac3e8567793a7cbce48ff` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-scheduled-jobs` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-scheduled-jobs--primary--desktop.png` | 1600x1000 | `sha256:5d029e6b023c217821ec708eb6e8e0f5f7f71021964daee745756e6b8ebeeb67` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |
| Governed UI direction/guidelines | Native R02 scaffold binds source digests; existing product direction remains unchanged | Scaffolded; exact visible review pending | None granted. |
| Visible environment/target and synthetic marker | Both desktop templates and all state variants | Specified; rendered review pending | None granted. |
| Desktop accessibility | Contrast, focus, labels, zoom, non-color status and persistent feedback | Intent specified; actual keyboard/accessibility behavior unverified | Mobile excluded by latest user instruction. |
| Exact source facts | Per-instance reported versions, raw presence, last-seen evidence and bounded pages | Specified against existing contracts; filtering needs P2 | None granted. |

## Rejected revisions

R01 and R02 requested revisions and their exact hashes are retained below;
rejected PNG files were removed by CIS. Local Git commit `d4ce9478` preserves
the unapproved earlier review package.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |
| `sha256:067570696e12a698fd9ec14fa3191bb95a166d3686c2438729d9bda3d025fcd6` | sha256:fc5276e3fb10b946ab1c60c56678a407aed83e6004379bae08b799dd8e235963, sha256:c1718883941859b8ececd24ceefce2e2557d9dae4962455ab3b308ca1eb5ae02, sha256:394e20606cc9f6d0848f5155b2fb60d6e1e704f31d8d0c40a5c73b197aec7cd3, sha256:c5332119b8f7601d97c6d273fba561a6fa7abea811886ec523fbd822edbe448a, sha256:53aeeb373d9e5d4385fbd7a3fc0809fe403e1dc22267e18f5fd303a60192ca40, sha256:d4c71ee73d571d68997ef370517962f41301d05ae7a352b03f84121330f0a35b, sha256:aadf4e03bf280b0a7de4d01cda51cb77f77848c67592686faf854c28714e5052, sha256:c3d0b88a961d23cad300ecdac9616e3d69800fbbb56742569441023d5389fc19, sha256:c44e56a90e34b95ae4000b99541f1956719b5bafd8ee9d21f9055d77c91169c6, sha256:5dc8a626f1793d525165a239ef54fface42a73c3b39b379245316670a03ffe69, sha256:6728363cf24776a02f5c00827b09744695a3122743fa7a31f5d23550484d828c, sha256:2d52d65f03c6161e5a5dddbbd67397c7f194d19cdfa2b2497cc109da8062c0f0, sha256:852705eb23016b57c67c818f6d65b1334a645dce20a86ba243b243aef6435f94, sha256:a01124078b2d20447b8473301e1fd9a0686ff35eff5602c4c4747a03fd7d7b6d, sha256:b7cd3314903ab20c083311937eba606c5e55f99ef545a849c0d521e7ece9e40b, sha256:99835842798ef20610438fa5376804a69c978ea2a44accdb0d222f16ce839d0f, sha256:590d7671664a21554b77316820612d1cf842ddcb5cefa5b1c651ac2ca9d0456c, sha256:f7909013a02d5892b5b50d7ab1d097c484aaf08013c322a391fdb42ee80fa90f, sha256:f0e5cddc2da5c063d053e1f8caef56e223b5bec32adde6555402a47af9cd975a, sha256:1ff755b75148bc2f1a33427c0bc4c0f6f6bcd177d4bc52427143f63598279ccb | Andrew Spiteri | 2026-11-25T13:04:15Z | Rejected during human design review | The user requested changes instead of approving R01: mobile is not required; proceed step by step with a filterable list of deployed applications, opening an application to show instances and their current reported application version. Limit R02 to this desktop inspection journey; defer broader portal workflows. |
| `sha256:5369b0a8c5cba6c8df8ead9678f6e7f50b7629bf35876fbe5601fbc628cfb496` | sha256:019d8d34a046b684193201d77756d9ab9519da78a3ac88220ba44fc7e3981fcf, sha256:bfe9387fd1a425980832733d473a40bfcb0139d31c24ce3a0538c6c63ec37134, sha256:d5c8c0625303b15351743d3e23753032e5917bbf6af7c0dec39b4a38bd6ea8f0, sha256:68c491bb7852c7a78e2d854cad223ad2dcd4fe9296ae9e956db27e40f16794f3, sha256:f4f99b6710e4eebf8d1f1783c44a77ddbd84b3e54f2d57df0e6e7d6acfccdfb1, sha256:3225b1c6d2c767b9108fe2eaa180d0e90985888e64e516f74a8144c3460bd0cb | Andrew Spiteri | 2026-11-25T13:22:03Z | Rejected during human design review | The user accepted the R02 applications/instance direction (this part is ok) and requested a material detail-navigation revision: tabs for Instances, Configuration list and Capabilities list. Preserve the accepted application list and instance data; revise only detail navigation and the two named read-only list tabs. This is partial acceptance plus requested changes, not approval of the entire unchanged R02 payload or future write workflows. Mobile remains excluded. |
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-11-25T14:14:23Z | `sha256:38af1540c843cb2abcf3e581d7d309a3694afc7dc32a67819bf9eb450e351b68` | `sha256:249016b8fde00257a25347205b69f5764052d8be501120f1dea565e12dbcf29d` | `sha256:1bbb3907ba5435564cd3a643e6a1fb68407722014644a5b8df5a36dbd7538358` | Andrew Spiteri approved the exact presented R04 Scheduled jobs image: ok it is good. clicking on an item needs to open the details maybe a side panel can open. This approves the current read-only Scheduled jobs view and four-tab navigation, preserving R03 approval. Item details in a side panel are an additive requested interaction; this decision does not pre-approve an unseen panel, configuration content/writes, runtime scheduling or broader workflows. |
