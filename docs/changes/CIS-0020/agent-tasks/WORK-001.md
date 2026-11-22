---
title: "WORK-001 Correct messaging pressure, quarantine and broker readiness continuation"
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
instance_key: "outcome:9cbf83797100430e9c964dfdc92d2ddb"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0020:outcome:b88fb8edc262456ba1bee71d5bbcdc05"
authorization_scope_ids: ["messaging-corrections"]
authorization_scope_basis: "sha256:b7f61ac7a4bc082150c4bae487cff77e4459c9b8e5bfce313ac348ddd9502bf6"
---

# WORK-001: Correct messaging pressure, quarantine and broker readiness continuation

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Correct messaging pressure, quarantine and broker readiness continuation

## Acceptance criteria

- [x] Temporary RPC byte pressure under valid12xEnvelope/3work-slot settings atomically defers before decoding and resumes both requests plus next delivery without consumer stop, including cancellation/cleanup; a wellformed Reply on ordinary ingress commits canonical-free rejection/quarantine preserving original bytes, publishes to quarantine and permits valid Event with same MessageId; RequireLiveBrokerForReadiness true/false yields documented difference after native broker disconnect while staging-only/startup/store/stop gates remain unchanged. Fix only these paths using existing ownership and machinery; include tests/docs and independent escaped-handoff review.

## Targeted validation

- [x] Focused units for combined admission/cancellation/resource release, quarantine validation/purpose metadata and readiness profile matrix; actual isolatedPostgreSQL+TLSRabbit concurrency and misroutedReply→same-IDvalidEvent continuation, actual native readiness disconnect/recovery if supported existing fixture. Build affectedprojects0warnings/errors, scoped pinnedSDK whitespace+verify, run affectedMessaging/Operations units onWindows/Ubuntu and affectednative tests; verify exactcurrentCore/source/TRXbindings and closure without broadcampaign. Independent review must confirm promised continuation and no material responsibility/harness expansion. Restore exact ownedfixtures/privateinputs afteractualdrain; resolve task acceptance/checklist/evidence and complete supported CIS lifecycle. All work including intake fits shared6h/8attempts and existing6GiBphysicalceiling; no push/merge.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native behavior and public boundary | Passed | [Verification](../verification.md); 542units+84SQL/TLSintegrationcases each actualWindows/Ubuntu; packagedRPC1 andCoreclosure1Windows,0failed/skipped. |
| Independent promised-workflow review | Passed | [Review](../review.md); cross-authorreview+exactresourcefixturecorrectionreview hasno required/optionalfindings; root97source/17binary/6TRXhashcheck has0mismatches. |
| Exact restoration and bounded cost | Passed | `.cis/local/messaging-review-intake/final-restoration.json`, `restoration-history-confirmation.json`, `final-cost-storage.json`;0retainedownedresources,213.9768of360minutes,1of8externalattempts,samephysical6GiBceiling. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2081; failed=286; possibleTokenSavings=591; ledgerDigest=sha256:37aae9b39afdbfa0515714096acc7bd3b759f69177b0476e4c91f1e35cffd8cd |
