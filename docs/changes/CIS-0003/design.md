---
title: "CIS-0003 design approval"
type: design-approval
status: Draft
change_id: CIS-0003
approval_status: NotReviewed
gate_status: Inactive
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

## Application shell and component templates

## Frontend surface coverage

| Frontend type | Screen IDs | Shell/context | Status |
| --- | --- | --- | --- |

| Template ID | Version | Kind | Source | Purpose | Status |
| --- | --- | --- | --- | --- | --- |

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
| Not reviewed | TODO | TODO | TODO | TODO | TODO | TODO |