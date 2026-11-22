---
title: "WORK-001 Deliver portable messaging framing, bounded definitions and infrastructure ports"
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
instance_key: "outcome:d727709529da471693a39dcdbcf61909"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["contracts"]
authorization_scope_basis: "sha256:243b08075c99f308edb4e95fc52263a29c13c552e6278a609aafea1570183c32"
---

# WORK-001: Deliver portable messaging framing, bounded definitions and infrastructure ports

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver portable messaging framing, bounded definitions and infrastructure ports

## Acceptance criteria

- [x] P01-P04/P15/P17/P19: owned strict envelope/codecs, stable portable names and transport digest distinct from business identity; immutable snapshot/options/route/subscription/request definitions and narrow original-context store/transport/disposition/ticket ports. No registered throwing runtime stubs. This foundation is not complete broker delivery.

## Targeted validation

- [x] Meaningful units and portable wire vectors cover UTF8/duplicatekeys/depth/size/requiredfields/version/UUID/precision/ownedbytes, invalid/duplicate registration, passiveDI and result distinctions. Actual affected build/style/docs and independent contract review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Ownedwire/passivecontracts | Passed | 19currentfiles; independentportablevectors; review-001.md. |
| NativeWindows/Ubuntu | Passed | 60/60each,0failed/skipped; WORK-001/foundation-receipt.json. |
| Scopedformat/affectedbuild | Passed | Pinnedformat+verify; correctedReleasebuild0warnings/errors. |
| Failures/costs | Retained | InitialIPC/MSTEST0032logs and all measuredcosts preserved; finite sharedsource/native limits and gaps inverification.md. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1959; failed=272; possibleTokenSavings=591; ledgerDigest=sha256:f040f20075e9b354cf302aa98b296eb8338afdd5d4d2e8c1361fc52b4afc6375 |
