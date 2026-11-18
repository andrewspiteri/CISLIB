---
title: "CIS-0010 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0010
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Freeze portable RestClient contracts and shared execution seams](agent-tasks/WORK-001.md) | Complete |  |
| WORK-007 | [Deliver actual RestClient host example and Core-only optional package consumers](agent-tasks/WORK-007.md) | Complete | WORK-002, WORK-003, WORK-004, WORK-005, WORK-006 |
| WORK-002 | [Implement native HTTP content and resource ownership](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-003 | [Compose ordered customization authentication and response mapping](agent-tasks/WORK-003.md) | Complete | WORK-001 |
| WORK-004 | [Implement optional upstream retry policy adapter and bounded delay proof](agent-tasks/WORK-004.md) | Complete | WORK-001 |
| WORK-006 | [Integrate finite RestClient observations and exact-host outgoing metrics](agent-tasks/WORK-006.md) | Complete | WORK-001 |
| WORK-005 | [Own logical RestClient deadlines dispatch outcomes and actual shutdown drain](agent-tasks/WORK-005.md) | Complete | WORK-002, WORK-003 |
| WORK-008 | [Refresh Configuration source-host build prerequisites](agent-tasks/WORK-008.md) | Complete | WORK-007 |
| WORK-009 | [Log completed REST calls through the existing logger](agent-tasks/WORK-009.md) | Complete | WORK-007 |
