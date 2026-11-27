---
title: "WORK-006 Interactive local portal fixture with synthetic test data"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-006
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:1113dc5b25e44893af93044baddfdfe8"
depends_on: ["WORK-005"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["integration"]
authorization_scope_basis: "sha256:92ad11361027b74ba31ce8b6016223afd476f8d0fe4a16946660778ad9305757"
---

# WORK-006: Interactive local portal fixture with synthetic test data

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Interactive local portal fixture with synthetic test data

## Acceptance criteria

- [ ] Reuse the existing named native portal fixture for an explicit opt-in loopback interactive session with isolated synthetic applications, instances, configuration metadata and Jobs reports. Ordinary production host and genuine controlled-issuer OIDC/PKCE/native grants remain unchanged. Provide visible isolated test browser, safe ready URL/status, explicit stop control, finite maximum four-hour session, sequential startup and joined owned provider/schema/key/browser cleanup. No production demo mode, installed trust, framework, external deployment, push or merge. User explicitly requested local test-data setup after desktop delivery; existing standing authority delegates bounded routine setup.

## Targeted validation

- [ ] Build/format changed C#, syntax/policy tests and real manual-mode smoke including sign-in, both application rows, per-instance versions, metadata/Jobs and stop drainage. Launch final interactive session and verify ready; keep ownership active through its finite lease. Independent review checks auth isolation, lifetime/cleanup and responsibility growth.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

Current handoff: implementation and stopped-fixture verification passed; exact
results are in .cis/local/testing/rm09-manual/verified-evidence.json. The separate
human session is ready in a visible isolated Edge window; its URL, one-hour
expiry, launcher identity and stop control are recorded in
.cis/local/testing/rm09-manual/active-session.json. This task remains InProgress
while that native session is intentionally alive. Stop requests/lease expiry use
the same verified joined cleanup. Inspect runner-complete.json and fixture/
provider postflight before completing this owner; do not infer completion from
this handoff or from internal CIS counters that exclude the ordinary CLI work.
