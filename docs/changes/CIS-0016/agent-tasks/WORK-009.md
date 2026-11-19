---
title: "WORK-009 Recover required Entra availability independently of business readiness"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-009
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:f5e8c9a61b5a4e49bcef364a5479c67e"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["entra"]
authorization_scope_basis: "sha256:b180e41292209748a93232c690eb7d7c0d183d9635f9c91ce8533953ffa2e8be"
---

# WORK-009: Recover required Entra availability independently of business readiness

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Recover required Entra availability independently of business readiness

## Acceptance criteria

- [x] Correct the reviewed required outbound Entra readiness deadlock using the existing Heartbeat owner and reused SDK credential. A temporary startup acquisition failure or idle last-token expiry can recover without a business endpoint or host restart. An observed token timestamp does not alone establish replacement acquisition failure. Keep all caching/mint decisions with the SDK; no force refresh, new cache, scheduler or business-call replay. Coalesce and join admitted recovery/acquisition work, bound repeated probe frequency and diagnostics, and preserve native deadline/cancellation and original faults.

## Targeted validation

- [x] Add actual facade readiness regressions for recovery after temporary initial acquisition failure and idle expiry without restarting the host; verify business 503 and no effect while genuinely unavailable, later successful acquisition and 200/effect after recovery. Verify explicit source recheck, concurrent/coalesced probes, cutoff/drain and optional/inbound-only behavior with existing public/native seams. Include focused native adapter regressions, scoped build/format, guide/contract review and preserved failure/resource evidence. Real AKS remains unverified. No push, merge or release.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Required SDK availability/recovery | Passed47Windows and47Linux cases, including2same-host facade cases and2queued spacing rows. | `.artifacts/security/CIS-0016/review-follow-up/10-windows-entra/results/entra-native.trx`; `14-linux-entra-native/results/linux-native.trx` |
| Native build/style and review | Passed pinned build0warnings/errors and scoped style; independent8minute lifecycle review corrected queued-probe throttle. | `../verification.md`; `../review-follow-up.md` |
| Contract and resource boundary | SDK cache remains sole cache; existing Heartbeat only; drains actual work. AC02 remains unverified; shared allocation and historical costs retained. No push/merge/release. | `../../../modules/security/entra.md`; `.cis/local/security-intake/resources.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1808; failed=254; possibleTokenSavings=591; ledgerDigest=sha256:60fd36b0ace3cc9e1cf7519ce9ae5fd7534ae43f60ab14d1b0d39452c2e21431 |
