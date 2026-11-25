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

**R02 pending human approval.** Andrew Spiteri requested changes to R01:
exclude mobile and proceed step by step with a filterable applications list,
then show a selected application's instances and reported versions. R01 is
preserved in local Git commit `d4ce9478`; the native rejection history below
retains its exact hashes and rationale.

R02 reviews only two desktop inspection screens. Broader portal workflows are
deferred. The source specification and existing product no-UI direction remain
unchanged. This review does not approve production work. The CIS render command
will bind the exact R02 renderer and PNGs before the user is asked for a decision.
The required human checkpoint remains paused while review assets are revised.
## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0029/wireframes.md` | `sha256:f423833ca481701d21886592d67471c57b87b780ed980d58fa32bc2dee1fd09a` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0029/assets/render-rm09-deployments-r02-screens.mjs` | `sha256:5369b0a8c5cba6c8df8ead9678f6e7f50b7629bf35876fbe5601fbc628cfb496` | v24.15.0 | 0.35.5 | 8.18.7 |

High-level UI direction: `docs/design/ui-direction.md` at `sha256:25ef386c6a84bc747b1bd87ab3d7ebe67fdd3de2034c20e4ee8f6fefed8012cc`.
## Application shell and component templates

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |
| `shell.standard-app` | 1.0 | shell | CIS built-in | Dark application rail, top bar, route context, and governed content canvas. | Selected |
| `component.page-header` | 1.0 | component | CIS built-in | Consistent title, description, status, and primary action hierarchy. | Selected |
| `component.button` | 1.0 | component | CIS built-in | Primary, secondary, destructive, icon, disabled, loading, and focus-visible button states. | Selected |
| `component.text-input` | 1.0 | component | CIS built-in | Labeled text input with helper, placeholder, required, focus, disabled, and validation states. | Selected |
| `component.select` | 1.0 | component | CIS built-in | Labeled select/dropdown with placeholder, selected, open, disabled, and validation states. | Selected |
| `component.breadcrumbs` | 1.0 | component | CIS built-in | Consistent hierarchical path, current-page semantics, and truncation behavior. | Selected |
| `component.pagination` | 1.0 | component | CIS built-in | Result range, page navigation, page size, disabled boundaries, and loading state. | Selected |
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.filter-bar` | 1.0 | component | CIS built-in | Search, filters, applied-filter chips, and result count. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
| `component.empty-state` | 1.0 | component | CIS built-in | Consistent empty, denied, error, and recovery-state composition. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `RM09-APPLICATIONS`, `RM09-INSTANCES` | `shell.standard-app` | RM09-APPLICATIONS:Render required, RM09-INSTANCES:Render required |
## Principal review journeys

1. **Find an application:** apply name/identity and authorized-environment
   filters, inspect a bounded page, and open an application by its exact ID.
2. **Inspect instances:** show each instance's reported app version, raw
   Active/Stale/Closed presence and Last seen. Back restores the previous list
   filters, page, scroll and focus.

No metadata/configuration/operations/login screen or future-placeholder
navigation is part of this revision. Mobile is excluded by the latest user
instruction. Desktop keyboard, focus and zoom behavior remain required.

## Capability prerequisites and limits

The existing `listInstances` response already supplies required
`applicationVersion`. Display it as **Reported app version**, with the helper
**Version reported by this process when it registered**. Last seen is separate
presence evidence. Different instances may have different reported versions;
no application-wide current version is inferred.

The application list supplies identity/display metadata and environment, without
instance counts, health, version or live-deployment aggregates. Applications may
remain after registrations expire. Empty instances means no retained instance
observations, not proof that no process is deployed.

P2 remains required: bounded application-name/identity and environment server
filtering plus authorized environment enumeration. Existing list APIs support
only pageSize/cursor; filtering loaded rows cannot satisfy global search. Real
human/delegated session and native resource authority remain production
prerequisites. Configuration/cohort prerequisites are deferred with those flows.

## Required states

These are review/implementation obligations, not completed functional tests.

- [ ] Populated applications and selected instance pages, with differing versions.
- [ ] Successful no-match, empty application and empty retained-instance responses.
- [ ] Loading, initial unavailability, valid-session dated snapshots and Retry.
- [ ] Session/read-access loss clears protected rows and environment choices.
- [ ] Changed targets/filters invalidate obsolete requests and cursor history.
- [ ] Invalid required record fields do not become fabricated normal values.
- [ ] Keyboard navigation, focus return, zoom, labels and non-color status cues.

## Planned render coverage

Six desktop frames at 1600 × 1000 over two templates:

| Screen ID | Representative states | Journey |
| --- | --- | --- |
| RM09-APPLICATIONS | Populated, no matches, initially unavailable | Filter and select an application. |
| RM09-INSTANCES | Populated, no retained observations, stale snapshot | Inspect per-instance reported versions and return. |

No mobile/narrow layout is required. Additional loading, denied and obsolete
response behavior remains specified in the wireframes; pictures do not prove
runtime, keyboard or accessibility behavior.

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
| `rm09-instances` | `backoffice` | empty | desktop | `docs/changes/CIS-0029/assets/rm09-instances--empty--desktop.png` | 1600x1000 | `sha256:68c491bb7852c7a78e2d854cad223ad2dcd4fe9296ae9e956db27e40f16794f3` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-instances--primary--desktop.png` | 1600x1000 | `sha256:f4f99b6710e4eebf8d1f1783c44a77ddbd84b3e54f2d57df0e6e7d6acfccdfb1` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | stale | desktop | `docs/changes/CIS-0029/assets/rm09-instances--stale--desktop.png` | 1600x1000 | `sha256:3225b1c6d2c767b9108fe2eaa180d0e90985888e64e516f74a8144c3460bd0cb` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |
| Governed UI direction/guidelines | Native R02 scaffold binds source digests; existing product direction remains unchanged | Scaffolded; exact visible review pending | None granted. |
| Visible environment/target and synthetic marker | Both desktop templates and all state variants | Specified; rendered review pending | None granted. |
| Desktop accessibility | Contrast, focus, labels, zoom, non-color status and persistent feedback | Intent specified; actual keyboard/accessibility behavior unverified | Mobile excluded by latest user instruction. |
| Exact source facts | Per-instance reported versions, raw presence, last-seen evidence and bounded pages | Specified against existing contracts; filtering needs P2 | None granted. |

## Rejected revisions

R01 was rejected for revision. Its exact decision and hashes are retained below;
rejected PNG files were removed by CIS. Local Git commit `d4ce9478` preserves
the unapproved earlier review package.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |
| `sha256:067570696e12a698fd9ec14fa3191bb95a166d3686c2438729d9bda3d025fcd6` | sha256:fc5276e3fb10b946ab1c60c56678a407aed83e6004379bae08b799dd8e235963, sha256:c1718883941859b8ececd24ceefce2e2557d9dae4962455ab3b308ca1eb5ae02, sha256:394e20606cc9f6d0848f5155b2fb60d6e1e704f31d8d0c40a5c73b197aec7cd3, sha256:c5332119b8f7601d97c6d273fba561a6fa7abea811886ec523fbd822edbe448a, sha256:53aeeb373d9e5d4385fbd7a3fc0809fe403e1dc22267e18f5fd303a60192ca40, sha256:d4c71ee73d571d68997ef370517962f41301d05ae7a352b03f84121330f0a35b, sha256:aadf4e03bf280b0a7de4d01cda51cb77f77848c67592686faf854c28714e5052, sha256:c3d0b88a961d23cad300ecdac9616e3d69800fbbb56742569441023d5389fc19, sha256:c44e56a90e34b95ae4000b99541f1956719b5bafd8ee9d21f9055d77c91169c6, sha256:5dc8a626f1793d525165a239ef54fface42a73c3b39b379245316670a03ffe69, sha256:6728363cf24776a02f5c00827b09744695a3122743fa7a31f5d23550484d828c, sha256:2d52d65f03c6161e5a5dddbbd67397c7f194d19cdfa2b2497cc109da8062c0f0, sha256:852705eb23016b57c67c818f6d65b1334a645dce20a86ba243b243aef6435f94, sha256:a01124078b2d20447b8473301e1fd9a0686ff35eff5602c4c4747a03fd7d7b6d, sha256:b7cd3314903ab20c083311937eba606c5e55f99ef545a849c0d521e7ece9e40b, sha256:99835842798ef20610438fa5376804a69c978ea2a44accdb0d222f16ce839d0f, sha256:590d7671664a21554b77316820612d1cf842ddcb5cefa5b1c651ac2ca9d0456c, sha256:f7909013a02d5892b5b50d7ab1d097c484aaf08013c322a391fdb42ee80fa90f, sha256:f0e5cddc2da5c063d053e1f8caef56e223b5bec32adde6555402a47af9cd975a, sha256:1ff755b75148bc2f1a33427c0bc4c0f6f6bcd177d4bc52427143f63598279ccb | Andrew Spiteri | 2026-11-25T13:04:15Z | Rejected during human design review | The user requested changes instead of approving R01: mobile is not required; proceed step by step with a filterable list of deployed applications, opening an application to show instances and their current reported application version. Limit R02 to this desktop inspection journey; defer broader portal workflows. |
## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Pending Approval | No human decision recorded | Not decided | Not sealed | Not generated | Not generated | R02 pending. Present the exact two-screen desktop applications/instance-version review. Mobile is excluded; broader workflows are deferred. Explicit human approval and normal narrowed-scope reconciliation precede production work. |
