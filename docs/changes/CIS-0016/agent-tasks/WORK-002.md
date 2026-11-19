---
title: "WORK-002 Bind security profiles and bounded credential generations inside existing REST transport"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:062195bb4cf3484cab8e22670719efc8"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["rest-binding"]
authorization_scope_basis: "sha256:2d4ffaeb03b359c801ca081933ce1a8995a032e0480238dc59df21dc49dd0082"
---

# WORK-002: Bind security profiles and bounded credential generations inside existing REST transport

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Bind security profiles and bounded credential generations inside existing REST transport

## Acceptance criteria

- [x] P06-P07/P11: stable native transport owns immutable profile/origins and bounded current/draining generations, with atomic per-exchange leases, safe borrowing and backward-compatible typed security detail. Reject bound suppression/replacement/TRACE while preserving ordinary clients, deadlines, retry and cleanup.

## Targeted validation

- [x] Native REST tests cover final destinations and overrides, profile/origin borrowing, held response/body leases, two-slot admission, concurrent retirement/selection and retry-delay rotation. Confirm root meter factory, primary dispatch/fault precedence and unchanged unbound behavior; include transport guide and scoped formatting.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Binding policy and result detail | Passed15current Unit cases;0failed/skipped |08-final-custody-proof/results/current-security-unit.trx; exactcurrentCorebytes match07native cases. |
| Native TLS/generations/retry | Passed14cases;0failed/skipped |07-bound-native-cases/results/bound-native-cases.trx; actualTLS, heldbodies, boundedadoption, retryselection, trust/hostname andactual401/403facts. |
| W01 owner/custody | Passed1two-leafprobe+2faultrows |06-w01-first-probe and08-final-custody-proof; reentrant/throwingcustody, exactbackingremoval, noabandonednativeowner. |
| Existing REST behavior | Passed134Unit+124Integration |02-unaffected-regressions; retainedcandidate/scope, noPackageConsumer orreleaseclaim. |
| Build/style/docs | Passed | Scopedformat+verify; affectedReleasebuilds0warnings/errors; security/transport.md andhttp.md agreewithimplementedAPI, preservedREST32originlimit documented. |
| Authority and custody | Withincurrentallocation | W01approvedDEC-001; source/nativecostsretained, current-candidate.json exactbytes andnative-custody-audit.json onlynewownedmetadata,0remainingownedfiles. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1765; failed=252; possibleTokenSavings=591; ledgerDigest=sha256:761fcfcfa02e5c3e728a8bf8d9c468696df231b61511616ff1a812b6349e012a |
