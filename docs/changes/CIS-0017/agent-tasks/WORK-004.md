---
title: "WORK-004 Integrate bounded transaction logging and metrics with existing collectors"
type: agent-task
status: Draft
task_status: Draft
task_id: WORK-004
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:f529f6f2e3004303b9da4978f9f8f1bd"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0017:outcome:aa6f3736e7f945c5bbd06406e9f22d8d"
authorization_scope_ids: ["observations"]
authorization_scope_basis: "sha256:f332d45d1a22664e7e5499e53a840400d16494ae80ab4efb1a5fc3421d58e8c2"
---

# WORK-004: Integrate bounded transaction logging and metrics with existing collectors

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate bounded transaction logging and metrics with existing collectors

## Acceptance criteria

- [ ] P09-P10 and sourceAC24-26/diagnostic16-17: safe categoryCIS.Transactions, LogCallsfalse default, once terminal observation and guarded instrumentation; exactthree conditional streams/128normal combinations, existing buckets/identity/capacities. Freezecomplete host binding atbeginadmission, startactive/duration onlypublishedowner and stopaftercleanup, reuseexactreport measurement, no late replay or mismatched decrement.

## Targeted validation

- [ ] Existing recorder/catalog/collector/scrape fixtures verify exportedfamilies/labels/outcomes/phases, native-only failure counts and twoDispose increments, failedbegin noactive/duration, callbackfault no guessednativefailure, unavailableclock nulltimer while activebalances, registrationorder/host isolation/noCollector/disabled/lateattach andcapacityoverrides. Throwinglogger/metric paths preserve outcomes/cleanup/original faults and redactSQL/DSN/fixturesecrets. Scoped build/style, metriccontract/guide and independent review.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
