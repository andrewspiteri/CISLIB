---
title: "WORK-151 Define passive trusted capability and scheduler execution contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-151
parent_id: WORK-080
category: contract
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-080"
instance_key: "outcome:d6764a15072547cfa8f49710abd6f528"
depends_on: ["WORK-020-BACKOFFICE"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["capability-contracts","configuration-portal","intake-design","integration","portal","scheduler"]
authorization_scope_basis: "sha256:99bbd8947743415b377d7f2f5904b68fa36dc4a2af26666042adb57fdff5d0c6"
---

# WORK-151: Define passive trusted capability and scheduler execution contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Define passive trusted capability and scheduler execution contracts

## Acceptance criteria

- [x] Deliver passive immutable versioned trusted application capability/trigger/job/form descriptions and optional scheduler/resource eligibility ports, including input/result/event/execution/recovery/checkpoint contracts, exact worker build and identity-binding references, detached run settings, supported finite operations/hints and all selected RM10 defaults/bounds. Keep logical trusted definitions separate from observed per-instance participation and from Management API endpoint capabilities. Existing binding constructors/unmanaged consumers/independent backends remain compatible; unknown kinds/contracts remain unavailable. Presence cannot install schema, schema cannot load code, and capture performs no handlers/provider I/O. This owns the passive contract slice of RM09-AC01, RM09-AC04, RM09-AC05, RM09-AC07, RM09-AC16, RM09-AC20 and RM10-AC01, RM10-AC02, RM10-AC03, RM10-AC12, RM10-AC24, RM10-AC27, RM10-AC29, RM10-AC32, RM10-AC36, RM10-AC39, RM10-AC40.

## Targeted validation

- [x] Run focused passive binding/public contract unit cases for exact identity, version conflicts, required fields, supported operations and forbidden executable hints; check no handler/network resolution and existing API compatibility. Document trusted owner/observations, captured identity/contract and finite form semantics; include proportionate independent review. Verify this owner consumes only the declared medium prerequisites (WORK-020-BACKOFFICE) and keeps the original parent accounting pool; broad provenance containers are not early completion prerequisites.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Passive contract build and scoped formatting | Passed | .cis/local/testing/rm09-completion/work151-evidence.json; pinnedSDK build0warnings/errors and scopedformat+verify. |
| Actual meaningful unit cases | 24passed,0failed,0skipped | retainedwork151.trx exactinputs/dll recorded. |
| Independent review | Resolved | work151-review-final.md and work151-catalog-peer-review.md; originalfindings andfailedbuilds preserved. |
| Portable artifact syntax | Parsed98literalcases/104schemas | Syntax/referenceonly; actualCSharp/TSwire conformance is152. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2874; failed=365; possibleTokenSavings=591; ledgerDigest=sha256:906ec7cd241c96d9d9872e698200ec98040f6dc8fd58cdb6b67aeb7f51492adb |

