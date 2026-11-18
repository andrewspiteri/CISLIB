---
title: "WORK-010 Forward existing REST trace context under explicit or local-host policy"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-010
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:6198ad5f00af4f8f925e76ebcb22f1b3"
depends_on: ["WORK-009"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client-tracing"]
authorization_scope_basis: "sha256:85c6c8ddc118e87e8ce7a07acda706a3899c966efa7a9ef254d249045e1670bf"
---

# WORK-010: Forward existing REST trace context under explicit or local-host policy

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Forward existing REST trace context under explicit or local-host policy

## Acceptance criteria

- [x] Add nullable client/per-call ForwardTraceId, inherit client override and default automatic only for finalvalidated .local hostname. Capture existing entryW3Ccontext once, addtraceparent before native normalization/replay/signing, neverfabricateIDs or auto-forward baggage/tracestate. Disable implicit.NETpropagation so off is effective. Preserve explicit manual traceparent and nativeMetrics, pools, deadlines/retry/ownership; include tests/docs/review without tracer/framework/package or DNS/test-platform expansion.

## Targeted validation

- [x] Focused final-host/suffix/override/snapshot cases, directfinalizer auth/signing order, actualloopback headers with Activity/listener/defaultoff/expliciton/noActivity/manualheader and stableentrycontext. Run affectedexisting REST integration and bothConfiguration source cases; verify nativeHTTPMetrics stillworks. Preserve originalsource/userfiles, failures/history and allsharedlimits.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Trace policy and existing behavior | 131 passed, zero failed/skipped; 22 new trace cases, 98 existing REST/logging/source-consumer cases, 11 collector cases; current inputs/zero pending | native/incremental/bcd57eb9d6ea40759ecd4737abd0fe9d; rest-client-trace-final-pass.trx |
| Scope and ownership | Existing W3C context only, final-host selection, native propagation suppressed, manual headers/auth ordering preserved; native Metrics pass; no new provider/framework/package | RestTraceForwarding.cs; actual tests; core.md; verification.md |
| Costs and limits | 529927 inclusive native ms, one logical attempt; total 11605695 ms/nine attempts; all original work and limits retained under approved 13.5-hour total | Settled work ledger; authorization history; verification.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1361; failed=199; possibleTokenSavings=591; ledgerDigest=sha256:953828a7142efd322d2c574720c62633357dbe66960b485a66ccd70e3eedecc2 |