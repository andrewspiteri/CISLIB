---
title: "WORK-003 Implement provider bookkeeping and serialized Configuration refresh/lifetime"
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
instance_key: "outcome:5f07cf43d3f94718b455745da3683e6b"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0008:outcome:cb8dfe5d5f71451f94bc5d7743cfcb5e"
authorization_scope_ids: ["configuration"]
authorization_scope_basis: "sha256:0321dc009b4b64ac159f1b1d5dcd1f25709f61e15500133956d713330e600af1"
---

# WORK-003: Implement provider bookkeeping and serialized Configuration refresh/lifetime

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement provider bookkeeping and serialized Configuration refresh/lifetime

## Acceptance criteria

- [x] Implement host-supplied provider reads and per-entry source cache using Found/Missing/Unchanged/Failure, keeping source tokens separate from accepted effective values and invalid conditional state. One serialized retrieval-to-notification path, bounded pending flags/runners and subscribe/reconcile/reconnect handling serve all triggers. Commit/cancel/stopping admission is coordinated; pre-commit cancellation preserves values and accepted commits finish ordered notifications outside the gate, isolating synchronous handlers. Close triggers/subscriptions, cancel owned provider work and drain actual operations without disposing host-owned adapters/POCOs. W3 owns tests/docs/review, not a production store or a scheduler.

## Targeted validation

- [x] Controlled native races for stale local read exclusion, pending-to-idle handoff, watch activation/reconnect gaps, masked source bookkeeping, invalid-token-plus-Unchanged, unsuccessful read versus deletion, no extra initial/unchanged/failed events, after-commit cancellation and handler read/reentrancy/error isolation. Stop while provider/notification held proves actual drain and no post-stop commit; bound work by entries. Use controlled W1 prepared-value seam beside W2, then bind actual pipeline at W4. Record provider fixture truth and exact actual results/cost.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Refresh/lifetime native cases | 27 passed, 0 failed, 0 skipped; Core/Unit compiled | configuration-refresh-unit retained receipt/TRX and four Unit files |
| Source bookkeeping, serialized work and actual drains | Masked/invalid conditional caches, trigger handoff, watch acquisition gaps, committed notifications, provider/handler/cancellation/disposal holds | ConfigurationStorageTests, ConfigurationRefreshCoordinationTests, ConfigurationRefreshLifetimeTests |
| Source review and corrections | Required load-publication race corrected; handler diagnostics separate from validity; acquired-watch detach precedes activation drain | Independent read-only lifecycle review; docs/modules/configuration/refresh.md |
| Scope boundary | Controlled provider/value seam; no production backend or periodic scheduler claimed | WORK-004 composes actual mapping, Type Manager, Startup, Heartbeat, health and package consumers |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=922; failed=132; possibleTokenSavings=123; ledgerDigest=sha256:1c6857db342fe932182d0897894b43631b032b2f70ee5710226f02f7aafbc14a |