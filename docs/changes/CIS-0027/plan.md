---
title: "CIS-0027 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0027
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Freeze RM08 portable and enrollment contracts](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [Implement trusted catalog provisioning and immutable source publication](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-003 | [Implement coherent releases and authoritative assignment maps](agent-tasks/WORK-003.md) | Complete | WORK-001, WORK-002 |
| WORK-004 | [Extend Configuration paired snapshots and actual acceptance provenance](agent-tasks/WORK-004.md) | Complete | WORK-001 |
| WORK-005 | [Prepare and adopt actual native bootstrap credential resources](agent-tasks/WORK-005.md) | Complete | WORK-001 |
| WORK-006 | [Compose same-session pre-Build bootstrap and sequential host activation](agent-tasks/WORK-006.md) | Complete | WORK-004, WORK-005, WORK-007 |
| WORK-007 | [Add CSharp configuration client adapter and public testing boundaries](agent-tasks/WORK-007.md) | Complete | WORK-001, WORK-004 |
| WORK-008 | [Extend portable TypeScript configuration and native request semantics](agent-tasks/WORK-008.md) | Complete | WORK-001 |
| WORK-010 | [Enforce quota and reference-safe configuration history retention](agent-tasks/WORK-010.md) | Complete | WORK-002, WORK-003 |
| WORK-011 | [Integrate CountryData opt-in operator workflow and composed consumer handoff](agent-tasks/WORK-011.md) | Complete | WORK-003, WORK-006, WORK-008, WORK-009, WORK-010 |
| WORK-009 | [Report and project actual configuration adoption on existing owners](agent-tasks/WORK-009.md) | Complete | WORK-002, WORK-003, WORK-004, WORK-007 |
| WORK-012 | [Support same-epoch authoritative recovery after confirmed connectivity](agent-tasks/WORK-012.md) | Complete | WORK-004, WORK-007 |
| WORK-013 | [Retain unobserved authority gaps before control renewal](agent-tasks/WORK-013.md) | Complete | WORK-006, WORK-012 |
| WORK-014 | [Preserve declared nested defaults in structural release composition](agent-tasks/WORK-014.md) | Complete | WORK-002, WORK-003, WORK-004 |
| WORK-015 | [Retain same-cohort generation equality across registration recovery](agent-tasks/WORK-015.md) | Complete | WORK-004, WORK-007 |
