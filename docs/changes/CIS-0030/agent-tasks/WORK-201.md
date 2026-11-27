---
title: "WORK-201 Suspend native Deploy modality during session verification"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-201
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a382a7fe0a574a6cbc0e89b59821ec41"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-201: Suspend native Deploy modality during session verification

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Suspend native Deploy modality during session verification

## Acceptance criteria

- [x] Correct review 85711214 P2 inaccessible session Retry behind a hidden native Deploy modal. Close or suspend actual native modality while verification is checking/unavailable, retaining the selected immutable version and deployment note in component state. Restore only after successful validation of the same session. Keep true identity/target invalidation, real cancel/Escape behavior, current native assignment authority and original-operation/cleanup custody. Do not remount the page or clear deployment state merely to mask it. Block deployment admission during verification including live checks around awaited preview.

## Targeted validation

- [x] Extend the existing native Save/Deploy/rollback journey: open Deploy with a note, hold/fail actual focus verification, assert native dialog is not modal/open, activate Retry by actual pointer then keyboard, and restore the exact selection/note after same-session verification with zero preview/assignment effects for those checks. Keep native commit/Operations/Audit assertions for the normal journey and actual response/CDP/page cleanup. Include pinned production build, affected tests, scoped C# format/build, concise docs and independent review; no new screen/framework/package/runtime pin, push, merge or release.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current review correction | Passed local qualification | [215 pure checks, both real native journeys, builds/guards/identity checks and independent review](../review-857-verification.md); failed compiler attempt and all resource history retained, Ubuntu CI separate. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3464; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:992ddfc11d0a5c55129837ff7928974af9ce470a10dcf3585b1c0e4f18dfefb7 |
