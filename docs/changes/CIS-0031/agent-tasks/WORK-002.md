---
title: "WORK-002 RM11 definition validation and local evaluator"
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
instance_key: "outcome:fef8794d77894bfc94fd03a438d0c75c"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-RUNTIME"]
authorization_scope_basis: "sha256:5d80b24766ca80940e937b4e2db81392fc2440ae2c29ff8d0148ea78b9e727fb"
---

# WORK-002: RM11 definition validation and local evaluator

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 definition validation and local evaluator

## Acceptance criteria

- [x] Expose a mockable evaluator and shared validator. Enforce exact operators, explicit conditions, declared order and reached-cell validation. Preserve Enabled/Test/Disabled, FirstMatch/AllMatching, failure versus Test diagnostics, deterministic order and immutable inputs/results. Cover RM11-AC02–AC11 and AC13–AC15.

## Targeted validation

- [x] Use native MSTest and portable fixtures for all operators, malformed definitions, exact precision, Unicode scalar and escape bounds, missing/null and markers, lazy validation, trace outcomes, both modes, ties, and detached inputs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| C# implementation | Passed | [Rule Engine source](../../../../implementations/csharp/src/CIS.Core/RuleEngine) and [MSTest fixture](../../../../implementations/csharp/tests/CIS.Core.UnitTests/RuleEngine/RuleEvaluatorTests.cs). The public interface is mockable and the local matcher remains internal pending the deployed-version and audit adapters. |
| Pinned native Release build and formatting | Passed | .NET SDK 10.0.401, 0 warnings and 0 errors. Scoped `dotnet format whitespace --folder --verify-no-changes` passed for all seven changed C# files after the project-mode MSBuild pipe was denied in this Windows session. |
| Focused native MSTest | Passed | 48 passed, 0 failed, 0 skipped. Selected only `CIS.Tests.UnitTests.RuleEngine.RuleEvaluatorTests`. Local TRX `.cis/local/testing/rm11-direct/rm11-unit.trx` has SHA-256 `e8167a5ba9deddc336eb581540d1389eded355e33342ef8f598a3c804bb7f9e4`. |
| Independent review | Passed | Reviewed matcher reachability, cancellation, wildcard scalar handling and bounds against v1. Invalid wildcard syntax now produces a contextual validation error. No unresolved findings in the local comparison boundary. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3797; failed=405; possibleTokenSavings=591; ledgerDigest=sha256:a9e6ce0e4e48f3b16127a561f669a2cc75b98735ce111fe04f21e827210a00f4 |
