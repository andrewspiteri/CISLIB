---
title: "WORK-009 Log completed REST calls through the existing logger"
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
instance_key: "outcome:8b6e00da32d44c57865d550a503612cf"
depends_on: ["WORK-007"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client-logging"]
authorization_scope_basis: "sha256:766707c87cec9dc8e50fa061fb114a914b90bcb1edcca5d0f65009bdd786518d"
---

# WORK-009: Log completed REST calls through the existing logger

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Log completed REST calls through the existing logger

## Acceptance criteria

- [x] Automatically emit one final logical REST call summary through the existing configured ILogger/Core provider after actual cleanup and terminal commit:Information success,Error finite failure/cancellation/fault; METHOD safeURI RESULT logicaldurationms and actual dispatched Attempt n/max when retries are selected. Preserve existing faults, results, caller cancellation, metrics and host lifetimes; ordinary logging failures cannot replace them. Strip credentials/query/fragment/payload/error details from rendered and structured data. Preserve original standalone constructor and add only a borrowed-factory four-argument overload. Include focused public tests, docs and independent review; no new logger/runtime/provider/dependency or framework.

## Targeted validation

- [x] Verify public success/error formatting and severity, actual2of3 retry summary without intermediate summary lines, business200 failure, invariant elapsed duration, finalmethod/path, privacy in both rendered/structured fields, heldcleanup/cancellation/originalfault preservation and disabled/throwing logger. Prove one existing CIS provider bridge and borrowed standalone factory. Run affected REST integration plus both ConfigurationSourceConsumerTests with current locked inputs; record actual scope/currentness/cost and preserve all three user launchSettings. Source correction old2-casepass/currentnesshold remains historical.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual logging and regression proof |98/98 passed,zero failed/skipped;11new logging cases,2Configuration source cases,85existing REST integration cases; current inputs/zero pending | native/incremental/1d0881d9e9904c6197b68356c32ccf10; rest-client-logging-final-pass.trx |
| Implementation and review | Existing logger/one terminal event; actual count/elapsed; safe bounded URI/state; borrowed factory; original constructor/fault/cancellation ownership preserved | RestCallLogging.cs; RestClientRuntime.cs; HttpRestClient.cs; core.md |
| Cost and limits |809929 inclusive measured native ms in one logical attempt; earlier compile error retained; overall11075768native ms/eight attempts; approved13hours/16attempts/6GiB unchanged | verification.md; settled ledger; original approval/history preserved |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1348; failed=199; possibleTokenSavings=591; ledgerDigest=sha256:f2b11946bd45980b137a2d02b468fdefb64f9a4a43b4fb75ff26ecde1d57a9c8 |