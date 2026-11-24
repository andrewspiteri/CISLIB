---
title: "WORK-007 Add CSharp configuration client adapter and public testing boundaries"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b05869461b4a4952a61181fbb0c6fd36"
depends_on: ["WORK-001","WORK-004"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["csharp-client","configuration","records"]
authorization_scope_basis: "sha256:09300ca33088072e49bc8174b25649ed8d1b5de9f9e4bf5f79406986833995ad"
---

# WORK-007: Add CSharp configuration client adapter and public testing boundaries

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Add CSharp configuration client adapter and public testing boundaries

## Acceptance criteria

- [x] Separate substitutable config result/problem/client, native operation-specific codec and compact alias profiles, exact SHA verification/conditional304, central paired adapter with legacy routing and bounded safe public scripts, original-key recovery and native cleanup retained.

## Targeted validation

- [x] Native malformed/digest/status/session/304/deadline/uncertainty and32-pair tests, outside-friend exact-byte consumers and old public-store compatibility; public consumer docs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Affected native units | 1,114 passed, zero failed/skipped; clean build | `.artifacts/configuration-publication/CIS-0027/WORK-007/csharp-configuration-client-final-unit-tests/results/configuration-clients.trx` |
| Public package consumer | Passed; exact current restored/deployed Core and CommonTesting bytes, original-key recovery, public paired adapter and legacy routing | `.artifacts/configuration-publication/CIS-0027/WORK-007/configuration-public-package-consumer/results/configuration-package.trx` |
| Current native parent build | Zero warnings/errors, all references built against current candidate | `.artifacts/configuration-publication/CIS-0027/WORK-007/configuration-current-integration-build/native.log` |
| Independent review and guide | Final response binding, reason mapping, conditional read, ownership and passive profile accepted; public guide added | `../review.md`; `../../../modules/management/configuration-client.md` |
| Scoped formatting | Applied and verified using pinned SDK; closing verification receipt retained | `.artifacts/configuration-publication/CIS-0027/WORK-007/configuration-client-closing-format-verify/` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2423; failed=330; possibleTokenSavings=591; ledgerDigest=sha256:df695f2bcc1956126e383bc3f9ef9350f195573c12a3f512d38ea938c7801d98 |
