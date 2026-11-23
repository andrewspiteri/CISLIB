---
title: "WORK-001 Freeze authoritative control and portable admission boundaries"
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
instance_key: "outcome:88e0442d5de14952bec683a14d197a4e"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0025:outcome:56e9417305534bad8bc8776094f659b8"
authorization_scope_ids: ["contracts","records"]
authorization_scope_basis: "sha256:994bdc9f9d93a66e579fe41a5fab11ff9790a1fe9f19fc64e60b0fced30cfc91"
---

# WORK-001: Freeze authoritative control and portable admission boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Freeze authoritative control and portable admission boundaries

## Acceptance criteria

- [x] Control13thAPI exact fields/nonce/no-store semantics and explicit no-retry clients; additive compatible public ports, CommonTesting and independent CSharp/TS/wire cases.

## Targeted validation

- [x] Focused unit/schema/TS checks including malformed,nonce,cleanup and no-retry cases; current docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Contracts and clients | Passed: 89 shared-contract cases, 114 Node tests, native CSharp/Node control and exact-byte public consumers | [Verification](../verification.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2350; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:95bf5fe37f9bfe8da3f6948a256930acd7d7a9b6b2f3860da7feed36710f99a8 |
