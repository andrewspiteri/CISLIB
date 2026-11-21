---
title: "WORK-002 Deliver production PostgreSQL messaging storage and atomic arbitration"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:43223b0564a24dcea966f3eb0a2a2d33"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["postgres-store"]
authorization_scope_basis: "sha256:d1794c1de2d0a3a1256b25026b82d573881123fe5051c812c61149136763bedb"
---

# WORK-002: Deliver production PostgreSQL messaging storage and atomic arbitration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver production PostgreSQL messaging storage and atomic arbitration

## Acceptance criteria

- [ ] P05-P07/P11-P14 and A01/A02/A04/A06/A08/A10: explicit versioned packaged installer, originalRCcontext staging and DBunique inbox claim, metadata reservation/fencedbodyread/settlement, parameterized conditionaldispositions and admin storage. Store never completes owner or calls broker; protect exact bytes/provenance and separate invalidrejection identity.

## Targeted validation

- [ ] Real newlyownedPostgreSQL commit/rollback business+outbox, same/different subscription/session races, expiredfence/oldowner, postconflictseparate snapshot, retrywinner/uniquehandoff, boundedinspection/redrive/retention and unsupportedisolation guards. Exactcandidate/bytes/shape evidence, docs/scopedstyle/build and independent SQLreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
