---
title: "WORK-007 Preserve strict origin validation for ordinary portal authentication forms"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: backend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:bbaeecfd86204477aab21c47d9d5d092"
depends_on: ["WORK-005"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0029:outcome:dc66ea8b85f04962a869fde8ce1e9939"
authorization_scope_ids: ["portal","integration"]
authorization_scope_basis: "sha256:b2121a841254ff5f31a1197b7f4b995161dc36c7e27fd6b8e17ede20d750005b"
---

# WORK-007: Preserve strict origin validation for ordinary portal authentication forms

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Preserve strict origin validation for ordinary portal authentication forms

## Acceptance criteria

- [x] Correct the real browser sign-in/sign-out form rejection exposed by requested local manual setup. Use consistent same-origin referrer policy across the existing native host and Next headers, preserving exact Origin validation, CSRF, maintained OIDC/PKCE and cross-origin referrer suppression. Keep the approved desktop journey and all runtime authority unchanged. Include focused policy negatives, actual browser form sign-in/sign-out regression, build and documentation/review. No new auth UI, fallback for null/missing Origin, dependency or framework.

## Targeted validation

- [x] Focused policy tests retain rejection of missing/cross-origin headers. Production build passes. Existing native26 host cases and actual browser chooser/sign-in/sign-out/relogin and native app grants pass; record safe Origin/site observations only, no tokens/cookies. Independent review confirms no responsibility expansion.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

Current verification: .cis/local/testing/rm09-manual/verified-evidence.json binds15policycases, final26nativecases and5actualbrowsercases with full normal fixture cleanup. Final source/independent review: .cis/local/testing/rm09-manual/host-review.md. The live human session has its separate WORK006 owner and finite custody record; it is not a stopped verification fixture.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2779; failed=359; possibleTokenSavings=591; ledgerDigest=sha256:f075dbf3cdcd08c9bc98342bf9af925fdef9dffd965a79b4ec0f601cd2b78af4 |

