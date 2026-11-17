---
title: "WORK-001 Establish portable Metrics primitives and host-owned recording contracts"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:seed:14d0b4845d9c4b4bbab3e8a509f1d360"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e"
authorization_scope_ids: ["metrics"]
authorization_scope_basis: "sha256:ce451dd7eb927ccdd3a6aa8473f946cddc546f9d4161af350f43a8a47ccd8c7d"
---

# WORK-001: Establish portable Metrics primitives and host-owned recording contracts

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Establish portable Metrics primitives and host-owned recording contracts

## Acceptance criteria

- [x] Implement the approved portable CIS-owned metric definitions, finite units/string dimensions, counter/up-down/gauge/histogram/timer/observable semantics as thin BCL adapters. Stable registrations reuse handles, incompatible translated definitions fail, invalid recordings drop with bounded diagnostics, timer first Stop/Dispose fixes exactly one attempt. Establish host-owned Meter/attachment and actual callback admission/drain seams preserving existing constructors/pre-DI services, disabled behavior and Core closure; no aggregate engine/global latest-provider. Own portable contracts/fixtures, meaningful tests/docs/review and freeze APIs/catalog before dependent work.

## Targeted validation

- [x] Native independent values and BCL listener cases for MET01/02 and A01-04: numeric semantics, timer race and invalid completion no retry, schema/name/translated child collisions and bounded capacity/invalid-input behavior. Verify public consumer API excludes BCL/SDK instrumentation types, Core dependency closure unchanged and proposed optional binding can use host-owned references. Inspect actual six-module attachment/lifetime seams before freezing; no speculative service or collector.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Core native semantics | 80 passed, zero failed/skipped; real Core+Unit compilation | metrics-core-unit retained receipt/TRX; five native Unit files |
| Existing and new architecture | 352 passed, zero failed/skipped; five new API/SDK-boundary cases | metrics-core-architecture receipt/TRX; MetricsArchitectureTests |
| Portable source and metadata | Five Core/Metrics files, optional-assembly IVT only; no new NuGet or existing-module change | contracts/metrics, conformance/metrics, docs/modules/metrics/core.md |
| Independent review | No blocking defect or responsibility/test-framework expansion; callback/registration/timer proofs remain direct | Read-only independent review; verification.md |
| Actual scope/cost | 466152 inclusive measured native ms, one logical attempt, no hold/reservation | Settled CIS work record; SDK/exporter/host/package/footprint proofs remain B/E |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1029; failed=149; possibleTokenSavings=123; ledgerDigest=sha256:f3f175200eb1ae111edadc55d39f3c7d2522587aba12d90b388bc43dcd7faa2a |
