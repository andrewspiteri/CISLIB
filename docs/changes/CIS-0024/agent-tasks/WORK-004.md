---
title: "WORK-004 Implement recoverable metadata command with atomic Audit"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:fdb3756821e84c5d964f4bbcbe97e05d"
depends_on: ["WORK-001","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["metadata-command"]
authorization_scope_basis: "sha256:8908ecc862e7ae522f80ab1984700c7fa2c51429e257c2120bf5ccb915de99f1"
---

# WORK-004: Implement recoverable metadata command with atomic Audit

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement recoverable metadata command with atomic Audit

## Acceptance criteria

- [x] AC07/08/09/10/15: actual-resource/current-actor scoped RM03 command and lookup; strong metadata-only ETag; canonical exact key/input; conditional update, original result and required RM06 Audit staged in original RM02 owner. Matching replay bypasses stale original ETag and repeats neither effect nor success Audit. Preserve original commit/Unknown/Found facts and validated human attribution.

## Targeted validation

- [x] Real PG concurrent same-ETag different-key edits, original-key same-input replay/changed conflict, actual commit/lost-response lookup, Audit refusal rollback, independent caller/human/delegation scope and forged headers, result expiry/no reexecution, reconstruction and late-cleanup fact preservation. No foundation coordinator redesign; focused source/build/format/docs/review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual product HTTP / original PG / Audit | 8 passed, 0 failed, 0 skipped | The eight HTTP cases in `WORK-004/complete-metadata-audit-native-tests`: concurrent same/distinct keys, reconstruction, original replay/lookup, actor separation, expiry protection and actual required Audit refusal. |
| Native commit and late cleanup facts | 2 passed, 0 failed, 0 skipped | `WORK-004/corrected-native-original-completion-tests`; actual committed Npgsql transaction with lost acknowledgement and actual rollback/disposal failure. Initial incorrect expectation retained, no product weakening. |
| Source quality and independent review | Passed | Current Integration build zero warnings/errors; explicit-file format/verification. Focused independent review found no material defect; `Metadata/README.md` explains ownership and uncertainty. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2304; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:61de64fc8fe53494289ee9d70f7ff1a9bcba159dd54a43a0e2df0f7b7e38e7c1 |
