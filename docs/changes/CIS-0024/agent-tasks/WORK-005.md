---
title: "WORK-005 Deliver CSharp keyed REST client and public substitutes"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:a3a1b0c7055d418b9527dcefa39704b5"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["csharp-client"]
authorization_scope_basis: "sha256:2c9f9910fdeb6cd108ad3745658e322e04ed6cba45004113688a3ae9b04c54c2"
---

# WORK-005: Deliver CSharp keyed REST client and public substitutes

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver CSharp keyed REST client and public substitutes

## Acceptance criteria

- [x] AC11/12/15: typed IManagementClient all APIs, real keyed REST/auth owner and one entry deadline including codec decode; original RestResult and safe typed HTTP/problem/durable facts, exact caller key/cancellation. Bounded CommonTesting scripts and safe public observations support outside-friend consumers; no registration from plain read usage.

## Targeted validation

- [x] Prefix/origin/option/env, strict/adaptive wire, bounded body, normal failure/security evidence, explicit no-mutation-retry and held cancellation/cleanup tests. Public Core/CommonTesting packageconsumer, key recovery/no fresh rerun and dependency boundaries, affectedbuild/format and concise consumer docs. Real same-service calls are delivery acceptance.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Typed clients, wire and public substitutes | 129 passed, 0 failed, 0 skipped | `WORK-005/rejection-union-management-unit-tests` native TRX and cost journal. |
| Outside-friend package consumer | 1 passed, 0 failed, 0 skipped | `WORK-005/exact-current-management-package-test`; Integration rebuilt after current Core/CommonTesting so exact-byte guard holds. |
| Source quality and ownership | Passed | Explicit-file whitespace/verification; current Integration build zero warnings/errors. `docs/modules/management/csharp-client.md` documents ownership and recovery. Earlier stale-output guard failure retained. |
| Same-service C#/Node calls | Delivery acceptance under WORK-007/008 | Component closure does not claim this still-pending cross-language proof. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2301; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7709c6f560c4d3b88e6f993d635265a63c4cc6ba85545d5b17bab92198742e8e |
