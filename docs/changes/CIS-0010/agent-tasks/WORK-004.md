---
title: "WORK-004 Implement optional upstream retry policy adapter and bounded delay proof"
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
instance_key: "outcome:0efac71cc2a140a2a8d95b6175365d1c"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-004: Implement optional upstream retry policy adapter and bounded delay proof

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement optional upstream retry policy adapter and bounded delay proof

## Acceptance criteria

- [x] Create only the approved optional Polly.Core8.9.1 adapter with exact source mapping/centralpin/lock and no Core dependency change. One shared upstream pipeline per finite registered policy owns iterations/jitter/wait, with local per-execution state. Within1800active seconds/one logicalworkattempt of this task, prove public OnRetry nativeL plus optional supplementalD-L implements D=max(L,server) chosenonce and budgetcheckedbeforewait, priorcleanup and real cancellation. Preserve inherit/None/whole replacement, conservative preset, fresh replayable requests, stable keys and terminal cause/finite stopreason. Custom decisions use bounded cancellable metadata and never dispatch. If prooffailsstopretry-dependentexpansion; no copiedbackoffengine/extra package or semanticrelaxation. Own tests/docs/review.

## Targeted validation

- [x] A11-A15/A20: real approvednet10lockedclosure and optionalpublicAPI; actualcontrolled upstreamtimers for long/short/past/invalidseconds/dateRetryAfter and admission-fit,503/503/200/exhaustion, no-defaultretries/writes/business/decode/postheaders replay, one-shotbody and stableidempotencykey, sharedclock and actualheldattemptcleanup. Separatepolicyiterationfromdispatchcount. Rootserializesnative and capturesearlyproof/cost before broadening.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Public composed verification |93/93 current-input Windows integration cases passed, including real retry/payload/host shutdown | verification.md; rc5-public-pass.trx |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1258; failed=187; possibleTokenSavings=310; ledgerDigest=sha256:2ac10c1ef0bca0926bc51f43d92779a19d3895fc9b024643bed018ac3dd6a8fe |
