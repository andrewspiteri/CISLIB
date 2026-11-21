---
title: "WORK-002 Implement authorized durable execution and independent committed-result lookup"
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
instance_key: "outcome:c25f3b39d30340c79dd38899eaeb39c8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["execution-recovery"]
authorization_scope_basis: "sha256:71db59ea67c7b9aa46a4d4c10e600b01d9ccff7ee4566c9b680f5cb1892cbf01"
---

# WORK-002: Implement authorized durable execution and independent committed-result lookup

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement authorized durable execution and independent committed-result lookup

## Acceptance criteria

- [x] P04-P08/P11/P13 and AC02/03/06/07/08/10/12: one nongeneric module-wide weak context admission across instances/types, native claim/read then outside-callback handler then native terminal write; exact original context, separate Prepared/Replayed/business/persistence facts. Existing capability authorization before first/replay/lookup; after-claim between-callback failures poison and preserve originals. Fresh authoritativeRC lookup owns only its read resources and preserves known committed facts plus read cleanup diagnostics.

## Targeted validation

- [x] Controlled store/ADO tests for cross-instance/generic nested/repeated/concurrent admission, exactcontext/DB/isolation preflight, input snapshot, authorization/denial/audit fault/cancellation beforestore orhandler, nativecallbacknonreentry, handler/encode/size/write/CT faults, rollbackonly refusal, exact resultfidelity/version/conflict/expiry precedence and freshlookup while claim uncommitted. No automatic ownercompletion/retry; docs/style/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Coordinator | Authorized sequential Execute, shared weak admission and fresh RC GetResult implemented | Core/Operations and coordinator guide |
| Native | Scoped pinned format/verify and affected Release build passed, zero warnings/errors | .artifacts/operations/CIS-0018/WORK-002/runtime-build-generics/native.log |
| Windows / Ubuntu | 84/84 passed on each actual OS, zero failures/skips; 28 foundation plus 56 runtime | .artifacts/operations/CIS-0018/WORK-002/runtime-receipt.json |
| Independent review | Source/test/necessity review found no remaining material finding | ../review-002.md |
| Ownership and limits | No owner completion/retry, no SQL or package proof; external source/native cost records preserved separately from automatic counters | ../verification.md; .cis/local/operations-intake/resources.json |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1918; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:da4f0cf4b5e45cbdb70aff072456f9c2678f85a0302428543e62914c88839ba6 |
