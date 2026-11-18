---
title: "WORK-002 Implement native HTTP content and resource ownership"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:da1667c904f24db3929b14e6ffc02519"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d"
authorization_scope_ids: ["rest-client"]
authorization_scope_basis: "sha256:95f1d24bf874be08964edc019f8c7ce8af6e9ce1eb9c6ac1be7c75934ddb0136"
---

# WORK-002: Implement native HTTP content and resource ownership

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Implement native HTTP content and resource ownership

## Acceptance criteria

- [x] Bind native HttpClient/SocketsHttpHandler/DelegatingHandler and System.Text.Json to fixed Core seams. Implement fresh per-attempt content, JSON/text/bytes/no-value decoding, form and explicitly borrowed/transferred/reopenable stream bodies, correct URI/header/query placement and exact encoding/signing representation,16MiB actual/decompressed success bound and16KiB detached error prefix. Preserve one consumption owner and real cleanup, actual completed byte observations and no pool per call. Validate supported owned/factory and borrowed transport settings honestly; preserve TLS, redirect/cookie defaults. No HTTP parser, multipart model or implicit upload buffering; include focused tests/docs/review.

## Targeted validation

- [x] A01/A06/A07/A14/A16: observed wire/value vectors, native format/type validation and empty/malformed/null cases; exactlimit versusonebyteover including decompression, error truncation, borrowed leave-open and owned/factory cleanup after auth rejection/short circuit, distinct native request/content identities and real cancellation tokens/disposal. Use controlled handlers/streams and focused loopbackHTTP, not a new harness.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native content/transport | 44 targeted cases passed within the 134-case Core checkpoint | `.cis/local/rest-client-intake/rc23-components-pass.trx`; `native/incremental/ba6fdecdc301435ab18876d85338c20f` (shared WORK-003 execution) |
| Review and documentation | Required length/fault/ownership fixes corrected; supported profile documented | [HTTP guide](../../../modules/rest-client/http.md), [verification](../verification.md) |
| Boundary | Component outcome complete; real public operation/retry/host composition remains WORK-005 | No standalone runtime, package or release qualification inferred |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1236; failed=181; possibleTokenSavings=310; ledgerDigest=sha256:a64deaf512418b1fb8c456d1f0149cde518f170c6f672a36c31760f06b53fc48 |
