---
title: "WORK-159 Implement authoritative calendar evaluator and bounded replicated production"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-159
parent_id: WORK-090
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-090"
instance_key: "outcome:280a9dcb3dde4be79a9d574792477a64"
depends_on: ["WORK-157","WORK-158"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["scheduler","integration"]
authorization_scope_basis: "sha256:3d225de977ca00ec09c52d4539ea58b6d510ecd409014e5f1fecd459b8b17cf1"
---

# WORK-159: Implement authoritative calendar evaluator and bounded replicated production

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement authoritative calendar evaluator and bounded replicated production

## Acceptance criteria

- [ ] Implement one pure BCL evaluator shared by authoritative preview and Management calendar production for FixedDelay/Daily/Weekly. Validate IANA zone, Skip invalid local times and OnceAtEarlierInstant repeats, explicit missed policies/defaults and bounded chronological catch-up. In one qualified transaction atomically arbitrate due instant/event/run set/progress across replicas, with canonical instant identity independent of revision. Active boundary changes supersede unadmitted old backlog honestly; unchanged pause/resume retains cursor. FixedDelay has exactly one designated TaskId/predecessor/successor, completion-relative timing, explicit immediate/Continue choices and default terminal pause; unresolved predecessor never advances and manual runs never advance it. Use existing sequential Management readiness/lifecycle with at most100 due records per yielding batch; no self-bootstrap cycle, per-trigger thread, unbounded ticks or poll history. Owns RM10-AC04, RM10-AC05, RM10-AC06, RM10-AC07, RM10-AC13, RM10-AC34.

## Targeted validation

- [ ] Use controllable time for zone/unavailable zone/DST/preview, boundary edits, all missed policies/window/batch and FixedDelay retry/unknown/disabled-task/resume/delay edit cases. Reuse real two-root PostgreSQL arbitration and commit-boundary interruption proof for no duplicate/lost cursor progress. Document production policies and quiet bounded idle behavior. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-157, WORK-158) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
