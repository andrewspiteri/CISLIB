---
title: "CIS-0031 design approval"
type: design-approval
status: Approved
change_id: CIS-0031
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
| `docs/changes/CIS-0031/wireframes.md` | `sha256:3ced61e739ff3e5dc9d0b9b522e2fb03757853aab62b15630b0aaf1540d0bcfc` | `docs/design/cis-default-guidelines.md` | `sha256:153abe4d66ef5dc15ebe9c9147635182279625e69a05b8758d1ce8fe3d627b59` | `docs/changes/CIS-0031/assets/render-rm11-rule-engine-screens.mjs` | `sha256:fce53a7782b3591a67890c88e2a754a56794a3ef3da6c1068713cbab1e54ecf5` | v24.15.0 | 0.35.5 | 8.18.7 |

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
| `component.dialog` | 1.0 | component | CIS built-in | Modal confirmation/form shell with title, description, focus boundary, actions, and destructive variant. | Selected |
| `component.alert` | 1.0 | component | CIS built-in | Inline/banner feedback for information, success, warning, error, denied, and recovery actions. | Selected |
| `component.table` | 1.0 | component | CIS built-in | Reusable table header, rows, status cells, empty state, and row action affordance. | Selected |
| `component.filter-bar` | 1.0 | component | CIS built-in | Search, filters, applied-filter chips, and result count. | Selected |
| `component.status-badge` | 1.0 | component | CIS built-in | Guideline-bound success, warning, critical, neutral, and accent status pills. | Selected |
| `component.card` | 1.0 | component | CIS built-in | Standard bordered content card with restrained radius and hierarchy. | Selected |
## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |
| `backoffice` | `RM11-RULESETS`, `RM11-GRID`, `RM11-VERSIONS`, `RM11-DEPLOY`, `RM11-ADOPTION`, `RM11-COUNTERS`, `RM11-EXECUTIONS`, `RM11-EXECUTION`, `RM11-CONFLICT` | `shell.standard-app` | RM11-RULESETS:Render required, RM11-GRID:Render required, RM11-VERSIONS:Render required, RM11-DEPLOY:Render required, RM11-ADOPTION:Render required, RM11-COUNTERS:Render required, RM11-EXECUTIONS:Render required, RM11-EXECUTION:Render required, RM11-CONFLICT:Render required |
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

## PNG manifest

| Screen ID | Frontend type | State | Viewport | Path | Dimensions | SHA-256 | Render validation | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `rm11-adoption` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-adoption--primary--desktop.png` | 1600x1000 | `sha256:8faa615456430c881c55f038773a3243e7de30c5a7b05db5365fd8439e925db9` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-conflict` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-conflict--primary--desktop.png` | 1600x1000 | `sha256:802a8b93f2cd0e9f23f0797ce8765f6e537faeb476c5ff69aaf389b4a5d70d0b` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-counters` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-counters--primary--desktop.png` | 1600x1000 | `sha256:513e507533a73ae7e80397e3bb1222013e01a3c9661e9d874224a0da04e84663` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-counters` | `backoffice` | reset | desktop | `docs/changes/CIS-0031/assets/rm11-counters--reset--desktop.png` | 1600x1000 | `sha256:1c097e164c4a12cd5e8acc44055c375c06d5b5f449059c8b77be673f2ec4f672` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-deploy` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-deploy--primary--desktop.png` | 1600x1000 | `sha256:f6c77bd2d3024fc9733dde399f6f98837b1f06eb2421895d836aee7b8067202e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-execution` | `backoffice` | expanded | desktop | `docs/changes/CIS-0031/assets/rm11-execution--expanded--desktop.png` | 1600x1000 | `sha256:564a42e12548d5d58736eed1556ac6e496c3fa17237ac467b8a12d908710c72d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-execution` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-execution--primary--desktop.png` | 1600x1000 | `sha256:ff768ebf43e6a59373fbe8d9254a005732e1e9b73c6136d6fc7a378baaba9316` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-executions` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-executions--primary--desktop.png` | 1600x1000 | `sha256:17180a0092bfb51308609308ff0aa5be0cd949545706431931214251252235b5` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-grid` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-grid--primary--desktop.png` | 1600x1000 | `sha256:af30365447f8bf7237199d86b10d33e52902e8022cc5455d64d875911f2a316f` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-grid` | `backoffice` | primary | narrow | `docs/changes/CIS-0031/assets/rm11-grid--primary--narrow.png` | 1000x1000 | `sha256:0205d1cdb42393d2e724162cdb525814b2248caec17f994fa6a13d4acd1d19d9` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-rulesets` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-rulesets--primary--desktop.png` | 1600x1000 | `sha256:ee76a828d33fbce2bd6e7a7a75b408d1736cb02afaf282be967b80243d08493e` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-versions` | `backoffice` | primary | desktop | `docs/changes/CIS-0031/assets/rm11-versions--primary--desktop.png` | 1600x1000 | `sha256:e50579d33304313abd1a27a4b7fd55ceb56dfdd5af8344ec05ec4e3f18982f3d` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
| `rm11-versions` | `backoffice` | read-only | desktop | `docs/changes/CIS-0031/assets/rm11-versions--read-only--desktop.png` | 1600x1000 | `sha256:be0892eb1820029b120f840d776e5c4b664dc189ca078f5e54d5c8a50c6b6875` | PNG signature, dimensions, size, renderer stats passed | ReadyForReview |
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
| Approved | Andrew Spiteri | 2026-11-27T21:13:44Z | `sha256:3ced61e739ff3e5dc9d0b9b522e2fb03757853aab62b15630b0aaf1540d0bcfc` | `sha256:fce53a7782b3591a67890c88e2a754a56794a3ef3da6c1068713cbab1e54ecf5` | `sha256:d76b1fd3b0bb2ccd785e91d4dd2005fa2d0f255a05df269adc189d846692d1c0` | Andrew Spiteri selected Approve this exact screen package in response to the CIS-0031 review request for all 13 RM11 previews. The recorded manifest is sha256:d76b1fd3b0bb2ccd785e91d4dd2005fa2d0f255a05df269adc189d846692d1c0. |
