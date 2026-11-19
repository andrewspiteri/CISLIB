---
title: "WORK-010 Propagate certificate retirement faults and define Windows chain support"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:ac82f21ce513449a850ad15197139efe"
depends_on: ["WORK-002","WORK-004","WORK-005","WORK-008"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["rest-binding","certificate-renewal","certificate-inbound"]
authorization_scope_basis: "sha256:6d4ffa2df07d13b8d98e0de5b39d46653059b3496b7a930acfbcd2700970da39"
---

# WORK-010: Propagate certificate retirement faults and define Windows chain support

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Propagate certificate retirement faults and define Windows chain support

## Acceptance criteria

- [x] Observe eligible retired-generation cleanup before reporting certificate adoption; preserve nonblocking retirement when actual response holds remain. Surface the original cleanup fault and reflect permanent native admission closure in profile status/readiness even on unchanged source material or later body cleanup. Retain the bounded current/draining ownership model. State the Windows leaf-only client transmission arrangements and unsupported root-only peer case explicitly in public guides; no automatic certificate-store writes or existing-key/store inventory, and no unverifiable peer-availability promise.

## Targeted validation

- [x] Use finite privately journaled W01 fault scopes to test immediate A-to-B Removed callback failure and later held-body retirement failure, original fault propagation, rejected healthy adoption, unchanged-source behavior and status/readiness versus future calls. Add a real intermediate-issued client TLS case: explicit-issuer peer succeeds; isolated root-only peer fails on Windows while Linux supplied-chain behavior is checked if a suitable existing runtime is available. Include focused native REST/certificate regressions, exact owned cleanup audit, scoped formatting/build and current consumer guides. Preserve all completed-owner history, shared limits and external AKS/cert-manager gaps; no tooling/install campaign, push, merge or release.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Retirement and availability | Passed60Windows and60Linux certificate cases; isolated Windows faults preserve original exception/closed status; held bodies remain nonblocking. | `.artifacts/security/CIS-0016/review-follow-up/03-windows-certificate/results/certificate-native.trx`; `13-linux-certificate-native/results/linux-native.trx` |
| Intermediate native TLS and ownership | Passed18Windows and18Linux cases. Explicit issuer succeeds on both; root-only peer fails onWindows and accepts supplied chain onLinux. | `11-windows-intermediate-and-generations/results/rest-native.trx`; `12-linux-rest-native/results/linux-native.trx` |
| Build/style, guide and independent review | Pinned affected builds0warnings/errors; final scoped whitespace verify and strict docs248/248 pass. Independent native ownership and chain/guide review10minutes. | `../verification.md`; `../review-follow-up.md`; `../../../modules/security/credentials.md`; `../../../modules/security/transport.md` |
| Custody and restoration | All95new imports have Removed receipts and0exact backing files; deliberately faulted finite private receipts preserved. Temporary parentDACLs restored; original source/user files preserved. | `.artifacts/security/CIS-0016/review-follow-up/custody-audit.json`; `linux/parent-access-restoration.json`; `preserved-inputs.json` |
| Limits | Unchanged shared limits/reserves and completed owner history. ActualAC02/24 deployment and Linuxutility/privacy remain unverified; no store writes, push, merge or release. | `.cis/local/security-intake/resources.json`; `../review-follow-up.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1810; failed=254; possibleTokenSavings=591; ledgerDigest=sha256:671a23860c35a2c17274f2caaf2c124c0fc41dbdd6b946e9e2ef6a898c2abf15 |
