---
title: "WORK-003 Deliver optional RabbitMQ transport, native authority bridge and topology"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-003
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:5a7f4367f7014f7d913fd2e36218db1f"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["rabbit-provider"]
authorization_scope_basis: "sha256:9b43164d784f41deb269efe0533076a9ef8249b8b1c81008b2fa3332559df3d8"
---

# WORK-003: Deliver optional RabbitMQ transport, native authority bridge and topology

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver optional RabbitMQ transport, native authority bridge and topology

## Acceptance criteria

- [x] P08-P10/P13/P16/P18 and A03/A05/A09/A12: optionalclient/deploymentclosure, supportedpinned broker, explicitdurableboundedquorumtopology, TLSvalidateduserid privateprojector/current native scope, fourserialized confirms+mandatorypublish lanes and bounded manualackdelivery. One native recoveryowner; noimplicitinstall, defaultTTL/deliverylimitloss or second retryscheduler.

## Targeted validation

- [x] Real brokerprincipal/missingorforgeduserid/permission/TLS/route/confirm/ack/restart checks, safeDLX malformedpath, nativebuffer/concurrency/drain guards and optionalCore package separation. Fresh labelled fixture only within resourceadmission, actualversions/prerequisites/restoration, providerdocs/style/build and independentsecurityreview.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual provider | Windows 21/21; Ubuntu 21/21; zero failures/skips, TLS RabbitMQ 4.4.1, exact owned restart and safe blocked-DLX recovery | [Verification](../verification.md), `.artifacts/messaging/CIS-0019/WORK-003/provider-receipt.json` |
| Core boundary/build/style | Existing shipping dependency/reference closure 1/1; affected Release zero warnings/errors; scoped formatting and verification pass | [Review](../review-003.md), retained native cost/log journal |
| Independent review | Current ticket/generation/registration ownership and 26 initial receipt bindings verified; original failures retained | [Review](../review-003.md) |
| Current candidate | 21 Windows +21 Ubuntu actual provider; current Core closure1; zero failures/skips; immutable candidate2 | [Review](../review-003.md), `.artifacts/messaging/CIS-0019/WORK-003/provider-current-part-a-receipt.json` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2020; failed=281; possibleTokenSavings=591; ledgerDigest=sha256:59215f70f96907990e556ecb9324ade4c398291241b6f38b497b772358b96a69 |
