---
title: "WORK-002 Share workflow host admission and actual State Machine lifetime drain"
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
instance_key: "outcome:df54038ad2c54925828a0571586c87b8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0014:outcome:a6e73e152ddb4ab7bb2d053a47aafdfe"
authorization_scope_ids: ["hosting"]
authorization_scope_basis: "sha256:ff4a987d10f020b106ea47c71f6e076deb98a1e3d859b2f574c8ff26dbca8da1"
---

# WORK-002: Share workflow host admission and actual State Machine lifetime drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Share workflow host admission and actual State Machine lifetime drain

## Acceptance criteria

- [x] P08: explicit State Machine factory, per-call borrowed-scope adapter, existing shared workflow owner/bridge and real host/facade/failed-Build drain; include held-work tests, documentation and review.

## Targeted validation

- [x] Actual native and facade hosts, both workflow opt-ins and only State Machine; one provider/owner/bridge, listener-before-drain and business dependency lifetime through held Error/disposal; canceled waits/failed startup/late Build cleanup; closed-host misuse and exact borrowed per-call scope.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual shared lifetime and scope integration | Passed:22, failed:0, skipped:0 | Ignored .artifacts/statemachine/CIS-0014/host/host.trx:13 new State Machine and9 retained Pipeline cases; native/facade/web/failed-Build/actual disposal, clean corrected Integration build, independent review and current hosting guide. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1662; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:b022b6a678c8ac903e7bedbd060eb6dd6b170f0c02e1dc65b28e499c53705a71 |
