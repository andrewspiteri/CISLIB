---
title: "WORK-001 Freeze portable RestClient contracts and shared execution seams"
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
instance_key: "outcome:seed:61db41c9aaa749e0889b067fc6684810"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-001: Freeze portable RestClient contracts and shared execution seams

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Freeze portable RestClient contracts and shared execution seams

## Acceptance criteria

- [x] Implement the approved portable9-method IRestClient surface, guarded immutable five-property RestResult and typed detached context, explicit value-presence/null/empty/truncation semantics, isolated client/call defaults and body/header/query/retry/extension contracts. Fix P04 fault-versus-known-cancellation precedence and W3C correlation mapping. Freeze minimal internal attempt/body-reader/retry/observation/passive-composition seams so transport, extensions, optional retry and collector can compile independently. Preserve native/SDK-free ordinary signatures, exact Core closure and no frameworks or stage graph. Include portable vectors, meaningful contract/architecture tests, docs and independent review.

## Targeted validation

- [x] A01-A03/A05/A22: ordinary public signatures and exact5 result properties, valid DTO/default/null/absent/failure construction, invalid combination rejection, immutable copied context/options, explicit callback/retry disable and finite16client/32pair metadata. Compile actual Core+tests; verify ordinary API exposes no HTTP/Polly/OTel types. Review shared ownership, entry-based clock, policy terminal summary and single observation binding before freeze; no fabricated transport/adapter pass.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Unit contracts | 36 passed, 0 failed, 0 skipped | `.cis/local/rest-client-intake/rc1-unit-pass.trx`; execution `native/incremental/2fac9f2fcae24d399d4292728119b148` |
| Architecture | 9 passed, 0 failed, 0 skipped | `.cis/local/rest-client-intake/rc1-architecture-pass.trx`; execution `native/incremental/f0ebbda2b53b47ad81064b64bee9fa8a` |
| Independent review | Required method/delay/bounds/result guards corrected | Contract and Metrics reviewers; [verification](../verification.md) |
| Scope | Contracts/snapshots/seams/docs complete; runtime proof belongs to successors | [Core guide](../../../modules/rest-client/core.md), portable contract and literal fixtures |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1206; failed=179; possibleTokenSavings=310; ledgerDigest=sha256:277e7ca2f9b400c097ed8156d669aca4a7030605689252a4c608c631a4809bca |
