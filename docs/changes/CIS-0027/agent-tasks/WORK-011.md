---
title: "WORK-011 Integrate CountryData opt-in operator workflow and composed consumer handoff"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-011
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:0407c5073d964c19a211b4d77587770e"
depends_on: ["WORK-003","WORK-006","WORK-008","WORK-009","WORK-010"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0027:outcome:0b715aa498aa4ef7a0ab5458f32b51a3"
authorization_scope_ids: ["consumer","records"]
authorization_scope_basis: "sha256:6d3dd98164fefcee9f4b392c42feede89e8228e94bad51ae290e504c52c154a4"
---

# WORK-011: Integrate CountryData opt-in operator workflow and composed consumer handoff

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate CountryData opt-in operator workflow and composed consumer handoff

## Acceptance criteria

- [x] Existing CountrySettings RestartRequired opt-in with compiled common/override/staged/rejection/restart/rollback recipes; default remains unmanaged/database-free. Combined operator/two-replica/pre-constructor/adoption/restart workflow and clear24-criterion evidence/limits, actual enrollment skill guidance.

## Targeted validation

- [x] Actual current public/source/package/native consumers with independent transaction and local-state assertions, meaningful API compatibility and representative bounded volume result; focused current Windows/Linux boundaries using retained tools, no broad campaign.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Public opt-in, examples and shared existing validation | All61 example unit cases passed, including13 new cases; independent review no remaining finding | WORK-011/country-unit-examples/results/country-examples.trx; public CountryCentralConfiguration, existing CountryCatalog and CountrySettingsValidation |
| Maintained actual consumer | New full native workflow passed: operator on replica0, native SDK/TLS consumer on replica1, publication without assignment, canary pending, identified typed rejection, promotion, fresh host and higher rollback | WORK-011/countrydata-authoritative-flow-and-regressions/results/countrydata.trx; same file retains28pass/two failures; both unchanged failures pass in WORK-011/consumer/results/consumer.trx |
| Current public package/source compatibility | Actual outside-friend CountryData and Management/Core/CommonTesting consumers passed, exact current deployed DLL assertions retained; original binary/source/API checks all3 passed | WORK-011/consumer/results/consumer.trx; WORK-011/api/results/api.trx; broad CountryData receipt above |
| Actual Linux current candidate | 1128 selected units and151 contract/architecture cases passed using retained SDK10.0.401 and runtime10.0.12; locked offline package inputs unchanged | WORK-011/linux-focus-current/results/linux-units.trx; linux-contracts.trx; native.log and retained input hashes |
| Evidence, volume and guidance | Explicit24-criterion matrix with actual host-versus-process limits,65-recipient Unicode bounded-page observation and original failure history; existing Management skill/AGENTS/catalog updated | ../acceptance.md; ../verification.md; ../review.md; docs/modules/management/countrydata-configuration.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2446; failed=332; possibleTokenSavings=591; ledgerDigest=sha256:76e855c99e3ed3e8d3b3c70ee2cce6a9517275574bf89bb429bbae0a11132767 |
