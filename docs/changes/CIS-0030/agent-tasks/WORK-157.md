---
title: "WORK-157 Implement qualified scheduler persistence, trusted catalog and coherent active revisions"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-157
parent_id: WORK-090
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-090"
instance_key: "outcome:c54499a8c8cc4c4cbcf8dd3ddda3ab61"
depends_on: ["WORK-151","WORK-152","WORK-171","WORK-154"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-157: Implement qualified scheduler persistence, trusted catalog and coherent active revisions

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement qualified scheduler persistence, trusted catalog and coherent active revisions

## Acceptance criteria

- [ ] Extend the one Management PostgreSQL authority through explicit versioned migration/readiness and optional scheduler backend companion. Persist trusted immutable definition/configured-trigger/task revisions, logical identities, desired/active references and bounded execution capsules with positive configured quota; normal startup does no DDL and unselected consumers stay compatible. Apply trusted catalog conflicts and guarded task preview/promotion using exact original Operations/Audit conventions and RM08 publication/release/assignment/owner-adoption. Prior active revision remains until coherent promotion/disable; shared changes visibly revalidate every affected task against current preconditions without overwriting edits. Definitions are passive; unsupported mapper/editor/identity/contract blocks activation. Preserve exact names/IDs and configuration ownership; no arbitrary executable handler creation. Owns RM10-AC01, RM10-AC02, RM10-AC03, RM10-AC11, RM10-AC12, RM10-AC13, RM10-AC27, RM10-AC28, RM10-AC29, RM10-AC31, RM10-AC35, RM10-AC40.

## Targeted validation

- [ ] Prove explicit migration from retained schema, startup read-only readiness, trusted version conflicts and exact target authorization. Run native guarded active-revision/shared-edit/adoption/ETag races and original operation replay/Audit rollback cases. Reserve shared schema edits to this owner; document options, transaction/lock order, adoption boundaries and migration. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-151, WORK-152, WORK-171, WORK-154) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
