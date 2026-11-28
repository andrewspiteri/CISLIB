---
title: "WORK-006 RM11 durable start and completion audit"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:720483f705d94af9a4d4eaf714deb3f7"
depends_on: ["WORK-002","WORK-005"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-AUDIT"]
authorization_scope_basis: "sha256:ad578cc9ea45d4ed4a545f841ecd8b5dbc092acbd364862f8ab38138cf7aec6c"
---

# WORK-006: RM11 durable start and completion audit

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 durable start and completion audit

## Acceptance criteria

- [x] Durably accept typed start before evaluation and completion before result release. Preserve host provenance, correlation, versions, timestamps, exact supplied values and observed trace. Preserve terminal selection and bounded finalization after cancellation. Cover RM11-AC25 and AC26.

## Targeted validation

- [x] Prove early refusal, failure after earlier matches, selection/cancellation races, finalization failure or unknown, and a committed outbox while the reporting destination is unavailable using existing RM06 fixtures.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Scoped Release and native Audit outbox | Passed | .NET SDK 10.0.401, zero build warnings and errors. Seven focused `RuleEngineAuditIntegrationTests` cases passed against pinned PostgreSQL 18.7, zero failed or skipped. `.cis/local/testing/rm11-management-isolated/results/rm11-durable-invocation-audit.trx` SHA-256 `16f628cc38af04949508134f2dc6618b2930d363aabcb231ce648afe4e8c4f86`. |

The cases inspect original source outbox bytes for typed inputs, local instance provenance, matched-before-failure observations, terminal state and causation. They confirm no completion is attempted after an unconfirmed start, and no selected match is released after a real unknown completion even when its row survives. Cancellation before terminal selection is recorded with an independent bounded token. Cancellation after selection leaves the durable chosen status intact. The source outbox tests run without a reporting consumer. Receiver ingestion and searchable history are WORK-007.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3835; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:384446b326c51bebf6db616f5e9bf95482dda65a70d0f5b27df1bd60dcda39da |
