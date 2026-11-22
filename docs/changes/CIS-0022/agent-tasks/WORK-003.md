---
title: "WORK-003 Implement conservative managed ownership and consumer substitutes"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:99b61859ac294ef48881b3c10553f8aa"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["managed"]
authorization_scope_basis: "sha256:13f3bd2d8c4e9d0711c71435ba26dade8c85b89895a6f3c2067e3ce09eec178f"
---

# WORK-003: Implement conservative managed ownership and consumer substitutes

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement conservative managed ownership and consumer substitutes

## Acceptance criteria

- [x] AC04/05/12: scoped authorization facade and bounded singleton lifecycle, independent renewalcapacity, request-start monotonic confirmed deadlines, bounded exact acquisition recovery/terminalabandonment without mutatingreplay, permanent loss/release and once notification. Observe late/unresolved operations and callback occupancy until actualsettlement. Direct consumer lifetime responsibility explicit. Bounded public scripted coordination/manual handle helper uses existing CommonTesting.

## Targeted validation

- [x] Deterministic existingManualTimeProvider+controlledprovider probes: lostack exactrecovery, absence notabsenceproof, abandoned/lateack noactivation, responseafterpriorstop no revival, unconfirmedrenewal, uncertainrelease terminal, independentrenewal/businesspressure, loss once/outsidegate, boundedcapacity and actualdrain. Public outsidefriend helper test+docs, scopedformat/build and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Managed source/tests/docs and independent review | Passed | Runtime/Requests/Leases, scoped CoordinationService, passive paginated snapshot/OwnedLeases aggregate, public CommonTesting scripted/manual helpers; managed/testing guides. Root review corrected passive-read, late notification, physical duration and startup cancellation risks. |
| Scoped pinned formatting/build | Passed | managed-cancellation-format, managed-cancellation-format-verify and managed-cancellation-unit-build; SDK10.0.401/runtime10.0.12, zero warnings/errors. |
| Focused Windows current source | Passed | managed-cancellation-unit-tests/results/managed-unit.trx: actual60/60, zero failed/skipped; includes all40 WORK003 cases plus11 contracts and9 provider-unit cases. |
| Actual Ubuntu/Linux runtime current source | Passed | linux-managed-provider-units/results/linux-native.trx: actual60/60, zero failed/skipped using existing ELF10.0.12 with pinned managedVSTest. No Linux Docker/SQL execution is claimed. |
| Retained failed attempt | Preserved | First59-case unit invocation had57 passed/2 overstrict exact-cancellation assertions failed; corrected to allow valid TaskCanceledException subtype. Original logs/TRX and costs retained. |

This completion proves managed/local contracts and public helper behavior. SQL authority, actual job callbacks/host integration, complete package consumption and final module proof remain with their existing owners; no release qualification is asserted.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2184; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:3d8626fbababb5f3b78f91ea8e1a5519b7a5bb2b19ee0ae047da4d9d64cb2ba8 |
