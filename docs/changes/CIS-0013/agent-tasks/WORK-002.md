---
title: "WORK-002 Integrate pipeline admission and actual host lifetime drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:843bde8a1237418c9061af45632b0560"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0013:outcome:9c519b131ea94ffd9a16e9ad0e8c1eaa"
authorization_scope_ids: ["hosting"]
authorization_scope_basis: "sha256:134f9936e84c90afc1500475318d73ed2f76ee19023624ab3d2635a5963f8198"
---

# WORK-002: Integrate pipeline admission and actual host lifetime drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate pipeline admission and actual host lifetime drain

## Acceptance criteria

- [x] P07: idempotent opt-in factory on one actual provider; close admission and drain after listener before dependent business teardown, native/facade/failure cleanup; preserve Startup independence and existing module shutdown order. Include held-work integration tests and host documentation.

## Targeted validation

- [x] Actual native web/nonweb facade and failed startup/late Build/canceled host waits with held accepted work. Listener precedes drain, business teardown follows drain; dependencies remain usable through real settlement. Reject unsupported bridge order; Startup coordinator remains independent.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Host lifetime | Passed:9 new held-work cases and141 affected integration regressions | host-first-results/unit.trx and regression-results/unit.trx; real listener, actual provider, failed startup/Build, independent review and scoped formatter verified. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1616; failed=235; possibleTokenSavings=591; ledgerDigest=sha256:a193c8674f2cb2ea9a165ab33f1c0cb1ce4fb683ad8b60c6cbcaefcf60884264 |
