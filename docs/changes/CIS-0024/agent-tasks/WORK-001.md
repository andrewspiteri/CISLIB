---
title: "WORK-001 Freeze portable management v1 and validated public contracts"
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
instance_key: "outcome:75fb253aa90942b7af5f8e91e64a7895"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["contracts"]
authorization_scope_basis: "sha256:301997828a71773dd13be0984849ee59aa6848eca40af58250216676e4ba7409"
---

# WORK-001: Freeze portable management v1 and validated public contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Freeze portable management v1 and validated public contracts

## Acceptance criteria

- [x] AC11 and shared API freeze: one OpenAPI3.1.1/schema/independent fixture set, all12typed requests/results, exact UUID/decimal/timestamp/metadata/ETag/problem/durable facts and actor/resource semantics. Public CSharp wire/model boundaries permit clients without CLR coupling; ordinary source readers preserve additive/unknown responses and strict closed commands.

## Targeted validation

- [x] Focused schema/wire and CSharp unit cases: above2^53, precise timestamps, Unicode/no normalization, absent/null/unknown/duplicate/invalid/oversized, status/key/ETag/replay/conflict fixture semantics. Existing schema subset only, no new engine/package; affectedbuild/scopedformat+verify and independent client/service contract review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Public wire/model and strict/adaptive boundaries | Passed | 88 actual targeted wire units, zero failed/skipped; [checkpoint](../contracts-checkpoint.md). |
| Independent schemas, literal values/outcomes and OpenAPI | Passed | 60 actual Architecture cases, zero failed/skipped, including 56 independent fixtures and authenticated typed route/example/reference checks. |
| Source quality and retained corrections | Passed | Affected builds zero warnings/errors; ordinary scoped format plus verification passed. Prose-test failure and native bounds/URI regressions remain recorded. |
| Independent scope/contract review | Passed | Native Jobs/identity/string and nested response drift resolved; direct existing JSON/evaluator reuse introduces no material responsibility or verification-framework expansion. |
| Acceptance boundary | Explicit | SQL/service/authentication/TypeScript/host/public-package and deployment proof belong the dependent owners; no source-only pass or receiver archive claim. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2293; failed=315; possibleTokenSavings=591; ledgerDigest=sha256:30d642467ad07e515cb22590f15c85968458d2ffbef419117a4d15d50d1146e2 |
