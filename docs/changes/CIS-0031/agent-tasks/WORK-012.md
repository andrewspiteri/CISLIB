---
title: "WORK-012 RM11 portal counter and execution inspection workflows"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-012
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:0e965275445b43659a8c342fd9b9244c"
depends_on: ["WORK-007","WORK-008","WORK-010"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-BACKOFFICE"]
authorization_scope_basis: "sha256:9336d0ca9c71d1acd7aa3f1700b74b265172cbbcb954da93c7336cebfdf99589"
---

# WORK-012: RM11 portal counter and execution inspection workflows

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 portal counter and execution inspection workflows

## Acceptance criteria

- [x] Connect counter reset/freshness and execution lookup/detail to the existing portal. Show authorized typed inputs with absent/null, evaluated row/cell outcomes, metadata order, and incomplete, failed or empty success distinctions. Own combined RM11-AC29 browser evidence with the editing/deployment leaf.

## Targeted validation

- [x] Run actual browser journeys for individual, selected and all resets, their scoped outcomes, reset time, grant/session boundaries, execution ID and ruleset/version/instance/time/outcome filters, and detailed traces.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Combined approved Management journeys | Passed | The exact `ActualRuleGridCounterAndInspectionJourneysKeepAuthorizedNativeEvidence` case passed in Microsoft Edge against the pinned .NET 10.0.401 native service, PostgreSQL 18.7 and authenticated RabbitMQ 4.4.1. It includes all three reset modes and original native outcomes, lossless historical detail, completed-empty/failed/cancelled/incomplete distinction, scoped grants and session boundary, and all WORK-011 editing and deployment checks. `.cis/local/testing/rm11-management-isolated/results/rm11-combined-portal-edge-final.trx` SHA-256 `da7c97f51bc17a17e2f3e1a75dc09af3b4d07b95cb97f1604fa737536feb9a07`. |
| Pinned portal build and local checks | Passed | Node 24.16.0 production Next webpack build and 223 portal repository tests, zero failures. |
| Browser and native host artifacts | Recorded | `.artifacts/management-portal/CIS-0031/WORK-012/browser-4cf24ff3-5665-4a95-8d36-852be70872d4/browser-evidence.json` SHA-256 `f32d4d79135aa36ea5e8eca27f619d31a4deec421e45fe09b4bdcbe2492a370c` and `.artifacts/management-portal/CIS-0031/WORK-012/native-host-34122842-9f75-44ff-8b4b-001fb25fe028/report.json` SHA-256 `1fdae832713e6c41bea836fe6153b4f9ff7e6b15ade821e58e08beb588e88b93`. The native report preserves the exact original reset keys and receipt times, the five captured execution IDs, and the saved/deployed/rolled-back versions checked by the fixture. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3844; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:c456e8dcae2bc517d85a2f6fc57806069a0811752b3aea1535ada33d7611f313 |
