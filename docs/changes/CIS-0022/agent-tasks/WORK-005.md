---
title: "WORK-005 Dispatch trusted externally admitted scheduled occurrences"
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
instance_key: "outcome:f7b730c5f91748078d6b30ed408da48f"
depends_on: ["WORK-001","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["scheduled"]
authorization_scope_basis: "sha256:eb1ee2c175dad0eaff4d9c81b387b07e110b5ed3f723e64c036907bc64dc7f80"
---

# WORK-005: Dispatch trusted externally admitted scheduled occurrences

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Dispatch trusted externally admitted scheduled occurrences

## Acceptance criteria

- [x] AC09/10/12/13/15: sourcevalidated immutable admission/settings/revision/EncodedCommand with matching stable businessidentity, immediatecapacitybeforelease, correcthandler/authorization, exact job/resource exclusivity acrossoccurrences, precise Completed/PreviouslyCompleted/Busy/Deferred/Lost/Cancelled/Failed/Indeterminate withoutprematureack. Descriptor shapes/DST/misfire capabilities clear; enabled missingSource/evaluator fails, disabledinspect works. Controlled bounded ExternalOccurrences trigger makes no recurrence claim.

## Targeted validation

- [x] Focused tests admissionforgery/unknown/unauthorized/mismatchedoperation, capacitydefer/no lease, localsamekeyoldcallback excludesredispatch, newoccurrence samekey, redelivery stableoperation, disabledprecallbackdeferral, immutableadmittedsnapshot, mode/source reactivation, all schedule forms/policies and evaluatorcapabilityfail. Reuse CommonTesting+real coordinatedstore fixture whereappropriate, docs+independentreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Coherent source/tests/docs and independent review | Passed | Shared actual capacity/key occupancy, finite scoped continuous work, whole Configuration subscription, or trusted scheduled dispatch/bounded public source as applicable. Review corrected authorization ordering, capacity-after-await, normal disable, cancellation barrier and original business uncertainty diagnostics. |
| Scoped pinned formatting/build | Passed | jobs-format/jobs-format-verify/jobs-unit-build; SDK10.0.401/runtime10.0.12, zero warnings/errors. |
| Current Windows focused native cases | Passed | WORK004/jobs-unit-tests/results/jobs-unit.trx: actual78passed/0failed/0skipped. Includes27 Task004 cases (19 runtime/continuous +8 subscription),35 Task005 cases (29 dispatcher +6 publichelper),16 catalog/binding/schedule cases. |
| Actual Linux runtime focused native cases | Passed | WORK004/linux-jobs-units/results/linux-native.trx: same78passed/0failed/0skipped on actual Ubuntu/Linux ELF10.0.12 with existing pinned managedVSTest. |
| Configuration and actual settlement | Passed | Whole-capture/initial-event/disposal races, category and reduced capacity, immutable admitted timeout/drain/settings, exact auth/resource/handler, fair yielding/idle capacity, actual callback/scope retention through loss, graceful stop, once cleanup and Committed/Unknown preservation. |

The tests use transparent controlled providers/public helpers for local job behavior, not SQL, authentication or broker durability. PostgreSQL authority has separate real11-case proof; native RM03/RM04/public-process consumers and actual host/exporter integration remain downstream. The narrow unpublished MaximumDrain constructor bound was reconciled to the actual managed lease's one-day maximum during Task004; previous source/evidence history remains preserved.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2192; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:5f0d560d6634beab27b711a267c3398d84685a149d28e41715ff4f28fd68c39b |
