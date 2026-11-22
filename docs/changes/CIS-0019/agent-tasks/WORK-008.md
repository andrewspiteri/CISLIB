---
title: "WORK-008 Deliver typed request/reply and current-attempt durable recovery"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:f03671ca53f34b78bb2363bde61de56a"
depends_on: ["WORK-013"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0019:outcome:7f1507a223264cf1986c6524aedc5a6c"
authorization_scope_ids: ["rpc","rabbit-provider","postgres-store"]
authorization_scope_basis: "sha256:94246b6857d11a35d0b4326add8b41ed540d123e65e781cc4a816e094c3e55fa"
---

# WORK-008: Deliver typed request/reply and current-attempt durable recovery

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver typed request/reply and current-attempt durable recovery

## Acceptance criteria

- [x] P17-P19 and B01-B06: Query/DurableCommand bindings on same store/dispatcher. Reservewaiter/registerbeforestage; originaltransaction disposableticket retains earlyresponse andmonotonicdeadline; knowncommittedreceiptbeforewait. Exacttrustedreplymatch/oncecompletion/currentattempt resultrecovery, endpointretirement and no remoterollbackorTasks claims. Implement narrow retained reply links: terminal canonical replies and safe authorized rejection-linked Denied replies attach in the same original fresh transaction with native-owner fencing; never poison canonical state. Redrive clears old-generation reply; retention protects unresolved anchors. Reserve one reply work lane and 4xMaxEnvelope encoded headroom INSIDE existing process totals for RPC profiles; reserve bounded response working bytes before claim/effects; coherent active-root profile, no nested capacity deadlock. Runtime closes local request admission/cancels waits before dispatch drain and joins owned cleanup. Real B05 crash uses one minimal public-only nested Messaging/CrashHost process entry and existing ProcessRunner, not graceful/in-process reconstruction. No new package/harness/framework. Use independent Messaging DBschema version2 with explicit embedded/packaged install-v2.sql for new reply-anchor/native-owner shape; strictly reject version1 at runtime. Remove unreleased current v1 asset via ordinary Git history. No automatic schema migration/installation; envelopewire remains v1.

## Targeted validation

- [x] Realfastquery/durabledecision/rejection, timeout/cancelaftercommit/late/duplicate/maliciousreply, concurrentcallers/restart/response loss thennewattempt recoverywithoutsecondcommittedeffect; conflict/denied/expired/incompatible result; active/unknownsubmission/overload/capacitycleanup. Guide/style/build and independentrecoveryreview; metrics/package facts refreshedatPartB. Add actual SQL anchor/fence/redrive/cleanup regressions, trusted denied-vs-conflicting reply distinctions, saturated concurrent RPC and genuine crash after committed effect/result/reply followed by caller restart and original-result recovery. Nested fixture/project linkage stays within existing rpc+rabbit-provider scopes and uses only retained test Npgsql10.0.4. Refresh actual exporter and public-package facts after PartB. All work shares revised7.5h owner008 allocation, existing64h containing total and original restoration reserves. Verifycurrentv2freshinstaller andstrictoldversionrejection, packaged/embeddedSQLidentity andallaffectedexisting33storecases plusnewreply-anchor/fence/retention cases; Core project SQLresource metadata is within retained postgres-store scope.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Actual request/recovery | Windows12/12;Ubuntu12/12;realRPCexportWindows1/1+Ubuntucase; genuinecaller/responderprocessdeath/currentattemptoriginalresult; zero failures/skips | [Review](../review-008.md), final-integrated-receipt.json |
| Affected currentCore | 502unitseachunchangedcandidatehash;26actualintegrations each;retainedstore41/protocol56eachcomponent;build/stylePASS | [Verification](../verification.md), [review](../review-008.md) |
| Independent review/ownership | Requiredcancellation/byte/ticketcleanup/denial/canonicalreplyfindingsresolved; actualowneddrainandfixture/privateinputrestorationPASS | [Review](../review-008.md), [handoff](../review-009.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2057; failed=284; possibleTokenSavings=591; ledgerDigest=sha256:bcde294abafb213352d959bcc54b3206fbb86641690372ccc6f5907fc778a560 |
