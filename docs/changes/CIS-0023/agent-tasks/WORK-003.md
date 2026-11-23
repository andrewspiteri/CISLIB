---
title: "WORK-003 Deliver the explicit compatible Security audit bridge"
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
instance_key: "outcome:05e809020477426e9fcc528a5f51d6dd"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0023:outcome:b5a77a9ea98644b98e9eb55f5a83d096"
authorization_scope_ids: ["security-bridge"]
authorization_scope_basis: "sha256:214525b52047763f7f9f628bd1299a4ed97aaa4e5d7693bd801528269df74fc4"
---

# WORK-003: Deliver the explicit compatible Security audit bridge

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver the explicit compatible Security audit bridge

## Acceptance criteria

- [x] AC09-11 logical boundary: explicit real-sink-safe registration, versioned authorization and reported-operation events, full safe immutable provenance/capabilities, unknown/missing facts preserved, singleton-safe bridge and5scooperative deadline, exact trusted ingress/retry suppression with actual authorization retained. Original hook signatures/post-effect behavior remain unchanged.

## Targeted validation

- [x] Focused mapping/bounds/Unicode256capability/delegation tests, NotSubmitted/Unknown/inconsistentresult failures, original cancellation/fault/latecommit preservation, real custom/no-selection sink compatibility, successive-ingress no-loop and direct submission no auth recursion. Scripted pressure/actual cleanup checks and independent security review. Real outer Operations and nested Messaging pressure evidence is WORK004 acceptance, not claimed here.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Explicit compatible Security bridge and focused mapping/deadline cases | Passed | security-bridge-unit-tests: 15 actual cases,0failed/skipped; optional Completion, exact result/fault/cancellation and latecommit preserved. |
| Affected native build and scoped format verification | Passed | publication-security-unit-build-corrected zero warnings/errors; publication-security-scoped-format + publication-security-format-verify exit0. |
| Independent cross-review | Passed |12active read-only minutes; no material mapping/ownership/deadline/suppression defect; exact native scalar/control validators verified. |
| Public schemas and native proof boundary | Explicit |2detail schemas/21vectors authored; existing schema evaluator proof owned by WORK005 to preserve startedTask003 contract. Actual SQL/broker/nestedpressure/no-loop proof remainsWORK004. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2258; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:630483067c99a1fe24d467f3f00b864470040787845678a2db3bb31f6a40648d |
