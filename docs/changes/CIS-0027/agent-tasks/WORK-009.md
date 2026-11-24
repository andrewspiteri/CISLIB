---
title: "WORK-009 Report and project actual configuration adoption on existing owners"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-009
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:262a946b18704c9b8def709dc9771502"
depends_on: ["WORK-002","WORK-003","WORK-004","WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["adoption","backend","records"]
authorization_scope_basis: "sha256:fe1bcd118cdb1eae9191649a0b607e73a9c151ef850bfa701b9f6222c1b43d21"
---

# WORK-009: Report and project actual configuration adoption on existing owners

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Report and project actual configuration adoption on existing owners

## Acceptance criteria

- [x] Separate complete sequenced config-status stream shares original reporter, one pending uncertainty latch and safe acceptance capture; native owner/session checks and independent receipt freshness, current assignment/cohort projection and explicit faulted/unknown/stale/offline/no-instance states.

## Targeted validation

- [x] Provider and local report replay/conflict/receipt tests, original workload ownership, failed-poll preservation, current map convergence and no operator ETag changes; fixed metric dimensions with mandatory app/environment and no instance/registration/release IDs, unbounded names, payloads or exception/reason text; safe status/diagnostic docs. WORK006 composed same-session reporting/cleanup remains a completion join, while server/projection and isolated reporter source may proceed independently with explicit file ownership.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Reporter behavior | Included in the passing 1,200 affected Core units: independent sequence, one uncertain capture, exact retry and held Stop join | WORK-006/current-host-order-affected-unit-tests/results/current-host-units.trx |
| Real adoption and affected provider behavior | All 79 selected PostgreSQL/HTTPS cases passed, including 12 adoption cases and three status commit/cleanup uncertainty rows | WORK-009/adoption-retention-provider-tests-corrected/results/adoption-retention.trx |
| Scope and bounds regressions | Actual legal Unicode multi-page coverage and guarded host-change/history-unknown tests passed; exact native owner, freshness, sequence, quota and signed cursor checks retained | Same native receipt; ../../../modules/management/configuration-adoption.md |
| Composed host and cleanup join | Five actual bootstrap/reconnect cases and the real status/session cascade retention case passed | WORK-006/native-bootstrap-live-authority-final-tests; WORK-009/adoption-retention-provider-tests-corrected |
| Review and formatting | Both required independent findings resolved and verified; final scoped formatting verified | ../review.md; native-costs.jsonl |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2441; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:129ef72b8d190065b72dc94727192f442230040c879c24e160aa8e55d91578f7 |
