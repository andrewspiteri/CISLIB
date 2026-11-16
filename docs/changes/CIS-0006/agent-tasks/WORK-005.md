---
title: "WORK-005 Integrate Heartbeat readiness, safe DI scope shutdown and developer package example"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:08b3429528b849d38a5e9bf869360752"
depends_on: ["WORK-003","WORK-004"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-005: Integrate Heartbeat readiness, safe DI scope shutdown and developer package example

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate Heartbeat readiness, safe DI scope shutdown and developer package example

## Acceptance criteria

- [x] AC-6/AC-7: provide a narrow application-owned Generic Host adapter and actual owner-bound scope wrapper: ready dependencies precede activation, no ApplicationStarted self-deadlock, both stopping paths close starts, actual drain precedes dependent stopping and scope/root disposal even after canceled waits. Deliver hosted polling example from source and exact development package in ordinary build/folder publish; include false/exception/refresh/stop observations, concise usage, native host/business/package checks and integrated independent review. Preserve existing module behavior and all historical owners/evidence. Settle actual current accounting; leave all unexecuted platform and release obligations explicit.

## Targeted validation

- [x] Run real local Windows host/scope/business tests covering readiness, early stop, running-condition/callback/error handling and dependency disposal ordering. Build and consume the current development package in the Heartbeat example, including folder publish and executable shutdown. Run meaningful affected architecture/compatibility regression only after source stabilizes. Record all AT mappings and actual evidence, review scope/complexity and verify resources settled. No Ubuntu/release/coverage/mutation/performance claim without actual separately authorized work.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual Generic Host / scope lifecycle | Seven real host/scope cases passed, including startup readiness, early stop, awaited recovery, actual async scope disposal and canceled host stop budget. | `heartbeat-integrationtests` retained report; [verification](../verification.md). |
| Exact source/package consumer | Corrected final integration selection passed 8/8. Actual source, isolated development-package build and folder-publish outputs all executed false/recovery/success/drain behavior using Core SHA-256 `453546e8ada81d7ed0d073544c153ef553a7aa82b973df931f4ea7ad10dbdf08`. | `.cis/local/testing/incremental/heartbeat-integrationtests/reports/file-e7e4df149e0949e4a8a2-unit.xml`; package hash and prior 7/1 failure retained in [verification](../verification.md). |
| Actual business behavior | One polling/availability story passed, preserving original request expiry, exception-only awaited recovery, usable-data publication and real owner-scope draining. | `.cis/local/testing/incremental/heartbeat-businesscases/reports/file-e7e4df149e0949e4a8a2-unit.xml`. |
| Review / documentation / boundaries | All 70 AT rows remain explicit. Independent source/docs review and corrected example cleanup require successful global drain before destructive root disposal; no new Core runtime dependency or verification harness. Actual architecture/compatibility and receiving evidence stays with completed prerequisite tasks. | [Guide](../../../modules/heartbeat/README.md), [acceptance mapping](../../../../planning/heartbeat/acceptance.md), [verification](../verification.md). |
| Resource settlement | Actual native roots/children safely stopped, zero active reservations/recovery holds; finite limits and genuine failures retained. Final measured ledger charges and counted-root observation recorded separately from unmeasured authoring effort. | Canonical CIS work ledger and [verification](../verification.md). |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=703; failed=109; possibleTokenSavings=123; ledgerDigest=sha256:70868d84b7d336e5c61bc564524c96210bf32b8a3bfce773cd4a432101a5a3e8 |
