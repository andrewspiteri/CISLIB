---
title: "WORK-004 Deliver CountryData pipeline consumer and repository usage guidance"
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
instance_key: "outcome:8164dab1df5b4d44b24d28ffb21cd82d"
depends_on: ["WORK-001","WORK-002","WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0013:outcome:9c519b131ea94ffd9a16e9ad0e8c1eaa"
authorization_scope_ids: ["consumer"]
authorization_scope_basis: "sha256:c77cd3acb653002b273d5d9524a2291a4e61ff1d14a6dced092286d16a4fa595"
---

# WORK-004: Deliver CountryData pipeline consumer and repository usage guidance

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver CountryData pipeline consumer and repository usage guidance

## Acceptance criteria

- [x] P11-P12: integrate three-step cached CountryData lookup, preserve HTTP and refresh behavior, compiled public/package consumer, actual guide, one repository skill/metadata/routing with use validation. Include coherent tests/docs/scoped formatting and final review.

## Targeted validation

- [x] A16: real Core outside-friend/package consumer plus compiled CountryData unchanged HTTP responses/aliases/cache behavior, middle-failure reverse handlers and caller-scope identity. Skill frontmatter/metadata/discoverability/link validation and independent usage check. Format only changed C# files; strict docs and affected regressions.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Consumer and skill | Passed:35 cases; current public/package gates repeated in final83-case run | Actual current Core/CommonTesting packages, compiled CountryData, native build/publish, no friend/reflection access, independent skill-authored story, generated metadata and strict skill/docs checks. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1635; failed=238; possibleTokenSavings=591; ledgerDigest=sha256:f0eabef395dbb9d5ede66c2bdabda7a6d93e5ffd5d32abc5f7f44391be58ceb9 |
