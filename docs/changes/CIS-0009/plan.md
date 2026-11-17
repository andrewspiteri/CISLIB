---
title: "CIS-0009 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0009
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Establish portable Metrics primitives and host-owned recording contracts](agent-tasks/WORK-001.md) | Complete |  |
| WORK-003 | [Instrument actual Logging Heartbeat and Healthcheck runtime boundaries](agent-tasks/WORK-003.md) | Complete | WORK-001 |
| WORK-004 | [Instrument bootstrap Startup Configuration and Type Manager facts](agent-tasks/WORK-004.md) | Complete | WORK-001 |
| WORK-002 | [Implement bounded OpenTelemetry collection policy and early lifecycle proof](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-006 | [Enable the approved optional Metrics NuGet source mappings](agent-tasks/WORK-006.md) | Complete | WORK-001 |
| WORK-007 | [Permit the approved DiagnosticSource transitive package mapping](agent-tasks/WORK-007.md) | Complete | WORK-001 |
| WORK-005 | [Integrate Metrics hosting native HTTP and measured consumer delivery](agent-tasks/WORK-005.md) | Complete | WORK-002, WORK-003, WORK-004 |
