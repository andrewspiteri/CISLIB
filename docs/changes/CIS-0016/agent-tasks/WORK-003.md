---
title: "WORK-003 Deliver explicit Entra workload acquisition and native app-only JWT integration"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:6a33d627762d469ab874caa992758a88"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["entra"]
authorization_scope_basis: "sha256:b180e41292209748a93232c690eb7d7c0d183d9635f9c91ce8533953ffa2e8be"
---

# WORK-003: Deliver explicit Entra workload acquisition and native app-only JWT integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver explicit Entra workload acquisition and native app-only JWT integration

## Acceptance criteria

- [x] P08/P11/P15: real optional WorkloadIdentityCredential and native JWT bearer integration use exact v2 app-only mapping, roles, trusted scopes/origins and SDK caching; no fallback/cache/replay owner. Guide documents tenant/federation/API inputs and AC-02 deployment prerequisites.

## Targeted validation

- [x] Test native signed-JWT signature/issuer/audience/lifetime/skew/app-only/roles and scheme isolation, plus SDK acquisition cancellation, known failures and expired-token rejection through actual REST hook. Distinguish SDK transport tests from real AKS proof; keep AC-02 unverified without authorized infrastructure. Include provider guide/build/format/review. Independent provider work may start after WORK-001, but final outbound integration/build and task completion wait for actual WORK-002 binding enforcement.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native provider proof | Passed:41/41,0failed/skipped | `.artifacts/security/CIS-0016/providers/08-current-inbound-proof/results/entra-native.trx` |
| Build/style/review | Release0warnings/errors; scoped format/verify pass; fixed final native options reviewed | `docs/modules/security/entra.md`; provider05style and08build receipts |
| External acceptance | AC-02 real AKS federation remains unverified | `docs/modules/security/entra.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1779; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:cce67fef77a9fe5b5578a2f019800942bd069600a7c7480fd6d9d1c67e280207 |
