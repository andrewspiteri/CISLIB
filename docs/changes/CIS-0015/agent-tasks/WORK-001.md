---
title: "WORK-001 Deliver CommonTesting-based CountryData unit examples and testing guidance"
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
instance_key: "outcome:f9e94aa04e224c4fad1b023686bf4339"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0015:outcome:440ffa6c914c4569ad3a0924bb6a175c"
authorization_scope_ids: ["unit-examples"]
authorization_scope_basis: "sha256:3070e924a3d0f6a2c0e56ed6b0fb9f0319dfc9bb9037e46f1bb2ec1fa854bf56"
---

# WORK-001: Deliver CommonTesting-based CountryData unit examples and testing guidance

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver CommonTesting-based CountryData unit examples and testing guidance

## Acceptance criteria

- [x] Readable discoverable unit examples against the actual compiled app cover publication/aliases/no lookup I/O, manual refresh replacement/failure, initial failure, original fault and owned cleanup, plus direct lookup-step semantics. Reuse public CommonTesting, add expected-result and integration-boundary guidance, preserve production and user files.

## Targeted validation

- [x] Focused native Unit build/restore and CountryData unit filter; scoped ordinary formatter+verify; strict documentation checks and independent source review. Keep examples host/network-free and public-only, with held operations released/joined. Confirm dependency lock changes reflect only the approved existing app closure and no production reference to CommonTesting.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Discoverable unit examples and controls | Passed:36, failed:0, skipped:0 | Ignored .artifacts/countrydata-unit/CIS-0015/examples/countrydata.trx (23) and common-regression/common.trx (13); ordinary dotnet test after clean Release Unit build. |
| Delivery/review boundaries | Passed | Scoped format+verify, strict docs219/219, existing package/version-pair validation, independent source review, unchanged production/user files and finite accounting in .cis/local/countrydata-unit-intake/resources-final.json. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1686; failed=241; possibleTokenSavings=591; ledgerDigest=sha256:55a6a05420fedae943048f2777dfc651ad360ae499ffe008cb977c862bc167db |
