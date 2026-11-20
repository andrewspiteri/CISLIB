---
title: "WORK-007 Deliver three-profile CountryData security consumer and package handoff"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-007
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:bc481a0acb65426f9850e62e76285c3b"
depends_on: ["WORK-003"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["consumer"]
authorization_scope_basis: "sha256:d5a1ce759a6c43d7b35daf7529c5aa7b514397764851cbcc955bb82f814b02ed"
---

# WORK-007: Deliver three-profile CountryData security consumer and package handoff

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver three-profile CountryData security consumer and package handoff

## Acceptance criteria

- [x] P16: one compiling selectable CountryData protected service/caller shows harmless read, resource-scoped denial and restricted selected-country effect. Existing public testing helpers support genuine consumer needs. Current portable/profile guides, three optional package-consumer shapes, actual metrics exposition and checked-in security skill/routing agree with implemented APIs.

## Targeted validation

- [x] Exercise real native local caller/service permit and denial with unchanged denied effect; package Core-only, certificate-only and Entra shapes and verify dependency separation. Check real scrape bounds/central labels, public-only consumers, skill/docs validation and scoped formatting. Retain AC-02/AC-24 as explicit unverified roadmap gaps without infrastructure; no release claim. Independent public consumer/unit/skill/package source may proceed alongside renewal and the utility after the native Entra contract settles; selected certificate/local composition, its native proof and final completion still wait for actual WORK-005 and WORK-006 APIs and verification.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Real service/caller | Passed:3native facade/caller/server-byte-bound cases, actual metrics/audit and denied effect | `remaining/11-country-and-final-package-proof/results/native-country-consumer.trx` |
| Public unit examples | Passed:29total including6new CommonTesting-based rows | `remaining/07-current-combined-proof/results/country-unit.trx` |
| Isolated packages | Passed:3current Core/certificate/Entra package consumers | `remaining/11-country-and-final-package-proof/results/current-security-package-consumers.trx` |
| Preserved tutorial | Passed:40existing source/default/public/package cases | `remaining/12-existing-country-regressions/existing-country-regressions.trx` |
| Review/docs/skill | Scoped Release/style, strict245docs and bundled skill validation pass; native scope order/server bounds corrected | `.github/skills/cislib-security/SKILL.md`; current guides and verification record |
| External acceptance | AC-02/AC-24 and Linux/release qualification remain unverified; roadmap not marked complete | `docs/modules/security/entra.md`; `docs/modules/security/cert-manager.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1793; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:2f5c4f3cbe26514359c41af8bddcb2ad7c1d1d83b63f0c221b5d369230377afc |
