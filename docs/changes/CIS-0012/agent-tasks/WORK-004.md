---
title: "WORK-004 Retain and dispose the actual provider after late failed native host Build"
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
instance_key: "outcome:22e19c84a1404557bd1c773cc1b2c96b"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0012:outcome:0c9a2fe77bba4a18a1c212c9197d54f2"
authorization_scope_ids: ["provider-cleanup"]
authorization_scope_basis: "sha256:0def5951a9b31a4e31b521e76f7b7a51ced75c1200a02b46fd124a61fec0b979"
---

# WORK-004: Retain and dispose the actual provider after late failed native host Build

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Retain and dispose the actual provider after late failed native host Build

## Acceptance criteria

- [x] P08: one delegating native default provider factory captures exactly the raw provider returned before host resolution; install correct web/nonweb hooks, transfer fallback ownership only after actual host returns. Failed Build joins actual owned async cleanup; no second provider, eager service resolution, lifecycle/disposal engine or borrowed-instance disposal. Retain original startup cause and separate cleanup failure aggregation. Include focused regressions, lifetime guide/correct preparation wording and independent review.

## Targeted validation

- [x] Actual web/nonweb IHostLifetime factory resolves held async DI-owned dependency plus borrowed instance then throws original sentinel. Repeated Dispose joins actual held cleanup once, borrowed disposal remains0, original Start/Failure identity retained; separate cleanup-fault row observable. Existing graph-validation, successful root/module/logging lifetime and caller-wait cancellation checks pass.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Focused native and review | Passed | 1725 passed, 0 failed/skipped; core-corrected-unit.trx and core-corrected-native-run.log in ignored application-followup-intake. Production review by Codex and independent agent; no scheduler/disposal engine replacement. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1543; failed=227; possibleTokenSavings=591; ledgerDigest=sha256:d0bf647b99625c65683d9f90e011b3467943a9be604f43bb7e467936a5614bce |
