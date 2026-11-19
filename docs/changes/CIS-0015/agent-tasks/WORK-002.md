---
title: "WORK-002 Place CountryData unit examples in a dedicated examples solution project"
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
instance_key: "outcome:95e9d9e7a00c4bd58e22bbf7d7e7a742"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0015:outcome:440ffa6c914c4569ad3a0924bb6a175c"
authorization_scope_ids: ["example-project-placement"]
authorization_scope_basis: "sha256:2470a3a502c23aad7fda5293c4f2030a525d5528e527cc78ddf8916389871604"
---

# WORK-002: Place CountryData unit examples in a dedicated examples solution project

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Place CountryData unit examples in a dedicated examples solution project

## Acceptance criteria

- [x] Same23public CommonTesting examples live in dedicated CountryData.UnitTests under examples physically and in CisLib.slnx; old shared suite loses the example reference/closure; guide paths and run command updated.

## Targeted validation

- [x] Build/discover/run the new MSTest project on pinned runtime; verify all23pass and solution membership, public-only assembly boundary, no duplicate test source or Core-suite app reference, locked approved dependency closure, scoped format and docs; preserve user files and unchanged aggregate resource limits.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Dedicated solution project | Passed | examples/CountryData.UnitTests/CountryData.UnitTests.csproj under /examples/ in CisLib.slnx; ordinary dotnet sln list; shared Unit project/lock match main; three moved files retain their test bodies with only namespace changes. |
| Build, discovery and execution | Passed: 23; failed: 0; skipped: 0 | .artifacts/countrydata-unit/CIS-0015/project-correction/build.log (0 warnings/errors), results/countrydata.trx and solution-projects.log; distinct CIS.CountryData.UnitTests assembly uses public APIs without friend access. |
| Formatting, dependencies, docs and review | Passed | Scoped format plus verify; new lock's 34 resolved package/version pairs already exist in repository locks; strict docs 220/220 with zero warnings/errors; independent source review; both README commands/links updated. |
| Finite resources and user files | Passed | .cis/local/countrydata-unit-intake/project-correction-resources.json: correction1,257,419ms, aggregate6,654,228ms and2attempts including all prior costs; shared300,000ms restoration preserved. Physical5,929,212,633bytes plus67,108,864reserved fits6GiB; launch settings hash unchanged. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1718; failed=247; possibleTokenSavings=591; ledgerDigest=sha256:0d93328a3774614ba9041c7f42525fe4cc630424b022020c9f422bb305e961f4 |
