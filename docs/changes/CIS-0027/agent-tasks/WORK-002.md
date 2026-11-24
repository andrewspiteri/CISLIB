---
title: "WORK-002 Implement trusted catalog provisioning and immutable source publication"
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
instance_key: "outcome:5471223eaa4046b598adbe215c7090d2"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["backend","records"]
authorization_scope_basis: "sha256:780f9b95f24b6073c957f3bf71f17360e9f9ac970b7d68ea2df6d4fa71cd7855"
---

# WORK-002: Implement trusted catalog provisioning and immutable source publication

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement trusted catalog provisioning and immutable source publication

## Acceptance criteria

- [x] Compatible schema migration seeds empty stable catalog only; native authenticated manifest application allocates exact app/env/source resources and ETags, preserves schema identity and capabilities, and publishes immutable exact UTF8 content under original key/result/required Audit.

## Targeted validation

- [x] Real PostgreSQL manifest idempotence, initial stable ETags, native actor/resource/content denial, concurrent publication/replay/lost-response/unknown/audit-failure tests and persistence docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Actual isolated PostgreSQL checks | Passed49, failed0, skipped0; app/Integration builds0warnings/errors | .artifacts/configuration-publication/CIS-0027/WORK-002/catalog-bounds-native-tests/results/configuration-catalog.trx |
| Original native completion and recovery | Real committed-but-unknown/cleanup cases, required Audit rollback, matching replay/Found/NotFound and concurrent original keys passed | ManagementConfigurationCompletionTests and PublicationTests in same native receipt |
| Bounds and migration | Strict/preallocation manifest and rawHTTP encoded-response rejection; v1/v2 upgrade and old ownership behavior passed | Current native receipt; exact failed earlier receipts retained |
| Scoped formatting and independent review | Passed; final corrections accepted with no remaining required finding | catalog-bounds-final-format phases; ../review.md |
| Product documentation | Explicit ManifestFile/QuotaBytes/apply recipe and evidence limits | docs/modules/management/configuration-service.md and linked product/backend guides |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2420; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:c83d6b57c39785c44bcd941aa5371c3d867841b15a6090b6b02af8b508601ce6 |
