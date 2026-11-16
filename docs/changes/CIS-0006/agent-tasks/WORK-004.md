---
title: "WORK-004 Extend detached Healthcheck facts and implement Heartbeat policy binding"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:8b607b94ddc548408cad0b458c5b28d3"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66"
authorization_scope_ids: ["heartbeat"]
authorization_scope_basis: "sha256:7be3625710d72f724d009b5375a3779a25ee5f2d9cab24c29dd6bb63fe8284e8"
---

# WORK-004: Extend detached Healthcheck facts and implement Heartbeat policy binding

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Extend detached Healthcheck facts and implement Heartbeat policy binding

## Acceptance criteria

- [x] AC-5/AC-7: append Dispatched/Suppressed facts and optional raw boolean/cancellation/timing context without changing existing enum values, outcomes or phase strings. Merge immutable bounded context, retain neutral suppression without policy arithmetic, preserve cleanup-error precedence in one final Outcome and distinct settlement. Select existing monitor/consecutive-failure/availability bindings once, isolate reporting failures, preserve external/created-monitor ownership and old reports. Keep live configuration in target-dependent graph and safe concrete binder available for collection retirement. Include affected receiving/wire/compatibility tests, docs and independent review.

## Targeted validation

- [x] Build and run focused existing/new native Healthcheck binder, fact validation, snapshot, formatter/protocol and compatibility cases for dispatch/suppression/result context, duplicates, contradictions and optional legacy payloads. New Healthcheck tests can run before engine binding integration; complete real adapter tests with engine before task completion. No forced old baseline rewrite or invented durable delivery guarantee.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Receiving contract/binding and independent review | Append-only dispatch/suppression facts, optional wire context, detached diagnostics and real Core monitor ownership implemented and reviewed. Existing policy arithmetic stays in Healthcheck. | Healthcheck and Heartbeat adapter source; [amendment](../../../../planning/healthcheck/heartbeat-facts-amendment.md). |
| Actual native verification | 1,563 affected Healthcheck regression cases and 129 compatibility cases passed; zero failed/skipped. Applicable real Heartbeat binding/wire cases passed in the final 171-case module selection. | `.cis/local/testing/incremental/heartbeat-healthcheck-regression/reports/file-e7e4df149e0949e4a8a2-unit.xml`; `heartbeat-compatibilitytests` and `heartbeat-module-unit` corresponding reports. |
| Owned settlement | WORK-004:1 closed with actual results admitted and no active native reservation/recovery hold. Inventory refusal/history and charges preserved. | Canonical work ledger; [verification](../verification.md). |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=691; failed=108; possibleTokenSavings=123; ledgerDigest=sha256:64fe7b3563a18c0ed5e659fdfaf826420702bf94ddfd9095af2eab20935c89df |
