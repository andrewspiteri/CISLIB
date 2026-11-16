---
title: "WORK-003 Complete Heartbeat cutoffs, retirement and true owner settlement barriers"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:e4eafe4e9a364dd2ac411cc70f602991"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-003: Complete Heartbeat cutoffs, retirement and true owner settlement barriers

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete Heartbeat cutoffs, retirement and true owner settlement barriers

## Acceptance criteria

- [x] AC-3/AC-4: unregister/owner close drain accepted queued/running work; global cutoff additionally suppresses condition/callback phases that have not won their start gate. Await posted wrappers, handlers, owned cleanup and reporting before barrier completion, with caller-only wait cancellation and no automatic execution cancellation. Retain identity until true drain, support sequential reuse, safely retire collected/unusable targets and report once through an independently retained safe Core binding. Detect direct own-barrier waits. Include deterministic boundary/race/reachability checks and review.

## Targeted validation

- [x] Run focused native tests with blocked phases, paused wrappers, already-canceled waits, concurrent stop/unregister, owner-close/admission races, cleanup/reporting faults and full target/condition/owner/exception graph GC. Assert no callback after losing a start gate, no early owner/slot release and exactly-once collection retirement while an external monitor survives.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Cutoff/settlement implementation and independent review | Ordinary accepted-work drain, global phase suppression, true owner/slot settlement, weak retirement, waiter-only cancellation and direct self-wait guards implemented and reviewed. | HeartbeatRuntime, HeartbeatOwner, HeartbeatExecution and [verification](../verification.md). |
| Actual shared native verification | Applicable drain/weak ownership/infrastructure/race cases passed in the final 171-case WORK-001 selection, including both controlled admission/global-cutoff orders and detached exception-graph collection. Zero failed/skipped in that selection. | `.cis/local/testing/incremental/heartbeat-module-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml`. |
| Evidence reuse | Reuse actual shared evidence; no separate native launch, cost or used attempt is claimed. Host/real-scope integration remains WORK-005. | Canonical work ledger; [verification](../verification.md). |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=690; failed=108; possibleTokenSavings=123; ledgerDigest=sha256:a6b26fc5308e782f6faaf623177ce1d63e9a02778b9662ef17ebcf74d2485332 |
