---
title: "WORK-005 Complete portable Security detail conformance"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: low
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a9bff6a7fc4445529c1b80c3466b4a2a"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0023:outcome:b5a77a9ea98644b98e9eb55f5a83d096"
authorization_scope_ids: ["capture-contracts","security-bridge"]
authorization_scope_basis: "sha256:59b508e93143e884d7b56e9319909094a75b00a763036ad3a370482ce1445146"
---

# WORK-005: Complete portable Security detail conformance

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Complete portable Security detail conformance

## Acceptance criteria

- [x] Finish the two portable Security detail contracts and independent bounded vectors against the actual existing schema evaluator, retaining complete ordered duplicate-capable256 capability observations and lossless safe codes. Keep common-wire and schema-validity distinctions, no new engine/provider.

## Targeted validation

- [x] Run existing AuditContractTests on all independent Security detail vectors after bridge/publicschemas freeze; review schema/runtime mapping and document actualproof. Reuse affected build/scopedformat and existing evaluator; include failed costs and current evidence, no broad test campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Portable Security schema/vector boundary | Passed |21independent securitydetailcases plus42retained commonwire/schema cases:63actualpassed0failed/skipped; existing evaluator only. |
| Mapping review / scoped formatting / affected build | Passed | Nativebridge immutablecapabilities/scalar-code mapping cross-reviewed; publication-security-format-verify includesAuditContractTests; security-contract-architecture-build0warnings/errors. |
| Scope and accounting | Preserved | [Hosting checkpoint](../hosting-checkpoint.md); lowcontractsupport uses30min/2attempts redistributed fromTask3 insideoriginal24h28, all earlier startedcontracts/costs retained. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2260; failed=312; possibleTokenSavings=591; ledgerDigest=sha256:fd030cd651fee7b509e263109ee3bfbf080f09d45833b7a951b416fd38408d66 |
