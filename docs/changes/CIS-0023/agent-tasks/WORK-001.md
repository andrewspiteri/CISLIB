---
title: "WORK-001 Deliver portable Audit v1 and trusted immutable capture"
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
instance_key: "outcome:51420184081f4af9a29f64c76e72a4a9"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0023:outcome:b5a77a9ea98644b98e9eb55f5a83d096"
authorization_scope_ids: ["capture-contracts"]
authorization_scope_basis: "sha256:bcbda520d5e033884f573c5bd0e32d91c992af0938b725b2702e71b268a83a1c"
---

# WORK-001: Deliver portable Audit v1 and trusted immutable capture

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver portable Audit v1 and trusted immutable capture

## Acceptance criteria

- [x] AC01-03 and public AC12: portable closed v1 schema and independent vectors, explicit event/detail definitions, exact-byte generic codec including unknown application details, non-promotable captured source type, scoped trusted capture and singleton-safe internal observation mapper, scalar-valid identity and byte/depth bounds, existing-budget copy admission and bounded ScriptedAuditWriter. Document public ownership/API freeze.

## Targeted validation

- [x] Focused native conformance/unit/schema vectors for duplicate/escaped keys, UTF8/Unicode/depth/bytes, opaque forward bytes, decimal strings, actor/provenance separation, mutation resistance and source eligibility. Reuse existing schema subset, no new package/harness. Pinned affected build/scoped formatting+verify and independent contract/memory review; no SQL durability claims. Permit only the narrowly needed existing-evaluator minItems/maxItems extension with focused regression; retain schema array bounds.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Portable/capture/public writer and bounded substitute | Passed | [Contracts checkpoint](../contracts-checkpoint.md); 56 actual unit cases, 0 failed/skipped. |
| Independent schema/wire/byte vectors and narrow array evaluator | Passed | 42 actual Architecture cases, 0 failed/skipped; 20 structural, 16 raw-wire, four identity pairs. |
| Pinned affected build and scoped whitespace verification | Passed | Core/CommonTesting/unit/Architecture builds zero warnings/errors; contract-native-scoped-format and contract-native-format-verify exit0. |
| Independent trust, immutable ownership and wire review | Passed | No remaining actionable defects after bounded alias, exact-size encoder and original Write-fault corrections; actual capture drain exercised. |
| Remaining qualification boundary | Explicit | SQL, broker, Security bridge, host and public-package evidence stays with WORK002-004. All failed results/costs retained. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2250; failed=310; possibleTokenSavings=591; ledgerDigest=sha256:b675f07a79f42bc75fc1896b3c23422e3b81924b90cf1abf01fab83234353ce0 |
