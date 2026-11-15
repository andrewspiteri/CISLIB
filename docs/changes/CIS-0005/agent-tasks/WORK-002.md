---
title: "WORK-002 Prove two-case consumer reuse after the authorized additional execution"
type: agent-task
status: Draft
task_status: ReadyForReview
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:e0f64227aa704300b0b55138732e343e"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0005:outcome:2ea46f1ff11549b39ed0d9e76cf35e7b"
authorization_scope_ids: ["continuation"]
authorization_scope_basis: "sha256:ea2e4f44b9152325c0fd1b0afd43f586ca68f62ec93791923d8b6f7ab8b79575"
---

# WORK-002: Prove two-case consumer reuse after the authorized additional execution

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Prove two-case consumer reuse after the authorized additional execution

## Acceptance criteria

- [ ] Execute only healthcheck-diagnostic-reuse-continuation once within 540 seconds. Require two passed cases, zero failures or skips, and a settled accepted report. Complete ordinary notes and task lifecycle, commit, refresh the graph, and inspect plan/status/verify in fresh processes with zero additional native launches and unchanged accepted report bytes and prior execution costs. Preserve all original CIS-0001 through CIS-0004 tasks, reports, contracts and accounting.

## Targeted validation

- [ ] Use the exact installed 2b916962 development build, profile SHA-256 ad22abca641dd2585bc5f6266c1a76b5159b8c51cadd28608d1f15f6ce3f767a, explicit --layer unit --max-batches 1 --max-seconds 540 without force. Retain compact report, native execution references, pre/post read-only checks and original preservation comparisons. No further test execution is authorized.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
