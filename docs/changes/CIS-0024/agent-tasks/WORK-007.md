---
title: "WORK-007 Deliver portable TypeScript clients and server-only examples"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:22f3614a945a4b6b822f1267de52891c"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0024:outcome:e274768c237d45a5b9d0851dd5e27551"
authorization_scope_ids: ["typescript-clients"]
authorization_scope_basis: "sha256:07a63c0234b4a0678586c72c3c9f60b3d99cee08a6f67beef3e4c63982318569"
---

# WORK-007: Deliver portable TypeScript clients and server-only examples

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver portable TypeScript clients and server-only examples

## Acceptance criteria

- [x] AC11/12/13: compiler/lock pinned small client-core and management-client with runtime validation/all12typed methods, per-request credentials, HTTPS prefix/no redirects, finite one deadline/retry/body/cleanup semantics and immutable original operation identity. Browser imports omit Node/React/Next; explicit Node/server-only examples select safe DTOs; Kotlin note derives from protocol alone.

## Targeted validation

- [x] Built-in Node conformance/cancellation/held-cleanup/read retry/user-isolation and safe-projection tests, browser TypeScript build with DOM/types:[], package export checks and actual same-service Node HTTP consumer through existing MSTest owner in delivery. No new runner/bundler/framework runtime, global credentials or full TS CISLIB port.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual compiler and browser target | Passed | `WORK-007/typescript-direct-compiler-build` and `typescript-direct-browser-build`; pinned local TypeScript5.9.3, no ambient Node types. Quiet successes retained in native-costs.jsonl. |
| Native Node behavior | 90 passed, 0 failed, 0 skipped | `WORK-007/typescript-direct-native-tests`; exact shared fixtures, credentials/deadline/cleanup/retry boundaries and safe projections. |
| Same actual service | 1 passed, 0 failed, 0 skipped | `WORK-007/complete-real-csharp-node-consumer-tests`; both built clients used two real product roots, actual native TLS/JWT, PostgreSQL and RabbitMQ, then original-result recovery after another edit. |
| Source quality and documentation | Passed | Explicit-file CSharp fixture formatting/verification, zero-warning Integration build; TypeScript README, server examples and Kotlin protocol note. Local npm child PATH failure retained; exact pinned compiler and builtin test entry points were invoked directly with installed Node. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2303; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7418dede3ca30b3e748d83df4bb0fb3355da21a549719d5102291155fc566f7c |
