---
title: "WORK-186 Correct portal and isolated review session lifetime"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-186
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:e5c4b03276344e468f2f8575ecaf67e0"
depends_on: ["WORK-156"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:72fd6ed65b7baaf8857ec63520a74a7187368b9309b75583733ed200c849bca4"
---

# WORK-186: Correct portal and isolated review session lifetime

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Correct portal and isolated review session lifetime

## Acceptance criteria

- [x] Resolve the user's short-session report using sanitized actual owned-session/token deadline and HTTP rejection observations. Distinguish OAuth code, delegated access token, portal session and finite review host lease. Keep expired/revoked credentials rejected, production native policy and server-only tokens intact. Align the isolated manual review token/session lifetime with its explicit finite lease where appropriate, without refresh-token storage, authentication bypass or runtime/pin changes. Reuse existing auth/session ownership; no framework/campaign.

## Targeted validation

- [x] Focused actual session expiry/revocation/lifetime tests and existing fixture assertions; do not use elapsed time alone as diagnosis. Verify normal review survives beyond the reported short period while explicit expiry still retires protected data. Include docs and independent review within the same bounded owner. Preserve earlier Node failures and all costs.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Lifetime, expiry and owned cleanup | Passed for the bounded correction | [Prior review lifetime closeout](../r09-prior-review-lifetime-closeout.md), with retained focused expiry/revocation tests and actual 131615-ms independent continuity; final one-hour lease expired, native 1 passed / 0 failed / 0 skipped, TRX guard passed, fixture and assembly cleanup joined. Failed optional export remains separate; no snapshot preserved. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3296; failed=377; possibleTokenSavings=591; ledgerDigest=sha256:1d3aba490ccf3602cb4d42f2bae8df33c25b1055eeed34e903e3b976d38ffc18 |

