---
title: "WORK-004 Implement committed-only outbox publication and explicit submission"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:30fd7ab9bc1445afb7ff45552d2d5e8e"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["publication"]
authorization_scope_basis: "sha256:725b9601de4578e6e9e14d18377516a3fcd1874a23f00a7ad70d4de5050c7329"
---

# WORK-004: Implement committed-only outbox publication and explicit submission

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement committed-only outbox publication and explicit submission

## Acceptance criteria

- [x] P06/P07/P15/P16 and A01-A03/A09/A11: originalcontext typed staging and stable receipts; convenience shortowner submission preservesactualcompletion. Reserveknowncommit, boundedbodyadmission, brokeroutsideSQL, freshfencedsettlement. Unknown/Blocked/transientcappedbackoff preserve accepted work and exact bytes; no publication beforecommit. Source may start after frozen WORK001 contracts to provide the already-required shared encoded-byte admission port for the independent optional provider. Final acceptance still requires completed WORK002 and WORK003 and their real store/broker integration; this is not an early delivery or completion claim.

## Targeted validation

- [x] Controlledandreal publicpath tests for stagedvscommitted, serialization/storefault rollbackonly, metadata+bytecapacity, staleowner, lostconfirm and uncertainstatecommit, cancellation/outage/shutdown actualcleanup, no perpollsuccessstream. Guide/style/build and independent owningboundaryreview. Verify shared process work/byte/wait admission and optional-provider byte lease through actual settlement; complete only after WORK002/003 are Complete and publication runs against them.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Bounded acceptance | 425 affected units each actual Windows/Ubuntu and current real-provider component cases; exact candidate custody retained | [Review](../review-004.md), `.artifacts/messaging/CIS-0019/WORK-004/part-a-component-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2023; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:cf57811b70067f30fb758e241a1c9f3d66d1a9cde70e81a4739365232a10a052 |
