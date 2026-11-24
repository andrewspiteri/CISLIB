---
title: "WORK-013 Retain unobserved authority gaps before control renewal"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-013
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:43bfab2a815e442fa9d5cd2f7c9c9b04"
depends_on: ["WORK-006","WORK-012"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["bootstrap","adoption","configuration","records"]
authorization_scope_basis: "sha256:c038eae24cddf3219b508eb31d6fcfdb50ac7aee422a612b9a2b05f1011cb2d0"
---

# WORK-013: Retain unobserved authority gaps before control renewal

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Retain unobserved authority gaps before control renewal

## Acceptance criteria

- [x] BeginAttempt retains expired or untrusted prior authority as an actual gap before Reconciling can erase it. Existing Configuration epoch owner must settle fresh authoritative work before gate reopening, even with no intermediate TryAdmit/status/health observation. Healthy control and stop/session fences remain unchanged.

## Targeted validation

- [x] Focused no-intermediate-observation expiry/clock-gap regression holds actual Configuration read, then proves gate closed until fresh result settles. Existing admission/control/bootstrap/reconciler tests, focused native composed handoff where applicable, docs and independent review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Authority gap and existing Core behavior | Passed1142 units on Windows and separately on Ubuntu, including six new held-read gap rows;151 Linux contracts passed | WORK-013/reviewed-core-corrections-unit-tests-final/results/reviewed-core.trx; WORK-013/linux-review-current/results/linux-units.trx and linux-contracts.trx |
| Current composed host and review | All101 provider/host cases passed, including five native SDK/TLS bootstrap cases and CountryData; independent prerequisite-mask finding corrected and follow-up resolved | WORK-014/reviewed-service-provider-and-handoff/results/reviewed-service.trx; ../review-corrections.md; docs/modules/management/admission.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2454; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:6f6144687e5a528c6f219abf467cd6487672382e98486ab1ce5b8f9bbe2b0a62 |
