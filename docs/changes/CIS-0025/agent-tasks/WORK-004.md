---
title: "WORK-004 Apply managed admission to existing Jobs finite work"
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
instance_key: "outcome:31016ca2d2af49ddbacacac43074c954"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0025:outcome:56e9417305534bad8bc8776094f659b8"
authorization_scope_ids: ["jobs","records"]
authorization_scope_basis: "sha256:a6319368c565b3efcf1a88ebcf8c1d05c191b1a1a074d8064f70f89ec0c1d61d"
---

# WORK-004: Apply managed admission to existing Jobs finite work

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Apply managed admission to existing Jobs finite work

## Acceptance criteria

- [x] New acquisition/occurrence/iteration gated at actual effect boundary; pending work deferred and existing waits awakened. Existing admitted callbacks and valid renewals/fencing settle. Shared ports frozen on disk before parallel source; integrated native verification waits for matching implementations.

## Targeted validation

- [x] Focused continuous/scheduled/lease admission and wakeup regressions; existing owners/native tests and docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Existing Jobs admission | Passed: 15 focused gate/wakeup/held-boundary cases and affected Jobs regressions on Windows and Ubuntu | [Verification](../verification.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2353; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:7be3138ef0669d5f1c9801eab0529a7d508f3f75d667f72a7b3d205c5a108000 |
