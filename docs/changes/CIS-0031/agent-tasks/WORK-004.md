---
title: "WORK-004 RM11 deployment authority and retained-version eligibility"
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
instance_key: "outcome:fa6f317dbf12465891e7998cccf9c8df"
depends_on: ["WORK-003"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-MANAGEMENT","RM11-RUNTIME"]
authorization_scope_basis: "sha256:ad6740c0c7214a820b19730f4651b54167965ae683c01d9a5ed884b9adff4c0e"
---

# WORK-004: RM11 deployment authority and retained-version eligibility

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 deployment authority and retained-version eligibility

## Acceptance criteria

- [x] Deploy and roll back already saved immutable definitions through existing guarded RM08 assignment. Record same-application/environment deployment eligibility in the original transaction and protect live and retained audit references. Cover RM11-AC17 and AC19.

## Targeted validation

- [x] Use native provider fixtures for save without deploy, saved-only and wrong-scope exact rejection, successful rollback, original-key replay, conflicting rollout, and cleanup around retained references.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| RM08 carrier and evaluator regressions | Passed | Pinned .NET SDK 10.0.401, 79 native MSTest results. `.cis/local/testing/rm11-deployment-carrier/rm11-deployment-carrier.trx` SHA-256 `a46abf6b29d0919e7ac69093e5ffaf74d60167020e42c7906609ba2ea97adea0`. |
| Management deployment client | Passed | Pinned Node 24.16.0, TypeScript package compilation and 6 focused tests. Covers assignment ETag, original-key recovery, exact version UUIDs, and desired assignment response. |
| Integration Release build | Passed | Pinned .NET SDK 10.0.401, 0 errors and 0 warnings using standard project and fixture output paths with the same two focused test-only imports. |
| Native Management deployment | Passed | Four focused PostgreSQL 18.7/RabbitMQ 4.4.1 tests executed from the current scoped Release test assembly. Includes competing assignment originals, saved-only exclusion, wrong-rule rejection, rollback to an earlier unassigned version, exact RM08 bytes, and rollback through retained history after carrier pruning. `.cis/local/testing/rm11-management-isolated/results/rm11-deployment-provider-current.trx` SHA-256 `6be2628a78c85f9c5d5bb59fcd9d6f155b0936d04abfd7f2043451d79e816214`. |

The isolated integration build supplied the existing two Scheduler test-only imports documented under WORK-003. The isolated output caused the post-build copy target to miss the non-RM11 messaging crash fixture in its standard output directory. The current Management service and integration assembly compiled, and direct VSTest execution used that assembly with the existing service test settings and real providers.

The durable version witness and immutable saved definition survive native release cleanup. Deployment produces desired assignment. Workload adoption, historical execution selection and per-instance applied status are covered by WORK-005.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3826; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:7032b239e88521b1043eed06b173ad6108543d3bf368b045bf3230ba316ce6c0 |
