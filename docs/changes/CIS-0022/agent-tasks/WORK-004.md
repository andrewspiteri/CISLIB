---
title: "WORK-004 Run finite continuous duties with shared capacity and configuration"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:925f991368d3470aad5c8cf3b0fbc825"
depends_on: ["WORK-001","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["continuous"]
authorization_scope_basis: "sha256:35c1f3cf761c0210cf77f9bada6f2e589ab8bde32c798e3c432a2c2e5c012c52"
---

# WORK-004: Run finite continuous duties with shared capacity and configuration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Run finite continuous duties with shared capacity and configuration

## Acceptance criteria

- [x] AC09/10/11/12/15: exactregistered/authorized scoped handler, bounded readyadmission and shared key/category/host occupancy, finite MoreWork/Idle with fairyield and positive5sidle, lease retainedwhileidle but businessslotreleased. CompletevalidatedConfiguration snapshots preserve lastvalid/rejectedrevision and required mode/source deactivate/drain/reactivate. Faultunexpected work bydefault, no automatic arbitrary/Unknown replay; no local restart before actualcallback+scope finish.

## Targeted validation

- [x] Controlled scoped jobs/clock/config prove correctwallet/customer binding, onehostauthorizedboth, categorydisable/hardlimit/reduction/rename identity, invalidatomicupdate, idle/MoreWork fairness, independentrenewal and uncooperativecallback actualslot+scope retention. Hundredsdefinitions/boundedpressure and snapshots withoutI/O. OrdinaryMSTest/defaultprofile+focusednativeUnit, docs and independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Coherent source/tests/docs and independent review | Passed | Shared actual capacity/key occupancy, finite scoped continuous work, whole Configuration subscription, or trusted scheduled dispatch/bounded public source as applicable. Review corrected authorization ordering, capacity-after-await, normal disable, cancellation barrier and original business uncertainty diagnostics. |
| Scoped pinned formatting/build | Passed | jobs-format/jobs-format-verify/jobs-unit-build; SDK10.0.401/runtime10.0.12, zero warnings/errors. |
| Current Windows focused native cases | Passed | WORK004/jobs-unit-tests/results/jobs-unit.trx: actual78passed/0failed/0skipped. Includes27 Task004 cases (19 runtime/continuous +8 subscription),35 Task005 cases (29 dispatcher +6 publichelper),16 catalog/binding/schedule cases. |
| Actual Linux runtime focused native cases | Passed | WORK004/linux-jobs-units/results/linux-native.trx: same78passed/0failed/0skipped on actual Ubuntu/Linux ELF10.0.12 with existing pinned managedVSTest. |
| Configuration and actual settlement | Passed | Whole-capture/initial-event/disposal races, category and reduced capacity, immutable admitted timeout/drain/settings, exact auth/resource/handler, fair yielding/idle capacity, actual callback/scope retention through loss, graceful stop, once cleanup and Committed/Unknown preservation. |

The tests use transparent controlled providers/public helpers for local job behavior, not SQL, authentication or broker durability. PostgreSQL authority has separate real11-case proof; native RM03/RM04/public-process consumers and actual host/exporter integration remain downstream. The narrow unpublished MaximumDrain constructor bound was reconciled to the actual managed lease's one-day maximum during Task004; previous source/evidence history remains preserved.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2191; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:bc32726940226f312819be412be622084c513d8cc65b5b42a746ca0ce64337a8 |
