---
title: "WORK-174 Deliver application metadata review confirmation and original-key recovery"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-174
parent_id: WORK-163
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:f0b79a2e812c444ca94c006ec31ddd29"
depends_on: ["WORK-151","WORK-152","WORK-171","WORK-155","WORK-156","WORK-173"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration","intake-design"]
authorization_scope_basis: "sha256:7b170cc1f8c304c041abbec1c20818adbd5c0ce7b4fcb64c80614452f5d67463"
---

# WORK-174: Deliver application metadata review confirmation and original-key recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver application metadata review confirmation and original-key recovery

## Acceptance criteria

- [x] Deliver approved displayName/description author-review-confirm via explicit same-origin authenticated BFF and existing native Metadata operations. Bind exact application/user/session/service, current ETag and write plus original-result lookup grant bundle; retain shared minimal payload-free receipts before submission and recover the same original key after lost response/reload. Independently reauthorize direct write/recovery, preserve original commit versus current read cleanup, conflict/grant/expiry/denial states; no automatic write replay, new-key fallback, cross-user late refill or persisted payload. Reuse sibling technical disclosure and protected drafts; no Scheduler dependency/runtime. Owns metadata/recovery portions of RM09-AC02/03/04/06/08/09/10/12/14/15/16/17/18/19/20. Clear description explicitly previews empty and submits native omission-to-clear, never null; canonical identity stays read-only. Dirty Cancel/Back confirms discard and declining remains in the draft.

## Targeted validation

- [x] Use existing native Metadata/Operations/Audit and auth/browser fixtures for actual ETag race, same-key uncertain recovery/reload with one effect/Audit, direct BFF CSRF/current grant/session refusals, receipt/draft isolation and keyboard/review screenshots. Include focused tests/docs/independent review in this owner; no new test campaign. Refine the direct dependency to the current-inspection sibling once native IDs exist. Keep original WORK-100-BACKOFFICE pool; no extra capacity. Check clear-description review, dirty Cancel/Back rejection, and unresolved NotFound/expired/lookup-denied outcomes retaining the original key.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |


| Current native and browser behavior | Both real OIDC/authorization and desktop tests passed, 0 failed/skipped; report guard passed. Conflict, explicit description clear, lost response after real commit, reload and original-key recovery retain one original effect/rejection and required Audit | .cis/local/testing/rm09-completion/work174-evidence.json |
| Ownership, build and review | 75 focused cases and current production/strict TypeScript build passed. Three independent source/fixture/recovery reviews resolved; exact source and built inputs retained | work174-review.md; work174-fixture-review.md; work174-recovery-review.md in .cis/local/testing/rm09-completion |
| Scope, history and context | Finite metadata only. Earlier fixture and product failures/costs retained; no Configuration editor or Scheduler delivery claim. Prior partial graph predates the final recovery helper; direct current proof qualifies it and the next context refresh is batched with the independent backend work | No graph currentness, full-suite or deployment claim |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3071; failed=372; possibleTokenSavings=591; ledgerDigest=sha256:333b30eeb5e9a52c89fb789a83349fef45da70ffd6661069f2f09524191fb0a9 |
