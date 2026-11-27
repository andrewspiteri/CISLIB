---
title: "WORK-172 Qualify future Scheduler Task action and recovery grants"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-172
parent_id: WORK-153
category: security
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-040"
instance_key: "outcome:0726210d042c4a67b2c6279e299d2712"
depends_on: ["WORK-151","WORK-152"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","portal","integration","scheduler"]
authorization_scope_basis: "sha256:a862370ec270ed4d6905b390d696174b65f84416be187dd9697df9ed201740fd"
---

# WORK-172: Qualify future Scheduler Task action and recovery grants

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Qualify future Scheduler Task action and recovery grants

## Acceptance criteria

- [ ] Preserve the unfinished WORK-153 Scheduler Task/manual/cancel action eligibility and exact native resource authorization obligations: current stored target checks, same-resource write plus original-result recovery bundles before submission and replay, independent recovery reauthorization, hidden target/count/cursor protection and truthful unsupported optional backends. This is deferred future Scheduler implementation under the user contract-only BRD steering, not current runtime authority. Preserve RM10-AC02/08/14/21/24/29/30 and related original RM09 action obligations without claiming delivery.

## Targeted validation

- [ ] Future real selected Scheduler backend must establish exact Task/manual/cancel grant behavior, wrong-resource or revoked recovery refusal, original result and native Audit/transaction facts, and unsupported-discovery honesty through existing native fixtures and independent review. Keep this leaf unstarted until Scheduler implementation authority and its actual backend exist. Inherit the existing WORK-040 parent pool; no new capacity.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
