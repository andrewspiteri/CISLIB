---
title: "WORK-182 Review R08 JSON-first application UX and readable tab routes"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-182
parent_id: none
category: design
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a7dd32c6c8e847a09301d50f27379f91"
depends_on: ["WORK-173","WORK-179","WORK-180"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["intake-design","portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:7b170cc1f8c304c041abbec1c20818adbd5c0ce7b4fcb64c80614452f5d67463"
---

# WORK-182: Review R08 JSON-first application UX and readable tab routes

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Review R08 JSON-first application UX and readable tab routes

## Acceptance criteria

- [x] Prepare the first bounded user-requested desktop UX iteration: readable application/tab/Configuration paths with exact stable selectors and legacy links; compact Configuration rows with directly available authorized actions, assigned shared versus application-layer distinction and explicit Override; JSON-first layer detail and application-only editor without main source-reference machinery, exact-token formatted JSON and bottom Technical details; predeployment New Configuration name/JSON screen with a documented actual native creation gap; expanded current and explicitly closed past registration sections. Preserve unknown/stale/paging/resource-authority facts. Preserve approved R07 source/assets/decision unchanged. Produce five synthetic previews and exact review package; pause only for the requested screenshot decision before production layout changes. No Scheduler runtime, new package/pin, existing database, production demo, push, merge or release.

## Targeted validation

- [x] Read current native/backend/UI contracts; document the minimal real creation/save boundary and subsequent coherent implementation/tests. Render five self-contained local design frames under finite native owner, inspect dimensions/nonblank pixels and visual layout, independently review honest source/inheritance/current-instance meanings and record exact source/PNG hashes. These are synthetic design previews, not browser execution or production acceptance. Existing manual lease remains separately owned; retain all costs/failures and active envelope. Later implementation owns route/legacy/collision/formatter tests and real provider/Playwright proof after screenshot approval.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Contract reconciliation and bounded follow-up | Independently reviewed; no required corrections | `ux-r08-plan.md`; current native creation/layer/presence/route boundaries retained. |
| Five synthetic desktop previews | Both local rendering phases passed; final five frames are 1600 × 1000 and nonblank | `r08-preview-manifest.json`; `rm09c-ux-r08-render-first` and `rm09c-ux-r08-render-refined`. |
| Visual/source review | Root and independent reviewer inspected all five final PNGs; no required findings or clipping | Final renderer SHA-256 `309502759a49b9fb53b01ff7455df64f98a7fda08942afdd9d32f5234b5bd6b5`. |
| Exact user screenshot decision | Approved: Approve R08 screens | `r08-user-approval.json` records the exact renderer, five PNGs and user answer. This approves the supplemental design; production acceptance belongs to WORK-183/184/185. |
| Preserved history and limits | R07 renderer/wireframe/package and protected launch settings hashes retained | Manifest records exact hashes. Prior native rollout/adoption failures remain retained. The separate original manual lease expired and drained normally; it is not current readiness evidence. |

This is the approved supplemental R08 design. Native R07 approval is not reused
for these new images. Exact human approval is retained separately; production
verification of WORK-183/184/185 is independent of this design checkpoint.

| Check | Result | Evidence |
| --- | --- | --- |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3190; failed=374; possibleTokenSavings=591; ledgerDigest=sha256:c609dc238c0942d9c466ec2cc363c114a64e6b904e7e1b4634216d2681a0edbc |
