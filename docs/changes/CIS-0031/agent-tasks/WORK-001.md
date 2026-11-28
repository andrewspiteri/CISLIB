---
title: "WORK-001 RM11 shared portable and Management contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: coordination
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:2a9834613b154a0ba195c3968ffdf3fd"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-CONTRACTS"]
authorization_scope_basis: "sha256:bd45c9862f2399b75de4b046901964830f144896ff28577f615c03050c13c902"
---

# WORK-001: RM11 shared portable and Management contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 shared portable and Management contracts

## Acceptance criteria

- [x] Record the versioned ruleset, ordered schema, invocation, result and trace, audit, capability, deployment and Management contracts. Resolve exact decimal representation, native Boolean, absent/null, unknown input capture, metadata, Unicode, IDs and whole-envelope limits. Record trusted provenance and the shared eligibility, retention and counter acceptance decisions.

## Targeted validation

- [x] Validate portable C# and TypeScript wire examples, exact decimal bounds and rejected rounding, Unicode and presence; verify a representative maximum trace fits real RM08, RM06, Messaging and HTTP limits. Supports RM11-AC12 and every consumer.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Portable contract and operation handoff | Recorded | [RM11 portable contract v1](../../../../contracts/rule-engine/v1/README.md) |
| CLR and Node portable vectors | Passed | [Nine decimal cases, Boolean false, omitted and null, unknown Unicode value](../../../../conformance/rule-engine/v1/portable-values.json) |
| Representative serialization | Passed | 48 rules × 24 columns. Definition 128342 bytes, Management command 138788, input 24043, audit start 24257 and completion 19707. These are wire-shape checks and provider qualification stays with the implementation tasks. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3726; failed=394; possibleTokenSavings=591; ledgerDigest=sha256:3fcb23f91ae0e82c792d842fda4e39e94727b27d03b64fbaeb389df2f7319c46 |
