---
title: "WORK-001 Close managed admission on canceled direct startup"
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
instance_key: "outcome:4be7134d7fb44212b315a8021e52289b"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0026:outcome:150b7d43040443e2947ba9939e6405a4"
authorization_scope_ids: ["startup","records"]
authorization_scope_basis: "sha256:84480d082de877e72a69b580e32bcd10c05d2e05f32e0d34279007906cbb8fec"
---

# WORK-001: Close managed admission on canceled direct startup

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Close managed admission on canceled direct startup

## Acceptance criteria

- [x] Cancellation through initial observation or inventory closes and invalidates admission before startup cancellation completes, no recurring subscription remains, accepted work/cleanup joined and event programming faults preserved.

## Targeted validation

- [x] Existing bounded lifecycle tests cover cooperative/uncooperative reporting cancellation and unchanged success/stop behavior; pinned focused MSTest/build/format plus current docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Canceled startup, actual settlement and retained faults | Passed: 211 Management unit cases, zero-warning build, scoped formatting and independent review | [Verification](../verification.md); `.artifacts/management/CIS-0026/WORK-001/startup-unit-tests/results/management-startup.trx` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2375; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:fa401dc80da354857d72ce5b06cdb7997061e608d15d8301940b162c2ca962f9 |
