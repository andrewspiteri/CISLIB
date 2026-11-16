---
title: Heartbeat implementation scope and limits
type: delivery-plan
status: Draft
change_id: CIS-0006
authority: derived-outcome-authorization
---

# Heartbeat implementation scope and limits

**Change:** CIS-0006. **Status:** Authorized implementation; native verification and canonical task closeout remain separately recorded. **Date:** 16 November 2026.

The user authorized the Heartbeat outcome and shared limits recorded in
[authorization.json](authorization.json), decision
`CISLIB-HEARTBEAT-2026-11-16:USER-GO-AHEAD`.
Local implementation now exists in the single `CIS.Core` package and assembly.
This document records scope, task organization and ceilings; it does not declare
any task Complete, allocate another attempt, or certify release qualification.
Use [plan.json](plan.json) for canonical task state and the
[verification record](verification.md) for actual commands, outcomes and limits.

## Requirements and implemented responsibilities

The [supplied revision 0.2 and source identity](../../../planning/heartbeat/README.md),
[technical intent](../../../planning/heartbeat/technical-intent.md),
[Healthcheck integration](../../../planning/heartbeat/healthcheck-integration.md)
and [70-case mapping](../../../planning/heartbeat/acceptance.md) define the accepted
implementation direction with the current repository precedence.
[Proposal acceptance criteria AC-1 through AC-7](proposal.md) describe the outcome.

The local source implements one application engine, weak-key target bindings,
explicit owner holds, monotonic completion-based scheduling, FIFO pending work,
bounded dispatch, separate phase-start gates, unregister/global drains, detached
Healthcheck reporting, narrow Generic Host coordination and an actual DI scope
wrapper. The subscriber instance remains the consumer handle. Required lifecycle
and disposal ordering remain cooperative application responsibilities.

Preserve actual `CIS.EnvironmentOptions`, its readonly static module reference
and a validated runtime copy of mutable Heartbeat setup. The attachment's
`EnviormentsOptions.Current` spelling and obsolete initialization/dependency model
do not override current accepted architecture. Keep the approved Microsoft
dependency closure and `Microsoft.AspNetCore.App`; add no runtime dependency on
CIS, test tooling or external orchestration. Source assumptions A01–A05 remain
visible assumptions, not fabricated historical human approvals.

## Canonical executable work

The earlier unnamed planning packages have been reconciled into these actual
canonical tasks. Their contracts, dependency edges and status live in
[plan.json](plan.json) and the linked task records; this table is not a second
task-state authority.

| Canonical task | Owned outcome, including necessary verification and review | Dependencies | Current allocation |
| --- | --- | --- | ---: |
| [WORK-001](agent-tasks/WORK-001.md) | Consumer contracts, copied settings, reference identity, weak binding graph and actual owner protocol; configuration/identity/reachability verification. | None | 3,900 s |
| [WORK-002](agent-tasks/WORK-002.md) | Monotonic eligibility, fair admission/dispatch, bounded operations, exclusive asynchronous execution and fixed-delay recurrence; controlled scheduling/outcome checks. | WORK-001 | 2,700 s |
| [WORK-003](agent-tasks/WORK-003.md) | Unregister, owner/global cutoffs, suppression, retirement, true settlement, wait cancellation and direct self-wait rejection; deterministic drain/fault checks. | WORK-002 | 1,000 s |
| [WORK-004](agent-tasks/WORK-004.md) | Minimal additive Healthcheck dispatch/suppression/raw-result facts, compatible receiving/projection behavior and owner-safe policy binding; affected contract checks. | Shared contracts | 2,400 s |
| [WORK-005](agent-tasks/WORK-005.md) | Readiness/stopping adapter, actual owner scope wrapper, polling example, guides and real host/business/source/package/folder-publish checks. | WORK-003, WORK-004 | 2,400 s |
| [WORK-006](agent-tasks/WORK-006.md) | User-reported Type Manager compatibility correction: deploy the approved shared framework without changing frozen inputs, discover the pinned private API host, preserve both negative deployment checks and run actual full compatibility tests. | Original module checkpoint | 2,000 s |
| **Shared total** | All agents, implementation, verification, review and settlement. | | **14,400 s** |

The original distribution moved **1,500 seconds from WORK-002 to WORK-001**
relative to the initial 2,400/4,200-second split. It leaves the authorized
14,400-second outcome ceiling unchanged. The subsequent WORK-006 correction moves 2,000 unused seconds from WORK-003 and WORK-005's unused second attempt to WORK-006; each task now has one attempt, with all previous consumed work and used attempts preserved. All six tasks are Complete; the full corrective compatibility selection passed 179 cases as recorded in [verification](verification.md). It grants no fresh aggregate capacity to a
subagent, native invocation or review. Actual consumption, reservations and
logical attempts remain in the canonical work accounting.

Each task includes its necessary tests, documentation, corrections, independent
review, accounting and completion checks. Keep executable leaves at most medium;
do not manufacture separate test/review tasks for those ordinary obligations.
Coordinate builds sharing project/output directories and preserve the identity
and accounting of all completed CIS-0001 through CIS-0005 work.

## Shared authority and resource boundaries

- **14,400 active seconds** are shared across the whole outcome, including
  **300 protected restoration/settlement seconds**. The reserve is within that
  total, not additional time or a performance promise.
- **6 logical attempts** are shared across the whole outcome. Supported CIS
  accounting controls actual use; a new process or subagent receives no new
  independent attempt allowance.
- **6 GiB (6,442,450,944 bytes)** is the physical shared storage ceiling under
  reference **`CISLIB-CIS-0001-STORAGE-2026-11-10`**, including already present
  counted files and **64 MiB (67,108,864 bytes)** of protected restoration space.
  This is a shared physical bound, not another 6 GiB per module, task, agent or
  invocation. Count existing occupancy before reservations and avoid counting
  the same physical files twice when reconciling overlapping roots.
- Declared roots are `.artifacts/heartbeat/CIS-0006`, `.cis/local` and
  `implementations/csharp`. Count-only/native-scratch schema markers confer
  **no source, user-file, historical-output or general deletion authority**.
  No cleanup controller is delegated. Preserve earlier tooling, receipts,
  reports and unrelated output. Any necessary cleanup remains limited to
  generated native scratch actually owned by this change, within verified
  workspace paths and the recorded authorization.

The current allocations and shared storage reference do not reopen previous
completed owners, change their limits, rewrite their costs or create additional
storage capacity. Stop for a specific new decision if the remaining finite
capacity cannot support safe settlement.

## Verification, completion and exclusions

Use the pinned .NET 10 toolchain and existing C# MSTest projects. Necessary
verification includes controlled time/executor/owner tests, actual host and scope
behavior, existing affected EnvironmentOptions/Healthcheck contracts, compatible
receiving/wire changes and the exact local development-package consumer.
Ordinary regression selections retain
`TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress`.
Native commands run under their legitimate work association and finite remaining
allocation. Source inspection, constructed fixtures or a build are not substitutes
for actual runtime evidence.

Implementation and some focused local checks have occurred. Further module,
compatibility, host/package and business observations must be taken from
[verification.md](verification.md); a pending or failed check is not a pass.
This plan intentionally avoids copying live counters or asserting a final
completion state before the native results and accounting are settled.

The supported execution evidence for this outcome is the actual local Windows
environment. No Ubuntu pass follows from Windows tests, source inspection or
fixture construction. Actual platform/package qualification, 95% line / 90%
conservative branch coverage, 90% conservative mutation, useful performance
observations and all other accepted release gates remain separate obligations
unless individually executed and recorded under applicable authority.
No full coverage/mutation/stress/performance campaign, external source transfer,
package publication, main merge, generic startup framework, watchdog, automatic
retry, GmCore compatibility shim or additional deployment profile is included.

Final closeout requires actual evidence and unresolved limits, completed owned
work, correct scope/root disposal ordering, preserved dependency/contract
boundaries and settled consumption/reservations. Retain real failures and used
attempts. Do not force result reuse or rewrite earlier reports.

## Initial proposal checkpoint — historical

On 16 November 2026, intake prepared a Proposed dossier and Draft seed without
implementation authority. The planner then reported unresolved seed placeholders
and missing outcome authorization. At that checkpoint no Heartbeat runtime code,
build, test, pack or native execution had occurred. Those statements describe
preparation, not the subsequently authorized implementation.

The initial planning estimates were 2,400/4,200/3,000/2,400/2,400 seconds for
contracts/ownership, timing/execution, cutoffs/settlement, Healthcheck and
application integration respectively. They were packages rather than fabricated
canonical task IDs. The current task table and redistributed allocation above
supersede those estimates while retaining the same overall ceiling.

The original [authorization-proposal.json](authorization-proposal.json) remains
the preserved request, not the canonical approval or present accounting.
[authorization.json](authorization.json) records the real go-ahead; the actual
task contracts, allocations and verification supersede the earlier preparation
warnings. No new human approval of each derived task view is implied.

Read-only intake observed 712,128,324 bytes in `.cis/local` and 4,456,861,409 bytes
in `implementations/csharp` (5,168,989,733 bytes by that simple file-length sum).
These historical observations are not current free-space, deduplicated physical
occupancy, peak storage or performance evidence. Current reservations and the
shared storage reference govern further work.
