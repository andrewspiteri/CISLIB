---
title: "WORK-001 Deliver public application service boundaries and ownership-safe aliases"
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
instance_key: "outcome:f8341823620746be88fe48990b95d170"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0012:outcome:0c9a2fe77bba4a18a1c212c9197d54f2"
authorization_scope_ids: ["public-boundaries"]
authorization_scope_basis: "sha256:709b0e620959df1f9c5222616e7bd81d771d3cd72fee6008303f4898ae74fefa"
---

# WORK-001: Deliver public application service boundaries and ownership-safe aliases

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver public application service boundaries and ownership-safe aliases

## Acceptance criteria

- [x] P02-P04: public owner factory/owner and optional monitored-registration capability preserve concrete APIs and existing IHeartbeatService implementations. The ordinary extension dispatches publicly; production keeps all atomic Core binding/weak-ownership/settlement checks. Default monitor/snapshot/owner-factory aliases share actual state, respect explicit overrides/removal/disable choices and borrowed instances without multiplying cleanup. Existing guarded REST context/result construction and existing disabled Metrics become publicly usable. Include tests/docs/independent review.

## Targeted validation

- [x] Alias identity/default/replace/borrowed ownership and idempotent cleanup tests; existing public implementations still compile, Core public result invariants/absence versus present-null remain, real monitored binding negative and actual drain tests retain semantics. Document manual-plus-recording substitute pair versus unchanged production binder/hosted lifecycle boundaries.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Focused native and review | Passed | 1725 passed, 0 failed/skipped; core-corrected-unit.trx and core-corrected-native-run.log in ignored application-followup-intake. Production review by Codex and independent agent; no scheduler/disposal engine replacement. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1540; failed=227; possibleTokenSavings=591; ledgerDigest=sha256:651ff9b0fdf0c71e6ce59fe9a70532c4e5a37e6b2e9091cb90a6099db1fda60a |
