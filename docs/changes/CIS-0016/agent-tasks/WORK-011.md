---
title: "WORK-011 Preserve joining Entra source-recheck cancellation after shared settlement"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-011
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:8841e71e4b7b41e0897ff8eb284fe86e"
depends_on: ["WORK-009"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["entra"]
authorization_scope_basis: "sha256:b180e41292209748a93232c690eb7d7c0d183d9635f9c91ce8533953ffa2e8be"
---

# WORK-011: Preserve joining Entra source-recheck cancellation after shared settlement

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Preserve joining Entra source-recheck cancellation after shared settlement

## Acceptance criteria

- [x] Keep one shared SDK acquisition/recovery task while cancellable public source-recheck callers observe their own cancellation after successful actual settlement. Preserve original shared SDK/cleanup faults over caller cancellation, noncancellable shared task identity, SDK cache ownership, existing lifecycle/drain, and bounded recovery cadence.

## Targeted validation

- [x] Add focused held-recovery regressions for creator without cancellation and a second cancellable joiner: only one recovery acquisition, no early completion while held, creator success/joiner cancellation after SDK success, and exact original shared fault precedence when SDK fails. Demonstrate the regression before the fix, then run affected Entra tests on actual Windows and the existing pinned Linux lane. Include scoped formatting/build, guide/docs and independent review; preserve all earlier costs/failures/reserves. No new harness/cache/owner, push, merge, release or external deployment.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Regression before correction | Reproduced:1failed/1passed. Joining cancellation was lost after successful shared SDK settlement; original-fault case passed. Failed receipt retained. | `.artifacts/security/CIS-0016/review-follow-up/17-cancellation-regression-red/results/joining-red.trx` |
| Fixed native Entra suites |49Windows and49actualLinux cases pass,0failed/skipped. Both held-join rows verify shared ownership, actual settlement, caller token and exact fault precedence; existing47cases remain green. | `21-cancellation-windows-entra/results/entra-cancellation.trx`; `22-cancellation-linux-entra/results/linux-native.trx` |
| Build/style/docs/review | Pinned affected Release build0warnings/errors; scoped format+verify and strict249docs pass. Independent2minute read-only review found no material issue. Guide explains per-caller settlement. | `../verification.md`; `../review-follow-up.md`; `../../../modules/security/entra.md` |
| Custody/preservation/resources | One newly imported Windows leaf has Removed receipt and exact backing absence; parent access restored, current provider copy matches and source/user hashes preserved. All prior costs/reserves and deployment gaps retained. No push/merge/release. | `.artifacts/security/CIS-0016/review-cancellation`; `.cis/local/security-intake/resources.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1817; failed=255; possibleTokenSavings=591; ledgerDigest=sha256:cdb06af62ff9f221d14d03cce9a72f6faf361400ea6548b41663f2286b61be43 |