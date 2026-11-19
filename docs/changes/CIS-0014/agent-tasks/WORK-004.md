---
title: "WORK-004 Deliver CountryData State Machine and shared consumer guidance"
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
instance_key: "outcome:f8d22f0d929443979471f22e787b0f8e"
depends_on: ["WORK-001","WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0014:outcome:a6e73e152ddb4ab7bb2d053a47aafdfe"
authorization_scope_ids: ["consumer"]
authorization_scope_basis: "sha256:591320e8396a1697548ea1c966d60e6c3b995565ec0e016cb285ccedbe3e7a48"
---

# WORK-004: Deliver CountryData State Machine and shared consumer guidance

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver CountryData State Machine and shared consumer guidance

## Acceptance criteria

- [x] P11-P12: request-local CountryData State Machine using fresh existing lookup Pipeline; public/package mockable result, Busy/child scope story, one shared Workflows skill/metadata/routing; include HTTP/cache regressions, docs, formatting and review.

## Targeted validation

- [x] Existing real Core/CommonTesting local-package consumer and CountryData HTTP/readiness/normalization/cache/refresh cases, source build/publish, outside-friend mock and Draft/Submit/Approve skill story, test-owned Busy/reentrant and fresh child scope. Preserve Pipeline counts 5/12/3 and prove State Machine calls8/phases7. Validate shared skill metadata/discoverability, scoped format and strict docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual consumer, local packages and fresh skill use | Passed:41, failed:0, skipped:0 | Ignored .artifacts/statemachine/CIS-0014/consumer/consumer.trx;40 CountryData cases plus exact Core-byte source fixture. Package build/publish and independent public skill story run against actual packages outside friend assemblies. |
| Affected unchanged workflow/Core boundaries | Passed:66, failed:0, skipped:0 | Ignored retained-pipeline/pipeline.trx (45) and affected-architecture/architecture.trx (21) under .artifacts/statemachine/CIS-0014. |
| Delivery hygiene | Passed | Native Release solution build0warnings/errors; scoped whitespace format/verify; strict docs214/214 and two valid discovered skills; source/launch-settings hashes and finite resources preserved. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1672; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:06727acab81719a24c398c8e07f32c2de547876709379c3a060cfdfae6f58078 |
