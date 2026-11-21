---
title: "WORK-001 Deliver portable operation identity, definitions and canonical request contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:6d9c3481ded841c6ba997bf241836719"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["contracts-identity"]
authorization_scope_basis: "sha256:817dff6377b724cc38f6950aa21c8ef362ecb15e1bcc2134a44f36c24921f6fd"
---

# WORK-001: Deliver portable operation identity, definitions and canonical request contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver portable operation identity, definitions and canonical request contracts

## Acceptance criteria

- [x] P01-P06/P13 foundation: immutable bounded portable identity/decision/execution/lookup/store/codec boundaries, passive16definition registration, owned normalized input and operation-specific deterministic versioned SHA256 frame, stable readers and concrete limits. Include exact portable byte/digest vectors and retain RM02/RM01 contracts. No registered service with unimplemented throwing stubs; this foundation alone is not full RM03 delivery.

## Targeted validation

- [x] Meaningful native units for alias/key/scope/UTF8/input/result limits, duplicate definitions, passiveDI, immutable copies, culture/Unicode/decimal/missing-default-null/map-order vectors and input mutation. Build Core/CommonTesting boundary, scoped pinned format+verify, portable contract docs and independent current-source review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Publiccontracts/passiveDI/canonicalvectors | Passed |14C#files,12manualbyte/digestvectors; currentcontractand review-001.md. |
| Scopedformat/build | Passed | foundation-format/verify and changed-filecorrections; latestbuild0warnings/errors. |
| Actualnativecases | Passed | Windows28/28andUbuntu28/28,0failed/skipped; WORK-001/foundation-receipt.json exactcurrentbytes/TRX/sourcehashes. |
| Preservedfailures/costs | Retained | CS8602testassertion and actualpayloadgetterregression/fixed; initial143scope115legacy+28new retainedseparately. Externalcosts/manualattempts notautomaticCISconsumption. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1909; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:0d3bd10c2e23f7ea158d297d97861e5b714d1617d726b5352e9b82d8d6342bdf |

