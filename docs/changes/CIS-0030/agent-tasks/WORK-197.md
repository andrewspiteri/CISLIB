---
title: "WORK-197 Invalidate protected Configuration across tabs and session changes"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-197
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:9a37e0bc9de34548b4d5f0ca4304c216"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-197: Invalidate protected Configuration across tabs and session changes

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Invalidate protected Configuration across tabs and session changes

## Acceptance criteria

- [x] Correct review87d18ba6P2 cross-tab sign-out and operator switch. Notify same-origin documents with credential-free invalidation and clear old protected values/drafts/original references immediately when signed out or identity changes. Revalidate the current trusted BFFsession when a tab becomes active before retaining protected display; preserve a still-valid same-session draft through a bounded in-flight check without unmounting the editor. Keep actual retired callbacks owned and joined, prevent late replies from reviving old state, and retain service authority. No token/user/Configuration payload in notifications, no polling, second authentication/session framework or reopening deliberately denied auth/account routes.

## Targeted validation

- [x] Use two actual pages in one browser context, real sign-out and another operator sign-in/fresh cookie context; verify old JSON/draft cleared, same-session focus preserves draft, missed/blocked notification still fails closed on focus, late responses do not revive values and native no-effect fingerprints remain unchanged. Add focused pure lifecycle tests and document notification failures; root serializes existing native fixtures/builds. Reuse approved backoffice layout and current states; include independent review, tests and docs in this task.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Review 87d18ba6 session correction | Passed local qualification | [Actual source, four browser journeys, strict native and independent-review evidence](../review-87d-verification.md); all earlier failures and charged attempts retained. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3448; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:946465044ff8cacfb45d3ab05e39865f802693b0786a24099a9124e8bf8074a1 |
