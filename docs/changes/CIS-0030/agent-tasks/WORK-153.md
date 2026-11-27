---
title: "WORK-153 Implement native resource action eligibility and exact operator grant enforcement"
type: agent-task
status: Draft
task_status: Decomposed
task_id: WORK-153
parent_id: WORK-040
category: security
complexity: high
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-040"
instance_key: "outcome:a3e8ae7e7d9e451cae520e1eab94f47d"
depends_on: ["WORK-151","WORK-152"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-153: Implement native resource action eligibility and exact operator grant enforcement

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement native resource action eligibility and exact operator grant enforcement

## Acceptance criteria

- [ ] Deliver finite source/binding/task administrative action eligibility using actual stored targets and current native ICapabilityAuthorizer. Publication/release/assignment/manual/cancel needs its original-result recovery grant bundle; direct mutations and recovery independently reauthorize after preview or grant changes. Preserve independently validated human versus workload identity, existing strict Entra semantics, and no broadly privileged portal fallback/copied-claim/inventory authority. Hidden resources, counts/details/cursors are not disclosed, and optional backends report unsupported honestly. This owns the native action and grant slice of RM09-AC03, RM09-AC04, RM09-AC07, RM09-AC10, RM09-AC11, RM09-AC12, RM09-AC15, RM09-AC16 and RM10-AC02, RM10-AC08, RM10-AC14, RM10-AC21, RM10-AC24, RM10-AC29, RM10-AC30.

## Targeted validation

- [ ] Reuse existing native Management auth/HTTPS fixture with exact source/binding/task grants, write-without-lookup and revoked lookup; test direct hidden-target/action/cursor abuse and old-backend unsupported behavior. Document grant bundles and include proportionate independent review. Actual scheduler callback/lease proof stays with runtime/integration owners. Verify this owner consumes only the declared medium prerequisites (WORK-151, WORK-152) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
