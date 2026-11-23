---
title: "WORK-006 Validate integrated outage recovery and provider handoffs"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-006
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:d96bbbf5b7434b31ae0eede5bd838e07"
depends_on: ["WORK-002","WORK-003","WORK-004","WORK-005"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0025:outcome:56e9417305534bad8bc8776094f659b8"
authorization_scope_ids: ["delivery","records"]
authorization_scope_basis: "sha256:cd6de5cb6e9c38665f70f69b042e0a2e8f53df651f04ae9b607fae5b4a15fdd3"
---

# WORK-006: Validate integrated outage recovery and provider handoffs

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Validate integrated outage recovery and provider handoffs

## Acceptance criteria

- [x] AC01-20 updated requirement mapping; focused actual-provider/host/client/package/portable checks, standalone/shared examples, current skill and explicit evidence limits. Fix only acceptance-demonstrated gaps.

## Targeted validation

- [x] Native focused Windows+actualLinux eligible tests, CSharp/TS current clients, public API/compatibility affected checks, exact formatting, clean source/resource closeout; no broad campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Integrated delivery | Passed: 749 Windows and 749 actual Ubuntu units, 89 contract, 114 Node, 58 native integration cases across three scopes, three API/original-consumer checks; scoped formatting and current source/resource evidence | [Verification](../verification.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2356; failed=323; possibleTokenSavings=591; ledgerDigest=sha256:528dabdd0f6607271537e93a7eeb0f100bdb06fffb541672e90c9731ee558d08 |
