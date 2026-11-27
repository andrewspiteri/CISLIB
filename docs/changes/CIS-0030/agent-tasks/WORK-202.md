---
title: "WORK-202 Retire exact pooled Management broker topology after native drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-202
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:4aa62fc901864ebbbe1f72dddaa3031f"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:72fd6ed65b7baaf8857ec63520a74a7187368b9309b75583733ed200c849bca4"
---

# WORK-202: Retire exact pooled Management broker topology after native drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Retire exact pooled Management broker topology after native drain

## Acceptance criteria

- [x] Correct PR20 CI run46010343490 RabbitMQ128queue accumulation in the existing pooled test fixture. Capture newly owned Management namespace/setup identity before installation, drain all actual replicas/clients/runtime owners before exact topology deletion, and clean partial setup on failure. Preserve queue/connection limits, durable topology, native authority, TLS, sibling namespaces and actual joined cleanup. Reuse existing fixture/provider operations; no broad vhost/prefix sweep, cap increase, runtime topology change, new package/framework or existing database access.

## Targeted validation

- [x] Add a focused real pooled lifecycle regression proving repeated fixture disposal removes its exact queue/exchanges while a live sibling sentinel remains usable. Include scoped C# format/build, actual broker/resource proofs, all cleanup failure retention and independent review/docs. Own the complete standard Ubuntu service-CI retry once, sharing its evidence with203; preserve all363 selected tests/zero-skip strict guard, deadlines and pins. User authorizes push/merge after verified correction; no release.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Final native and Ubuntu qualification | Passed | [Exact candidate, 2 + 4 local cases, 364 Ubuntu cases, independent review, merge and retained accounting](../review-pr20-ci-corrections.md). Earlier failed evidence remains retained. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3485; failed=380; possibleTokenSavings=591; ledgerDigest=sha256:28562513ffd019ba91ed540d6e578084b6ec5ae28af99fbee728619852cf8f58 |
