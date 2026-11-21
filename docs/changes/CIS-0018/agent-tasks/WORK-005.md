---
title: "WORK-005 Integrate bounded operation logging and existing Metrics collection"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-005
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:331e595402bd4d29b05c68895386a167"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0018:outcome:a5124e88d31b486fafd34f4241c8fa4c"
authorization_scope_ids: ["observations","execution-recovery"]
authorization_scope_basis: "sha256:fdfec979f6e533151e027266b08c1f158678264f550232b14b25198a2f5a25e2"
---

# WORK-005: Integrate bounded operation logging and existing Metrics collection

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Integrate bounded operation logging and existing Metrics collection

## Acceptance criteria

- [x] P14/AC14: one captured binding and two conditional operation timers, fixed16aliases times13execute/9lookup outcomes (208+144=352), mandatory centralidentity/standardbuckets and unchanged globalcapacities. Prepared is preparation notcommittedsuccess; no duplicate transaction/polling measurements, rawkeys/scopes/payloads/SQL/exceptionlabels. Normalcallsquiet bydefault, diagnosticfailure cannotreplace originalfacts/faults. Include only the narrow public-boundary observation decorator, required passive hosting wiring and captured LogCalls option for this already-approved P14 integration; preserve the sequential coordinator protocol and keep runtime concerns independent of collector SDKs.

## Targeted validation

- [x] Core diagnostic-throw/disabled/lateattach/mismatchedowner and once-timing tests; actual native SDK/exporterscrapes with central labels, finiteoutcomes, bothregistrationorders, explicitsmallprofilevalidation, CoreModulesoptout and hostisolation. WithRM02selected assert26streams/3808capacity and meaningful affectedTransaction/Rest/Security/catalog regressions. Catalog/observationdocs, scopedbuild/style and independentcurrentTRX/hashreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Core observations | Scoped public-method boundary, immutable LogCalls, safe nullable timing/diagnostics and exact captured host binding | Operations/Instrumentation and hosting metadata |
| Actual native | Clean Release builds/scoped format; perOS116Unit,11SDK and31affectedcatalog passed, zero failures/skips | .artifacts/operations/CIS-0018/WORK-005/observations-receipt.json |
| Capacity | Two conditional timers,208+144 combinations;26streams/3808 withRM02, unchanged globals | contracts/metrics/operations.md; operation SDK tests |
| Review | Independent14source/doc/DLL/sixTRX alignment and necessity review, no required finding | ../review-005.md |
| Limits | SQL, retention, final public-package integration and release claims remain distinct | ../verification.md |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1928; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:1b017a939475897a66fdfefe7cc6a376da8bf5cd36baa0fb27e8afa02b78863d |
