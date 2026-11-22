---
title: "WORK-002 Migrate secure RabbitMQ ownership and RPC child handoffs"
type: agent-task
status: Draft
task_status: InProgress
task_id: WORK-002
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:7b344cbef1774dafa99dfa9cf02c793e"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0021:outcome:504b54f7091445f489d20413d8d56bd2"
authorization_scope_ids: ["rabbit"]
authorization_scope_basis: "sha256:ad2a75785f3eed5bf7210d0f1d947772bdee5fa4fbf03774a6bb0d8275b8bf65"
---

# WORK-002: Migrate secure RabbitMQ ownership and RPC child handoffs

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Migrate secure RabbitMQ ownership and RPC child handoffs

## Acceptance criteria

- [ ] Use the frozen common owned-service ports; finalnative acceptance requires qualifiedPGfoundation. Self-provisionedTLSRabbit preserves actualsecurity/topology/confirms/returns/quorum/ack/redelivery/restart tests viaownedTestcontainers APIs withoutfixedhostpaths/ports. Bound/excludeoverlapforrestart andresolve actualdynamic-port semantics withoutweakeningrecovery. Parent-owned minimalprivatechilddescriptors preserve genuineRPCprocessdeath/currentresult recovery and publicpackage consumers, withselected-only secrets. Initialfailure/freshstartup behavior and failurecleanup are accuratelylabelled; no setupfiles/manualserviceprerequisite.

## Targeted validation

- [ ] Existing realRabbit21 pluscombinedMessaging/RPC/hosting/package cases on actualWindows/Ubuntu, same realassertions and zero selectedskips; meaningfulinitialconnection-failure/fresh-success case onlyifgap. Scopedformat/build; actualTLSnegativeidentity/permissions, storage/recovery and descriptor/drain/cleanup review; noTestcontainers incompiledlinkedchild/shippingclosures. Categories accompanymigration, updatecurrentMessagingguides, independentreview; same12h16attempt6GiB.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
