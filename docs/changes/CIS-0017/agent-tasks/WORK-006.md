---
title: "WORK-006 Restore the retained integration package graph through approved exact mappings"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:877e48b6e10b4975a5323c019e32aee0"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["retained-mappings"]
authorization_scope_basis: "sha256:e7cc2acf2b99f58badc076f5f603a7396223aace258ee355a526090395c644af"
---

# WORK-006: Restore the retained integration package graph through approved exact mappings

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Restore the retained integration package graph through approved exact mappings

## Acceptance criteria

- [x] Add only the two explicitly approved retained-package source mappings; preserve existing sources, dependencies, pins and resolved versions/content hashes. Existing started PostgreSQL task and historical evidence remain unchanged.

## Targeted validation

- [x] Fresh existing Integration project restore succeeds with Npgsql as its only new package; compare all prior locked package versions/content hashes, run locked restore, and retain actual costs/failures. No new harness or feed.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Exact mappings | Passed | [independent review](../review-006.md); only three total additions including separately approved Npgsql. |
| Preserved graph | Passed | All54 prior versions/contenthashes unchanged; Npgsql10.0.4 only addition; WORK-006/retained-lock-comparison.json. |
| Native locked restore | Passed | WORK-006/preserved-graph-locked-restore/native.log; failed fresh/range outputs and measured costs retained. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1883; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:5feb0152ea5148cfdb246cd263620a6fb0cf54c161f553c2b02d22edbef64e9e |
