---
title: "CIS-0016 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0016
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Deliver trusted Core caller and capability integration with explicit host stages](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [Bind security profiles and bounded credential generations inside existing REST transport](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-003 | [Deliver explicit Entra workload acquisition and native app-only JWT integration](agent-tasks/WORK-003.md) | Complete | WORK-001 |
| WORK-004 | [Deliver native certificate authentication and listener trust boundaries](agent-tasks/WORK-004.md) | Complete | WORK-001 |
| WORK-008 | [Settle exact newly owned Windows TLS key containers after native drain](agent-tasks/WORK-008.md) | Complete | WORK-002 |
| WORK-005 | [Deliver bounded PEM credential reload and mTLS generation adoption](agent-tasks/WORK-005.md) | Complete | WORK-002, WORK-004, WORK-008 |
| WORK-006 | [Deliver isolated native development credential setup and reset](agent-tasks/WORK-006.md) | Complete | WORK-004 |
| WORK-007 | [Deliver three-profile CountryData security consumer and package handoff](agent-tasks/WORK-007.md) | Complete | WORK-003 |
| WORK-009 | [Recover required Entra availability independently of business readiness](agent-tasks/WORK-009.md) | Complete | WORK-003 |
| WORK-010 | [Propagate certificate retirement faults and define Windows chain support](agent-tasks/WORK-010.md) | Complete | WORK-002, WORK-004, WORK-005, WORK-008 |
| WORK-011 | [Preserve joining Entra source-recheck cancellation after shared settlement](agent-tasks/WORK-011.md) | Complete | WORK-009 |
