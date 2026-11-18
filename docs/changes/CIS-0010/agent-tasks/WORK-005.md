---
title: "WORK-005 Own logical RestClient deadlines dispatch outcomes and actual shutdown drain"
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
instance_key: "outcome:2b48bfa8db0546fabcbae02373c0f512"
depends_on: ["WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-005: Own logical RestClient deadlines dispatch outcomes and actual shutdown drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Own logical RestClient deadlines dispatch outcomes and actual shutdown drain

## Acceptance criteria

- [x] Implement one fixed outer operation path: entrytimestamp/budget, isolated options/admission and one observation binding, fresh ownedattempt policycallback, dispatch immediatelybeforeprimary afterauth, actualcancellation/cleanup and immutableterminalresult or caller/fault propagation. NoTimeoutWait abandon, sharedclient cancellation or deadline reset. Keep Core/None working withoutoptionaladapter. Add narrowoptionalRESTcutoff toexistingStartup/HBhosting beforeHB/Configwaits andjoinactualdrains, preservingcoordinator independence/sequentialstartup and borrowed ownership. Configuredretryintegration waitsWORK004; completeactualMetricsboundary proof withWORK006. Own tests/docs/review.

## Targeted validation

- [x] A07-A10/A15/A21: actualblocked send/upload/body/auth cancellation and heldcleanup; expectedcaller>total>stopping>attempt precedence while unexpectedfaultssurface, before/aftercommit races, othercallunaffected and zero predispatch counts. Actual heldHBcallbackawaitingREST must cancel beforeexistingbarrierswait andkeepdependencies usable until cleanupreleases, includingcanceledhostwait. Alwaysrelease/joinbarriers in finally; no lifecycleframework.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Actual composed ownership |Public93 plus finalcomponent136 cases passed, currentinputs, zero failed/skipped/pending; original encoder faults and actual host drain verified | verification.md; rc5-public-pass.trx; rc5-components-final-pass.trx |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1268; failed=189; possibleTokenSavings=310; ledgerDigest=sha256:938fc89a7ed35e3f9397f4553d62a59af9ff35ac0598eeee9fe1131de9218a86 |
