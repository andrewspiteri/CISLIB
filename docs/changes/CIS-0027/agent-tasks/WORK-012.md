---
title: "WORK-012 Support same-epoch authoritative recovery after confirmed connectivity"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-012
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:1ad104cda257499db440c6fe3c9de151"
depends_on: ["WORK-004","WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["configuration","records"]
authorization_scope_basis: "sha256:8214a00865ed59ef4a87df301151e2e1a5d1cf965405232a76ce151c135831d8"
---

# WORK-012: Support same-epoch authoritative recovery after confirmed connectivity

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Support same-epoch authoritative recovery after confirmed connectivity

## Acceptance criteria

- [x] A newly confirmed native control can request exactly one same-epoch retry on the existing queued Configuration owner after an offline failure, including an old read still draining. Ordinary cached behavior and root/session/epoch fencing remain intact; no new timer, owner or allocation.

## Targeted validation

- [x] Focused completed-and-draining offline failure regressions, existing Configuration units and the WORK006 actual native reconnect fixture with a one-hour poll interval. A45-minute portion and one attempt are redistributed from existing shared capacity without resetting any prior consumption.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Existing queued owner | Completed-failure and still-draining recovery rows passed within all 1,200 affected Core units; ordinary cached-result regression retained | WORK-006/current-host-order-affected-unit-tests/results/current-host-units.trx |
| Actual recovery | Native SDK/TLS/PostgreSQL outage/restart converged before the one-hour ordinary poll, retaining root and registration identity | WORK-006/native-bootstrap-live-authority-final-tests/results/native-bootstrap.trx |
| Review and scope | Five-minute independent retry/task/session/epoch/Stop fence review found no blocker; tiny Runtime/Refresh support only | ../review.md |
| Capacity and formatting | 45 minutes/one attempt redistributed within unchanged shared totals, all prior reserves/spent costs preserved; scoped formatting verified | ../work-allocation.json; native-costs.jsonl |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2439; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:7cf22da523927ed3922e9e7decac1e3f48127ac7251aa8777335e4aa8f0d727b |
