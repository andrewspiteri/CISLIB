---
title: "WORK-165 Deliver real whole-ScheduledTask editing, preview and operational commands"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-165
parent_id: WORK-100-BACKOFFICE
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:4283ad0acf844d78aec8ff2d3f060d44"
depends_on: ["WORK-157","WORK-158","WORK-159","WORK-161","WORK-162","WORK-179","WORK-190","WORK-168","WORK-172","WORK-155","WORK-156"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","scheduler","configuration-portal","integration"]
authorization_scope_basis: "sha256:c3bfe0a3f2c3dbe4245eaa206b2f2c283c42e4ca80680531b31d422f10d6e192"
---

# WORK-165: Deliver real whole-ScheduledTask editing, preview and operational commands

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver real whole-ScheduledTask editing, preview and operational commands

## Acceptance criteria

- [ ] Deliver the accepted ScheduledTask main settings/name/enablement/trigger/job/resource/mapping/identity selectors above exactly Trigger and Job contextual configuration tabs, with separate history action. Selection changes update exact owner/schema/contract/identity context immediately and keep per-definition drafts isolated; incompatible mappings block publication. Distinguish shared configured trigger, job defaults, task input/overrides and affected-task impact. Whole-task preview from either tab uses authoritative calendar evaluator/time zone/next instants or truthfully event-driven behavior. Compose native RM08 publication/release/assignment/adoption and guarded task activation sequentially with exact independent original keys, completed/outstanding step facts and old active revision preserved while pending/rejected. Show inherited real workload identity and supported contract/build compatibility only; unsupported delegation cannot fall back. Add supported enable/disable/retire/run-now/cancel with fresh permissions and original operation recovery; cancellation does not claim physical settlement. Owns RM09-AC07, RM09-AC09, RM09-AC10, RM09-AC11, RM09-AC12, RM09-AC14, RM09-AC16, RM09-AC17, RM09-AC18, RM09-AC19, RM09-AC20 and RM10-AC21, RM10-AC26, RM10-AC27, RM10-AC28, RM10-AC29, RM10-AC35, RM10-AC40.

## Targeted validation

- [ ] Use approved two-tab design and exact uncovered preview approval; test selectors/per-definition drafts/mappings/identity, authoritative DST preview and no fake next time, shared setting scope/revalidation, whole-task conflict/partial/unknown operation recovery. Reuse actual scheduler/Configuration service and desktop fixture for create-preview-publish-adopt-activate and one selected manual command, with session/grant/outage and keyboard/focus checks. Document task versus trigger/job ownership and actual runtime gates. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-157, WORK-158, WORK-159, WORK-161, WORK-162, WORK-164, WORK-190, WORK-168, WORK-172, WORK-155, WORK-156) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
