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
| WORK-002 | Production PostgreSQL schema, atomic staging/inbox arbitration, fencing and bounded administrative storage; real SQL atomicity/rollback/concurrency and provider guide. | 001 | 6h / 10 / 1800s |
| WORK-003 | Optional RabbitMQ adapter, private trusted projector, explicit topology/TLS/principals and native confirms/manual ack; real provider trust/route/restart and setup guide. | 001 | 10.5h / 10 / 1800s |
| WORK-004 | Committed-only publisher, byte admission, fenced reserve/publish/settle, transient/blocked handling and convenience submission; stale-owner/confirmation/settlement tests and ownership guide. | 001 for source; completed 002,003 for acceptance | 5h10m / 6 / 300s |
| WORK-005 | Scoped authorized transactional dispatch and RM-03 binding, duplicate and current-reply responsibility; concurrent effects/replay/Unknown tests and handler guide. | completed 002 for source; completed 003 for acceptance | 8h / 8 / 600s |
| WORK-006 | Retry/terminal/rejection handoffs, current retry authority, explicit admin redrive/retention; race/crash/ordinal/quarantine/horizon tests and failure guide. | completed 002 and frozen 005 seam for source; completed 004,005 for acceptance | 4.5h / 8 / 900s |
| WORK-007 | Existing hosting/facade shutdown order, shared bounds/readiness, seven Metrics streams and Part A acceptance; actual exporter, provider/package checks and Part A checkpoint. | 001 and compiled frozen ports for source; completed 004,005,006 for acceptance | 6h / 6 / 300s |
| WORK-008 | Typed request/reply, tickets/deadlines/early replies, response validation/current-attempt recovery; linked Denied/terminal replies, reserved reply capacity within existing totals and actual B05 crash/restart proof. | completed 013 Part A | 9.5h / 8 / 600s |
| WORK-009 | Bounded public helpers, actual integrated package consumer, complete Part B evidence/guides/skill/current routes and final integrated review. | 001 and frozen Part A ports for source; completed 008 for final acceptance | 5h20m / 8 / 300s |

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
After the production store source/cases froze and Core built cleanly, two unused
store hours move to broker integration and publication, one each. Reported source
costs, native invocations, attempts and every protected reserve remain intact.
After the store's accepted 315 source minutes and measured native verification,
90 more unused store minutes support real broker qualification (60) and combined
publication acceptance (30). The store retains 6.5 hours including its original
30-minute restoration reserve. Every estimate, failure and native cost remains;
the shared totals and physical ceiling are unchanged.
The owned broker then reported an actual 180,000 ms native dead-letter confirm
retry period. Thirty unused disposition minutes support that provider proof on
both platforms; its one test deadline covers the observed period without tuning
broker policy. The same 64-hour total and every restoration reserve remain.

Each leaf is a bounded module outcome of medium delivery size: shared contracts,
one store, one provider, one publication loop, one dispatch path, one disposition
path, existing composition, one RPC extension and one public-consumer handoff.
Sensitivity/concurrency warrants independent review, without new assurance work
items. Native builds sharing projects/output paths are serialized. After 001
freezes interfaces, store and broker source can proceed in parallel with explicit
file ownership. Runtime startup and native transaction callbacks stay sequential.
The unstarted publication task was refined to begin shared admission against the
frozen foundation while store/provider source proceeds. Its final acceptance
still requires completed 002/003 and their actual integration. A proposed change
to started 003 was rejected and retained locally; its executable contract/history
remain unchanged. No additional task, capacity or human decision is manufactured.

The still-unstarted public-consumer outcome was similarly refined to begin its
Part A package and representative footprint source after completed WORK-001 and
frozen Part A ports. Its existing final acceptance still requires completed
WORK-008 and refreshed Part B package/exporter evidence. This uses the same
WORK-009 allocation; it creates no additional assurance owner or tutorial.
An attempted transfer of unused disposition time to the historical decomposed
composition owner was rejected by CIS's current-scope/accounting-owner checks.
No transfer was applied and the original allocation/costs remain authoritative.
Instead, ninety unused disposition minutes support the unstarted RPC outcome's
concrete retained-reply, saturated-capacity and genuine process-crash obligations.
That supported transfer uses the current canonical WORK-006/WORK-008 identities;
the same 64-hour total, 72 attempts and every protected reserve remain unchanged.
The minimal B05 process entry stays inside the existing IntegrationTests/Messaging
scope and reuses ProcessRunner and retained test dependencies; it is not a new
test runner or tutorial application.
A further sixty unused disposition minutes support that existing public-consumer
outcome's helpers, package runtime and skill handoff after observed Part A costs.
WORK-006 and WORK-009 now each have five hours; the containing totals, attempts,
protected reserves and historical costs remain unchanged.
After the actual Part A tasks settle, twenty remaining publication minutes support
the public package handoff and thirty minutes each from store/disposition support
RPC implementation. The table now reflects the current canonical distribution;
earlier task prose retains the distribution at its creation as history. No outer
allowance or protected reserve changes. The independent DB v2 installer introduces
the required reply-anchor shape and explicitly rejects v1; wire v1 is unchanged.

The optional RabbitMQ assembly borrows the immutable internal encoded memory
through the same narrow friend-assembly precedent as existing optional
integrations. The owning publisher retains its byte lease through actual native
settlement. This removes a redundant provider copy and nested byte reservation;
Core acquires no RabbitMQ SDK reference. Public message getters remain detached.

After actual storage acceptance, unstarted dispatch source may proceed against
the frozen provider interfaces while the broker fixture is corrected. Its final
acceptance still requires completed provider work and real integrated delivery.
The necessary RM-03 reconciliation adds a narrow expected-binding check on the
exact original context before the existing Operations coordinator performs
authorization/journal/effects. It preserves the public Operations API, one-shot
admission and module ownership. Optional typed retry validation receives retained
trusted provenance without caller impersonation; one shared reserved Core reply
alias prevents collision with business subscriptions. These remain dispatch
requirements under the existing delegation and allocation.
The same source sequencing permits the unstarted disposition task to use the
accepted SQL store and now-frozen dispatcher seam in parallel with handler work.
Its final acceptance still requires completed publication and dispatch, including
actual integrated handoffs. Runtime startup and native callbacks remain sequential.
Hosting and Metrics source may likewise use compiled frozen module ports while
their native tests run. Its Part A completion remains gated on completed
publication, dispatch and disposition tasks and actual lifecycle/exporter/package
proof. This overlaps independent source work; it does not reorder startup or
claim early delivery. Existing public constructor signatures remain available
when adding Metrics composition metadata.

Part A must establish A01–A12 through actual SQL/broker entry points before its
completion checkpoint. Part B must establish B01–B06 on that machinery. One
actual exported-metrics and public-package separation check covers each part;
reuse unchanged evidence only when material inputs match. Retain bytes/records/
shape/concurrency and one representative end-to-end observation, separately
from installation/unit evidence. No broad benchmark, mutation or release campaign.
Any unavailable platform/deployment proof is stated explicitly, never inferred.

This plan excludes push, merge, release and external deployment. Completion of
the module does not automatically close the dossier or alter historical owners.
