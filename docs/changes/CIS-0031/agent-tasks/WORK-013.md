---
title: "WORK-013 RM11 opt-in CountryData consumer example"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-013
parent_id: none
category: integration
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:24892dc3dc664c36a3b67e979cc88d0b"
depends_on: ["WORK-005","WORK-006","WORK-007","WORK-008","WORK-011","WORK-012"]
assignee: null
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0031:outcome:e825fd00dd9f42388e09d9835f4bf34a"
authorization_scope_ids: ["RM11-EXAMPLE"]
authorization_scope_basis: "sha256:6fa4b973e735b63bc2f131bee9cf5ae88776eb82b7fb9d1c9d7b52e54949d7eb"
---

# WORK-013: RM11 opt-in CountryData consumer example

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

RM11 opt-in CountryData consumer example

## Acceptance criteria

- [x] Provide one opt-in CountryData recipe with ResidenceCountry and Pensioner, both modes, explicit deployment and a real execution ID that resolves to audit. Preserve the normal database-free default. Collect cross-surface proof for RM11-AC15, AC20 and AC29.

## Targeted validation

- [x] Run existing composed host/provider fixtures through both modes, two distinct application instances, captured version/inputs and audited execution lookup. Record the exact selected cases, runner, counts and links.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| CountryData default and explicit opt-in | Passed | .NET 10.0.401 Release MSTest, exact `CountryResidenceRuleExamples` fixture. 4 passed, zero failed or skipped. Covers default database-free composition, explicit passive opt-in and the checked rule recipe for both modes. `.cis/local/testing/rm11-management-isolated/results/rm11-countrydata-opt-in-unit.trx` SHA-256 `077bc129ec1993070dc808e881cb15d01f12c704a43969210d8b954d204e179a`. |
| Real CountryData applications and exact history | Passed | .NET 10.0.401 Release MSTest, exact `CountryDataProcessesKeepTheirCapturedModesInputsAndNativeExecutionHistory` fixture, PostgreSQL 18.7 and RabbitMQ 4.4.1 over TLS. One passed, zero failed or skipped. Two real processes held overlapping invocations with different registrations and captured deployed versions. The same fixture verified a Test-only completed invocation with zero returned rows and checked each execution ID against the protected native history. `.cis/local/testing/rm11-management-isolated/results/rm11-countrydata-native-processes-final.trx` SHA-256 `2c32a50a4a9742d756348bab942547ea5e14eaca1f3f88b2a70d34d84aad9b99`. |
| Consumer and operator recipe | Recorded | The [CountryData README](../../../../implementations/csharp/examples/CountryData/README.md) describes host enrollment, explicit Save and Deploy, invocation and execution lookup. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=3846; failed=406; possibleTokenSavings=591; ledgerDigest=sha256:55ac7ccd215e393cd95b0cc2382526a278029eeee7ff2dc2ed9eda53ef4b2b7e |
