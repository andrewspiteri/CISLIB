---
title: "WORK-001 Deliver sequential pipeline execution and explicit DI ownership"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:5d11d5e156534c4eadf73eaf6add1eec"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0013:outcome:9c519b131ea94ffd9a16e9ad0e8c1eaa"
authorization_scope_ids: ["execution"]
authorization_scope_basis: "sha256:8cb7879a330c55c4f313628f57f744fef1466af08ca99291267505e20ab720a0"
---

# WORK-001: Deliver sequential pipeline execution and explicit DI ownership

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver sequential pipeline execution and explicit DI ownership

## Acceptance criteria

- [x] P01-P06: sequential single-use pipeline, public contracts, immutable reports, activation and explicit scope ownership, primary failure/cancellation, reverse unwind/disposal; include A01-A12 tests, portable contract and review.

## Targeted validation

- [x] A01-A12: ordered held awaits, first/middle/last failures, duplicate types and single-use, null/invalid results, safe snapshots, all-activation-first, owned/borrowed lifetimes, reverse disposal, exact requested token attribution, independent cleanup budget and all-handler settlement. Preserve original outward faults and no second unwind.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Execution gate | Passed:45, failed:0, skipped:0 | Ignored workflows-intake/core-final-results/unit.trx; actual Release Unit build, scoped format+verify, strict docs203/203 and independent runner/lifetime review. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1608; failed=235; possibleTokenSavings=591; ledgerDigest=sha256:948ae6d03622b67d4ca5e69139d3ea28db0aa74c49a270ed53f62973e0e90555 |
