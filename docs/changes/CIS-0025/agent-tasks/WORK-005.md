---
title: "WORK-005 Apply managed admission to Messaging HTTP and readiness"
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
instance_key: "outcome:d7d34d95026f4c538ddcc9a64208c59f"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0025:outcome:56e9417305534bad8bc8776094f659b8"
authorization_scope_ids: ["ingress","managed","records"]
authorization_scope_basis: "sha256:251964505721eeeadc029cc62bbeddd593b5b85798e2ce2e008dc86a1b3b21f4"
---

# WORK-005: Apply managed admission to Messaging HTTP and readiness

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Apply managed admission to Messaging HTTP and readiness

## Acceptance criteria

- [x] New application inbox/handler admission requeues safely; native replies/outbox/original recovery stay open. Explicit effect HTTP gate and readiness-only local composition preserve liveness. Shared ports frozen on disk before parallel source; integrated native verification waits for matching implementations.

## Targeted validation

- [x] Focused dispatcher/native-reply/HTTP/readiness regressions, no global transaction gate or second server; docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Ingress and readiness | Passed: six Messaging cases including held RM03 claim/replay, readiness publication/metrics and actual authorized effect/status HTTP | [Verification](../verification.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2354; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:0f78fe44abb5eab13214de2cf53b663c20fe94f08f8710e7f61ab872c986362c |
