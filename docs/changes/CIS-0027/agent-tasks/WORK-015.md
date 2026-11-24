---
title: "WORK-015 Retain same-cohort generation equality across registration recovery"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-015
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:9d9c6732341d4607ad2f8ea26c723ac6"
depends_on: ["WORK-004","WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["configuration","csharp-client","records"]
authorization_scope_basis: "sha256:09300ca33088072e49bc8174b25649ed8d1b5de9f9e4bf5f79406986833995ad"
---

# WORK-015: Retain same-cohort generation equality across registration recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Retain same-cohort generation equality across registration recovery

## Acceptance criteria

- [x] Same-cohort authenticated target rebind retains compact release/source-reference comparison facts at highest observed generation, excluding replaced registration identity. Old conditional token/snapshot envelope discarded and old work joined. Reject changed release/source refs at equal generation, allow genuine cohort change same generation and unchanged content new session.

## Targeted validation

- [x] Focused same-cohort changed release/ref equality regressions, positive identical new-session and existing different-cohort cases, ordinary central session/generation/read budget/cancellation tests, docs and independent review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Session/generation regressions | Eight new cases passed inside1142 current Windows units and separately1142 Linux units, plus existing changed-cohort/lower-generation/cancellation cases | WORK-013/reviewed-core-corrections-unit-tests-final/results/reviewed-core.trx; WORK-013/linux-review-current/results/linux-units.trx |
| Independent integrity/ownership review | No remaining finding; logical equality excludes only replaced registration, exact current-session fences persist; old pair/conditional ownership released;151 Linux contracts and current101 provider/host join pass | ../review-corrections.md; docs/modules/configuration/central-snapshots.md; WORK-014/reviewed-service-provider-and-handoff/results/reviewed-service.trx |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2456; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:8ddd62b9226acbb7a77a42c8b2c3e374a956d202d0929eca5a724a957063cd4d |
