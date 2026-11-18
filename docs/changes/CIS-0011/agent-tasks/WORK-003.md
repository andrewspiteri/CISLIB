---
title: "WORK-003 Automatically attach the installed optional Metrics integration"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:9675c0941613421cb4d7c658196d8314"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda"
authorization_scope_ids: ["application-facade"]
authorization_scope_basis: "sha256:2ba6ff96550e6f8403209e849521c15a7c0224d9e53d4a659df08abb25579550"
---

# WORK-003: Automatically attach the installed optional Metrics integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Automatically attach the installed optional Metrics integration

## Acceptance criteria

- [ ] AP3 / APP-04,APP-05: implement only the narrow prepared-TypeManager registrar seam needed for supported optional Metrics service registration and existing web integration. Parameterless passive contributors run once in deterministic type order, deduplicate and reject conflicting contributions before one Build. Honor adapter enablement/custom profile/replacement choices; Core no-op cannot preempt installed Metrics. Keep one actual existing SDK/provider and central identity, exact host isolation and real callback-owner drain. Include tests, integration guide and review; no scanning/runtime plugin or lifecycle graph.

## Targeted validation

- [ ] A04/A05/A11: a Core-only facade runs without optional packages/endpoint; installed adapter automatically exposes real metrics in the web host; disabled/customized modes are respected without manual sample AddCisMetrics. Real scrape/two-host/root identities and existing relevant lifetime checks pass; contributor duplication/conflicts fail clearly and no contributor performs I/O/provider creation.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
