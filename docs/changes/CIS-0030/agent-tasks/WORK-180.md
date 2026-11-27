---
title: "WORK-180 Deliver exact binding release history and scoped adoption reads"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-180
parent_id: WORK-167
category: data
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-150"
instance_key: "outcome:e9c912dad8d64f78bcaeb2ea13bf01b1"
depends_on: ["WORK-171","WORK-154","WORK-176","WORK-178"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-180: Deliver exact binding release history and scoped adoption reads

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver exact binding release history and scoped adoption reads

## Acceptance criteria

- [x] Deliver only the two missing bounded reads needed for the accepted Configuration rollout/adoption journey: paged retained immutable releases for one exact enabled binding, and adoption restricted to exact application/configuration/schema before target expansion and paging. Reuse existing release/adoption rows, native resource permissions, qualified clean read, retention, response bounds and cursor authority. Authorize binding and disclosed release resources before the bounded page; bind continuation to exact selector/page/current actor and policy. Keep old optional backend/client compatibility and unfiltered adoption behavior. Do not infer latest/current from UUID order, manufacture release names/versions, reconstruct expired content or fan out across all instances. No mutation, schema/package, Scheduler runtime, new store/framework or broad scan. Same original150 parent and shared allocation; this read prerequisite does not remove the original167 whole-map acceptance.

## Targeted validation

- [x] Use existing native PostgreSQL/HTTPS and CSharp/TypeScript wire/client fixtures for exact binding and schema/disabled/foreign refusal, filtered authorization before paging, bounded cursors and changed actor/selector policy, no-instance and mixed adoption with unrelated definitions excluded before pagination, clean cancellation/cleanup and unsupported optional backend. Preserve old unfiltered native status and existing release lookup tests. Canonical independent cases, affected builds, scoped formatting, docs, independent review and actual proof limits belong to this task.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Exact optional reads | Implemented and directly verified | `.cis/local/testing/rm09-completion/work180-focused-evidence.json`; 36 exact source hashes; required ports, old unfiltered route, retention/schema/packages preserved. |
| Native provider/HTTPS | 45 distinct scenarios have passing observations across retained batches | First batch remains 44 passed/1 failed; corrected actual lock observer then passes both cancellation rows. No single green 45-case batch is claimed. |
| Affected unit/contract/client | 545 unit, 183 contract, 123 Configuration TypeScript and separately 8 shared administration cases passed | Native journals and retained TRX/logs in focused evidence; 81 Configuration canonical vectors. |
| Build and review | Affected builds have zero warnings/errors; scoped format/verify and independent source/evidence reviews resolved | `work180-csharp-review.md`, `work180-typescript-review.md`, `work180-final-review.md`. |
| Completion boundary | Native CIS Complete accepted, valid with zero blockers; shared graph admission refused before execution | Direct current proof covers this read prerequisite only; prior partial graph is supporting and stale for these additions. Configuration rollout UI, Scheduler runtime and external deployment are not established. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3118; failed=372; possibleTokenSavings=591; ledgerDigest=sha256:1e202f24b3b6dffa529b968567b0786a21935b6c74afd7375c1d44180385bb2d |
