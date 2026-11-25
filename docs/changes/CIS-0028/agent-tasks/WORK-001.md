---
title: "WORK-001 Install corrected CIS and verify one current RM-08 criterion with unchanged-result reuse"
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
instance_key: "outcome:seed:489dda53edc041b895fcc74824b0d74c"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0028:outcome:f1068f406dc84823ace4293a490f5c28"
authorization_scope_ids: ["tool-adoption"]
authorization_scope_basis: "sha256:06340d863b0d648f0b5e3cf5575ab7714f642bb8ffc442ccbe2dbf0816b1194b"
context_roots: ["cislib:change:cis-0027:task:work-004"]
---

# WORK-001: Install corrected CIS and verify one current RM-08 criterion with unchanged-result reuse

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Install corrected CIS and verify one current RM-08 criterion with unchanged-result reuse

## Acceptance criteria

- [x] Through the unchanged explicit CIS invocation, observe the new exact source/artifact identity and preserved rollback. Project original canonical component identities plus accepted current module paths without amending the approved design bundle. One source-backed AC-10 reaches its original WORK-004, CIS.Configuration implementation, exact real check and actual current native receipt. A second unchanged verification launches no native tests; original reports, approvals, costs, recovery records and unrelated launch settings remain unchanged.

## Targeted validation

- [x] Verify exact package/install SHA256s and --version through normal route; refresh current derived graph under the6GiB ceiling, inspect component and criterion paths/unmapped counts, run only the one existing ConfigurationCentralAcceptanceTests.PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision MSTest case under a fresh native owner/profile, verify unchanged twice with zero extra launches, inspect bounded prepare/transition output and retained historical hashes. No broad product qualification or old-owner reopening.

## Related implementation context

The focused demonstration consumes the original
[RM-08 paired-snapshot implementation task](../../CIS-0027/agent-tasks/WORK-004.md),
the [current component projection](../../../references/current-component-ownership.md)
and the [source-backed criterion mapping](../rm08-traceability.md).
These references select existing behavior; they do not reopen the completed task
or amend the approved component bundle.

The focused input is the existing
[refresh coordinator](../../../../implementations/csharp/src/CIS.Core/Configuration/Refresh/ConfigurationRefreshCoordinator.cs).
Its applicable native check is
[the paired initialization and unchanged acceptance case](../../../../implementations/csharp/tests/CIS.Core.UnitTests/Configuration/ConfigurationCentralAcceptanceTests.cs#PairedInitializationUnchangedAcceptanceAndExplicitRemovalKeepOneRootAndTheActualRevision).
An exact result is recorded only after the installed verification route executes
that case and reassesses its current material inputs.

## Completion evidence

| Check | Evidence | Result | Limit |
| --- | --- | --- | --- |
| Installed identity and rollback | `.cis/local/testing/current-context-correction/install-preparation-budget-receipt.json` | Verified exact d034 manifest/full file hashes through unchanged route | Local installation |
| Focused native and unchanged verification | `.cis/local/testing/current-context-correction/rm08-run-parent-budget.json`; `rm08-verify-first.json`; `rm08-verify-repeat.json` | One actual passed case; two installed Verify receipts; no repeat native launch | Whole-criterion coverage unassessed |
| Current trace/context and preservation | `.cis/local/testing/current-context-correction/graph-context-proof.json`; `preservation-proof.json` | 37 components/39 paths; exact AC-10 chain; bounded fresh frame; preserved original inputs/control history | Explicit frame omissions retained |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2524; failed=339; possibleTokenSavings=591; ledgerDigest=sha256:334d8cc006bbd035c721ed5a2155ac6191c28abfeaa63e7c6e697140260bd577 |
