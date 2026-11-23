---
title: "WORK-003 Integrate native-fetch regression prerequisites and existing CI selection"
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
instance_key: "outcome:f9274ef45edf4801abdb48bfc5e2c1cd"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0026:outcome:150b7d43040443e2947ba9939e6405a4"
authorization_scope_ids: ["native-prerequisites","records"]
authorization_scope_basis: "sha256:9331927979d377fa9545cb893f7b94920cb61fb9e58e8275bc75bb8a814415a5"
---

# WORK-003: Integrate native-fetch regression prerequisites and existing CI selection

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate native-fetch regression prerequisites and existing CI selection

## Acceptance criteria

- [x] RequiresNode accurately classifies the new fixture, fast .NET-only selection excludes it, existing pinned Node/TS CI selection includes it, and root/testing prerequisites match without a new job or harness.

## Targeted validation

- [x] Review matching native category/filters and prerequisites, run the same focused native regression under WORK002 and include pinned formatting of the category attribute.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Native dependency routing and prerequisites | Passed: accurate RequiresNode selection runs the native case; fast exclusions, existing CI union and docs independently reviewed; CI run remains separate | [Verification](../verification.md); existing service-tests.yml and root/testing README changes |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2377; failed=325; possibleTokenSavings=591; ledgerDigest=sha256:168719cf4a7e1c8d410c0096ab254836ef39a1c3a2ac90b61cc8e9214e832020 |
