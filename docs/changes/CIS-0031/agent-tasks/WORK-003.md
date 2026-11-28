---
title: "WORK-003 RM11 trusted registration and immutable version saves"
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
instance_key: "outcome:3cf3451f356a41ffae31d89d3fdae4e9"
depends_on: ["WORK-001","WORK-002"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-MANAGEMENT","RM11-RUNTIME"]
authorization_scope_basis: "sha256:ad6740c0c7214a820b19730f4651b54167965ae683c01d9a5ed884b9adff4c0e"
---

# WORK-003: RM11 trusted registration and immutable version saves

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 trusted registration and immutable version saves

## Acceptance criteria

- [x] Provision the trusted ruleset and schema, submit validated complete immutable versions under current edit revision, and recover by the original operation key. Preserve deployment and saved history during registration refresh. Protect the same rules from generic Configuration routes. Cover RM11-AC01, AC16 and AC30.

## Targeted validation

- [x] Use the existing provider and client fixtures for scoped grants, two-editor contention, actual restart/re-registration, copied/deleted identities, unchanged/resaved definitions, and generic write/bypass boundaries.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Strict definition codec and evaluator regression | Passed | Native MSTest selected 15 codec and 48 evaluator cases. All 63 passed. Local TRX `.cis/local/testing/rm11-management-codec/rm11-management-codec.trx` has SHA-256 `ad2cc2870ef81e82ae19eb1b71c9e05e0f89a277158db0c12a9ce3aa071d2ccb`. |
| TypeScript Management client | Passed | Pinned Node 24.16.0 compiled the client and passed all four focused cases for scope, exact schema and definition text, preconditions and operation recovery. |
| Native Management service and provider | Passed | The ordinary Management service compiled. The focused provider build supplied `CIS.Transactions.Hosting` and `CIS.Management.Service.Metadata` global imports to the integration test project because two Scheduler fixture files had missing imports. The provider run used the current application source and passed both RM11 cases on pinned PostgreSQL and RabbitMQ. The TRX at `.cis/local/testing/rm11-management-isolated/results/rm11-management-provider-current.trx` has SHA-256 `16d63f0cbe44dcd423c07265c6ecc223896d0b669efdcb726c598f8c972d7a6e`. |
| Independent review | Passed | Resolved exact base-version spelling, Unicode and whitespace rule identities, signed page positions, embedded schema scope, and unknown original results before journal access. An inherited ordinary assignment requires RM11 provenance before the evaluator can accept it under WORK-004 and WORK-005. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3805; failed=405; possibleTokenSavings=591; ledgerDigest=sha256:859f5f4f4fcbec04cff3aa8dff112bd2537f3b0e6dc9436689f910533443ac5e |
