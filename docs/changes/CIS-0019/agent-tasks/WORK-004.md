---
title: "WORK-004 Implement committed-only outbox publication and explicit submission"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:30fd7ab9bc1445afb7ff45552d2d5e8e"
depends_on: ["WORK-002","WORK-003"]
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

- [ ] P06/P07/P15/P16 and A01-A03/A09/A11: originalcontext typed staging and stable receipts; convenience shortowner submission preservesactualcompletion. Reserveknowncommit, boundedbodyadmission, brokeroutsideSQL, freshfencedsettlement. Unknown/Blocked/transientcappedbackoff preserve accepted work and exact bytes; no publication beforecommit.

## Targeted validation

- [ ] Controlledandreal publicpath tests for stagedvscommitted, serialization/storefault rollbackonly, metadata+bytecapacity, staleowner, lostconfirm and uncertainstatecommit, cancellation/outage/shutdown actualcleanup, no perpollsuccessstream. Guide/style/build and independent owningboundaryreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
