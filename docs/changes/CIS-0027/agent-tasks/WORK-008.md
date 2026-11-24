---
title: "WORK-008 Extend portable TypeScript configuration and native request semantics"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:e6762a40aa344be783a3372863fe2af7"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["typescript","records"]
authorization_scope_basis: "sha256:0e7c05208eb70cc27f083469c41efd3bc83279c94676cabd7bc976df4efc1326"
---

# WORK-008: Extend portable TypeScript configuration and native request semantics

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Extend portable TypeScript configuration and native request semantics

## Acceptance criteria

- [x] All nineteen APIs and strict lossless config wire semantics, POST, compatible awaited WebCrypto decode, explicit non-mutating validate/preview and opt-in conditional304 without redirect relaxation, original keys and expected native failures/owned cleanup preserved.

## Targeted validation

- [x] Pinned compiler/browser/Node vectors and export checks, actual HTTPS read-only POST versus mutating POST disconnect/deadline/cancel, delayed digest/fault/cleanup and legacy PUT/DELETE semantics; SDK/Kotlin protocol docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Portable compiler/browser/Node | Passed181 Node cases, failed0, skipped0; pinned compiler/browser passed | WORK-008/configuration-typescript-direct-tests/native.log and actual native-costs.jsonl under .artifacts/configuration-publication/CIS-0027 |
| Actual native HTTPS/fetch | Passed1 parent MSTest asserting all12 GET/PUT/readonlyPOST/mutatingPOST body-failure scenarios,0failures/skips | WORK-008/configuration-native-fetch-tests-corrected/results/configuration-native-fetch.trx under same root |
| Scoped CSharp formatting and current compile | Passed; corrected expected-first coverage assertion, actual run passed | configuration-native-fetch format/final verify and current Integration build |
| Independent review and SDK/Kotlin docs | Accepted direct async/native owner extension; no framework/dependency added | ../review.md; docs/modules/management/typescript.md; implementations/typescript/KOTLIN.md and READMEs |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2422; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:547c1c28181661385e9837332fb941c54f1b4533f58ddeda9f35a5d5a4cffa33 |
