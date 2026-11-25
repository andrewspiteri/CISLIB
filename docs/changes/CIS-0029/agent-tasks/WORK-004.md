---
title: "WORK-004 Independent portal host and OIDC session boundary"
type: agent-task
status: Draft
task_status: Complete
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

- [x] Backoffice: add independent Next/TypeScript host using supported exact dependency pins and maintained OIDC code/PKCE session adapter. Management tokens remain server-only with secure HttpOnly cookies, callback/state/nonce/PKCE and origin/redirect controls, expiry/sign-out/revocation, per-request credentials and cancellation. Portable packages remain framework-free. Public responses traverse explicit cache policy (auth no-store); health has no database I/O, auth uses only the maintained owned session adapter with no direct Management repository access; no auth/test framework. Tests/docs/license notices included.

## Targeted validation

- [x] Focused host/session tests and build verify request isolation, denied browser token APIs, authenticated access, expiry/revocation, callback/return validation, origin/CSRF rules and cancellation. Check current supported versions/licenses and physical storage before install/build. Document separate owned session schema and local-versus-live identity evidence. Independent review checks responsibility expansion.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Production build and strict types | Passed | Final Next production build checks all application/custom-server/migration source; portable SDKs remain independent. |
| Host policy/projection checks | Passed | 11 native Node tests, with exact issuer/object ID, closed routes/inputs, expiry policy, trusted URLs and metadata-only projection. |
| Actual native HTTPS ownership | Passed | 1 MSTest asserts all 6 real TLS deadline/body-limit/saturation/stop scenarios and actual resource closure. |
| Real OIDC/PostgreSQL/native service | Passed | 1 MSTest asserts all 26 recorded controlled-issuer scenarios; real PKCE/state/nonce/signature checks, distinct users/native grants, expiry/reauthentication/revocation, token confinement, restricted SQL role and all native shutdown joins. Exact retained report hash: .cis/local/testing/rm09/host-verified-evidence.json. |
| Independent source review | Resolved | .cis/local/testing/rm09/host-independent-review.md; actual public PostgreSQL Client end barriers, failed-connect/timeout ownership, safe URL capture and fixture evidence corrections reviewed. |
| Preserved limits | Explicit | Controlled local issuer and fresh isolated provider proof are not live Entra, deployed encryption/replicas, browser UI, another OS lane or release qualification. No existing database, package expansion, push or merge. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2724; failed=357; possibleTokenSavings=591; ledgerDigest=sha256:13b3701feb30eba1ccf3b5b52851075c542a3552893a509f3a9ab25cb6f417bd |
