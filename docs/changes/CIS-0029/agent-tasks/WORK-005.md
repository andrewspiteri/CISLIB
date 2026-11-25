---
title: "WORK-005 Approved desktop inspection UI and connected behavior"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-005
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:8c8463e5c3a440758bce30594fc0f24e"
depends_on: ["WORK-002","WORK-003","WORK-004"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:b2121a841254ff5f31a1197b7f4b995161dc36c7e27fd6b8e17ede20d750005b"
---

# WORK-005: Approved desktop inspection UI and connected behavior

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Approved desktop inspection UI and connected behavior

## Acceptance criteria

- [ ] Backoffice: implement approved R06 Applications filters, selected-app Instances/Configuration/Capabilities/Scheduled jobs, read-only quick panels and Job Overview/local Configuration. Keep app/env/registration/report/scoped identities and reported versions; truthful Not reported/unknown/unavailable states. Run history and Task/Event APIs unavailable until RM10; no synthetic production records or inferred cron/bindings. Abort stale reads, clear denied session state, preserve keyboard/Escape/focus/context. Include runnable docs/tests/review; no new journeys.

## Targeted validation

- [ ] Focused UI and existing connected native/provider tests prove bounded paging, per-registration versions, definition metadata only, inventory identity, target switching/cancellation/stale response, denial/session expiry and panel keyboard/focus behavior. Actual desktop screenshots compared with approved R06; material new journey requires screenshot review. Build affected clients/portal, record proof limits and native costs; no broad qualification campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
