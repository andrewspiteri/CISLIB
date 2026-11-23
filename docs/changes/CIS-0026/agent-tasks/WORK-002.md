---
title: "WORK-002 Preserve structured native-fetch response failure and mutation recovery"
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
instance_key: "outcome:278001bf3d56492b83f75f127bc5c98a"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0026:outcome:150b7d43040443e2947ba9939e6405a4"
authorization_scope_ids: ["fetch","records"]
authorization_scope_basis: "sha256:d8927f0ddbf47b5d2f9661cd8ca1db20decf95d1c59ff31c286717d044268b6b"
---

# WORK-002: Preserve structured native-fetch response failure and mutation recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Preserve structured native-fetch response failure and mutation recovery

## Acceptance criteria

- [x] Expected native-fetch response stream/disposal disconnect, deadline and caller cancellation return structured facts including original operationKey/mutationUncertain after PUT headers; actual cleanup awaited and unexpected programming faults propagate.

## Targeted validation

- [x] Native fetch HTTPS body disconnect/deadline/cancel regressions using existing fixture; bounded unexpected-fault/held-cleanup tests; pinned TypeScript build/browser/Node and focused native MSTest fixture checks/docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native body failure and original mutation recovery | Passed: 118 Node tests, compiler/browser checks, zero-warning integration build, formatting and six actual HTTPS scenarios; independent review clear | [Verification](../verification.md); `.artifacts/management/CIS-0026/WORK-002/native-fetch-corrected-https-tests/results/native-fetch-corrected.trx` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2376; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:a05ebdbffb9146a0f95ff0fa937c9baab3705b8b30c5ee773f40bf7e5f31a254 |
