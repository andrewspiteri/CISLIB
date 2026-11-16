---
title: "CIS-0006 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0006
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Define Heartbeat consumer contracts, copied configuration and weak registration ownership](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [Implement monotonic fair Heartbeat scheduling and exclusive asynchronous execution](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-003 | [Complete Heartbeat cutoffs, retirement and true owner settlement barriers](agent-tasks/WORK-003.md) | Complete | WORK-002 |
| WORK-004 | [Extend detached Healthcheck facts and implement Heartbeat policy binding](agent-tasks/WORK-004.md) | Complete |  |
| WORK-005 | [Integrate Heartbeat readiness, safe DI scope shutdown and developer package example](agent-tasks/WORK-005.md) | Complete | WORK-003, WORK-004 |
| WORK-006 | [Repair Type Manager compatibility consumer deployment and pinned API host discovery after Heartbeat integration](agent-tasks/WORK-006.md) | Complete |  |
