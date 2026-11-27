---
title: "WORK-166 Deliver scoped event/run/attempt history and worker-provenance details"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-166
parent_id: WORK-100-BACKOFFICE
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:f5c5f08e782f4687a8187346f71e23f4"
depends_on: ["WORK-160","WORK-161","WORK-162","WORK-163","WORK-172","WORK-156"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","scheduler","integration"]
authorization_scope_basis: "sha256:a3285495898dc7280212a2058df4210b5175580cfab7f927d4075c08597be102"
---

# WORK-166: Deliver scoped event/run/attempt history and worker-provenance details

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver scoped event/run/attempt history and worker-provenance details

## Acceptance criteria

- [ ] Replace unavailable history states only with actual authorized scheduler API records: separate task history, aggregate job history over the same logical runs, event detail and run attempt expansion/linked detail. Preserve exact application/environment/task/job/resource context and bounded filters/pages/cursors. Present captured configuration/revision/identity facts; configured actor, producer, optional requester, actual executor/profile and reported instance remain distinct. Each attempt shows actual release/build/handler and execution/recovery role, mixed-version transitions, NoCompatibleWorkerVersion, unknown original provenance and stale observed ages without desired-version substitution. Distinguish waiting, committed business outcome, unknown, cancel request and actual settlement. Do not reveal unauthorized result/input/detail references or invent task/event bindings/history samples. Owns RM09-AC04, RM09-AC05, RM09-AC06, RM09-AC10, RM09-AC14, RM09-AC15, RM09-AC16, RM09-AC18, RM09-AC19, RM09-AC20 and RM10-AC22, RM10-AC23, RM10-AC24, RM10-AC29, RM10-AC30, RM10-AC36, RM10-AC38, RM10-AC40.

## Targeted validation

- [ ] Test exact projections, same-run Task/Job identity, mixed-build/recovered original attribution, unknown and stale labels, bounded paging and protected result links. Reuse real authenticated scheduler store/API fixture and desktop checks for history/run/attempt navigation, hidden-target/session-loss/late-response cases and keyboard/focus at approved desktop layout. Broader executed worker recovery evidence remains with native integration owner, not fabricated by this UI fixture. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-160, WORK-161, WORK-162, WORK-163, WORK-172, WORK-156) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
