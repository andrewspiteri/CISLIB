---
title: "WORK-002 Bind security profiles and bounded credential generations inside existing REST transport"
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
instance_key: "outcome:062195bb4cf3484cab8e22670719efc8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["rest-binding"]
authorization_scope_basis: "sha256:2d4ffaeb03b359c801ca081933ce1a8995a032e0480238dc59df21dc49dd0082"
---

# WORK-002: Bind security profiles and bounded credential generations inside existing REST transport

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Bind security profiles and bounded credential generations inside existing REST transport

## Acceptance criteria

- [ ] P06-P07/P11: stable native transport owns immutable profile/origins and bounded current/draining generations, with atomic per-exchange leases, safe borrowing and backward-compatible typed security detail. Reject bound suppression/replacement/TRACE while preserving ordinary clients, deadlines, retry and cleanup.

## Targeted validation

- [ ] Native REST tests cover final destinations and overrides, profile/origin borrowing, held response/body leases, two-slot admission, concurrent retirement/selection and retry-delay rotation. Confirm root meter factory, primary dispatch/fault precedence and unchanged unbound behavior; include transport guide and scoped formatting.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
