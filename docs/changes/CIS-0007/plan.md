---
title: "CIS-0007 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0007
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-002 | [Integrate sequential startup with live probes, Heartbeat, safe hosting and runnable cache example](agent-tasks/WORK-002.md) | Complete | WORK-001 |
| WORK-001 | [Implement once-only sequential startup with real DI construction and lifetime barriers](agent-tasks/WORK-001.md) | Complete |  |
| WORK-003 | [Move concrete Heartbeat lifecycle wiring from startup coordination into hosting](agent-tasks/WORK-003.md) | Complete | WORK-001, WORK-002 |
| WORK-004 | [Preserve immediate Heartbeat admission cutoff during held startup cancellation](agent-tasks/WORK-004.md) | Complete | WORK-003 |
