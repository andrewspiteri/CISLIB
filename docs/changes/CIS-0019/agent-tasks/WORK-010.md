---
title: "WORK-010 Qualify exact Operations binding in transactional message delivery"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010
parent_id: WORK-005
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-005"
instance_key: "outcome:1f2e1b3410084ad7b48d33d119655b53"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["dispatch"]
authorization_scope_basis: "sha256:a2a2def4e64a7d7ccd46a8d4403e22e307c9a0a0a6b84f4cee888c690126d957"
---

# WORK-010: Qualify exact Operations binding in transactional message delivery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify exact Operations binding in transactional message delivery

## Acceptance criteria

- [x] Finish the existing WORK005 P11/P19 Operations boundary: original context one-shot Execute, exact pre-resolved versus actual trusted identity/resource before authorization/journal/effects, current service authority, Prepared/Replayed distinction and atomic new inbox completion. Preserve public Operations API and original accounting/history. No new operation framework.

## Targeted validation

- [x] Run existing affected Operations regressions plus three exact-binding guard cases and the actual SQL/broker durable Prepared/new-message replay/conflict case; preserve current source/byte identity and independent guard review. Final acceptance requires completed WORK003 and original shared limits.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Bounded acceptance | 425 affected units each actual Windows/Ubuntu and current real-provider component cases; exact candidate custody retained | [Review](../review-005.md), `.artifacts/messaging/CIS-0019/WORK-005/part-a-component-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2024; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:225b7f1f1761d025e47e0aa0cf65721d1471bd5bd4a5f8c29d1842fef26096ee |
