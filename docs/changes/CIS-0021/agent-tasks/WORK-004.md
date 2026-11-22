---
title: "WORK-004 Make the selected consumer restore portable on fresh Linux CI"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:836d9afccb724b3b8c2d6db92ec17dbc"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0021:outcome:504b54f7091445f489d20413d8d56bd2"
authorization_scope_ids: ["portable-ci-consumer"]
authorization_scope_basis: "sha256:4515f31a68effa43680e8dc5be0e4507764a53325e9da7a42f41be4345324d67"
---

# WORK-004: Make the selected consumer restore portable on fresh Linux CI

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Make the selected consumer restore portable on fresh Linux CI

## Acceptance criteria

- [ ] Add only three exact mappings for already-retained Microsoft packages and declare both supported CountryData runtime identifiers in its lock profile. Keep selected RuntimeIdentifier/UseCurrentRuntimeIdentifier/inventory/output behavior, package versions, production behavior and protected launchsettings unchanged. Fresh pinned locked restore/build and actual nonempty Linux service CI pass; include focused Windows consumer/closure checks and documentation.

## Targeted validation

- [ ] Inspect actual failed cold Linux restore, prior locks and TypeManager selected-RID targets. Re-evaluate only CountryData lock, verify both win-x64/linux-x64 groups and no version drift/new package IDs; full locked restore/build, meaningful affected source-host/consumer/closure tests, then145-case actual Linux CI. Record the source/binary delta from frozen local proof and retained failures; independent review; all work/restoration within unchanged shared12h16attempt6GiB.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
