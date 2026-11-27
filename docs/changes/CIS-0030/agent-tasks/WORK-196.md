---
title: "WORK-196 Bound Configuration row action reader lifetime"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-196
parent_id: none
category: frontend
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:32d583d413be4112b9f613ea62c77205"
depends_on: []
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0030:outcome:1bd0878814134335b822d2827a100e02"
authorization_scope_ids: ["portal","configuration-portal","integration"]
authorization_scope_basis: "sha256:b02aaf8eb55627f17f66629e0e955a1880f78a12106b36a8d6021c2b5bb47282"
---

# WORK-196: Bound Configuration row action reader lifetime

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Bound Configuration row action reader lifetime

## Acceptance criteria

- [x] Correct review87d18ba6P1 in the approved backoffice Configuration list. Closed row menus must not register protected readers; retain the meaningful16owner bound and ordinary selection/authority. Use a lazy menu child or one selected reader, without conditional hooks or a new reader framework. Display at least20real catalog-provisioned Configuration rows, open/close multiple menus repeatedly, filter/paginate and keep actual row scope, zero closed-menu requests, no owner accumulation, no mutations and actual callback/resource joins. Preserve approvedR09screens and unrelated drafts.

## Targeted validation

- [x] Extend the existing native Playwright/MSTest/provider fixture with a finite20row case and exact scenario grants, preserving ordinaryA/B/security preludes. Meaningful pure coverage where applicable, scoped build/format, actual browser/result guard, independent review and concise docs belong to this leaf. Classify frontend backoffice, reuse existing approved design; no new layout, runtimepin, package, harness or Scheduler implementation.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Review 87d18ba6 correction | Passed local qualification | [Actual source, native and independent-review evidence](../review-87d-verification.md); retained failures, runtime identities and unchanged shared limits. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3446; failed=378; possibleTokenSavings=591; ledgerDigest=sha256:7932bd494b6fb66a48d7826ce45054cdfa8865d009cf30170511428acd5e8618 |
