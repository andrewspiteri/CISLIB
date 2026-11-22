---
title: "RM-04 bounded implementation plan"
type: document
status: Draft
---

# One module in two ordered parts

See [reconciliation/profile](reconciliation.md) and the preserved
[source](../../../planning/messaging/sources/CISLIB_RM04_Messaging_Reliable_Delivery_Specification.md).
The user's current instruction supplies authority to proceed through both parts
without another authorization request. Codex selects the finite allocation and
organizes coherent tasks under that instruction; no separate human review of
this generated plan or its exact numbers is claimed.

| Task | Outcome, tests and documentation | Dependencies | Shared allocation |
| --- | --- | --- | --- |
| WORK-001 | Freeze owned envelope/codecs, definitions, validated bounds, outcomes and narrow store/provider ports; strict wire/unit vectors, passive registration and portable contract. | None | 10h / 8 attempts / 600s restoration |
| WORK-002 | Production PostgreSQL schema, atomic staging/inbox arbitration, fencing and bounded administrative storage; real SQL atomicity/rollback/concurrency and provider guide. | 001 | 10h / 10 / 1800s |
| WORK-003 | Optional RabbitMQ adapter, private trusted projector, explicit topology/TLS/principals and native confirms/manual ack; real provider trust/route/restart and setup guide. | 001 | 8h / 10 / 1800s |
| WORK-004 | Committed-only publisher, byte admission, fenced reserve/publish/settle, transient/blocked handling and convenience submission; stale-owner/confirmation/settlement tests and ownership guide. | 002,003 | 4h / 6 / 300s |
| WORK-005 | Scoped authorized transactional dispatch and RM-03 binding, duplicate and current-reply responsibility; concurrent effects/replay/Unknown tests and handler guide. | 002,003 | 8h / 8 / 600s |
| WORK-006 | Retry/terminal/rejection handoffs, current retry authority, explicit admin redrive/retention; race/crash/ordinal/quarantine/horizon tests and failure guide. | 004,005 | 8h / 8 / 900s |
| WORK-007 | Existing hosting/facade shutdown order, shared bounds/readiness, seven Metrics streams and Part A acceptance; actual exporter, provider/package checks and Part A checkpoint. | 004,005,006 | 6h / 6 / 300s |
| WORK-008 | Typed request/reply, tickets/deadlines/early replies, response validation/current-attempt recovery; Part B real loss/restart/overload tests and request guide. | 007 | 6h / 8 / 600s |
| WORK-009 | Bounded public helpers, actual integrated package consumer, complete Part B evidence/guides/skill/current routes and final integrated review. | 008 | 4h / 8 / 300s |

Totals are **64 shared active hours, 72 logical attempts, including 7,200
restoration seconds**, across root, all agents, already-performed intake, code,
tests, documentation, review and restoration. The same existing physical
**6 GiB** counted ceiling and **64 MiB** reserve remain; a conservative **2 GiB**
Docker allowance is inside that ceiling for one fresh PostgreSQL and one fresh
RabbitMQ fixture, including images/data/logs. Verify measured admission before
provisioning. No existing database, broker, operator credentials or trust store
is accessed. Use newly owned labels, private generated credentials/TLS materials,
loopback listeners and bounded containers; remove only verified newly owned
resources after actual drain. Do not prune the engine or reclaim unknown caches.

Controller may redistribute within these totals without multiplying capacity or
resetting costs. Preserve unmeasured effort as a conservative estimate alongside
measured native elapsed; external tools are not CIS automatic counters. Record
substantive checkpoints before broadening and retain failures and corrections.
The initial distribution moved one hour from 003 to 001 to include the already
performed shared interface research/intake and independent review; all totals,
attempts and protected restoration reserves remain unchanged.
Two further hours move from the simpler publication task 004 to the shared wire
foundation for concrete source/research costs and independent wire tests. The
controller preserves all spent estimates, total limits and protected reserves.
One further hour moves from 003 to 001 before final foundation tests/docs/review,
retaining all cumulative conservative parallel-source costs inside the same
64-hour total. The current canonical allocation is authoritative.

Each leaf is a bounded module outcome of medium delivery size: shared contracts,
one store, one provider, one publication loop, one dispatch path, one disposition
path, existing composition, one RPC extension and one public-consumer handoff.
Sensitivity/concurrency warrants independent review, without new assurance work
items. Native builds sharing projects/output paths are serialized. After 001
freezes interfaces, store and broker source can proceed in parallel with explicit
file ownership. Runtime startup and native transaction callbacks stay sequential.

Part A must establish A01–A12 through actual SQL/broker entry points before its
completion checkpoint. Part B must establish B01–B06 on that machinery. One
actual exported-metrics and public-package separation check covers each part;
reuse unchanged evidence only when material inputs match. Retain bytes/records/
shape/concurrency and one representative end-to-end observation, separately
from installation/unit evidence. No broad benchmark, mutation or release campaign.
Any unavailable platform/deployment proof is stated explicitly, never inferred.

This plan excludes push, merge, release and external deployment. Completion of
the module does not automatically close the dossier or alter historical owners.
