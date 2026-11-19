---
title: "WORK-005 Deliver bounded PEM credential reload and mTLS generation adoption"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:24d4d99871d0475fae553efa4bc8a5ca"
depends_on: ["WORK-002","WORK-004","WORK-008"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["certificate-renewal","rest-binding"]
authorization_scope_basis: "sha256:e9be967e16dcde55aea6d4034f1a2448b20e5529040053f61ee9ce0d1687c754"
---

# WORK-005: Deliver bounded PEM credential reload and mTLS generation adoption

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver bounded PEM credential reload and mTLS generation adoption

## Acceptance criteria

- [x] P07/P09-P13: PEM-only bounded source validates consistent material/local identity, uses existing Heartbeat30second coalesced checks, atomically adopts within two native generations, retains valid old owners and cuts off on stopping. Publish safe status/transition metrics and pinned cert-manager recipe without claiming AC-24 deployment. Explicit RequiredForReadiness defaults false; essential unavailability uses existing Healthcheck without changing liveness or receiver-only role validation. Retain immutable once-only server trust initialization before the first credential generation; loading trust and key files remains sequential Startup work, with no live trust reconfiguration.

## Targeted validation

- [x] Native file/TLS tests cover real new connection after adoption, mismatch/malformed/wrong identity/expired rejection, held responses/third changes/deferred expiry, concurrent leases/retry delay, source cancellation and actual drain. Check finite repeated diagnostics and memory-only gauges; record AC-24 prerequisites separately. Include credential/recipe docs/build/format/review. Verify sends/adoption cannot construct material before initial trust is explicitly initialized and a second initialization is refused.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native lifecycle | Passed:20PEM/ownership cases,0failures/skips within scoped selection | `remaining/10-corrected-native-proof/results/native-security-modules.trx` |
| Host/current consumer | Actual native facade/caller3cases and current3package shapes pass | `remaining/11-country-and-final-package-proof/results` |
| Review/build/style | Cutoff/authority-expiry fixes and regressions pass; affected Release0warnings/errors; scoped format/verify pass | `docs/modules/security/credentials.md`; `docs/changes/CIS-0016/verification.md` |
| Deployment boundary | cert-manager1.21.3 recipe supplied; AC-24 real deployment unverified | `docs/modules/security/cert-manager.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1789; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:fa0e7a3b2adf1fe2857cd72fecc47bc7ab5c9d3030f0c671131b73ef1e06a344 |
