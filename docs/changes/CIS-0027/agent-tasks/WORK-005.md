---
title: "WORK-005 Prepare and adopt actual native bootstrap credential resources"
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
instance_key: "outcome:540025a6712641a497b9f5d07bedb961"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["security-bootstrap","records"]
authorization_scope_basis: "sha256:0e39e9937f99939270de861511db50e42563eaea643070a898a58d8a7b999846"
---

# WORK-005: Prepare and adopt actual native bootstrap credential resources

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Prepare and adopt actual native bootstrap credential resources

## Acceptance criteria

- [x] Explicit existing RM01 credential/transport preparation before provider, supported optional certificate renewal target adoption with exact policy identity and one actual resource cleanup barrier, no temporary provider or hosted meter-identity bypass.

## Targeted validation

- [x] Actual isolated certificate/bearer authority, rotation, failed preparation/adoption and joined transport/body/key drain tests; preserve existing optional provider contracts and document deployment proof limits.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Actual native certificate/Entra checks | Passed124, failed0, skipped0; build0warnings/errors | .artifacts/configuration-publication/CIS-0027/WORK-005/security-final-provider-tests/results/security-providers.trx |
| Core Security units | Passed72, failed0, skipped0 | .artifacts/configuration-publication/CIS-0027/WORK-005/security-policy-unit-tests/results/security-unit.trx |
| Scoped formatting and verification | Passed | Actual security-preparation/security-final/security-policy-unit native phases and journal |
| Independent review and documentation | Accepted; required retirement-lock finding resolved; native regression passed | ../review.md and ../verification.md; docs/modules/security/credentials.md and entra.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2413; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:f09775537f1b6019e64266a78c3b1b2eeb3aa71ccb01b8406701e6b10cc32d37 |
