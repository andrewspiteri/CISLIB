---
title: "WORK-160 Implement durable run/attempt history, report reconciliation and protected retention"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-160
parent_id: WORK-090
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-090"
instance_key: "outcome:55e7bbd056944d628353b47355356168"
depends_on: ["WORK-157","WORK-158"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["scheduler","integration","portal","intake-design"]
authorization_scope_basis: "sha256:1d9fbc00b869383d6c5bb0c1098bcb8d19cf9faed577f4d637b595564ee35809"
---

# WORK-160: Implement durable run/attempt history, report reconciliation and protected retention

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement durable run/attempt history, report reconciliation and protected retention

## Acceptance criteria

- [ ] Persist/query the same durable logical runs and meaningful attempts for task/job/event views, with separate admission/entry/business-outcome/settlement and occurred/received observations, exact original operation and captured settings references. Authorize actual application/resource/run/attempt and every page/filter/detail; producer admission grants no target-history disclosure. Authenticate immutable sequenced reports: identical replay idempotent, conflicting same sequence refused, older progress cannot overwrite, late authorized original completion evidence may resolve unknown without reviving ownership. Retain actual immutable per-attempt release/build/handler and execution/recovery role; recovering build never steals original effect attribution. Coalesce ordinary progress5seconds, validate16KiB summaries/protected references, bound milestones/diagnostics and state growth. Use existing bounded cleanup/cached metrics-health owners: terminal details30days, aggregates180days, never prune unresolved/unsettled responsibility or needed compact receipts; quota reserves completion/recovery headroom and refuses new work. Owns RM10-AC16, RM10-AC17, RM10-AC22, RM10-AC23, RM10-AC24, RM10-AC25, RM10-AC30, RM10-AC34, RM10-AC36, RM10-AC38, RM10-AC40; this also supplies actual API and acceptance references for the approved history UI.

## Targeted validation

- [ ] Use real native store/auth fixtures for duplicates/conflicts/lower/late reports, mixed-version recovered outcomes/unknown provenance, exact original-result evidence, task/job same-row totals, hidden filters/totals/cursors/details, progress/result bounds, protected history pruning/replay and settlement quota. Coordinate schema solely with WORK-157. Document logical versus physical quota, replay horizons, stale/unknown states and source/attempt authority. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-157, WORK-158) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
