---
title: "WORK-001 Own lazy test services and migrate durable PostgreSQL integration"
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
instance_key: "outcome:471aed679f574b97b242b722c1f5db56"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0021:outcome:504b54f7091445f489d20413d8d56bd2"
authorization_scope_ids: ["postgres"]
authorization_scope_basis: "sha256:c9b85ec9184c1557791ff1430ac598977c14047a5ca3802cf4895039cb7ae41e"
---

# WORK-001: Own lazy test services and migrate durable PostgreSQL integration

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Own lazy test services and migrate durable PostgreSQL integration

## Acceptance criteria

- [x] Integration-only lazy concurrency-safe typed ownership with enabled nonprivileged pinnedreaper, bounded shared/dedicated services, normal/failure cleanup and no Dockercontactfromfastmetadata. Exact test-only4.17.0 dependencies/actualgraph/mappings/locks; original PGschemas/factories/pools/timeouts/securityconstraints and real SQLassertions preserved across3fixtures with durabilityON. Dedicated same-storage clean restart recoverscommittedresult/effect/outgoing once, excludesunfinished effects viafresh app/independent sessions. No ambientDSN prerequisite/Inconclusive or shipping dependency drift.

## Targeted validation

- [x] Freeze minimal sharedtyped API forRabbit peer beforeindependent source use. Verify actualpinnedSDK/library/image APIs andboundedport/reaper behavior beforebroadening. Scopedformat/build/lockedrestore; existingPGbehaviorcases+newrestart/lifecycle/isolation cases onactualsupportedWindows/Ubuntu with nonempty/zero-skips reports; failfast missingDocker. Verify cleanupabsence and separate original/cleanup faults. Include categories, PGguides, independentreview and currentevidence within shared12h16attempt6GiB.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Owned PostgreSQL/common lifecycle, durability and cleanup | Passed | Windows5focused+145services; actualUbuntu145/145 run45034280172; cleanReleasechildbuild0W/E; shippinggraphs and independentreview. |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2140; failed=291; possibleTokenSavings=591; ledgerDigest=sha256:b84bffcedabc83c4719f6c0375982a8b96911e1b3e423fcc7475322267172dda |
