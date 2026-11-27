---
title: "WORK-198 Refresh open Configuration editor context without losing drafts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-198
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:45c57ad247774829934fe2e1f32c2a3d"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-198: Refresh open Configuration editor context without losing drafts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Refresh open Configuration editor context without losing drafts

## Acceptance criteria

- [x] Correct review87d18ba6P2 editor Refresh. Parent Refresh must reread the current editor context, source/assignment ETags and action permissions while retaining the exact same-target unsaved JSON/note and selected Shared provenance. Do not change the React key or remount to refresh. Use the combined read revision consistently for effects/recovery readiness; block Save while fresh context is pending and keep existing session/binding invalidation and original-outcome custody. A transient editor read must be retried by the normal Refresh control.

## Targeted validation

- [x] Use actual concurrent native source publication and map changes, hold the real Refresh editor response to assert disabled Save and retained draft, release it and save using fresh exact preconditions without borrowing latest Shared content. Separately fail one actual read delivery and prove Refresh retries. Reuse existing pure/native fixture and independent SQL/original/Audit assertions, join callback/delivery owners, scoped build/format and review. Approved backoffice screens stay unchanged; tests/docs are in this leaf.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Review 87d18ba6 correction | Passed local qualification | [Actual source, native and independent-review evidence](../review-87d-verification.md); retained failures, runtime identities and unchanged shared limits. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3447; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:821d41b4f10f0326e775a90d23a33f4c41810e48cad342865a85c586e1d66698 |
