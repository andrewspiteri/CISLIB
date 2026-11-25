---
title: "WORK-004 Independent portal host and OIDC session boundary"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-004
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:ecad0252531a467fab988f00caf14fc4"
depends_on: ["WORK-001"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:b2121a841254ff5f31a1197b7f4b995161dc36c7e27fd6b8e17ede20d750005b"
---

# WORK-004: Independent portal host and OIDC session boundary

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Independent portal host and OIDC session boundary

## Acceptance criteria

- [ ] Backoffice: add independent Next/TypeScript host using supported exact dependency pins and maintained OIDC code/PKCE session adapter. Management tokens remain server-only with secure HttpOnly cookies, callback/state/nonce/PKCE and origin/redirect controls, expiry/sign-out/revocation, per-request credentials and cancellation. Portable packages remain framework-free. Public responses traverse explicit cache policy (auth no-store); health has no database I/O, auth uses only the maintained owned session adapter with no direct Management repository access; no auth/test framework. Tests/docs/license notices included.

## Targeted validation

- [ ] Focused host/session tests and build verify request isolation, denied browser token APIs, authenticated access, expiry/revocation, callback/return validation, origin/CSRF rules and cancellation. Check current supported versions/licenses and physical storage before install/build. Document separate owned session schema and local-versus-live identity evidence. Independent review checks responsibility expansion.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
