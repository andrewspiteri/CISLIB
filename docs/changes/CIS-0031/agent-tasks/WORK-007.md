---
title: "WORK-007 RM11 authorized execution history"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:fef0b17bb34d439f9fb112e6588bc7ca"
depends_on: ["WORK-003","WORK-004","WORK-006"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-AUDIT","RM11-MANAGEMENT"]
authorization_scope_basis: "sha256:d346500c123ac8bdd335d942f353d06b0ebfe809abcec763836478c8469d807f"
---

# WORK-007: RM11 authorized execution history

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 authorized execution history

## Acceptance criteria

- [x] Ingest durable start/completion idempotently on the existing provider. Supply permission-filtered bounded query and detail with exact immutable definition evidence and truthful incomplete or interrupted status. Cover RM11-AC24, AC27 and AC28.

## Targeted validation

- [x] Exercise authenticated delivery, exact duplicates and conflicts, out-of-order observations, broker/store outage and recovery, retained definitions, actual interruption, scoped grants and cursor isolation using native fixtures.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Authenticated execution history | Passed | Locked .NET 10.0.401 Release build with zero warnings or errors. One focused native history case passed on PostgreSQL 18.7 and RabbitMQ 4.4.1 over TLS. Includes receiver outage, completion-first delivery, duplicate and conflicting occurrences, saved-only replay refusal, real child process interruption after durable start, exact retained definitions, incomplete status, cancelled cell trace, native list/detail permissions, pagination, and real completed counts of zero and one. `.cis/local/testing/rm11-management-isolated/results/rm11-authenticated-history-match-count.trx` SHA-256 `c0d5b6b59f0c8bdef0103e3f4e5a6f4ac5028dab84c497507fb89ffc54a96274`. |
| Typed execution history client | Passed | Node 24.16.0 and 11 focused RM11 client cases. Raw audit evidence, submicrosecond times, original phase causation, immutable definition identity, completion-only state and returned counts passed. |

The native receiver preserves permanent domain refusals with the original transport transaction. A refused saved-only occurrence stays refused after a later assignment, while a previously accepted original remains recoverable if another body claims the same Audit ID. The actual terminated producer left a committed source event that was recovered without inventing a terminal result. The scoped query reports that execution as Incomplete.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3837; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:4e0bc3f4928520ae723eddd706981bd69b1183677ef2d1178f42c3cfc131c344 |
