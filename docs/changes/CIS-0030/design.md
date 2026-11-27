---
title: "CIS-0030 design approval"
type: design-approval
status: Approved
change_id: CIS-0030
approval_status: Approved
gate_status: Approved
authority: human-reviewed
---

# Design approval

For UI-bearing work, a completed renderer and PNG pack set `gate_status` to
`PausedForReview`. All non-review work stops until an explicit approval sets the gate
to `Approved`. Rejection keeps the global pause active and permits only wireframe and
design revision.

## Inputs and renderer

| Wireframe path | Wireframe SHA-256 | Guideline path | Guideline SHA-256 | Renderer path | Renderer SHA-256 | Node | Sharp | libvips |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `docs/changes/CIS-0030/wireframes.md` | `sha256:97de2b070f51078b2dfed5b865fe394bff851ced319bb3cac7a91a6c45d5ef65` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0030/assets/render-rm09-completion-r07-screens.mjs` | `sha256:84167b628b10729927e5d0643035721befe53d22a39cf887e3c1d66851d5fc37` | v24.15.0 | 0.35.5 | 8.18.7 |

High-level UI direction: `docs/design/ui-direction.md` at `sha256:50bdfa57159735813b88fa90145e0bc770e8678ec30197578f46c3766a37392f`.
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
| `component.timeline` | 1.0 | component | CIS built-in | Audit/activity timeline with status-aware markers and metadata. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `R07-APPLICATIONS`, `R07-INSTANCES`, `R07-CAPABILITIES`, `R07-CONFIGURATION-LIST`, `R07-CONFIGURATION-EDITOR`, `R07-CONFIGURATION-REVIEW`, `R07-ASSIGNMENT`, `R07-ADOPTION`, `R07-SCHEDULED-JOBS`, `R07-TASK-FULL`, `R07-TASK-PREVIEW`, `R07-RUN-HISTORY`, `R07-JOB-FULL`, `R07-EVENT-FULL`, `R07-METADATA`, `R07-OPERATION`, `R07-CONFLICT` | `shell.standard-app` | R07-APPLICATIONS:Render required, R07-INSTANCES:Render required, R07-CAPABILITIES:Render required, R07-CONFIGURATION-LIST:Render required, R07-CONFIGURATION-EDITOR:Render required, R07-CONFIGURATION-REVIEW:Render required, R07-ASSIGNMENT:Render required, R07-ADOPTION:Render required, R07-SCHEDULED-JOBS:Render required, R07-TASK-FULL:Render required, R07-TASK-PREVIEW:Render required, R07-RUN-HISTORY:Render required, R07-JOB-FULL:Render required, R07-EVENT-FULL:Render required, R07-METADATA:Render required, R07-OPERATION:Render required, R07-CONFLICT:Render required |
## Required states

- [ ] Primary flow
- [ ] Empty state
- [ ] Loading state
- [ ] Error state
- [ ] Permission-denied state
- [ ] Responsive states
- [ ] Relevant workflow and lifecycle states
- [ ] Accessibility intent

## Reused approved PNGs

These exact PNGs remain owned by their approved source change. CIS verifies the source
approval digests and current file hashes before rendering or approving this pack.

| Target screen ID | Frontend type | Source change | Source screen ID | State | Viewport | Path | Compatibility | Dimensions | SHA-256 | Source wireframe approval SHA-256 | Source wireframe content SHA-256 | Source renderer SHA-256 | Source manifest SHA-256 | Reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `r07-applications` | `backoffice` | `CIS-0029` | `rm09-applications` | `no-matches` | `desktop` | `docs/changes/CIS-0029/assets/rm09-applications--no-matches--desktop.png` | Approved unchanged | 1600x1000 | `sha256:019d8d34a046b684193201d77756d9ab9519da78a3ac88220ba44fc7e3981fcf` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact previouslyapproved Applications list/filter/layout as unchangedvisual reference only. New capability/editor/history workflows are separateR07images; oldR06 approval is not extended to those newpixels or wholecurrentbackend. |
| `r07-applications` | `backoffice` | `CIS-0029` | `rm09-applications` | `primary` | `desktop` | `docs/changes/CIS-0029/assets/rm09-applications--primary--desktop.png` | Approved unchanged | 1600x1000 | `sha256:bfe9387fd1a425980832733d473a40bfcb0139d31c24ce3a0538c6c63ec37134` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact previouslyapproved Applications list/filter/layout as unchangedvisual reference only. New capability/editor/history workflows are separateR07images; oldR06 approval is not extended to those newpixels or wholecurrentbackend. |
| `r07-applications` | `backoffice` | `CIS-0029` | `rm09-applications` | `unavailable` | `desktop` | `docs/changes/CIS-0029/assets/rm09-applications--unavailable--desktop.png` | Approved unchanged | 1600x1000 | `sha256:d5c8c0625303b15351743d3e23753032e5917bbf6af7c0dec39b4a38bd6ea8f0` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact previouslyapproved Applications list/filter/layout as unchangedvisual reference only. New capability/editor/history workflows are separateR07images; oldR06 approval is not extended to those newpixels or wholecurrentbackend. |
| `r07-instances` | `backoffice` | `CIS-0029` | `rm09-instances` | `details` | `desktop` | `docs/changes/CIS-0029/assets/rm09-instances--details--desktop.png` | Approved unchanged | 1600x1000 | `sha256:89a95d0e0d9dbaf8d7ca4b732fd81b1e0385e96c778bf463ec0f503f954fc1f2` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact approved per-instance version/list/panel layout as unchangedreference. Synthetic source names are historicalexamples; names-first globaltechnicaldetails and newR07flows are governed separately. Preserve originalsourcePNG/renderer/wireframeapprovalbytes. |
| `r07-instances` | `backoffice` | `CIS-0029` | `rm09-instances` | `empty` | `desktop` | `docs/changes/CIS-0029/assets/rm09-instances--empty--desktop.png` | Approved unchanged | 1600x1000 | `sha256:8ee7dda00312619ca01a4b6ab379a3bc0e6dc35a4f78fe4485215623c0c026df` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact approved per-instance version/list/panel layout as unchangedreference. Synthetic source names are historicalexamples; names-first globaltechnicaldetails and newR07flows are governed separately. Preserve originalsourcePNG/renderer/wireframeapprovalbytes. |
| `r07-instances` | `backoffice` | `CIS-0029` | `rm09-instances` | `primary` | `desktop` | `docs/changes/CIS-0029/assets/rm09-instances--primary--desktop.png` | Approved unchanged | 1600x1000 | `sha256:b4247169f4f12d6b83b75eda65a36a5e30268cddeefde5b2a70bca5952bd00b7` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact approved per-instance version/list/panel layout as unchangedreference. Synthetic source names are historicalexamples; names-first globaltechnicaldetails and newR07flows are governed separately. Preserve originalsourcePNG/renderer/wireframeapprovalbytes. |
| `r07-instances` | `backoffice` | `CIS-0029` | `rm09-instances` | `stale` | `desktop` | `docs/changes/CIS-0029/assets/rm09-instances--stale--desktop.png` | Approved unchanged | 1600x1000 | `sha256:53e1205cbba315139c922c9833909824e8a0459ad321ae2596006d320c17b6f4` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:40d9e22cd90e68221daf2ea4be385ed683e022abdf797ef8f86cc632a2a0645c` | `sha256:d1e9348ce9174b742ad7d3bc94227decc26014b38955d97950bf387b9bd04aac` | `sha256:ff55f6dafc4710fa865420567cb8e51acdc8968118dca12461abfb47971fed7a` | Carry exact approved per-instance version/list/panel layout as unchangedreference. Synthetic source names are historicalexamples; names-first globaltechnicaldetails and newR07flows are governed separately. Preserve originalsourcePNG/renderer/wireframeapprovalbytes. |
## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `r07-adoption` | `backoffice` | mixed | desktop | `docs/changes/CIS-0030/assets/r07-adoption--mixed--desktop.png` | 1600x1000 | `sha256:2ce8c548127afa1f950fc89974d1b960104b4ede36e779e63322fa1fb555c71e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-assignment` | `backoffice` | promotion | desktop | `docs/changes/CIS-0030/assets/r07-assignment--promotion--desktop.png` | 1600x1000 | `sha256:510226cefd74475447061c6aa816c20746673d04a7d0b5adf26da99368f46149` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-capabilities` | `backoffice` | catalog | desktop | `docs/changes/CIS-0030/assets/r07-capabilities--catalog--desktop.png` | 1600x1000 | `sha256:8230fd0f28852072e9003f46b82e5b0f6cc1a42e255c7aba841222e77fae263b` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-configuration-editor` | `backoffice` | edit | desktop | `docs/changes/CIS-0030/assets/r07-configuration-editor--edit--desktop.png` | 1600x1000 | `sha256:39ca5ec3efb5e4e3cfb565e96185256a788261df06f84e21fbc0a9ef2c257aec` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-configuration-list` | `backoffice` | history | desktop | `docs/changes/CIS-0030/assets/r07-configuration-list--history--desktop.png` | 1600x1000 | `sha256:70a1bc84c9ae67bc320bb04422d65ce43c91b2f04242bd9824e6f93cdd087006` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-configuration-review` | `backoffice` | review | desktop | `docs/changes/CIS-0030/assets/r07-configuration-review--review--desktop.png` | 1600x1000 | `sha256:08b5da677b30c67ab329542dd622b7fd934fc32e54268100484752b691d5c4e3` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-conflict` | `backoffice` | conflict | desktop | `docs/changes/CIS-0030/assets/r07-conflict--conflict--desktop.png` | 1600x1000 | `sha256:2cb56b7caf65d85c0d99dde24ac4407589003959751938d2d775c4381e4af67f` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-event-full` | `backoffice` | configuration | desktop | `docs/changes/CIS-0030/assets/r07-event-full--configuration--desktop.png` | 1600x1000 | `sha256:e0ebe364a0453ecba9c083a333e3a436a756688112f0c55c23f0d52ee69f88d2` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-job-full` | `backoffice` | configuration | desktop | `docs/changes/CIS-0030/assets/r07-job-full--configuration--desktop.png` | 1600x1000 | `sha256:a547952ba2c1c2acf73c149a682c5a86ab91cca5e1cc029f196e799d03c312aa` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-metadata` | `backoffice` | edit | desktop | `docs/changes/CIS-0030/assets/r07-metadata--edit--desktop.png` | 1600x1000 | `sha256:b28b15595c699e9fb017c71305c001d7b248afb8933a49651a1d60762ef21642` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-operation` | `backoffice` | unknown | desktop | `docs/changes/CIS-0030/assets/r07-operation--unknown--desktop.png` | 1600x1000 | `sha256:edac756859d9e85e39b712c82f3a12c99bed4bc0c5ad2774dbf32e087ed99fbf` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-run-history` | `backoffice` | attempts | desktop | `docs/changes/CIS-0030/assets/r07-run-history--attempts--desktop.png` | 1600x1000 | `sha256:1afb26b4508c9be89dd181358a61118f1e00700ccd440df5b57791f4b4053228` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-scheduled-jobs` | `backoffice` | tasks | desktop | `docs/changes/CIS-0030/assets/r07-scheduled-jobs--tasks--desktop.png` | 1600x1000 | `sha256:9e4472f9817e233b20e5c05315b718e8f4a377ccf2d3294baa8c37b504f4d239` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-task-full` | `backoffice` | blocked | desktop | `docs/changes/CIS-0030/assets/r07-task-full--blocked--desktop.png` | 1600x1000 | `sha256:c8da9b95c2353a65b5ae4b24f0f03fa69469acde6c34e0768ec11a7abeb4803e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-task-full` | `backoffice` | job | desktop | `docs/changes/CIS-0030/assets/r07-task-full--job--desktop.png` | 1600x1000 | `sha256:62a674c1f2253ab183339fe6e8cfa12a55b3c3cbb25c518e0735b8e83394e758` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-task-full` | `backoffice` | trigger | desktop | `docs/changes/CIS-0030/assets/r07-task-full--trigger--desktop.png` | 1600x1000 | `sha256:cb850b7a8b5f1b63e97d8e0afaa38cee3f773252a27e49d3183eae79f03b4a34` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `r07-task-preview` | `backoffice` | review | desktop | `docs/changes/CIS-0030/assets/r07-task-preview--review--desktop.png` | 1600x1000 | `sha256:bccecfcf4782da017f9c5d587ab6624381abcb690aed832e3c0d3d30133986da` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
## Guideline conformance and deviations

| Rule/token | Renderer mapping | Result | Deviation approval |
| --- | --- | --- | --- |

## Rejected revisions

Rejected PNG files are removed. Preserve their manifest hashes and review evidence.

| Renderer revision/digest | PNG hashes | Reviewer | Date | Findings | Rationale |
| --- | --- | --- | --- | --- | --- |

## Approval decision

| Decision | Reviewer | Date | Wireframe SHA-256 | Renderer SHA-256 | PNG manifest SHA-256 | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| Approved | Andrew Spiteri | 2026-11-26T03:53:54Z | `sha256:97de2b070f51078b2dfed5b865fe394bff851ced319bb3cac7a91a6c45d5ef65` | `sha256:84167b628b10729927e5d0643035721befe53d22a39cf887e3c1d66851d5fc37` | `sha256:54a9d35c1540895c1f99fd629b176f44d684ccc4d756cb079628877e16d4da49` | Explicit user reply Approve R07 screens to the exact17newdesktopreviewpackage and renderer84167b628b10729927e5d0643035721befe53d22a39cf887e3c1d66851d5fc37. This records combined wireframe/renderer/PNGapproval and preserves7R06reference states; it does not claim runtimeimplementation or providerproof. |

## R09 supplemental design review

WORK-189 owns the **backoffice** version-list/Save/Deploy design requested on
26 November. It reuses the approved desktop rail, typography and restrained
palette, with one Configuration page, an edit panel and one Deploy dialog.
The corresponding wireframe supplement and
[native semantic contract](ux-r09-version-deployment.md) define the selected
binding, immutable saved versions, truthful deployed marker, older-version
deployment and same-page recovery. Technical IDs remain collapsed.

Exact rendered previews and their manifest belong to
[the R09 package](review-package-r09.md). That supplemental decision is pending;
earlier R07/R08 approvals are preserved and do not approve these new interactions.
WORK-190 remains gated until the user's exact screenshot answer. Synthetic
renders establish proposed appearance only, not runtime/browser/provider proof.

### R09 recorded approval and comparison amendment

The preceding pending-decision text records the original review checkpoint.
Andrew subsequently approved the exact four-image direction in
[r09-user-approval.json](r09-user-approval.json): “much better and cleaner. maybe
if possible add option for diff at the bottom against another version ?” The
approved renderer `6cbd590b6fb51e023da7f5b53dba27c822b84c770df9bb08b36d1f2fd3aaf306`
and preview manifest
`e8833e1d6d84a8e584aa00b44f475e5dba861e806cceb597d9ce06e98a9031ae`
remain exact; the original proposal and PNGs are not rewritten by this supplement.

The user's later clarification, “the diff should be against the current deplyed
version.”, narrows the requested bottom read-only disclosure to the selected
saved version versus the actual uniformly deployed release in the fresh exact
map. No comparison picker is added. Correctly labeled columns, independent
content authorization, token-preserving bounded JSON/text comparison and a
Shared-reference change notice retain the native pair's meaning. No deployment,
targeted maps, stale/denied/unknown content and changed session/map contexts
cannot masquerade as a complete unchanged comparison. This is a requested
functional amendment, not a claim that unseen comparison pixels were approved.

WORK-190 and supporting WORK-191 share the existing 5h/6 implementation pool
(4h/5 and 1h/1). Current build/browser verification remains pending. Human-readable
version labels use actual saved notes/dates; actor kind/reference is technical
metadata, not an invented human author. Earlier R07/R08 approvals and all their
exact evidence are preserved.

R09 verification checkpoint, 26 November: current production build and focused native/browser acceptance passed after the actual next-Deploy receipt correction. See [implementation verification](r09-implementation-verification.md) for exact candidate identity, six browser cases, SQL/Audit assertions, cleanup and preserved earlier failures. This supersedes the pending implementation status above; it does not expand the screenshot approval or claim whole-portal qualification. The finite manual-review lease remains a separate active ownership obligation.


### Pending Instance status simplification

Current global gate: PausedForReview for the exact two-frame [Instance status review package](review-package-instance-status.md), renderer d870705d39fff72b5b0107b1fec271c8e23f1bb51c6d9c9c0d5848ee0af63ba7. Root and independent review found no required visual/contract issue. The user requested the rename and three-column simplification; their exact screenshot decision is still pending. All previous approval rows and immutable assets remain approved historical evidence and do not approve these new pixels. WORK-168 stays InProgress under150; no new product implementation starts until the decision. The current finite190 human-review fixture is left intact and may drain normally as review work.


Instance status decision: Andrew Spiteri explicitly answered Approve this layout to the exact two-frame package. [Decision record](instance-status-user-approval.json) retains renderer and image digests. The current global gate is Approved for this scoped revision. This does not approve new unseen workflows or establish implementation/runtime proof. WORK-168 continues its existing contract and parent150 budget.

