---
title: "WORK-001 Freeze coordination contracts and explicit job catalog"
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
instance_key: "outcome:15dfd886547b45bba896ac2586b513c8"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["contracts"]
authorization_scope_basis: "sha256:28bdf4fafdb34e4e1efccc7ecc56dbd97607aa14523493869dd0fda370ea1ce8"
---

# WORK-001: Freeze coordination contracts and explicit job catalog

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Freeze coordination contracts and explicit job catalog

## Acceptance criteria

- [x] AC09/10/15 shape: bounded ordinal UTF8 keys and lossless generation/authority/acquisition identity, immutable validated defaults/results/provider/guard/lease ports, categories/modes/descriptors/admitted identity and explicit exact handler eligibility. Passive catalog/DI shape honors scoped SecurityHostContextScope adapters and existing EncodedMessage/codec input; no I/O/constructors/discovery activation. Publish portable/C# contracts, real validation tests and shared ownership/API freeze.

## Targeted validation

- [x] Compile contracts/catalog with current Core/package policy; focused native unit vectors for key bounds/encoding/generationJSON/duplicate/missing/mode/category/catalog limits and disabledinspection. Scopedformat+verify, review public lifetime/result combinations and currentIntegration references. Docs/tests/review included; no runtime behavior asserted before implementation.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Shared immutable ports/catalog and meaningful vectors | Passed | Root coordination contracts + explicit Jobs bindings/scoped interfaces; 27 focused native cases, 0 failed/skipped. |
| Pinned SDK format/verify/build | Passed | contracts-format, contracts-format-verify, contracts-catalog-build: zero warnings/errors. |
| Independent source review | Passed | Store-role review corrected unknown versus NotCurrent and separated Busy observed-owner; root catalog review confirms ordinary DI, no provider/handler construction or background loops. |
| Runtime qualification boundary | Explicit | Task1 establishes contracts/catalog only. Real SQL/renewal/jobs/host/collector behavior remains in Tasks2–7. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2168; failed=293; possibleTokenSavings=591; ledgerDigest=sha256:903c84bcc50dc2c3e146282abcb0bce8bff634dc11048a7a3b7030901b690023 |
