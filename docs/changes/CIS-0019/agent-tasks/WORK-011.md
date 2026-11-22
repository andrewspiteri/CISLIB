---
title: "WORK-011 Qualify authorized transactional dispatch, bounded admission and drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-011
parent_id: WORK-005
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-005"
instance_key: "outcome:00e9823c6db74c3f8a61b3eedc1a28d3"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["dispatch"]
authorization_scope_basis: "sha256:a2a2def4e64a7d7ccd46a8d4403e22e307c9a0a0a6b84f4cee888c690126d957"
---

# WORK-011: Qualify authorized transactional dispatch, bounded admission and drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify authorized transactional dispatch, bounded admission and drain

## Acceptance criteria

- [x] Finish existing WORK005 P10/P11/P15/P19 dispatch on independent native Security/DI scopes: authorize actual targets before claim/effect; original-context inbox arbitration and committed-only ack; safe duplicate/old retry handling; bounded async work/byte admission; actual handler/transaction/scope/native drain; no ack-loss reclassification. Include bounded wire encoding and reserved reply alias. Final completion requires the exact Operations-binding continuation and completed WORK003; no added feature or capacity.

## Targeted validation

- [x] Actual native two-root concurrent duplicates use non-idempotent SQL counters, independent subscriptions/reconstructed runtime, unknown commit then fresh arbitration, denial and conflicting-content canonical isolation. Focused dispatcher/budget/bounded-encoding units, existing wire vectors, scoped formatting/build, dispatch guide and independent owning-boundary review. Preserve both platform evidence limits and original WORK005 costs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Bounded acceptance | 425 affected units each actual Windows/Ubuntu and current real-provider component cases; exact candidate custody retained | [Review](../review-005.md), `.artifacts/messaging/CIS-0019/WORK-005/part-a-component-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2025; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:262b90eab846692300e58980371d61aa6122256341752abf8c9c4db747b70271 |
