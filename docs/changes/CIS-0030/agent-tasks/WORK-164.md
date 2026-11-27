---
title: "WORK-164 Deliver finite Configuration authoring, exact immutable publication and release"
type: agent-task
status: Draft
task_status: Decomposed
task_id: WORK-164
parent_id: WORK-100-BACKOFFICE
category: frontend
complexity: high
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-100-BACKOFFICE"
instance_key: "outcome:a3961c027751450e81e9110b47150909"
depends_on: ["WORK-171","WORK-154","WORK-155","WORK-156","WORK-163"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-164: Deliver finite Configuration authoring, exact immutable publication and release

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver finite Configuration authoring, exact immutable publication and release

## Acceptance criteria

- [ ] Deliver authorized source selection, existing content/default scope, finite trusted text/number/Boolean/enum/duration/group controls and bounded exact generic JSON fallback. Render only known declared field hints; preserve unrelated/inherited members and exact numeric/Unicode lexical meaning instead of round-tripping through Number. Separate read/default/value authorization, redacted/unavailable versus empty/absent and owner semantic adoption versus structural validation. Validate/preview side-effect-free, review exact payload then publish immutable content and create an exact common/application coherent release under original independent keys/ETags. Publication/release creation does not assign or activate. Conflict retains only still-authorized in-memory draft for explicit reconciliation; partial/unknown steps recover original outcomes without replay. Use the shared closed BFF/receipt primitive, not a new store/framework. Owns RM09-AC07, RM09-AC09, RM09-AC10, RM09-AC11, RM09-AC12, RM09-AC14, RM09-AC15, RM09-AC16, RM09-AC17, RM09-AC18, RM09-AC19, RM09-AC20 and RM10-AC26, RM10-AC27, RM10-AC28, RM10-AC29.

## Targeted validation

- [ ] Test declared hint/path/type limits and unsupported components, exact large numbers/Unicode/false-zero-null/unrelated members, no mask/reset/hidden value publication, conflict and same-key partial recovery. Run actual native RM08 author-review-publication-release desktop flow with read/write/lookup separation, session/outage/grant loss and focus/keyboard. Document authoring semantics and exact content handling. Include meaningful focused native/unit or browser checks for this implementation, affected documentation, and proportionate independent review; preserve failures and actual joined cleanup. Do not turn authored tests or reused evidence from different source bytes into a pass. Verify this owner consumes only the declared medium prerequisites (WORK-171, WORK-154, WORK-155, WORK-156, WORK-163) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
