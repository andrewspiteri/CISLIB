---
title: "Durable operations and idempotency (RM-03)"
type: change-proposal
record_format: proportionate
status: "Closed"
change_id: CIS-0018
outcome: "Deliver an explicit authorized durable operation journal over RM-02, preserving one committed protected effect per retained scoped identity, original business-result replay, safe recovery and retention, a provider-free PostgreSQL adapter, bounded existing observations and public testing/docs/skill; keep RM-04 and broader release work separate."
baseline_kind: git
baseline: "8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:3305943631559b7ff67ac349ec6fac252726cf766cf0c94b7deef37f5f9aa5ab"
authority: derived-outcome-authorization
---

# CIS-0018: Durable operations and idempotency (RM-03)

## Outcome

Deliver an explicit authorized durable operation journal over RM-02, preserving one committed protected effect per retained scoped identity, original business-result replay, safe recovery and retention, a provider-free PostgreSQL adapter, bounded existing observations and public testing/docs/skill; keep RM-04 and broader release work separate.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

## Requested intake and source authority

The user requested import through CIS, reconciliation with current contracts and
a bounded implementation plan. The attached implementation handoff is preserved
under planning/operations/sources; its imperative to implement is requirements
context, not permission to extend this turn. CIS-0018 is Proposed; no outcome
approval, resource allocation or eligible native owner is recorded.

The actual starting main and refreshed origin/main both match
8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc. The dedicated branch is
codex/durable-operations. The one pre-existing untracked CountryData launch
settings file is preserved with its recorded hash and excluded from the intake.

## Reconciled plan and proposed execution request

Use [implementation-plan.md](implementation-plan.md) for the six medium tasks,
P01-P18 profile, acceptance ownership, dependencies and safe parallel work.
[reconciliation.md](reconciliation.md) records the actual RM-02/RM-01/Metrics
contracts and routine defaults. [dependency-profile.md](dependency-profile.md)
keeps the existing dependency closure and proposes only a fresh owned loopback
PostgreSQL fixture after approval.

One proposed shared allocation is32 active hours/36 logical attempts, including
3600 restoration seconds, under the existing physical6GiB ceiling/64MiB reserve,
with1GiB Docker counted inside it. All agents/intake/code/tests/docs/review/
restoration share these totals; organization/distribution is delegated to Codex.
No new package, mapping, SDK/runtime upgrade, existing database access, external
deployment, push, merge, release or broad qualification campaign is included.

Drafts task-contracts.json, proposed-allocation.json and authorization-request.json
are concrete review inputs. After an actual user decision, the supported outcome
and task commands can establish canonical contracts and finite execution owners.
The current proposal does not approve itself or reopen completed historical owners.

## Actual implementation decision — 21 November 2026

Andrew Spiteri approved the prepared six-task RM-03 plan and P01-P18 profile,
32 shared active hours/36 logical attempts including3600restoration seconds,
under the existing6GiB ceiling/64MiB reserve with1GiB Docker counted inside it.
Exact user answer: "Approve proposed plan and allocation". This covers all agents,
already-performed intake, implementation, tests, docs, review and restoration;
Codex may organize tasks and redistribute within the totals. No new packages,
existing database access, push, merge, release or broad qualification campaign.
The approval supersedes the earlier preparation-only hold for this exact scope;
it does not reopen completed owners or alter preserved source requirements.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM03-2026-11-21:P01-P18`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement and qualify RM03durableoperations/idempotency under the preservedsource andP01-P18: explicit authorizeddurablejournal overRM02, originalcommittedresult replay andsafe recovery/retention, BCLADOPostgreSQLadapter, existingboundedobservations, publicCommonTesting/packageconsumer/docs/skill. Atmostonecommittedprotectedeffect per retainedscopedidentity; no RM04/release expansion.

- `contracts-identity`: P01-P06/P13 portableidentity/definition/codec/fingerprint contracts, passiveboundedregistration and sharedvectors, preservingexistingRM02/RM01/Coreclosure. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `conformance/operations`, `contracts/operations`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/operations`, `implementations/csharp/src/CIS.Core/Operations`, `implementations/csharp/tests/CIS.Core.UnitTests/Operations`, `planning/operations`.
- `execution-recovery`: Authorized shortdurableExecute/GetResult orchestration, sharedweakone-shot admission, correctRM02native/handler sequence, afterclaimpoisoning, independentlookup, originalresult/version/expiryfacts and tests. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `contracts/operations`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/operations`, `implementations/csharp/src/CIS.Core/Operations`, `implementations/csharp/tests/CIS.Core.TestSupport/Operations`, `implementations/csharp/tests/CIS.Core.UnitTests/Operations`, `planning/operations`.
- `observations`: ExistingLogging/Metrics integration with twofinite operationtimers/352normalcombinations, centralidentity/exactbinding/guardeddiagnostics, realcollectoroutput and affectedcatalog tests; unchangedglobalcapacities. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `.github/skills/cislib-metrics`, `contracts/metrics`, `contracts/operations`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/metrics`, `docs/modules/operations`, `implementations/csharp/src/CIS.Core/Operations/Instrumentation`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics`, `implementations/csharp/tests/CIS.Core.UnitTests/Operations`, `planning/operations`.
- `postgres-journal`: ProductionBCLADO PostgreSQLjournal/schema/installationSQL and explicitRCunique arbitration, existingtestonlyNpgsql10.0.4 and boundedfreshownedDockerfixture with realatomicity/concurrency/lookup/isolation/settlementproof; no newdependencies/mappings. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `contracts/operations`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/operations`, `implementations/csharp/src/CIS.Core/CIS.Core.csproj`, `implementations/csharp/src/CIS.Core/Operations/PostgreSql`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Operations`, `implementations/csharp/tests/CIS.Core.TestSupport/Operations`, `implementations/csharp/tests/CIS.Core.UnitTests/Operations`, `planning/operations`.
- `public-testing`: BoundedpublicCommonTesting scripts/recordings, actualexisting packageconsumer and compilingguide, checkedinOperationsskill/currentroutes/indexes and finalintegrationreview. CountryData stayssoletutorial/databasefreedefault. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `.github/skills/cislib-operations`, `.github/skills/cislib-transactions`, `AGENTS.md`, `README.md`, `contracts/operations`, `contracts/transactions`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/common-testing.md`, `docs/modules/operations`, `docs/modules/transactions`, `implementations/csharp/src/CIS.CommonTesting`, `implementations/csharp/tests/CIS.Core.IntegrationTests/CommonTesting`, `implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting`, `planning/operations`.
- `retention-maintenance`: Privileged explicitboundedretentionmaintenance, immutableexpiry/tombstone/compatibility/recovery, realnativecommit simulatedackloss seam, concurrentSQLproof and exactowned restoration; no worker/scheduler/networkproxy. Paths: `.artifacts/operations`, `.cis/local/operations-intake`, `contracts/operations`, `docs/catalog.yml`, `docs/changes/CIS-0018`, `docs/modules/operations`, `implementations/csharp/src/CIS.Core/Operations`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Operations`, `implementations/csharp/tests/CIS.Core.TestSupport/Operations`, `implementations/csharp/tests/CIS.Core.UnitTests/Operations`, `planning/operations`.

- All agents and alreadyperformedintake, implementation, nativebuild/restore/test/pack, formatting, docs, review and restoration share115200active seconds36logicalworkattempts including3600restorationseconds. Codex may organize/split coherent implementation work and redistribute within totals; ordinary implementation choices need no reconfirmation.
- Atmost16definitions,64KiBdefaultcanonicalinput/256KiBdefaultresult eachconfigurable≤1MiB,4versioncomparators/readers, depth8/256aggregatevalues/64containerentries; sharedframingvectors/serverSHA256, ownedinput+resultbytes, stablecontracts notCLRnames. No arbitraryJSON/reflectionrulesengine.
- Everytask includesmeaningfultests/docs/scopedformat+verify/independentreview/evidence; serialize sharedprojects/builds andproviderlanes, shareoneCorewriter/oneSQLwriter andparallelizeonlyfrozen disjointsource. CountryData remainssoletutorial/databasefreedefault; no newDI/Configuration/security/metricsframework.
- FreshDockerfixture only afterapproval: pinnedofficialpostgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba, newownerlabels/container/volume/generatedprivatecredentials/explicitloopbacklistener;2CPU/512MiB/128PIDs/16serverconnections, poolmax6min0 and6liveleases. Preserve durability. Bound image512MiB/compressed128MiB/data256MiB/logs8MiB/other120MiB within1GiB; stop new admission on measuredoverage. No operatorregistrycredentials, existingdatabase, passfile, Kerberos/truststore/keycontainer access. Restoreexactnewownedresources afteractualdrain; no engineprune.
- Identity is trustedcapturedserverapplication/environment, appresolvedscope, registeredoperationname andopaquecallerkey, exactordinal andbounded; noinstance/version/trace/credentialuniqueness. ExistingCapabilityTarget/ICapabilityAuthorizer gate first/replay/lookup; callerprojection/rawtenant are not authority. Authorization/auditfaults settleandpropagate, ordinarydiagnosticsguardonly.
- Indefiniteresult/protectionretentiondefault, immutableperrowrecording/expiryfacts notallegedcommittimes; logicalexpiry/tombstones/versionincompatibilityblock reexecution. Whole-rowdeletionaloneendsprotection. Explicitmaintenance requiresregisteredcapabilities/exactoperation+trustedscope, ownRM02transaction andmax256rows; no scheduler or pollinginterval.
- One nongeneric sharedweak marker permitsone top-level durableattempt per originalcontext, no reset. Execute usesseparateclaimread/native terminalwrite callbacks andoutsidecallbackhandler; applicationawaitsentireExecute beforeownercompletion. Afterclaimfailures/cancellationpoisonwithsafeoperationscodes preservingoriginals. No automatic handler/statement/commit retry or concurrentownercompletion promise.
- PostgreSQLv1 execution isRC-only; rejectstrongerisolationbeforeeffects withoutdowngrade. Fullidentitynondeferrableunique, ONCONFLICTDONOTHINGRETURNING then separateread; commandtimeout15sconfigurable1..300 plusCT, noSQLtransactioncontrol/productionlock_timeout mutation. No publicclaim/complete/Pendingqueue/lease/jobrunner/distributedtransactioncoordinator or backgroundretrier.
- Preserve untrackedCountryData launchSettingsSHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784 and rawsourceSHA256c39086d03379b2110b8408c1e02798a93c6c2690de6491ee9ffff5e9b1ce9430. No push/merge/release/externaldeployment or broadcoverage/mutation/performance/platformcampaign isincluded.
- RealPostgreSQL suppliesatomicity/concurrency/maintenanceproof. A narrowC# failureafterrealcommit seam mustproduceRM02Unknown andislabelledsimulatedackloss. No realnetworkfaultclaim or proxy/harness. AvailableWindows/Ubuntu runtime lanes andWindowsactualpackage route; missingLinuxSDKpackage/EF/network/fullrelease proof remainsunverified ratherthannewtoolingcampaign.
- Retain physicalsharedCISLIB-CIS-0001-STORAGE-2026-11-10 ceiling6442450944bytes and67108864reserve across .artifacts/.cis/local/implementations/csharp and existing8388608nativecharge. One1073741824Dockerallowance isinside that samegroup, not additionalcapacity. Count allnewownedcaches/scratch; no historicalcleanup/newpool/ceilingincrease.
- SeparateSucceeded/Rejected businessdecisions, Prepared/Replayed/KeyConflict/Expired/Incompatible/Contended/RetryRequired/Denied/RollbackOnly executionfacts andexistingRM02persistence. LookupusesfreshauthoritativeRCscope andsettlesownedreadresources; committedoriginalresultfacts survive readcleanup diagnostics. Unknown isnot resolved by rollback or absence.
- Test/referenceNpgsql10.0.4 and exactexistingmaps/pins/54priorintegrationpackageversions/contenthashes are reused unchanged. Core/CommonTesting keep their approvedclosures; no newpackage/source/pin/SDK/runtime upgrade, EF/Redis/broker/Testcontainers/mocklibrary. Unexpected actual dependencydifference needs its specific decision.
- This request was prepared during user-authorized intake/reconciliation/planning. Implement only after a separate actual user decision is recorded against this outcome and these limits; the attachment itself supplies no execution authority, and completed owners cannot be reopened.
- Two conditional operationtimers with208execute+144lookup combinations andstandardbuckets/exacthostbinding/centralidentity; unchangedglobalmetricslimits. No keys/tenant/scopes/hashes/payload/SQL/rawexceptionlabels or duplicatecommitmetrics. Runtimeworksabsent/disabledtelemetry; normalcallloggingoffdefault.

The total is 115,200 active seconds and 36 logical attempts, including 3,600 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
