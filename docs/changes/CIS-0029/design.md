---
title: "CIS-0029 design approval"
type: design-approval
status: Approved
change_id: CIS-0029
approval_status: Approved
gate_status: Approved
authority: human-reviewed
---

# Design approval

**R03 detail tabs approved by Andrew Spiteri.** Andrew Spiteri accepted the
application/instance direction and requested tabs for Instances, Configuration
list and Capabilities list. The application list remains unchanged; Instances
stays the default detail tab. No other tab is inferred from "etc".

R02 is preserved at local Git commit `6176b895`. Native requested-revision
history below records both the partial acceptance and the requested changes;
it does not manufacture approval of the unmodified full R02 manifest. R01
history is also retained. Mobile remains excluded. Wider portal workflows stay
deferred. The user accepted the exact R03 image and requested Scheduled jobs as a separate additive tab; that unseen design remains to be reviewed. Normal scope/plan reconciliation still precedes production work.
## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0029/wireframes.md` | `sha256:06582aab7edb438721e3d82c72c08761839474932dbc02cc6fc6e3271e6b155a` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0029/assets/render-rm09-application-tabs-r03-screens.mjs` | `sha256:071d759ed388a2852f15ce82ff9a07ceb3e072991df4e1c50774be8c33ae24b4` | v24.15.0 | 0.35.5 | 8.18.7 |

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
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.filter-bar` | 1.0 | component | CIS built-in | Search, filters, applied-filter chips, and result count. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
| `component.empty-state` | 1.0 | component | CIS built-in | Consistent empty, denied, error, and recovery-state composition. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `RM09-APPLICATIONS`, `RM09-INSTANCES`, `RM09-CONFIGURATION-LIST`, `RM09-CAPABILITIES` | `shell.standard-app` | RM09-APPLICATIONS:Render required, RM09-INSTANCES:Render required, RM09-CONFIGURATION-LIST:Render required, RM09-CAPABILITIES:Render required |
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
- [ ] Three exact detail tabs preserve application/environment; Instances is default.
- [ ] Instance versions, optional last seen and raw presence meanings are preserved.
- [ ] Configuration list is exact-scope metadata without content/default disclosure.
- [ ] Capabilities distinguishes advisory true/false/absent hints and actual authority.
- [ ] Per-tab loading, empty, denied, stale and unavailable results remain distinct.
- [ ] Target/query switches invalidate obsolete requests; access loss clears data.
- [ ] Desktop keyboard tabs, focus return, zoom and non-color status remain required.

## Planned render coverage

Eight desktop frames at 1600 × 1000. Three primary detail previews show the new
navigation; the application list is the accepted R02 reference.

| Screen ID | States | Review purpose |
| --- | --- | --- |
| RM09-APPLICATIONS | Populated, no matches, unavailable | Preserve accepted list/filter behavior. |
| RM09-INSTANCES | Populated, no retained observations, stale | Add tab row while preserving instance facts. |
| RM09-CONFIGURATION-LIST | Populated | Inspect selected application definition metadata. |
| RM09-CAPABILITIES | Populated | Inspect Management API discovery and caller hints. |

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
| `rm09-capabilities` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-capabilities--primary--desktop.png` | 1600x1000 | `sha256:1a09584836a21c44db8ca4358e3bc6289be5385c1f9ade4dd912c8ff5693cbb1` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-configuration-list` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-configuration-list--primary--desktop.png` | 1600x1000 | `sha256:3f43e8f9d4a3bd09bdac755fb283c03c0dab795ea18d51ee9829d037084c4225` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | empty | desktop | `docs/changes/CIS-0029/assets/rm09-instances--empty--desktop.png` | 1600x1000 | `sha256:eeaf56e3c877702ed03758cb4824641aa197cb7dce0b59131523d1ed0e2cf446` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | primary | desktop | `docs/changes/CIS-0029/assets/rm09-instances--primary--desktop.png` | 1600x1000 | `sha256:e373c976d14ab0fdec609b7b36a5336da09f9296160cb4a75912e60254b8885e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm09-instances` | `backoffice` | stale | desktop | `docs/changes/CIS-0029/assets/rm09-instances--stale--desktop.png` | 1600x1000 | `sha256:82f9aca53f1a48ab49dc9c9c02cda9ebaa690a7d9df3d1e7d872d6498198adbc` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
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
| Approved | Andrew Spiteri | 2026-11-25T13:39:18Z | `sha256:06582aab7edb438721e3d82c72c08761839474932dbc02cc6fc6e3271e6b155a` | `sha256:071d759ed388a2852f15ce82ff9a07ceb3e072991df4e1c50774be8c33ae24b4` | `sha256:ca0b5b2960776dd6a6d474807c80b54bb2ffc1ee94ef646e0f33847c1d070bc5` | Andrew Spiteri explicitly accepted R03: ok, we will need to add scheduled jobs as well. but the image seems ok. This approves the exact presented R03 desktop Applications/Instances/Configuration metadata/Capabilities design and preserves earlier application-list acceptance. Scheduled jobs is a requested additive follow-up tab; this approval does not pre-approve its unseen design, configuration write flows or broader portal workflows. |
