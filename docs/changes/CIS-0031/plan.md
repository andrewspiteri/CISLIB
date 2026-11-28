---
title: "CIS-0031 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0031
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [RM11 shared portable and Management contracts](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [RM11 definition validation and local evaluator](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-006 | [RM11 durable start and completion audit](agent-tasks/WORK-006.md) | Complete | WORK-002, WORK-005 |
| WORK-007 | [RM11 authorized execution history](agent-tasks/WORK-007.md) | Complete | WORK-003, WORK-004, WORK-006 |
| WORK-008 | [RM11 hit counters and audited resets](agent-tasks/WORK-008.md) | Complete | WORK-003, WORK-007 |
| WORK-009 | [RM11 backoffice wireframes](agent-tasks/WORK-009.md) | Complete | WORK-001 |
| WORK-010 | [RM11 rendered backoffice design and review](agent-tasks/WORK-010.md) | Complete | WORK-009 |
| WORK-012 | [RM11 portal counter and execution inspection workflows](agent-tasks/WORK-012.md) | Complete | WORK-007, WORK-008, WORK-010 |
| WORK-013 | [RM11 opt-in CountryData consumer example](agent-tasks/WORK-013.md) | Complete | WORK-005, WORK-006, WORK-007, WORK-008, WORK-011, WORK-012 |
| WORK-003 | [RM11 trusted registration and immutable version saves](agent-tasks/WORK-003.md) | Complete | WORK-001, WORK-002 |
| WORK-004 | [RM11 deployment authority and retained-version eligibility](agent-tasks/WORK-004.md) | Complete | WORK-003 |
| WORK-005 | [RM11 application adoption and exact-version evaluation](agent-tasks/WORK-005.md) | Complete | WORK-002, WORK-004 |
| WORK-011 | [RM11 portal grid and version deployment workflows](agent-tasks/WORK-011.md) | Complete | WORK-003, WORK-004, WORK-005, WORK-010 |
