---
title: "WORK-002 Implement monotonic fair Heartbeat scheduling and exclusive asynchronous execution"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a9722ad0730c4f42859aa6fdfa75144d"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-002: Implement monotonic fair Heartbeat scheduling and exclusive asynchronous execution

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement monotonic fair Heartbeat scheduling and exclusive asynchronous execution

## Acceptance criteria

- [x] AC-1/AC-2: one application engine uses reference identity/CWT bindings, weak idle entries and a single monotonic timer; at most one accepted execution per registration and MaxConcurrent unfinished dispatched wrappers. FIFO ready work progresses one released slot at a time. Conditions, callback, awaited exception-only OnError, cleanup and reporting retain exclusivity and owner holds; full settlement starts the fixed delay. Register never invokes subscriber code inline; duplicate settings stay unchanged. Include controlled time/executor/capacity/outcome tests, docs and review.

## Targeted validation

- [x] Build the affected native projects and run Heartbeat timing, identity, duplicate-race, non-overlap, fairness, bounded-state, synchronous-prologue and result/error tests using ManualTimeProvider and controlled tasks. Verify no user code or arbitrary owner/reporting callback under scheduler lock, no per-idle-registration task history, no business timeout/retry/health arithmetic.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Scheduling/execution implementation and independent review | Monotonic completion-delay timing, FIFO pending queue, bounded post capacity, per-instance exclusivity and awaited asynchronous outcomes implemented and reviewed. | HeartbeatRuntime, HeartbeatExecution and [verification](../verification.md). |
| Actual shared native verification | Applicable scheduling/outcome/infrastructure/capacity cases passed in the final 171-case WORK-001 module selection, including 100 eligible registrations at capacity 16. Zero failed/skipped in that selection. | `.cis/local/testing/incremental/heartbeat-module-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml`. |
| Evidence reuse | Reuse the actual shared test result; no separate native launch, cost or used attempt is claimed for this task. | Canonical work ledger; [verification](../verification.md). |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=688; failed=108; possibleTokenSavings=123; ledgerDigest=sha256:116269640603e58274279f2b897798a5011a4fda530084a43ce3a3df5d3b373b |
