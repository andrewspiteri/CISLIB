---
title: "CIS-0019 delivery plan"
type: delivery-plan
status: Approved
change_id: CIS-0019
authority: derived-outcome-authorization
---

# Delivery plan

[Shared outcome and limits](proposal.md). Canonical task state is `plan.json`.
Derived under `cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c` by Codex; no separate human review of these task views is claimed.

| Task | Work | Status | Depends on |
| --- | --- | --- | --- |
| WORK-001 | [Deliver portable messaging framing, bounded definitions and infrastructure ports](agent-tasks/WORK-001.md) | Complete |  |
| WORK-002 | [Deliver production PostgreSQL messaging storage and atomic arbitration](agent-tasks/WORK-002.md) | InProgress | WORK-001 |
| WORK-003 | [Deliver optional RabbitMQ transport, native authority bridge and topology](agent-tasks/WORK-003.md) | InProgress | WORK-001 |
| WORK-004 | [Implement committed-only outbox publication and explicit submission](agent-tasks/WORK-004.md) | Draft | WORK-002, WORK-003 |
| WORK-005 | [Implement authorized transactional dispatch and durable operation binding](agent-tasks/WORK-005.md) | Draft | WORK-002, WORK-003 |
| WORK-006 | [Implement durable retry, terminal rejection and authorized redrive](agent-tasks/WORK-006.md) | Draft | WORK-004, WORK-005 |
| WORK-007 | [Integrate hosting, bounded observations and Part A acceptance](agent-tasks/WORK-007.md) | Draft | WORK-004, WORK-005, WORK-006 |
| WORK-008 | [Deliver typed request/reply and current-attempt durable recovery](agent-tasks/WORK-008.md) | Draft | WORK-007 |
| WORK-009 | [Deliver bounded public testing, complete consumer guidance and Part B handoff](agent-tasks/WORK-009.md) | Draft | WORK-008 |
