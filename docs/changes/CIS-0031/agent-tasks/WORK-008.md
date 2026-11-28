---
title: "WORK-008 RM11 hit counters and audited resets"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:eea761c9c39540e598b9841f9374895e"
depends_on: ["WORK-003","WORK-007"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-COUNTERS"]
authorization_scope_basis: "sha256:6a1cca706d90408461836e9abfb4efd4d0e676d9f3bac97a47e25c39bb04e855"
---

# WORK-008: RM11 hit counters and audited resets

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 hit counters and audited resets

## Acceptance criteria

- [x] Project observed hits idempotently across instances and versions. Order reset and increment by first durable acceptance per counter and expose per-counter outcomes, reset time and freshness. Cover RM11-AC21–AC23.

## Targeted validation

- [x] Race the real store across instances and verify pre-reset replay, post-reset first acceptance for an earlier invocation, recovery, actual counter outage independent of audit success, partial bulk outcomes and one hit per execution/rule.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Counter ordering, resets and recovery | Passed | .NET 10.0.401 Release, PostgreSQL 18.7, RabbitMQ 4.4.1 TLS, and two Management replicas. The exact `NativeCountersOrderFirstAcceptanceAcrossResetAndRecoverFromProjectionFailure` case passed, with zero failed or skipped. It exercises replay, an older invocation first accepted after reset, earlier matches retained after a later evaluation error, archived RuleIds, rollback, stale counters during a native SQL write failure, recovery, and original-key results for one, selected and all counters. It reads the three original native Audit records with exact actor, resource, operation key and reset time, and verifies paged full-reset outcomes. `.cis/local/testing/rm11-management-isolated/results/rm11-authoritative-counters-audit.trx` SHA-256 `2257d2c8850ee206b7d420513375195982b1aa86f2af9d4712398c3f3e01c956`. |
| Typed Management client | Passed | Pinned Node 24.16.0 build and 13 focused tests. Counter values retain exact integer text, optional availability facts and scoped original outcomes. |

Counter projection uses a short native transaction after Management durably receives the audit. The ruleset lock orders counter acceptance and reset. A full reset retains the identity revision it covered, so later saves leave the original outcome pages intact. Freshness describes the configured intake and current received backlog for that application and environment. A count omitted during a stale observation is unknown. Reset actors and receipts use the existing Management operation and Audit path.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3841; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:84d2044ef1eb01a7eb24a268e121b1846f93043c005a99a350dfe06474286de1 |
