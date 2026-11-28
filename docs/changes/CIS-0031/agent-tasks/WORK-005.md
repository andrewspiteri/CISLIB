---
title: "WORK-005 RM11 application adoption and exact-version evaluation"
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
instance_key: "outcome:0ae6fb8a191f4db78d6fd49e79a37399"
depends_on: ["WORK-002","WORK-004"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-RUNTIME","RM11-MANAGEMENT"]
authorization_scope_basis: "sha256:ad6740c0c7214a820b19730f4651b54167965ae683c01d9a5ed884b9adff4c0e"
---

# WORK-005: RM11 application adoption and exact-version evaluation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 application adoption and exact-version evaluation

## Acceptance criteria

- [x] Validate and atomically adopt an executable version through the existing host path. Capture one immutable version per invocation, report requested/applied/rejected status, and enforce same-scope exact history, current host admission and retained failure behavior. Cover RM11-AC18 and AC20.

## Targeted validation

- [x] Use native host/provider cases for concurrent adoption, in-flight version capture, distinct instances, accepted older exact version with newer active, incompatible/unavailable/foreign exact reference, and bootstrap/recovery/outage.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Locked standard Release build | Passed | .NET 10.0.401, zero warnings or errors. The test-only Scheduler imports already recorded under WORK-003 were supplied to the existing test assembly. |
| Native retained-version provider and deployment regressions | Passed | Five passing cases in `.cis/local/testing/rm11-management-isolated/results/rm11-host-adoption-provider.trx`, SHA-256 `ba83c728fdac34d49eeb3f3fa16c5808d7f197583585f45cb8b773853ad3e300`. The report also retains the first two-host failure. |
| Independently managed accepting and rejecting clients | Passed | The corrected focused two-host run passed on native PostgreSQL 18.7/RabbitMQ 4.4.1. `.cis/local/testing/rm11-management-isolated/results/rm11-host-adoption-recovery.trx`, SHA-256 `dd8e70b0d3419554d13d4575dfb5d0134d68d6ed31e4e39d6b7e104f40857229`. |

The two-host fixture supplies the same manual clock to Entra preparation, Management bootstrap and final DI, and explicitly awaits native Configuration reconciliation between recovered control checks. Earlier captured definitions remain immutable across later acceptance. An unsupported desired schema yields a real rejection with the previous version still intact. The separate exact-read provider case preserves definition eligibility and release-to-version provenance after carrier cleanup. The bounded in-memory selection cache holds up to 16 immutable same-session versions, with older selections resolved through the authorized native witness when available.

The public evaluator's durable execution and result boundaries are implemented under WORK-006 using this capture seam.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3832; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:e9bc9ff5fd88df4e70d552b2a1b5590503c240c6f7f58dfcc84f0a625e3bcd14 |
