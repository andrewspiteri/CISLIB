---
title: "WORK-162 Integrate managed worker rescan immutable build provenance and drain compatibility"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-162
parent_id: WORK-140
category: lifecycle
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-140"
instance_key: "outcome:608cb090a35945908c7f3753b55492aa"
depends_on: ["WORK-161","WORK-158","WORK-160"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["scheduler","capability-contracts","integration"]
authorization_scope_basis: "sha256:238a5c2b2c79c9c10ae55275104cac98a1df0a142bb258102087f40ef3d71576"
---

# WORK-162: Integrate managed worker rescan immutable build provenance and drain compatibility

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate managed worker rescan immutable build provenance and drain compatibility

## Acceptance criteria

- [ ] Deliver the concrete Management-backed run source and bounded worker ready-run rescan using the existing managed bootstrap/control/reconciliation and Jobs owners. Each actual local immutable handler/startup build/contract binding is checked before selecting/entering work and copied to durable attempts; payload/desire labels do not supply authority or build identity. Missing compatible workers retain same pending/unknown run with bounded NoCompatibleWorkerVersion and no business retry consumed. Close new runs/retries/acquisitions/continuous iterations during local drain or Management outage, retain entered finite code/renewal/settlement dependencies, and join actual callback/scope lifetime. Reconnect/restart rescan authoritative responsibility; no lost local prompt or new independent execution fleet/lease/retry engine. Owns worker/lifecycle slices of RM10-AC01, RM10-AC14, RM10-AC17, RM10-AC18, RM10-AC19, RM10-AC20, RM10-AC29, RM10-AC32, RM10-AC36, RM10-AC37, RM10-AC38, RM10-AC39, RM10-AC40.

## Targeted validation

- [ ] Reuse existing actual native Management/Jobs/Coordination fixtures for closed-bootstrap/admission and current build/identity/contract selection, bounded rescan/reconnect, old-build finite drain and continuous next-iteration refusal. Test missing compatibility and unsupported rollback without changing ownership key or captured run. Keep original-outcome interrupted effect proof with delivery integration; document deployment/drain limits and review independently. Verify this owner consumes only the declared medium prerequisites (WORK-161, WORK-158, WORK-160) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
