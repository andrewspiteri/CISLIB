---
title: "WORK-192 Diagnose and correct Windows Node native serving failures"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-192
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:bc9cabd09f2149e6b7342888284194f4"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-192: Diagnose and correct Windows Node native serving failures

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Diagnose and correct Windows Node native serving failures

## Acceptance criteria

- [x] Diagnose the retained serving-time Windows Node 0xC0000409 independently of the already repaired Next response-close wait. Use bounded newly owned credential-free HTTP traffic, fixed count/concurrency and actual public request/socket/server/process joins. Retain native exit/fast-fail observations and distinguish upstream symptom matches from an observed local cause. Implement the smallest evidence-backed correction with a meaningful regression and actual affected portal qualification. Preserve authentication, TLS validation, native error/commit/recovery facts and existing cleanup. No credentialed memory dump, production test mode, security/JIT suppression, speculative connection-policy workaround, new framework, Scheduler work or broad campaign. The current no-runtime-pin-change constraint remains binding unless its specific exception is explicitly recorded after a concrete proposal.

## Targeted validation

- [x] Record exact existing official runtime identity and bounded reproduction input/output; require failed red outcomes to remain failures, verify actual owned native closure and obtain independent source review. Reuse existing pure/public-native/portal tests after a correction. Keep prior Windows failures, Next causal red/green, all consumption and current pins until a required specific decision; include docs and current evidence in this same leaf. Root owns native serialization and resource admission.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Current follow-up acceptance | Passed, with retained limits and failures | [Actual verification](../configuration-followup-verification.md): strict native2/2/4/2;395portable and131focused portal checks; scoped format/source/pin manifests; independent review found no required changes. Windows correction is finite mitigation, not exact native stack proof. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3402; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:118300f3d3dfb4dc4810db9ee571b46dce17679f9d2ea3c844edfd32d3ca85c0 |
