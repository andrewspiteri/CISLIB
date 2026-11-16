---
title: "WORK-002 Integrate sequential startup with live probes, Heartbeat, safe hosting and runnable cache example"
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
instance_key: "outcome:90a2252ef46e484db6e22d09720efa12"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0"
authorization_scope_ids: ["startup"]
authorization_scope_basis: "sha256:104bba5c3a57048c726589173933e59ceb6c67e313fdad8915953a164cf3d4f6"
---

# WORK-002: Integrate sequential startup with live probes, Heartbeat, safe hosting and runnable cache example

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate sequential startup with live probes, Heartbeat, safe hosting and runnable cache example

## Acceptance criteria

- [x] AC-01, AC-10 through AC-15 plus host AC-06/AC-09: prepare actual Type Manager before the one root Build and register the same instance; use one awaited StartedAsync lifecycle bridge with an explicit supported ordered host composition; retain truthful existing readiness and critical/noncritical health; health routes bypass the business 503 gate while real listener answers 500 during held startup and 200 after success; existing actual pipe probe observes 1/0. Keep HB inactive until all participants complete, positively acknowledge activation without waiting for callbacks/health, and preserve standalone HB behavior. Startup/HB close and true drains precede dependent stopping/disposal, including held initialization/final-publication stop races and failed-host-start cleanup. Deliver country-cache prime then dependent reader then recurring refresh/normal requests example using actual modules. Include existing-project integration/business/affected compatibility tests, guide updates and independent review. No alternate host framework, scheduler, health system, generic shutdown abstraction, new test harness or tooling campaign.

## Targeted validation

- [x] Use existing native projects/transport support to prove actual listener availability before blocked initialization completes, business counter stays zero, health/pipe transitions, critical recovery without replay, noncritical behavior, no premature HB condition/callback, activation without waiting for first callback, failures before activation, actual timer/activation failure or stopped no-op after successful startables leaving StartupCompleted false, stop/publication race and real startup/HB settlement before dependency disposal. Validate the cooperating profile rejects concurrent host start/stop options and unclassifiable hosted-service factory tails, preserving standalone HB behavior. Exercise the real Type Manager preparation/instance/failure path and runnable example; add only meaningful affected lifecycle/compatibility/architecture assertions. Preserve ordinary PerformanceObservation/ExtendedStress exclusions and existing package/runtime closure. Root controller serializes shared native builds; reuse valid unchanged results. Record exact AC-to-test mapping, actual native commands/results/cost/storage and responsibility-growth review; make no unexecuted platform or release claim.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Actual host integration |25passed,0failed,0skipped | startup-host-integration receipt/TRX; realKestrel/pipe/probe/stopdrains |
| Runnable source consumer and TypeManager |11passed,0failed,0skipped | startup-business-example receipt/TRX; actual source process plus positive/negative preparation stories |
| Final affected unit / architecture / compatibility |176 /334 /179 passed, zero failures/skips | startup-unit-regression, startup-architecture and startup-compatibility receipts/TRX; finalCoreSHA3ec9a27ba89bc22b8353fe048eedb91419da4e7dc6c52764bc29b89685a9cf90 |
| Documentation, acceptance and independent review | Complete within the two coherent task outcomes | planning/startup/acceptance.md; docs/modules/startup; source/receipt/hash/packaging reviews; standardDIlimitation explicit in verification |
| Accounting and storage |1713911ms inclusive tasknativecharge,1of2logicalattempts; no reservations/recovery | Same shared3h/4attempt/6GiB group; final countedstorage5391584490B and10427569B netgrowth |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=809; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:080fa7aba8fbfebdc6d4b12fad90be245cd1f204e15f40fbed9cf6b12d532480 |
