---
title: "CIS-0024 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0024
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Freeze portable management v1 and validated public contracts](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [Deliver independent authenticated service and authorized read APIs](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-003 | [Persist sequenced registration, presence and complete inventory](agent-tasks/WORK-003.md) | Complete | WORK-001 |
| WORK-004 | [Implement recoverable metadata command with atomic Audit](agent-tasks/WORK-004.md) | Complete | WORK-001, WORK-003 |
| WORK-005 | [Deliver CSharp keyed REST client and public substitutes](agent-tasks/WORK-005.md) | Complete | WORK-001 |
| WORK-006 | [Integrate optional registration and actual Jobs inventory lifecycle](agent-tasks/WORK-006.md) | Complete | WORK-001, WORK-005 |
| WORK-007 | [Deliver portable TypeScript clients and server-only examples](agent-tasks/WORK-007.md) | Complete | WORK-001 |
| WORK-008 | [Deliver deployable cross-language composition and focused native proof](agent-tasks/WORK-008.md) | Complete | WORK-002, WORK-003, WORK-004, WORK-005, WORK-006, WORK-007 |
